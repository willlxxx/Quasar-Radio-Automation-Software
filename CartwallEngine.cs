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
        public event EventHandler<string>? PlaybackFailed;

        public CartwallEngine(AudioDspProcessor? dspProcessor = null, MasterBus? bus = null)
        {
            _dspProcessor = dspProcessor ?? new AudioDspProcessor();
            _bus = bus ?? new MasterBus();
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

        public void TogglePad(CartPadItem pad)
        {
            if (pad == null || !pad.HasAudio) return;

            if (_activePlayers.TryRemove(pad.PadId, out var existingPlayer))
            {
                existingPlayer.StopAndDispose();
                return;
            }

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
                    SourceId = $"PAD {pad.PadId}",
                    Bus = _bus
                };

                waveOut.Init(levelTap.ToWaveProvider());

                var player = new ActivePadPlayer(audioFile, volumeProvider, levelTap, waveOut, pad);

                waveOut.PlaybackStopped += (s, e) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_activePlayers.TryRemove(pad.PadId, out var removedPlayer))
                        {
                            removedPlayer.StopAndDispose();
                        }
                        else
                        {
                            pad.IsPlaying = false;
                        }
                    });
                };

                if (_activePlayers.TryAdd(pad.PadId, player))
                {
                    pad.IsPlaying = true;
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
                var message = UserErrorMessages.For(ex, $"reproducir el efecto '{pad.Title}'");
                Log.Error(ex, "No se pudo reproducir el pad {PadId} ({PadTitle}) desde {FilePath}", pad.PadId, pad.Title, pad.FilePath);
                PlaybackFailed?.Invoke(this, message);
            }
        }

        public void StopAll()
        {
            foreach (var key in _activePlayers.Keys)
            {
                if (_activePlayers.TryRemove(key, out var player))
                {
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
