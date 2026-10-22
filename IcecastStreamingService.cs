using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Windows;
using NAudio.Wave;
using Serilog;

namespace quasar_radio
{
    public class IcecastStreamingService : ViewModelBase, IDisposable
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private Stream? _writeStream;
        private Thread? _streamingThread;
        private bool _isStreaming;
        private bool _isSessionConnected;
        private bool _shouldStop;
        private volatile bool _sessionFailed;
        private DateTime _sessionConnectedAtUtc;
        private readonly object _lock = new object();
        private readonly StreamingCircuitBreaker _circuitBreaker = new(3, TimeSpan.FromSeconds(30));
        private WasapiRecorder? _loopbackCapture;
        private WaveFormat? _waveFormat;
        private Process? _ffmpegProcess;
        private Channel<byte[]>? _captureBuffer;
        private Thread? _captureWriterThread;
        private long _bufferOverrunCount;
        private long _bufferUnderrunCount;
        private string _ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "bin", "ffmpeg.exe");

        private string _serverUrl = "127.0.0.1";
        private int _serverPort = 8000;
        private string _mountPoint = "/live";
        private string _password = "hackme";
        private string _stationName = "Quasar Radio Pro";
        private string _stationGenre = "General";
        private string _stationDescription = "Emisora Digital Profesional";
        private string _stationUrl = "https://quasar-radio.com";
        private int _bitrate = 128;
        private int _sampleRate = 44100;
        private int _channels = 2;

        public string FFmpegPath
        {
            get => _ffmpegPath;
            set
            {
                _ffmpegPath = value;
                OnPropertyChanged();
            }
        }

        private string _statusMessage = "Desconectado";
        private DateTime _connectionTime = DateTime.MinValue;
        private TimeSpan _streamDuration = TimeSpan.Zero;
        private long _bytesSent = 0;

        public bool IsStreaming
        {
            get => _isStreaming;
            private set
            {
                _isStreaming = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StreamingStatusText));
                OnPropertyChanged(nameof(StreamingButtonText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public string StreamingStatusText => !IsStreaming
            ? "STREAMING OFF"
            : _isSessionConnected ? "EMITIENDO EN VIVO (ICECAST)" : "CONECTANDO / RECONECTANDO";
        public string StreamingButtonText => IsStreaming ? "⏹ DESCONECTAR STREAM" : "📡 CONECTAR STREAM";
        public string StatusColor => !IsStreaming ? "#262D3D" : _isSessionConnected ? "#00E5FF" : "#FF9100";
        public string CircuitBreakerState => _circuitBreaker.State.ToString();
        public int ConsecutiveConnectionFailures => _circuitBreaker.ConsecutiveFailures;

        public string ServerUrl
        {
            get => _serverUrl;
            set { _serverUrl = value; OnPropertyChanged(); }
        }

        public int ServerPort
        {
            get => _serverPort;
            set { _serverPort = value; OnPropertyChanged(); }
        }

        public string MountPoint
        {
            get => _mountPoint;
            set { _mountPoint = value; OnPropertyChanged(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        public string StationName
        {
            get => _stationName;
            set { _stationName = value; OnPropertyChanged(); }
        }

        public string StationGenre
        {
            get => _stationGenre;
            set { _stationGenre = value; OnPropertyChanged(); }
        }

        public string StationDescription
        {
            get => _stationDescription;
            set { _stationDescription = value; OnPropertyChanged(); }
        }

        public string StationUrl
        {
            get => _stationUrl;
            set { _stationUrl = value; OnPropertyChanged(); }
        }

        public int Bitrate
        {
            get => _bitrate;
            set { _bitrate = Math.Clamp(value, 32, 320); OnPropertyChanged(); }
        }

        public int SampleRate
        {
            get => _sampleRate;
            set { _sampleRate = Math.Clamp(value, 22050, 48000); OnPropertyChanged(); }
        }

        public int Channels
        {
            get => _channels;
            set { _channels = Math.Clamp(value, 1, 2); OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage == value) return;
                _statusMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StreamingStatusText));
                OnPropertyChanged(nameof(StatusColor));
                StatusMessageChanged?.Invoke(this, value);
            }
        }

        public DateTime ConnectionTime
        {
            get => _connectionTime;
            set { _connectionTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionTimeFormatted)); }
        }

        public string ConnectionTimeFormatted => ConnectionTime != DateTime.MinValue ? ConnectionTime.ToString("HH:mm:ss") : "--:--:--";

        public TimeSpan StreamDuration
        {
            get => _streamDuration;
            set { _streamDuration = value; OnPropertyChanged(); OnPropertyChanged(nameof(StreamDurationFormatted)); }
        }

        public string StreamDurationFormatted => StreamDuration.ToString(@"hh\:mm\:ss");

        public long BytesSent
        {
            get => _bytesSent;
            set { _bytesSent = value; OnPropertyChanged(); OnPropertyChanged(nameof(BytesSentFormatted)); }
        }

        public string BytesSentFormatted => BytesSent >= 1024 * 1024 ? $"{BytesSent / (1024 * 1024)} MB" : $"{BytesSent / 1024} KB";
        public long BufferOverrunCount => Interlocked.Read(ref _bufferOverrunCount);
        public long BufferUnderrunCount => Interlocked.Read(ref _bufferUnderrunCount);

        public event EventHandler? StreamingStateChanged;
        public event EventHandler<string>? StatusMessageChanged;

        public IcecastStreamingService()
        {
            // Valores por defecto para Icecast remoto
            _serverUrl = "streaming.quasar-radio.com"; // Ejemplo de dirección web
            _serverPort = 8000;
            _mountPoint = "/live";
            _password = "hackme";
            _stationName = "Quasar Radio Pro";
            _stationGenre = "General";
            _stationDescription = "Emisora Digital Profesional";
            _stationUrl = "https://quasar-radio.com";
            _bitrate = 128;
            _sampleRate = 44100;
            _channels = 2;
        }

        public bool StartStreaming()
        {
            if (IsStreaming || _streamingThread?.IsAlive == true) return false;

            try
            {
                lock (_lock)
                {
                    _shouldStop = false;
                    _isSessionConnected = false;
                    _circuitBreaker.Reset();
                    OnPropertyChanged(nameof(CircuitBreakerState));
                    OnPropertyChanged(nameof(ConsecutiveConnectionFailures));
                    _streamingThread = new Thread(StreamingWorker)
                    {
                        IsBackground = true,
                        Name = "IcecastStreamingThread"
                    };
                    _streamingThread.Start();
                    IsStreaming = true;
                    StatusMessage = "Conectando a Icecast...";
                    ConnectionTime = DateTime.Now;
                    Log.Information("Iniciando streaming hacia {ServerUrl}:{ServerPort}{MountPoint}", ServerUrl, ServerPort, MountPoint);
                }

                StreamingStateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo iniciar el worker de streaming");
                StatusMessage = UserErrorMessages.For(ex, "iniciar el streaming");
                return false;
            }
        }

        public void StopStreaming()
        {
            if (!IsStreaming) return;
            Log.Information("Solicitud de detención del streaming");

            lock (_lock)
            {
                _shouldStop = true;
                IsStreaming = false;
                StatusMessage = "Desconectando...";
            }

            if (_streamingThread != null && _streamingThread.IsAlive)
            {
                _streamingThread.Join(3000); // Esperar hasta 3 segundos
            }

            CleanupConnection();
            StreamingStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void StreamingWorker()
        {
            var retryAttempt = 0;
            try
            {
                while (!_shouldStop)
                {
                    if (!_circuitBreaker.TryEnter(out var circuitDelay))
                    {
                        StatusMessage = $"Circuit breaker abierto. Reintentando en {Math.Ceiling(circuitDelay.TotalSeconds)} s.";
                        if (WaitForRetry(circuitDelay)) break;
                        continue;
                    }
                    OnPropertyChanged(nameof(CircuitBreakerState));

                    _sessionFailed = false;
                    _sessionConnectedAtUtc = DateTime.MinValue;
                    var attemptStartedAtUtc = DateTime.UtcNow;
                    try
                    {
                        StreamingWorkerAttempt();
                    }
                    catch (Exception exception)
                    {
                        _sessionFailed = true;
                        Log.Error(exception, "Error inesperado en la sesión de streaming");
                        StatusMessage = UserErrorMessages.For(exception, "mantener la conexión de streaming");
                    }
                    finally
                    {
                        CleanupConnection();
                        _isSessionConnected = false;
                        OnPropertyChanged(nameof(StreamingStatusText));
                        OnPropertyChanged(nameof(StatusColor));
                    }

                    if (_shouldStop) break;

                    var stableDuration = _sessionConnectedAtUtc == DateTime.MinValue
                        ? TimeSpan.Zero
                        : DateTime.UtcNow - _sessionConnectedAtUtc;
                    if (stableDuration >= TimeSpan.FromSeconds(30))
                    {
                        _circuitBreaker.RecordSuccess();
                        retryAttempt = 0;
                    }

                    var circuitState = _circuitBreaker.RecordFailure();
                    retryAttempt++;
                    var retryDelay = circuitState == StreamingCircuitState.Open
                        ? _circuitBreaker.OpenDuration
                        : TimeSpan.FromSeconds(Math.Min(1 << Math.Min(retryAttempt - 1, 5), 30));

                    OnPropertyChanged(nameof(CircuitBreakerState));
                    OnPropertyChanged(nameof(ConsecutiveConnectionFailures));
                    StatusMessage = circuitState == StreamingCircuitState.Open
                        ? $"Circuit breaker abierto tras {_circuitBreaker.ConsecutiveFailures} fallos. Reintento en 30 s."
                        : $"Streaming interrumpido. Reintento automático en {retryDelay.TotalSeconds:0} s.";
                    Log.Warning("Streaming fallido ({FailureCount}); circuito {CircuitState}, reintento en {RetryDelaySeconds}s",
                        _circuitBreaker.ConsecutiveFailures, circuitState, retryDelay.TotalSeconds);

                    if (WaitForRetry(retryDelay)) break;
                }
            }
            catch (Exception exception)
            {
                Log.Fatal(exception, "El worker de streaming terminó de forma inesperada");
                StatusMessage = UserErrorMessages.For(exception, "mantener el streaming");
            }
            finally
            {
                CleanupConnection();
                _isSessionConnected = false;
                Application.Current.Dispatcher.Invoke(() => IsStreaming = false);
                StatusMessage = "Desconectado";
                StreamingStateChanged?.Invoke(this, EventArgs.Empty);
                Log.Information("Worker de streaming detenido");
            }
        }

        private void StreamingWorkerAttempt()
        {
            StatusMessage = "Conectando al servidor...";
            if (!ConnectToIcecast())
            {
                _sessionFailed = true;
                return;
            }

            StatusMessage = "Enviando autenticación...";
            if (!SendIcecastAuthentication())
            {
                _sessionFailed = true;
                return;
            }

            StatusMessage = "Conectado - Iniciando captura de audio...";
            var capture = new WasapiRecorderBuilder()
                .WithLoopbackCapture()
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, _channels))
                .Build();
            _loopbackCapture = capture;
            var captureFormat = capture.WaveFormat;
            _waveFormat = captureFormat;
            var ffmpegArgs = $"-f f32le -ar {_waveFormat.SampleRate} -ac {_waveFormat.Channels} -i pipe:0 -f mp3 -b:a {_bitrate}k -acodec libmp3lame pipe:1";
            var ffmpegStartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!File.Exists(ffmpegStartInfo.FileName))
            {
                throw new FileNotFoundException("No se encontró el ejecutable de FFmpeg.", ffmpegStartInfo.FileName);
            }

            _ffmpegProcess = new Process { StartInfo = ffmpegStartInfo };
            _ffmpegProcess.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    Log.Debug("FFmpeg: {FfmpegMessage}", args.Data);
                }
            };
            _ffmpegProcess.Start();
            _ffmpegProcess.BeginErrorReadLine();
            var ffmpegProcess = _ffmpegProcess;
            var outputStream = _writeStream ?? throw new IOException("La conexión a Icecast no está disponible.");
            var captureBuffer = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true
            });
            var silenceBuffer = new byte[Math.Max(captureFormat.AverageBytesPerSecond / 10, captureFormat.BlockAlign)];
            _captureBuffer = captureBuffer;
            _captureWriterThread = new Thread(() => WriteCaptureBuffer(captureBuffer.Reader, ffmpegProcess, silenceBuffer))
            {
                IsBackground = true,
                Name = "IcecastAudioInputThread"
            };
            _captureWriterThread.Start();

            var ffmpegReaderThread = new Thread(() =>
            {
                try
                {
                    var buffer = new byte[4096];
                    using var ffmpegOutput = ffmpegProcess.StandardOutput.BaseStream;
                    while (!_shouldStop && !ffmpegProcess.HasExited)
                    {
                        var bytesRead = ffmpegOutput.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;
                        outputStream.Write(buffer, 0, bytesRead);
                        Interlocked.Add(ref _bytesSent, bytesRead);
                    }

                    if (!_shouldStop)
                    {
                        _sessionFailed = true;
                        Log.Warning("La salida de FFmpeg terminó inesperadamente (código {ExitCode})", ffmpegProcess.ExitCode);
                    }
                }
                catch (Exception exception)
                {
                    _sessionFailed = true;
                    Log.Error(exception, "Falló la lectura/envío de audio al servidor Icecast");
                    Application.Current.Dispatcher.BeginInvoke(() =>
                        StatusMessage = UserErrorMessages.For(exception, "enviar el audio a Icecast"));
                }
            })
            {
                IsBackground = true,
                Name = "FFmpegReaderThread"
            };
            ffmpegReaderThread.Start();

            capture.DataAvailable += (buffer, _, _, _) =>
            {
                if (_shouldStop) return;
                var packet = buffer.ToArray();
                if (!captureBuffer.Writer.TryWrite(packet))
                {
                    captureBuffer.Reader.TryRead(out _);
                    captureBuffer.Writer.TryWrite(packet);
                    var overruns = Interlocked.Increment(ref _bufferOverrunCount);
                    OnPropertyChanged(nameof(BufferOverrunCount));
                    if (overruns == 1 || overruns % 100 == 0)
                    {
                        Log.Warning("El buffer de streaming descartó paquetes de audio; total {OverrunCount}", overruns);
                        Application.Current.Dispatcher.BeginInvoke(() =>
                            StatusMessage = $"Buffer saturado; paquetes descartados: {overruns}");
                    }
                }
            };
            capture.RecordingStopped += (_, args) =>
            {
                if (!_shouldStop)
                {
                    _sessionFailed = true;
                    Log.Warning(args.Exception, "La captura WASAPI terminó inesperadamente");
                }
            };

            capture.StartRecording();
            _sessionConnectedAtUtc = DateTime.UtcNow;
            _isSessionConnected = true;
            ConnectionTime = DateTime.Now;
            StreamDuration = TimeSpan.Zero;
            OnPropertyChanged(nameof(StreamingStatusText));
            OnPropertyChanged(nameof(StatusColor));
            StatusMessage = $"Streaming MP3 {_bitrate} kbps activo";
            StreamingStateChanged?.Invoke(this, EventArgs.Empty);
            Log.Information("Conexión Icecast activa en {ServerUrl}:{ServerPort}{MountPoint}", ServerUrl, ServerPort, MountPoint);

            var stabilityRecorded = false;
            while (!_shouldStop && !_sessionFailed)
            {
                Thread.Sleep(1000);
                StreamDuration = DateTime.Now - ConnectionTime;
                if (!stabilityRecorded && DateTime.UtcNow - _sessionConnectedAtUtc >= TimeSpan.FromSeconds(30))
                {
                    _circuitBreaker.RecordSuccess();
                    stabilityRecorded = true;
                    OnPropertyChanged(nameof(CircuitBreakerState));
                    OnPropertyChanged(nameof(ConsecutiveConnectionFailures));
                    Log.Information("Streaming estable; circuit breaker cerrado");
                }

                if (ffmpegProcess.HasExited)
                {
                    _sessionFailed = true;
                    Log.Warning("FFmpeg terminó con código {ExitCode}", ffmpegProcess.ExitCode);
                }
            }

            capture.StopRecording();
            captureBuffer.Writer.TryComplete();
            if (!_captureWriterThread.Join(3000))
            {
                try { ffmpegProcess.Kill(entireProcessTree: true); } catch (Exception exception) { Log.Debug(exception, "No se pudo finalizar FFmpeg durante el cierre del intento"); }
                _captureWriterThread.Join(1000);
            }
            _captureWriterThread = null;
            _captureBuffer = null;

            if (!ffmpegProcess.HasExited)
            {
                ffmpegProcess.StandardInput.Close();
                ffmpegProcess.WaitForExit(3000);
            }
        }

        private bool WaitForRetry(TimeSpan delay)
        {
            var remaining = delay;
            while (!_shouldStop && remaining > TimeSpan.Zero)
            {
                var wait = (int)Math.Min(250, Math.Max(1, remaining.TotalMilliseconds));
                Thread.Sleep(wait);
                remaining -= TimeSpan.FromMilliseconds(wait);
            }

            return _shouldStop;
        }

        private bool ConnectToIcecast()
        {
            try
            {
                StatusMessage = $"Intentando conectar a {_serverUrl}:{_serverPort}...";
                
                // Extraer hostname si es una URL completa
                string host = _serverUrl;
                if (host.StartsWith("http://") || host.StartsWith("https://"))
                {
                    var uri = new Uri(host);
                    host = uri.Host;
                }

                Log.Information("Intentando conexión Icecast con {Host}:{Port}", host, _serverPort);
                _tcpClient = new TcpClient();
                _tcpClient.ConnectAsync(host, _serverPort).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                _tcpClient.SendTimeout = 3000;
                _tcpClient.ReceiveTimeout = 5000;
                _tcpClient.NoDelay = true;
                _networkStream = _tcpClient.GetStream();
                _writeStream = _networkStream;
                StatusMessage = $"Conexión TCP establecida con {host}:{_serverPort}";
                Log.Information("Conexión TCP Icecast establecida con {Host}:{Port}", host, _serverPort);
                return true;
            }
            catch (SocketException ex)
            {
                Log.Warning(ex, "Falló la conexión TCP con Icecast en {ServerUrl}:{ServerPort}", _serverUrl, _serverPort);
                StatusMessage = $"No se pudo conectar a {_serverUrl}:{_serverPort}. Comprueba el servidor y la red.";
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error al establecer conexión Icecast con {ServerUrl}:{ServerPort}", _serverUrl, _serverPort);
                StatusMessage = UserErrorMessages.For(ex, "conectar con el servidor Icecast");
                return false;
            }
        }

        private bool SendIcecastAuthentication()
        {
            try
            {
                if (_writeStream == null) return false;

                // Construir el encabezado de autenticación Icecast (HTTP PUT)
                string authHeader = $"PUT {MountPoint} HTTP/1.0\r\n";
                authHeader += $"Content-Type: audio/mpeg\r\n";
                authHeader += $"Authorization: Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes("source:" + Password))}\r\n";
                authHeader += $"ice-name: {StationName}\r\n";
                authHeader += $"ice-genre: {StationGenre}\r\n";
                authHeader += $"ice-description: {StationDescription}\r\n";
                authHeader += $"ice-url: {StationUrl}\r\n";
                authHeader += $"ice-public: 0\r\n";
                authHeader += $"ice-bitrate: {Bitrate}\r\n";
                authHeader += "\r\n";

                byte[] authBytes = Encoding.ASCII.GetBytes(authHeader);
                _writeStream.Write(authBytes, 0, authBytes.Length);
                _writeStream.Flush();

                // Esperar respuesta del servidor y leerla
                Thread.Sleep(1000);

                // Leer respuesta del servidor
                byte[] responseBuffer = new byte[1024];
                int bytesRead = _networkStream?.Read(responseBuffer, 0, responseBuffer.Length) ?? 0;

                if (bytesRead > 0)
                {
                    string response = Encoding.ASCII.GetString(responseBuffer, 0, bytesRead);
                    StatusMessage = $"Respuesta servidor: {response.Split('\r')[0]}";

                    // Verificar si la autenticación fue exitosa
                    if (response.Contains("200 OK") || response.Contains("HTTP/1.0 200"))
                    {
                        Log.Information("Icecast aceptó el mountpoint {MountPoint}", MountPoint);
                        return true;
                    }
                    else
                    {
                        Log.Warning("Icecast rechazó el mountpoint {MountPoint}: {ResponseLine}", MountPoint, response.Split('\r')[0]);
                        StatusMessage = "Icecast rechazó la autenticación o el mountpoint. Comprueba la contraseña y la configuración.";
                        return false;
                    }
                }

                Log.Warning("Icecast cerró la conexión antes de responder a la autenticación");
                StatusMessage = "El servidor cerró la conexión durante la autenticación.";
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Falló la autenticación con Icecast para {MountPoint}", MountPoint);
                StatusMessage = "No se pudo completar la autenticación con Icecast.";
                return false;
            }
        }

        private void CleanupConnection()
        {
            var capture = _loopbackCapture;
            _loopbackCapture = null;
            if (capture != null)
            {
                try { capture.StopRecording(); } catch { }
                try { capture.Dispose(); } catch { }
            }

            _captureBuffer?.Writer.TryComplete();
            try { _writeStream?.Close(); } catch { }
            try { _networkStream?.Close(); } catch { }
            try { _tcpClient?.Close(); } catch { }

            if (_captureWriterThread?.IsAlive == true && _captureWriterThread != Thread.CurrentThread && !_captureWriterThread.Join(1500))
            {
                try { _ffmpegProcess?.Kill(entireProcessTree: true); } catch { }
                _captureWriterThread.Join(1000);
            }
            _captureWriterThread = null;
            _captureBuffer = null;

            if (_ffmpegProcess != null)
            {
                try
                {
                    if (!_ffmpegProcess.HasExited)
                    {
                        try { _ffmpegProcess.StandardInput.Close(); } catch { }
                        if (!_ffmpegProcess.WaitForExit(1000))
                        {
                            _ffmpegProcess.Kill(entireProcessTree: true);
                        }
                    }
                }
                catch { }
                _ffmpegProcess.Dispose();
                _ffmpegProcess = null;
            }

            _writeStream = null;
            _networkStream = null;
            _tcpClient = null;
        }

        private void WriteCaptureBuffer(ChannelReader<byte[]> reader, Process ffmpegProcess, byte[] silenceBuffer)
        {
            Task<bool>? pendingRead = null;
            try
            {
                while (true)
                {
                    while (reader.TryRead(out var chunk))
                    {
                        if (ffmpegProcess.HasExited)
                        {
                            return;
                        }

                        ffmpegProcess.StandardInput.BaseStream.Write(chunk);
                    }

                    if (_shouldStop)
                    {
                        break;
                    }

                    pendingRead ??= reader.WaitToReadAsync().AsTask();
                    if (pendingRead.Wait(100))
                    {
                        var hasData = pendingRead.Result;
                        pendingRead = null;
                        if (!hasData)
                        {
                            break;
                        }
                    }
                    else if (!_shouldStop)
                    {
                        ffmpegProcess.StandardInput.BaseStream.Write(silenceBuffer);
                        var underruns = Interlocked.Increment(ref _bufferUnderrunCount);
                        OnPropertyChanged(nameof(BufferUnderrunCount));
                        if (underruns == 1 || underruns % 100 == 0)
                        {
                            Application.Current.Dispatcher.BeginInvoke(() =>
                                StatusMessage = $"Falta de audio temporal; silencios insertados: {underruns}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _sessionFailed = true;
                Log.Error(ex, "Error al escribir audio en la entrada de FFmpeg");
                Application.Current.Dispatcher.BeginInvoke(() =>
                    StatusMessage = UserErrorMessages.For(ex, "enviar audio al encoder"));
            }
            finally
            {
                try { ffmpegProcess.StandardInput.Close(); } catch { }
            }
        }

        public void UpdateMetadata(string artist, string title)
        {
            if (!IsStreaming || _writeStream == null) return;

            try
            {
                string metadata = $"StreamTitle='{artist} - {title}';StreamUrl='';";
                byte[] metadataBytes = Encoding.ASCII.GetBytes(metadata);

                // Formato de metadata de Icecast
                int length = (metadataBytes.Length + 15) / 16; // Redondear al siguiente múltiplo de 16
                byte[] lengthByte = new byte[] { (byte)length };

                _writeStream.Write(lengthByte, 0, 1);
                _writeStream.Write(metadataBytes, 0, metadataBytes.Length);

                // Rellenar con ceros si es necesario
                int padding = (length * 16) - metadataBytes.Length;
                if (padding > 0)
                {
                    byte[] paddingBytes = new byte[padding];
                    _writeStream.Write(paddingBytes, 0, paddingBytes.Length);
                }

                _writeStream.Flush();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error actualizando metadata: {ex.Message}";
            }
        }

        public void Dispose()
        {
            StopStreaming();
        }
    }
}