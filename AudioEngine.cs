using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Serilog;

namespace quasar_radio
{
    public class AudioEngine : IDisposable
    {
        private readonly ILogger _logger = Log.ForContext<AudioEngine>();

        private class Deck
        {
            public string Name { get; }
            public WaveStream? AudioFile { get; set; }
            public VolumeSampleProvider? VolumeProvider { get; set; }
            public WaveOut? WaveOut { get; set; }
            public TrackItem? CurrentTrack { get; set; }
            public BroadcastSampleProvider? SpectrumProvider { get; set; }
            public LevelMeterSampleProvider? LevelMeter { get; set; }

            /// <summary>
            /// Medidor despues del volumen: lo que este deck entrega de verdad a la tarjeta.
            /// El VU del deck pinta el nivel previo al fader (para que el operador vea la pista
            /// aunque tenga su canal bajado); este va al <see cref="MasterBus"/>.
            /// </summary>
            public MasterLevelTap? PostFaderTap { get; set; }
            public float TargetVolume { get; set; } = 1.0f;
            public float CurrentVolume { get; set; } = 1.0f;
            public bool IsFading { get; set; }

            /// <summary>
            /// UI-7 paso B: el boton REPETIR de cada deck. Con esto el deck que esta en el aire
            /// vuelve a su CUE al terminar la pista en lugar de cruzar a la siguiente o cerrar el
            /// aire. Es por deck, no por pista, porque lo que repite un operador en cabina es el
            /// canal que tiene sonando (un tema que enlaza con una locucion, una base musical), no
            /// un titulo del disco.
            /// </summary>
            public bool Repeat { get; set; }

            public Deck(string name)
            {
                Name = name;
            }

            public void SetVolume(float volume)
            {
                CurrentVolume = Math.Clamp(volume, 0.0f, 1.0f);
                if (VolumeProvider != null)
                {
                    VolumeProvider.Volume = CurrentVolume;
                }
            }

            public void StopAndDispose()
            {
                IsFading = false;
                if (WaveOut != null)
                {
                    try
                    {
                        WaveOut.Stop();
                    }
                    catch { }
                    WaveOut.Dispose();
                    WaveOut = null;
                }
                if (AudioFile != null)
                {
                    AudioFile.Dispose();
                    AudioFile = null;
                }
                VolumeProvider = null;
                SpectrumProvider = null;
                LevelMeter = null;
                PostFaderTap = null;
                CurrentTrack = null;
            }
        }

        private readonly Deck _deckA = new Deck("DECK A");
        private readonly Deck _deckB = new Deck("DECK B");
        private Deck _activeDeck;

        private readonly DispatcherTimer _playbackTimer;
        private readonly DispatcherTimer _fadeTimer;
        public AudioDspProcessor DspProcessor { get; } = new AudioDspProcessor();

        /// <summary>
        /// El bus donde los decks, la botonera y el micro dejan su nivel post-fader. Es singleton
        /// en DI: la consola lo lee una vez cada 100 ms para pintar el VU de salida y para el
        /// detector de silencio. Sin inyeccion (pruebas, o el motor suelto) sigue funcionando con
        /// uno propio que nadie consulta.
        /// </summary>
        public MasterBus Bus { get; }

        /// <summary>
        /// Quién está pidiendo bajar la música. Cada origen deja su factor objetivo: 1 no baja
        /// nada, 0,25 deja la música a un cuarto. Manda el más profundo mientras haya pedidos,
        /// así que micro + pad + pisador no se pisan entre sí ni se reactivan al soltarse uno.
        /// </summary>
        private readonly Dictionary<string, float> _duckRequests = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _duckSync = new object();

        /// <summary>El factor realmente aplicado. Persegue al objetivo con rampa, no a salto.</summary>
        private float _duckApplied = 1.0f;

        private readonly DispatcherTimer _duckTimer;

        /// <summary>La petición del micro; <see cref="IsDucked"/> es la puerta de toda la vida.</summary>
        private const string MicroDuckSource = "MICRO";

        private const int DuckTickMs = 50;

        /// <summary>Segundos en bajar la música cuando alguien empieza a hablar o suena un pad.</summary>
        public float DuckAttackSeconds { get; set; } = 0.18f;

        /// <summary>Segundos en devolverla cuando ese origen calla.</summary>
        public float DuckReleaseSeconds { get; set; } = 0.45f;

        public bool IsDucked
        {
            get { lock (_duckSync) return _duckRequests.ContainsKey(MicroDuckSource); }
            set
            {
                if (value) RequestDuck(MicroDuckSource, DuckingRatio);
                else ReleaseDuck(MicroDuckSource);
            }
        }

        public float DuckingRatio { get; set; } = 0.25f;

        /// <summary>El factor que se está aplicando a la música ahora mismo (1 = sin ducking).</summary>
        public float DuckFactor => _duckApplied;

        /// <summary>Cuantos origenes estan pidiendo bajar la musica (estado de la consola y verificación).</summary>
        public int ActiveDuckRequests
        {
            get { lock (_duckSync) return _duckRequests.Count; }
        }

        /// <summary>
        /// Pide que la música baje al <paramref name="targetFactor"/> mientras se mantenga la
        /// petición. Se puede repetir con el mismo origen para cambiar la profundidad en vivo.
        /// </summary>
        public void RequestDuck(string sourceId, float targetFactor)
        {
            if (string.IsNullOrEmpty(sourceId)) return;
            lock (_duckSync) _duckRequests[sourceId] = Math.Clamp(targetFactor, 0f, 1f);
            if (!_duckTimer.IsEnabled) _duckTimer.Start();
            StepDuck();
        }

        /// <summary>Retira la petición de un origen; si no queda ninguna, la música vuelve subiendo.</summary>
        public void ReleaseDuck(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)) return;
            lock (_duckSync) _duckRequests.Remove(sourceId);
            if (!_duckTimer.IsEnabled) _duckTimer.Start();
            StepDuck();
        }

        private float DuckTarget()
        {
            lock (_duckSync)
            {
                float deepest = 1.0f;
                foreach (var factor in _duckRequests.Values)
                {
                    if (factor < deepest) deepest = factor;
                }
                return deepest;
            }
        }

        /// <summary>
        /// Un paso de la rampa. Se llama en cada tick y también justo al pedir o soltar, para que
        /// el primer salto sea inmediato y el operador no espere 50 ms en empezar a oír el cambio.
        /// </summary>
        private void StepDuck()
        {
            float target = DuckTarget();

            if (_duckApplied > target)
            {
                float step = DuckTickMs / 1000f / Math.Max(0.05f, DuckAttackSeconds);
                _duckApplied = _duckApplied - step <= target ? target : _duckApplied - step;
            }
            else if (_duckApplied < target)
            {
                float step = DuckTickMs / 1000f / Math.Max(0.05f, DuckReleaseSeconds);
                _duckApplied = _duckApplied + step >= target ? target : _duckApplied + step;
            }

            UpdateActiveDeckVolume();

            if (_duckApplied == target && target >= 1.0f) _duckTimer.Stop();
        }

        private float _masterVolume = 1.0f;
        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = Math.Clamp(value, 0.0f, 1.0f);
                UpdateActiveDeckVolume();
            }
        }

        private void UpdateActiveDeckVolume()
        {
            if (_activeDeck == null) return;

            // Un deck que está subiendo en un fundido cruzado lleva su propia rampa: el tick del
            // fade ya le aplica el ducking, y escribirle aqui el destino cortaria la rampa.
            if (_activeDeck.IsFading) return;

            _activeDeck.SetVolume(_activeDeck.TargetVolume * _masterVolume * _duckApplied);
        }

        public double CrossfadeSeconds { get; set; } = 4.0;
        public bool IsAutoMode { get; set; } = true;
        public bool IsPlaying { get; private set; }
        public string? LastError { get; private set; }

        public TrackItem? DeckA_Track => _deckA.CurrentTrack;
        public TimeSpan DeckA_Position => _deckA.AudioFile?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan DeckA_Duration => _deckA.AudioFile?.TotalTime ?? TimeSpan.Zero;
        public bool DeckA_IsActive => _activeDeck == _deckA && IsPlaying;
        public float DeckA_Volume
        {
            get => _deckA.TargetVolume;
            set { _deckA.TargetVolume = Math.Clamp(value, 0f, 1f); UpdateActiveDeckVolume(); }
        }
        public float DeckA_VuLevel { get; private set; }
        public float[] DeckA_Spectrum => _deckA.SpectrumProvider?.Spectrum ?? Array.Empty<float>();

        public TrackItem? DeckB_Track => _deckB.CurrentTrack;
        public TimeSpan DeckB_Position => _deckB.AudioFile?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan DeckB_Duration => _deckB.AudioFile?.TotalTime ?? TimeSpan.Zero;
        public bool DeckB_IsActive => _activeDeck == _deckB && IsPlaying;
        public float DeckB_Volume
        {
            get => _deckB.TargetVolume;
            set { _deckB.TargetVolume = Math.Clamp(value, 0f, 1f); UpdateActiveDeckVolume(); }
        }
        public float DeckB_VuLevel { get; private set; }
        public float[] DeckB_Spectrum => _deckB.SpectrumProvider?.Spectrum ?? Array.Empty<float>();

        /// <summary>
        /// REPETIR por deck (UI-7 paso B). Es estado del canal, no de la pista: el operador repite
        /// lo que tiene sonando ahora, no un titulo del disco.
        /// </summary>
        public bool DeckA_Repeat
        {
            get => _deckA.Repeat;
            set
            {
                if (_deckA.Repeat == value) return;
                _deckA.Repeat = value;
                _logger.Information("Deck A {State}", value ? "en REPETIR" : "sale de REPETIR");
            }
        }

        public bool DeckB_Repeat
        {
            get => _deckB.Repeat;
            set
            {
                if (_deckB.Repeat == value) return;
                _deckB.Repeat = value;
                _logger.Information("Deck B {State}", value ? "en REPETIR" : "sale de REPETIR");
            }
        }

        /// <summary>Vuelve el estado que queda despues de pulsar el boton (para el enlace).</summary>
        public bool ToggleDeckRepeat(string? deckName)
        {
            if (string.Equals(deckName, "B", StringComparison.OrdinalIgnoreCase))
            {
                DeckB_Repeat = !DeckB_Repeat;
                return DeckB_Repeat;
            }

            DeckA_Repeat = !DeckA_Repeat;
            return DeckA_Repeat;
        }

        /// <summary>
        /// Vuelve a atras la pista que esta en el aire y dice si de verdad la encontro. Con
        /// REPETIR activo el tick llama aqui al llegar al final, en lugar de cruzar o cerrar.
        /// </summary>
        public bool RestartActiveDeck()
        {
            var deck = _activeDeck;
            var file = deck.AudioFile;
            var track = deck.CurrentTrack;
            if (file == null || track == null) return false;

            var cue = track.CueIn > TimeSpan.Zero && track.CueIn < file.TotalTime ? track.CueIn : TimeSpan.Zero;
            file.CurrentTime = cue;
            _crossfadeTriggered = false;
            PositionChanged?.Invoke(this, cue);
            _logger.Information("REPETIR {DeckName}: {Title} vuelve a {Cue}", deck.Name, track.Title, cue);
            return true;
        }

        public TrackItem? CurrentTrack => _activeDeck?.CurrentTrack;
        public string ActiveDeckName => _activeDeck?.Name ?? "DECK A";

        private bool _crossfadeTriggered = false;

        // EVENTOS
        public event EventHandler<TrackItem?>? TrackChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler? PlaybackFinished;

        public AudioEngine(MasterBus? bus = null)
        {
            Bus = bus ?? new MasterBus();
            _activeDeck = _deckA;

            _playbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _playbackTimer.Tick += OnPlaybackTimerTick;

            _fadeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _fadeTimer.Tick += OnFadeTimerTick;

            // UI-8: la rampa del ducking. Solo corre mientras hay algo que bajar o devolver,
            // asi que con la consola en reposo no despierta a nadie.
            _duckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DuckTickMs)
            };
            _duckTimer.Tick += (_, _) => StepDuck();
        }

        public TimeSpan CurrentPosition => _activeDeck?.AudioFile?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan CurrentDuration => _activeDeck?.AudioFile?.TotalTime ?? TimeSpan.Zero;

        public Func<TrackItem?>? GetNextTrackProvider { get; set; }

        public bool PlayTrack(TrackItem track)
        {
            return PlayTrackOnDeck("A", track);
        }

        public bool PlayTrackOnIdleDeck(TrackItem track)
        {
            var idleDeck = _activeDeck == _deckA ? _deckB : _deckA;
            var targetDeckName = idleDeck == _deckA ? "A" : "B";
            var activeTrack = _activeDeck?.CurrentTrack;
            var preferredTrack = idleDeck.CurrentTrack;

            if (preferredTrack != null &&
                (activeTrack == null ||
                 !string.Equals(preferredTrack.FilePath, activeTrack.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                return PlayTrackOnDeck(targetDeckName, preferredTrack);
            }

            if (track != null &&
                (activeTrack == null ||
                 !string.Equals(track.FilePath, activeTrack.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                return PlayTrackOnDeck(targetDeckName, track);
            }

            LastError = "La pista seleccionada coincide con la que ya está reproduciendo el otro deck.";
            return false;
        }

        public bool PlayTrackOnDeck(string deckName, TrackItem track)
        {
            LastError = null;
            if (track == null)
            {
                LastError = "Selecciona una pista para reproducir.";
                return false;
            }

            if (string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath))
            {
                LastError = $"No se encontró el archivo de audio de '{track.Title}'. Comprueba su ubicación.";
                _logger.Warning("No existe el archivo de audio para {TrackTitle}: {FilePath}", track.Title, track.FilePath);
                return false;
            }

            try
            {
                StopAll();

                _activeDeck = deckName.Equals("B", StringComparison.OrdinalIgnoreCase) ? _deckB : _deckA;
                if (!InitAndPlayDeck(_activeDeck, track, 1.0f))
                {
                    return false;
                }

                IsPlaying = true;
                _crossfadeTriggered = false;
                _playbackTimer.Start();
                TrackChanged?.Invoke(this, track);
                return true;
            }
            catch (Exception exception)
            {
                LastError = UserErrorMessages.For(exception, "reproducir la pista");
                _logger.Error(exception, "No se pudo reproducir {TrackTitle} desde {FilePath}", track.Title, track.FilePath);
                return false;
            }
        }

        public void CueDeck(string deckName)
        {
            var deck = deckName.Equals("B", StringComparison.OrdinalIgnoreCase) ? _deckB : _deckA;
            if (deck.AudioFile == null || deck.CurrentTrack == null)
            {
                return;
            }

            var cueTime = deck.CurrentTrack.CueIn > TimeSpan.Zero ? deck.CurrentTrack.CueIn : TimeSpan.Zero;
            if (cueTime > deck.AudioFile.TotalTime)
            {
                cueTime = TimeSpan.Zero;
            }

            deck.AudioFile.CurrentTime = cueTime;
            PositionChanged?.Invoke(this, cueTime);
        }

        public void PauseDeck(string deckName)
        {
            var deck = deckName.Equals("B", StringComparison.OrdinalIgnoreCase) ? _deckB : _deckA;
            if (deck.WaveOut == null)
            {
                return;
            }

            if (deck.WaveOut.PlaybackState == PlaybackState.Playing)
            {
                deck.WaveOut.Pause();
                IsPlaying = _deckA.WaveOut?.PlaybackState == PlaybackState.Playing || _deckB.WaveOut?.PlaybackState == PlaybackState.Playing;
            }
            else if (deck.WaveOut.PlaybackState == PlaybackState.Paused)
            {
                deck.WaveOut.Play();
                IsPlaying = true;
                if (!_playbackTimer.IsEnabled)
                {
                    _playbackTimer.Start();
                }
            }
        }

        private bool InitAndPlayDeck(Deck deck, TrackItem track, float startVolumeRatio)
        {
            try
            {
                deck.StopAndDispose();

                var audioReader = AudioSourceFactory.Open(track.FilePath);
                var waveOut = new WaveOut();

                deck.AudioFile = audioReader;
                deck.WaveOut = waveOut;
                deck.CurrentTrack = track;
                deck.TargetVolume = 1.0f;
                var samples = audioReader is ISampleProvider sampleProvider
                    ? sampleProvider
                    : audioReader.ToSampleProvider();
                var processedSamples = new BroadcastSampleProvider(samples, DspProcessor);
                var levelMeter = new LevelMeterSampleProvider(processedSamples);
                deck.SpectrumProvider = processedSamples;
                deck.LevelMeter = levelMeter;
                deck.VolumeProvider = new VolumeSampleProvider(levelMeter);
                deck.SetVolume(startVolumeRatio * MasterVolume);

                // UI-7: la toma va DESPUES del volumen, no antes. Es lo que permite que el VU de
                // salida baje con el master y que el detector mida el aire real.
                var postTap = new MasterLevelTap(deck.VolumeProvider)
                {
                    SourceId = deck.Name,
                    Bus = Bus
                };
                deck.PostFaderTap = postTap;

                waveOut.Init(postTap.ToWaveProvider());

                if (track.CueIn > TimeSpan.Zero && track.CueIn < audioReader.TotalTime)
                {
                    audioReader.CurrentTime = track.CueIn;
                }

                waveOut.Play();
                return true;
            }
            catch (Exception exception)
            {
                LastError = UserErrorMessages.For(exception, "preparar la pista de audio");
                _logger.Error(exception, "No se pudo inicializar {TrackTitle} en {DeckName}", track.Title, deck.Name);
                deck.StopAndDispose();
                return false;
            }
        }

        public void PreloadNextTrack(TrackItem? track)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath))
                return;

            Deck idleDeck = (_activeDeck == _deckA) ? _deckB : _deckA;
            if (idleDeck.CurrentTrack == track && idleDeck.AudioFile != null)
                return;

            try
            {
                idleDeck.StopAndDispose();
                var audioReader = AudioSourceFactory.Open(track.FilePath);
                var waveOut = new WaveOut();

                idleDeck.AudioFile = audioReader;
                idleDeck.WaveOut = waveOut;
                idleDeck.CurrentTrack = track;
                idleDeck.TargetVolume = 1.0f;
                var samples = audioReader is ISampleProvider sampleProvider
                    ? sampleProvider
                    : audioReader.ToSampleProvider();
                var processedSamples = new BroadcastSampleProvider(samples, DspProcessor);
                idleDeck.SpectrumProvider = processedSamples;
                idleDeck.VolumeProvider = new VolumeSampleProvider(processedSamples);
                idleDeck.SetVolume(0.0f);

                // El deck en espera esta callado (volumen 0) hasta el cruce: su toma existe desde
                // el primer bloque para que en cuanto suba en el crossfade el VU master ya le vea.
                var preloadTap = new MasterLevelTap(idleDeck.VolumeProvider)
                {
                    SourceId = idleDeck.Name,
                    Bus = Bus
                };
                idleDeck.PostFaderTap = preloadTap;

                waveOut.Init(preloadTap.ToWaveProvider());
                if (track.CueIn > TimeSpan.Zero && track.CueIn < audioReader.TotalTime)
                {
                    audioReader.CurrentTime = track.CueIn;
                }
            }
            catch (Exception exception)
            {
                _logger.Warning(exception, "No se pudo precargar la pista {TrackTitle}", track.Title);
                idleDeck.StopAndDispose();
            }
        }

        public void TriggerCrossfadeToNext() => TryCrossfadeToNext();

        /// <summary>
        /// Igual que <see cref="TriggerCrossfadeToNext"/> pero diciendo si de verdad paso algo:
        /// quien llama necesita saber que no habia siguiente para poder cerrar el aire
        /// (ver el tick de <see cref="OnPlaybackTimerTick"/>).
        /// </summary>
        private bool TryCrossfadeToNext()
        {
            if (!IsPlaying || _crossfadeTriggered) return false;

            var nextTrack = GetNextTrackProvider?.Invoke();
            if (nextTrack == null)
            {
                // No hay más canciones en la lista
                return false;
            }

            _crossfadeTriggered = true;
            Deck outgoingDeck = _activeDeck;
            Deck incomingDeck = (outgoingDeck == _deckA) ? _deckB : _deckA;

            if (!InitAndPlayDeck(incomingDeck, nextTrack, 0.0f))
            {
                _crossfadeTriggered = false;
                return false;
            }

            _activeDeck = incomingDeck;
            TrackChanged?.Invoke(this, nextTrack);

            // Iniciar desvanecimiento cruzado
            outgoingDeck.IsFading = true;
            incomingDeck.IsFading = true;
            _fadeTimer.Start();
            return true;
        }

        private void OnPlaybackTimerTick(object? sender, EventArgs e)
        {
            if (!IsPlaying || _activeDeck?.AudioFile == null)
            {
                DeckA_VuLevel = 0f;
                DeckB_VuLevel = 0f;
                return;
            }

            TimeSpan pos = _activeDeck.AudioFile.CurrentTime;
            TimeSpan total = _activeDeck.AudioFile.TotalTime;
            PositionChanged?.Invoke(this, pos);

            // UI-7: el nivel de salida ya no sale de aqui. Estos dos valores siguen siendo el
            // nivel PREVIO al fader de cada deck (lo que pinta su medidor de canal, que tiene que
            // moverse aunque el master este abajo); lo que sale por la tarjeta lo mide el
            // MasterLevelTap de cada cadena en el hilo de audio.
            var deckALevel = _deckA.SpectrumProvider?.CurrentLevel ?? 0f;
            var deckBLevel = _deckB.SpectrumProvider?.CurrentLevel ?? 0f;

            var deckAVisual = Math.Clamp(MathF.Pow(deckALevel, 0.78f) * 125f, 0f, 100f);
            var deckBVisual = Math.Clamp(MathF.Pow(deckBLevel, 0.78f) * 125f, 0f, 100f);

            DeckA_VuLevel = deckAVisual;
            DeckB_VuLevel = deckBVisual;

            TimeSpan remaining = total - pos;

            // Verificar si alcanzamos el CueOut o el margen de Crossfade
            TrackItem? current = _activeDeck.CurrentTrack;
            bool reachedCueOut = current != null && current.CueOut > TimeSpan.Zero && pos >= current.CueOut;
            bool remainingUnderCrossfade = remaining.TotalSeconds <= CrossfadeSeconds && remaining.TotalSeconds > 0;

            // UI-7 paso B: con REPETIR en el deck en el aire no se cruza a la siguiente, se vuelve
            // al principio de la pista. El cruce se salta tambien el CueOut: un tema que se repite
            // se oye entero.
            var repeatActive = _activeDeck.Repeat;

            if (repeatActive && (reachedCueOut || remaining.TotalSeconds <= 0.25))
            {
                RestartActiveDeck();
                return;
            }

            if ((reachedCueOut || remainingUnderCrossfade) && !_crossfadeTriggered && !repeatActive)
            {
                if (IsAutoMode)
                {
                    TriggerCrossfadeToNext();
                }
            }

            // Fin absoluto de la pista
            if (pos >= total || remaining.TotalMilliseconds <= 100)
            {
                if (repeatActive && RestartActiveDeck())
                {
                    return;
                }

                // UI-4 paso B: aqui estaba el agujero. En AUTO se llamaba a TriggerCrossfadeToNext
                // y, si la cola no tenía siguiente, el metodo volvia sin hacer nada: _crossfadeTriggered
                // seguia en false, la rama del else nunca se ejecutaba y el motor se quedaba con
                // IsPlaying=true y el deck clavado en el ultimo tick. En cabina eso deja el "AL AIRE"
                // encendido con silencio, la pista nunca se cierra en estadisticas y "ahora suena"
                // se queda con la cancion que ya termino. Ahora el aire se cierra por su cuenta.
                if (IsAutoMode && TryCrossfadeToNext())
                {
                    return;
                }

                if (!_fadeTimer.IsEnabled && !_crossfadeTriggered)
                {
                    StopAll();
                    PlaybackFinished?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void OnFadeTimerTick(object? sender, EventArgs e)
        {
            Deck outgoingDeck = (_activeDeck == _deckA) ? _deckB : _deckA;
            Deck incomingDeck = _activeDeck;

            float step = (float)(0.05 / Math.Max(0.5, CrossfadeSeconds));

            // UI-8: el techo del fundido es el volumen con el ducking ya aplicado. Sin esto, un
            // pad con pisador que entra a media transicion se queda la pista nueva sonando entera
            // mientras el anterior seguia bajando, porque el tick del fade escribia MasterVolume
            // a secas y no pasaba por UpdateActiveDeckVolume.
            float ceiling = MasterVolume * _duckApplied;

            // Bajar saliente
            if (outgoingDeck.IsFading && outgoingDeck.AudioFile != null)
            {
                float newVol = outgoingDeck.CurrentVolume - step * MasterVolume;
                if (newVol <= 0.001f)
                {
                    outgoingDeck.SetVolume(0.0f);
                    outgoingDeck.StopAndDispose();
                }
                else
                {
                    outgoingDeck.SetVolume(newVol);
                }
            }

            // Subir entrante
            if (incomingDeck.IsFading && incomingDeck.AudioFile != null)
            {
                float newVol = incomingDeck.CurrentVolume + step * MasterVolume;
                if (newVol >= ceiling)
                {
                    incomingDeck.SetVolume(ceiling);
                    incomingDeck.IsFading = false;
                }
                else
                {
                    incomingDeck.SetVolume(newVol);
                }
            }

            // Si el saliente terminó de desvanecerse
            if (outgoingDeck.AudioFile == null && !incomingDeck.IsFading)
            {
                _fadeTimer.Stop();
                _crossfadeTriggered = false;
            }
        }

        public void Pause()
        {
            if (_activeDeck?.WaveOut != null && IsPlaying)
            {
                _activeDeck.WaveOut.Pause();
                IsPlaying = false;
                _playbackTimer.Stop();
            }
            else if (_activeDeck?.WaveOut != null && !IsPlaying)
            {
                _activeDeck.WaveOut.Play();
                IsPlaying = true;
                _playbackTimer.Start();
            }
        }

        public void StopAll()
        {
            _playbackTimer.Stop();
            _fadeTimer.Stop();
            _deckA.StopAndDispose();
            _deckB.StopAndDispose();
            IsPlaying = false;
            _crossfadeTriggered = false;

            // UI-7: el temporizador de medicion se para arriba, asi que nadie vuelve a cero los
            // niveles previos al fader. Sin esto el medidor del deck se queda CLAVADO en el ultimo
            // valor de la emision (medido en vivo el 2026-10-23: PARAR y el VU del deck seguia a
            // 76), que es justo la clase de engaño por la que se pidio esta fase.
            DeckA_VuLevel = 0f;
            DeckB_VuLevel = 0f;

            // Para no esperar los 250 ms de la ventana del bus: al PARAR, el VU de salida tiene
            // que quedarse en cero en el mismo instante.
            Bus.Forget(_deckA.Name);
            Bus.Forget(_deckB.Name);
        }

        public void Dispose()
        {
            StopAll();
            _duckTimer.Stop();
        }
    }
}
