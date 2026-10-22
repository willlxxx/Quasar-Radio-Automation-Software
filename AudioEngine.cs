using System;
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
            public float TargetVolume { get; set; } = 1.0f;
            public float CurrentVolume { get; set; } = 1.0f;
            public bool IsFading { get; set; }

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
                CurrentTrack = null;
            }
        }

        private readonly Deck _deckA = new Deck("DECK A");
        private readonly Deck _deckB = new Deck("DECK B");
        private Deck _activeDeck;

        private readonly DispatcherTimer _playbackTimer;
        private readonly DispatcherTimer _fadeTimer;
        public AudioDspProcessor DspProcessor { get; } = new AudioDspProcessor();

        private bool _isDucked;
        public bool IsDucked
        {
            get => _isDucked;
            set
            {
                _isDucked = value;
                UpdateActiveDeckVolume();
            }
        }

        public float DuckingRatio { get; set; } = 0.25f;

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
            if (_activeDeck != null)
            {
                float duckFactor = IsDucked ? DuckingRatio : 1.0f;
                _activeDeck.SetVolume(_activeDeck.TargetVolume * _masterVolume * duckFactor);
            }
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

        public TrackItem? CurrentTrack => _activeDeck?.CurrentTrack;
        public string ActiveDeckName => _activeDeck?.Name ?? "DECK A";

        private bool _crossfadeTriggered = false;

        // EVENTOS
        public event EventHandler<TrackItem?>? TrackChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler? PlaybackFinished;
        public event EventHandler<(float Left, float Right)>? MeterLevelsChanged;

        public AudioEngine()
        {
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

                waveOut.Init(deck.VolumeProvider.ToWaveProvider());

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

                waveOut.Init(idleDeck.VolumeProvider.ToWaveProvider());
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

        public void TriggerCrossfadeToNext()
        {
            if (!IsPlaying || _crossfadeTriggered) return;

            var nextTrack = GetNextTrackProvider?.Invoke();
            if (nextTrack == null)
            {
                // No hay más canciones en la lista
                return;
            }

            _crossfadeTriggered = true;
            Deck outgoingDeck = _activeDeck;
            Deck incomingDeck = (outgoingDeck == _deckA) ? _deckB : _deckA;

            if (!InitAndPlayDeck(incomingDeck, nextTrack, 0.0f))
            {
                _crossfadeTriggered = false;
                return;
            }

            _activeDeck = incomingDeck;
            TrackChanged?.Invoke(this, nextTrack);

            // Iniciar desvanecimiento cruzado
            outgoingDeck.IsFading = true;
            incomingDeck.IsFading = true;
            _fadeTimer.Start();
        }

        private void OnPlaybackTimerTick(object? sender, EventArgs e)
        {
            if (!IsPlaying || _activeDeck?.AudioFile == null)
            {
                DeckA_VuLevel = 0f;
                DeckB_VuLevel = 0f;
                MeterLevelsChanged?.Invoke(this, (0f, 0f));
                return;
            }

            TimeSpan pos = _activeDeck.AudioFile.CurrentTime;
            TimeSpan total = _activeDeck.AudioFile.TotalTime;
            PositionChanged?.Invoke(this, pos);

            var deckALevel = _deckA.SpectrumProvider?.CurrentLevel ?? 0f;
            var deckBLevel = _deckB.SpectrumProvider?.CurrentLevel ?? 0f;

            var deckAVisual = Math.Clamp(MathF.Pow(deckALevel, 0.78f) * 125f, 0f, 100f);
            var deckBVisual = Math.Clamp(MathF.Pow(deckBLevel, 0.78f) * 125f, 0f, 100f);

            DeckA_VuLevel = deckAVisual;
            DeckB_VuLevel = deckBVisual;
            MeterLevelsChanged?.Invoke(this, (deckALevel, deckBLevel));

            TimeSpan remaining = total - pos;

            // Verificar si alcanzamos el CueOut o el margen de Crossfade
            TrackItem? current = _activeDeck.CurrentTrack;
            bool reachedCueOut = current != null && current.CueOut > TimeSpan.Zero && pos >= current.CueOut;
            bool remainingUnderCrossfade = remaining.TotalSeconds <= CrossfadeSeconds && remaining.TotalSeconds > 0;

            if ((reachedCueOut || remainingUnderCrossfade) && !_crossfadeTriggered)
            {
                if (IsAutoMode)
                {
                    TriggerCrossfadeToNext();
                }
            }

            // Fin absoluto de la pista
            if (pos >= total || remaining.TotalMilliseconds <= 100)
            {
                if (!_crossfadeTriggered && IsAutoMode)
                {
                    TriggerCrossfadeToNext();
                }
                else if (!_fadeTimer.IsEnabled && !_crossfadeTriggered)
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
                if (newVol >= MasterVolume)
                {
                    incomingDeck.SetVolume(MasterVolume);
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
        }

        public void Dispose()
        {
            StopAll();
        }
    }
}
