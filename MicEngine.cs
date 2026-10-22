using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Serilog;

namespace quasar_radio
{
    public class MicEngine : IDisposable
    {
        private WaveIn? _waveIn;
        private BufferedWaveProvider? _waveProvider;
        private WaveOut? _waveOut;
        private readonly AudioEngine _audioEngine;

        public bool IsMicActive { get; private set; }
        public string? LastError { get; private set; }

        public event EventHandler<bool>? MicStateChanged;

        public MicEngine(AudioEngine audioEngine)
        {
            _audioEngine = audioEngine;
        }

        public bool ToggleMic()
        {
            if (IsMicActive)
            {
                StopMic();
                return false;
            }
            else
            {
                return StartMic();
            }
        }

        public bool StartMic()
        {
            if (IsMicActive) return true;
            LastError = null;

            try
            {
                _waveIn = new WaveIn
                {
                    WaveFormat = new WaveFormat(44100, 16, 2)
                };

                _waveProvider = new BufferedWaveProvider(_waveIn.WaveFormat, TimeSpan.FromMilliseconds(500))
                {
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };

                _waveOut = new WaveOut();
                var micSamples = new BroadcastSampleProvider(_waveProvider.ToSampleProvider(), _audioEngine.DspProcessor);
                _waveOut.Init(micSamples.ToWaveProvider());

                _waveIn.DataAvailable += (s, e) =>
                {
                    _waveProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
                };

                _waveIn.StartRecording();
                _waveOut.Play();

                IsMicActive = true;
                _audioEngine.IsDucked = true;
                MicStateChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception ex)
            {
                LastError = UserErrorMessages.For(ex, "activar el micrófono");
                Log.Error(ex, "No se pudo activar la entrada de micrófono");
                StopMic();
                return false;
            }
        }

        public void StopMic()
        {
            IsMicActive = false;
            _audioEngine.IsDucked = false;

            if (_waveIn != null)
            {
                try { _waveIn.StopRecording(); } catch { }
                _waveIn.Dispose();
                _waveIn = null;
            }

            if (_waveOut != null)
            {
                try { _waveOut.Stop(); } catch { }
                _waveOut.Dispose();
                _waveOut = null;
            }

            _waveProvider = null;
            MicStateChanged?.Invoke(this, false);
        }

        public void Dispose()
        {
            StopMic();
        }
    }
}
