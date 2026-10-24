using System;

namespace quasar_radio
{
    public class StreamingService : ViewModelBase
    {
        private bool _isStreamingActive;
        public bool IsStreamingActive
        {
            get => _isStreamingActive;
            set
            {
                _isStreamingActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StreamingStatusText));
                OnPropertyChanged(nameof(StreamingButtonText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public string StreamingStatusText => IsStreamingActive ? "EMITIENDO EN VIVO (ICECAST)" : "STREAMING OFF";
        public string StreamingButtonText => IsStreamingActive ? "⏹ DESCONECTAR STREAM" : "📡 CONECTAR STREAM";
        public string StatusColor => IsStreamingActive
            ? ThemeHex.Of("Brush.Accent", "#00E5FF")
            : ThemeHex.Of("Brush.Border.Default", "#262D3D");

        private string _serverUrl = "http://stream.quasar-radio.com:8000";
        public string ServerUrl
        {
            get => _serverUrl;
            set { _serverUrl = value; OnPropertyChanged(); }
        }

        private string _mountPoint = "/live";
        public string MountPoint
        {
            get => _mountPoint;
            set { _mountPoint = value; OnPropertyChanged(); }
        }

        private string _password = "admin";
        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        private string _stationName = "Quasar Radio Pro - Emisora Digital";
        public string StationName
        {
            get => _stationName;
            set { _stationName = value; OnPropertyChanged(); }
        }

        public void ToggleStreaming()
        {
            IsStreamingActive = !IsStreamingActive;
        }

        // UI-9: el color del chip de estado se resuelve contra el tema activo, y hay que
        // re-avisar cuando la piel cambie con el programa abierto.
        public StreamingService()
        {
            ThemeRefresh.Bind(this, nameof(StatusColor));
        }
    }
}
