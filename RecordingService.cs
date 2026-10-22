using System;
using System.IO;
using System.Windows.Threading;
using NAudio.Wave;
using Serilog;

namespace quasar_radio
{
    public class RecordingService : ViewModelBase, IDisposable
    {
        private WaveFileWriter? _waveWriter;
        private IWaveIn? _waveIn;
        private bool _isRecording;
        private DateTime _recordingStartTime;
        private string _recordingFolder = "grabaciones";
        private string _currentFilePath = string.Empty;
        private long _bytesRecorded = 0;
        private readonly DispatcherTimer _durationTimer;
        public string? LastError { get; private set; }

        public bool IsRecording
        {
            get => _isRecording;
            private set
            {
                _isRecording = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RecordingStatusText));
                OnPropertyChanged(nameof(RecordingButtonText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public string RecordingStatusText => IsRecording ? "GRABANDO EN VIVO" : "GRABACIÓN OFF";
        public string RecordingButtonText => IsRecording ? "⏹ DETENER GRABACIÓN" : "⏺ INICIAR GRABACIÓN";
        public string StatusColor => IsRecording ? "#FF1744" : "#262D3D";

        public string RecordingFolder
        {
            get => _recordingFolder;
            set { _recordingFolder = value; OnPropertyChanged(); }
        }

        public string CurrentFilePath
        {
            get => _currentFilePath;
            set { _currentFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentFileName)); }
        }

        public string CurrentFileName => string.IsNullOrEmpty(CurrentFilePath) ? "--" : Path.GetFileName(CurrentFilePath);

        public DateTime RecordingStartTime
        {
            get => _recordingStartTime;
            set { _recordingStartTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(RecordingStartTimeFormatted)); }
        }

        public string RecordingStartTimeFormatted => RecordingStartTime != DateTime.MinValue ? RecordingStartTime.ToString("HH:mm:ss") : "--:--:--";

        public TimeSpan RecordingDuration
        {
            get => DateTime.Now - RecordingStartTime;
        }

        public string RecordingDurationFormatted => IsRecording ? RecordingDuration.ToString(@"hh\:mm\:ss") : "00:00:00";

        public long BytesRecorded
        {
            get => _bytesRecorded;
            set { _bytesRecorded = value; OnPropertyChanged(); OnPropertyChanged(nameof(BytesRecordedFormatted)); }
        }

        public string BytesRecordedFormatted => BytesRecorded >= 1024 * 1024 ? $"{BytesRecorded / (1024 * 1024)} MB" : $"{BytesRecorded / 1024} KB";

        public event EventHandler? RecordingStateChanged;
        public event EventHandler<string>? RecordingFileCreated;

        public RecordingService()
        {
            _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _durationTimer.Tick += OnDurationTimerTick;

            // Crear carpeta de grabaciones si no existe
            if (!Directory.Exists(_recordingFolder))
            {
                Directory.CreateDirectory(_recordingFolder);
            }
        }

        public bool StartRecording()
        {
            if (IsRecording) return false;
            LastError = null;

            try
            {
                // Asegurar que la carpeta existe
                if (!Directory.Exists(_recordingFolder))
                {
                    Directory.CreateDirectory(_recordingFolder);
                }

                // Generar nombre de archivo con timestamp
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"emision_{timestamp}.wav";
                CurrentFilePath = Path.Combine(_recordingFolder, fileName);

                // Usar captura de audio del dispositivo de entrada por defecto
                _waveIn = new WaveIn
                {
                    WaveFormat = new WaveFormat(44100, 16, 2),
                    DeviceNumber = 0 // Dispositivo de entrada por defecto
                };

                // Crear escritor de archivo WAV
                _waveWriter = new WaveFileWriter(CurrentFilePath, _waveIn.WaveFormat);

                // Conectar evento de datos
                _waveIn.DataAvailable += OnDataAvailable;

                // Iniciar captura
                _waveIn.StartRecording();

                RecordingStartTime = DateTime.Now;
                IsRecording = true;
                BytesRecorded = 0;

                _durationTimer.Start();

                RecordingFileCreated?.Invoke(this, CurrentFilePath);
                RecordingStateChanged?.Invoke(this, EventArgs.Empty);

                return true;
            }
            catch (Exception exception)
            {
                LastError = UserErrorMessages.For(exception, "iniciar la grabación");
                Log.Error(exception, "No se pudo iniciar la grabación en {RecordingFolder}", _recordingFolder);
                try { Cleanup(); }
                catch (Exception cleanupException) { Log.Warning(cleanupException, "Falló la limpieza después de un error al iniciar grabación"); }
                CurrentFilePath = string.Empty;
                return false;
            }
        }

        public bool StopRecording()
        {
            if (!IsRecording) return false;

            try
            {
                _durationTimer.Stop();
                _waveIn?.StopRecording();
                Cleanup();

                IsRecording = false;
                RecordingStateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                LastError = UserErrorMessages.For(ex, "detener la grabación");
                Log.Error(ex, "No se pudo detener la grabación {RecordingPath}", CurrentFilePath);
                try { Cleanup(); }
                catch (Exception cleanupException) { Log.Warning(cleanupException, "Falló la limpieza después de un error al detener grabación"); }
                IsRecording = false;
                RecordingStateChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_waveWriter != null)
            {
                _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
                BytesRecorded += e.BytesRecorded;
            }
        }

        private void Cleanup()
        {
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.StopRecording();
                _waveIn.Dispose();
                _waveIn = null;
            }

            if (_waveWriter != null)
            {
                _waveWriter.Dispose();
                _waveWriter = null;
            }
        }

        private void OnDurationTimerTick(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(RecordingDurationFormatted));
        }

        public string[] GetRecordingFiles()
        {
            if (!Directory.Exists(_recordingFolder))
                return Array.Empty<string>();

            return Directory.GetFiles(_recordingFolder, "*.wav")
                           .OrderByDescending(f => File.GetCreationTime(f))
                           .ToArray();
        }

        public long GetTotalRecordingSize()
        {
            if (!Directory.Exists(_recordingFolder))
                return 0;

            var files = Directory.GetFiles(_recordingFolder, "*.wav");
            long totalSize = 0;
            foreach (var file in files)
            {
                totalSize += new FileInfo(file).Length;
            }
            return totalSize;
        }

        public string GetTotalRecordingSizeFormatted()
        {
            long totalSize = GetTotalRecordingSize();
            if (totalSize >= 1024 * 1024 * 1024)
                return $"{totalSize / (1024 * 1024 * 1024)} GB";
            else if (totalSize >= 1024 * 1024)
                return $"{totalSize / (1024 * 1024)} MB";
            else if (totalSize >= 1024)
                return $"{totalSize / 1024} KB";
            else
                return $"{totalSize} bytes";
        }

        public bool DeleteRecording(string filePath)
        {
            LastError = null;
            try
            {
                if (!File.Exists(filePath))
                {
                    LastError = "La grabación ya no existe o fue movida.";
                    return false;
                }

                File.Delete(filePath);
                return true;
            }
            catch (Exception ex)
            {
                LastError = UserErrorMessages.For(ex, "eliminar la grabación");
                Log.Error(ex, "No se pudo eliminar la grabación {RecordingPath}", filePath);
                return false;
            }
        }

        public bool DeleteAllRecordings()
        {
            LastError = null;
            try
            {
                if (Directory.Exists(_recordingFolder))
                {
                    var files = Directory.GetFiles(_recordingFolder, "*.wav");
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = UserErrorMessages.For(ex, "eliminar todas las grabaciones");
                Log.Error(ex, "No se pudieron eliminar todas las grabaciones en {RecordingFolder}", _recordingFolder);
                return false;
            }
        }

        public bool OpenRecordingFolder()
        {
            LastError = null;
            try
            {
                if (!Directory.Exists(_recordingFolder))
                {
                    Directory.CreateDirectory(_recordingFolder);
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _recordingFolder,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                LastError = UserErrorMessages.For(ex, "abrir la carpeta de grabaciones");
                Log.Error(ex, "No se pudo abrir la carpeta de grabaciones {RecordingFolder}", _recordingFolder);
                return false;
            }
        }

        public void Dispose()
        {
            StopRecording();
            _durationTimer.Stop();
        }
    }
}