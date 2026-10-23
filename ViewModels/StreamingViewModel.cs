using System;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using Serilog;

namespace quasar_radio
{
    public class StreamingViewModel : ViewModelBase
    {
        private readonly ILogger _logger = Log.ForContext<StreamingViewModel>();
        private readonly IcecastStreamingService _streamingService;

        public IcecastStreamingService StreamingService => _streamingService;

        private string _currentNowPlayingText = "Quasar Radio Pro - En Vivo";
        public string CurrentNowPlayingText
        {
            get => _currentNowPlayingText;
            set
            {
                _currentNowPlayingText = value;
                OnPropertyChanged();
            }
        }

        private string _statusText = "ESTADO: LISTO";
        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public ICommand ToggleStreamingCommand { get; }
        public ICommand TestIcecastConnectionCommand { get; }

        public StreamingViewModel(IcecastStreamingService streamingService)
        {
            _streamingService = streamingService ?? throw new ArgumentNullException(nameof(streamingService));

            bool CanOperate() => App.Auth.IsAuthorized(UserRole.Operator);

            // AUTH-1: PROBAR la conexion es una lectura (conecta, pregunta y se va), y un
            // operador que ve el aire caido tiene que poder comprobarlo sin buscar la clave
            // del jefe. Bajar al aire (ToggleStreaming) ya era de operador. Lo que sigue
            // pidiendo nivel de administrador es tocar la configuracion: URL, puerto, mount,
            // contrasena del Icecast y ruta de ffmpeg, que van gated en StreamingWindow.xaml.
            ToggleStreamingCommand = new RelayCommand(o => ToggleStreaming(), o => CanOperate());
            TestIcecastConnectionCommand = new RelayCommand(o => TestIcecastConnection(), o => CanOperate());
        }

        private void ToggleStreaming()
        {
            if (_streamingService.IsStreaming)
            {
                _streamingService.StopStreaming();
                StatusText = "Streaming desconectado";
                _logger.Information("Streaming desconectado");
            }
            else
            {
                if (_streamingService.StartStreaming())
                {
                    StatusText = "Iniciando conexión Icecast...";
                    _logger.Information("Iniciando streaming Icecast");
                }
                else
                {
                    StatusText = "Error al iniciar streaming";
                    _logger.Warning("Error al iniciar streaming");
                }
            }
        }

        private void TestIcecastConnection()
        {
            try
            {
                StatusText = $"Probando conexión a {_streamingService.ServerUrl}:{_streamingService.ServerPort}...";

                using var tcpClient = new TcpClient();
                try
                {
                    tcpClient.Connect(_streamingService.ServerUrl, _streamingService.ServerPort);
                    StatusText = "✅ Conexión TCP exitosa con servidor Icecast";
                    _logger.Information("Conexión TCP exitosa con Icecast");
                }
                catch (SocketException ex)
                {
                    StatusText = $"❌ Error de conexión: {ex.Message}. Verifique que Icecast esté corriendo.";
                    _logger.Warning(ex, "Error de conexión TCP con Icecast");
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Error al probar conexión: {ex.Message}";
                _logger.Error(ex, "Error al probar conexión Icecast");
            }
        }

        public void UpdateMetadata(string artist, string title)
        {
            CurrentNowPlayingText = $"{artist} - {title}";
            if (_streamingService.IsStreaming)
            {
                _streamingService.UpdateMetadata(artist, title);
            }
        }
    }
}
