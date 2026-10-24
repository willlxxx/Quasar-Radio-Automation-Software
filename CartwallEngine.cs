using System;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Serilog;

namespace quasar_radio
{
    public class CartwallEngine : IDisposable
    {
        private class ActivePadPlayer
        {
            public WaveStream AudioFile { get; }
            public VolumeSampleProvider VolumeProvider { get; }
            public MasterLevelTap LevelTap { get; }
            public WaveOut WaveOut { get; }
            public CartPadItem Pad { get; }

            public ActivePadPlayer(WaveStream audioFile, VolumeSampleProvider volumeProvider,
                MasterLevelTap levelTap, WaveOut waveOut, CartPadItem pad)
            {
                AudioFile = audioFile;
                VolumeProvider = volumeProvider;
                LevelTap = levelTap;
                WaveOut = waveOut;
                Pad = pad;
            }

            public void StopAndDispose()
            {
                try
                {
                    WaveOut.Stop();
                }
                catch { }
                WaveOut.Dispose();
                AudioFile.Dispose();
                LevelTap.Bus?.Forget(LevelTap.SourceId ?? string.Empty);
                Pad.IsPlaying = false;
            }
        }

        private readonly ConcurrentDictionary<int, ActivePadPlayer> _activePlayers = new ConcurrentDictionary<int, ActivePadPlayer>();
        private readonly AudioDspProcessor _dspProcessor;
        private readonly MasterBus _bus;

        /// <summary>
        /// El motor de reproduccion, para poder bajar la musica mientras un pad esta en el aire
        /// (UI-8). Opcional: la botonera se prueba suela en las herramientas de verificacion y
        /// entonces simplemente no duckea.
        /// </summary>
        private readonly AudioEngine? _audioEngine;

        public event EventHandler<string>? PlaybackFailed;

        public CartwallEngine(AudioDspProcessor? dspProcessor = null, MasterBus? bus = null,
            AudioEngine? audioEngine = null)
        {
            _dspProcessor = dspProcessor ?? new AudioDspProcessor();
            _bus = bus ?? new MasterBus();
            _audioEngine = audioEngine;
        }

        private float _cartwallVolume = 1.0f;
        public float CartwallVolume
        {
            get => _cartwallVolume;
            set
            {
                _cartwallVolume = Math.Clamp(value, 0.0f, 1.0f);
                foreach (var player in _activePlayers.Values)
                {
                    player.VolumeProvider.Volume = _cartwallVolume * player.Pad.Volume;
                }
            }
        }

        /// <summary>
        /// Aplica el volumen que el pad tiene ahora mismo a la cadena que esta sonando. Se llama
        /// desde el ViewModel al mover el menu de volumen: sin esto, ajustar el nivel de un efecto
        /// solo se notaba en el siguiente disparo, que en cabina es exactamente al reves.
        /// </summary>
        public void ApplyPadVolume(CartPadItem pad)
        {
            if (pad == null) return;
            if (_activePlayers.TryGetValue(pad.PadId, out var player))
            {
                player.VolumeProvider.Volume = _cartwallVolume * pad.Volume;
            }
        }

        /// <summary>El id con el que el pad se registra en el bus de salida y en el ducking.</summary>
        public static string SourceIdFor(CartPadItem pad) => $"PAD {pad.PadId}";

        /// <summary>
        /// Vuelve a pedir el ducking con la profundidad que tiene ahora un pad que ya esta en el
        /// aire. Ajustar el ducking a mitad de clip tiene que notarse en ese clip, no en el
        /// siguiente: el operador sube el nivel de una cortinilla mientras suena.
        /// </summary>
        public void ReapplyPadDuck(CartPadItem pad)
        {
            if (_audioEngine == null || pad == null) return;
            if (!_activePlayers.ContainsKey(pad.PadId)) return;

            if (pad.DuckDepth <= 0f)
            {
                _audioEngine.ReleaseDuck(SourceIdFor(pad));
                return;
            }

            _audioEngine.RequestDuck(SourceIdFor(pad), 1.0f - pad.DuckDepth);
        }

        /// <summary>Si el pad esta ahora mismo en el aire.</summary>
        public bool IsPadPlaying(int padId) => _activePlayers.ContainsKey(padId);

        public void TogglePad(CartPadItem pad)
        {
            if (pad == null || !pad.HasAudio) return;

            if (_activePlayers.TryRemove(pad.PadId, out var existingPlayer))
            {
                ReleaseDuckFor(pad);
                existingPlayer.StopAndDispose();

                // Disparar y dejar: el pad no se corta, se vuelve a empezar. Es el modo del golpe
                // rapido, y por eso el segundo clic no puede dejar el pad mudo.
                if (pad.Mode == CartPlayMode.OneShot)
                {
                    StartPad(pad);
                }

                return;
            }

            StartPad(pad);
        }

        private void StartPad(CartPadItem pad)
        {
            try
            {
                var audioFile = AudioSourceFactory.Open(pad.FilePath);
                var samples = audioFile is ISampleProvider sampleProvider
                    ? sampleProvider
                    : audioFile.ToSampleProvider();
                var processedSamples = new BroadcastSampleProvider(samples, _dspProcessor);
                var volumeProvider = new VolumeSampleProvider(processedSamples)
                {
                    Volume = _cartwallVolume * pad.Volume
                };
                var waveOut = new WaveOut();

                // UI-7: un pad que suena ES aire. Sin esta toma el VU de salida se quedaba quieto
                // mientras la botonera estaba en el aire, y el detector de silencio no la veia.
                var levelTap = new MasterLevelTap(volumeProvider)
                {
                    SourceId = SourceIdFor(pad),
                    Bus = _bus
                };

                waveOut.Init(levelTap.ToWaveProvider());

                var player = new ActivePadPlayer(audioFile, volumeProvider, levelTap, waveOut, pad);

                // UI-8: el aviso de parada se mira contra EL REPRODUCTOR que lo emite, no contra el
                // hueco del pad. NAudio publica PlaybackStopped por el contexto de sincronizacion,
                // o sea despues de volver del hilo de UI: al cortar y volver a disparar el mismo pad
                // (modo "disparar y dejar"), el aviso del clip viejo llegaba con el clip nuevo ya
                // montado y se llevaba puesto al recien nacido. Medido en vivo el 2026-10-23: el pie
                // decia "Pad F3 al aire" y el bus se quedaba en SIN SENAL, el pad mudo.
                waveOut.PlaybackStopped += (s, e) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (!_activePlayers.TryGetValue(pad.PadId, out var actual))
                        {
                            pad.IsPlaying = false;
                            return;
                        }

                        if (!ReferenceEquals(actual, player)) return;

                        _activePlayers.TryRemove(pad.PadId, out _);
                        ReleaseDuckFor(pad);
                        actual.StopAndDispose();
                    });
                };

                if (_activePlayers.TryAdd(pad.PadId, player))
                {
                    pad.IsPlaying = true;

                    // UI-8: el pad pide el ducking que lleva anotado, y lo suelta al terminar.
                    // Se pide ANTES de Play para que la rampa empiece con el clip sonando.
                    RequestDuckFor(pad);
                    waveOut.Play();
                }
                else
                {
                    player.StopAndDispose();
                }
            }
            catch (Exception ex)
            {
                pad.IsPlaying = false;
                ReleaseDuckFor(pad);
                var message = UserErrorMessages.For(ex, $"reproducir el efecto '{pad.Title}'");
                Log.Error(ex, "No se pudo reproducir el pad {PadId} ({PadTitle}) desde {FilePath}", pad.PadId, pad.Title, pad.FilePath);
                PlaybackFailed?.Invoke(this, message);
            }
        }

        private void RequestDuckFor(CartPadItem pad)
        {
            if (_audioEngine == null || pad.DuckDepth <= 0f) return;
            _audioEngine.RequestDuck(SourceIdFor(pad), 1.0f - pad.DuckDepth);
        }

        private void ReleaseDuckFor(CartPadItem pad)
        {
            _audioEngine?.ReleaseDuck(SourceIdFor(pad));
        }

        public void StopAll()
        {
            foreach (var key in _activePlayers.Keys)
            {
                if (_activePlayers.TryRemove(key, out var player))
                {
                    ReleaseDuckFor(player.Pad);
                    player.StopAndDispose();
                }
            }
        }

        public void Dispose()
        {
            StopAll();
        }
    }
}
