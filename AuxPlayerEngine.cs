using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace quasar_radio
{
    public class AuxPlayer : ViewModelBase, IDisposable
    {
        private WaveStream? _audioFile;
        private VolumeSampleProvider? _volumeProvider;
        private WaveOut? _waveOut;

        public string Name { get; }

        private TrackItem? _currentTrack;
        public TrackItem? CurrentTrack
        {
            get => _currentTrack;
            set { _currentTrack = value; OnPropertyChanged(); }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(); }
        }

        private float _volume = 1.0f;
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0f, 1f);
                if (_volumeProvider != null) _volumeProvider.Volume = _volume;
                OnPropertyChanged();
            }
        }

        public AuxPlayer(string name)
        {
            Name = name;
        }

        public bool PlayTrack(TrackItem? track)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath))
                return false;

            Stop();

            try
            {
                _audioFile = AudioSourceFactory.Open(track.FilePath);
                var samples = _audioFile is ISampleProvider sampleProvider
                    ? sampleProvider
                    : _audioFile.ToSampleProvider();
                _volumeProvider = new VolumeSampleProvider(samples) { Volume = Volume };
                _waveOut = new WaveOut();
                _waveOut.Init(_volumeProvider.ToWaveProvider());

                _waveOut.PlaybackStopped += (s, e) =>
                {
                    IsPlaying = false;
                };

                CurrentTrack = track;
                _waveOut.Play();
                IsPlaying = true;
                return true;
            }
            catch
            {
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            if (_waveOut != null)
            {
                try { _waveOut.Stop(); } catch { }
                _waveOut.Dispose();
                _waveOut = null;
            }
            if (_audioFile != null)
            {
                _audioFile.Dispose();
                _audioFile = null;
            }
            _volumeProvider = null;
            IsPlaying = false;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public class AuxPlayerEngine : IDisposable
    {
        public AuxPlayer Aux1 { get; } = new AuxPlayer("AUX 1 (PFL)");
        public AuxPlayer Aux2 { get; } = new AuxPlayer("AUX 2 (PFL)");

        public void StopAll()
        {
            Aux1.Stop();
            Aux2.Stop();
        }

        public void Dispose()
        {
            Aux1.Dispose();
            Aux2.Dispose();
        }
    }
}
