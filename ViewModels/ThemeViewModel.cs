using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Serilog;

namespace quasar_radio
{
    public class ThemeViewModel : ViewModelBase
    {
        private readonly ILogger _logger = Log.ForContext<ThemeViewModel>();

        private string _stationLogoPath = string.Empty;
        public string StationLogoPath
        {
            get => _stationLogoPath;
            set
            {
                _stationLogoPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomLogo));
            }
        }

        public bool HasCustomLogo => !string.IsNullOrEmpty(StationLogoPath) && File.Exists(StationLogoPath);

        private string _selectedTheme = "Studio Dark Neon";
        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                _selectedTheme = value;
                OnPropertyChanged();
                ApplyTheme(value);
            }
        }

        public List<string> AvailableThemes { get; } = new List<string>
        {
            "Studio Dark Neon",
            "Cyan Broadcast",
            "Charcoal Pro",
            "Deep Navy FM"
        };

        private string _windowBackground = "#0F111A";
        public string WindowBackground
        {
            get => _windowBackground;
            set { _windowBackground = value; OnPropertyChanged(); }
        }

        private string _cardBackground = "#161B26";
        public string CardBackground
        {
            get => _cardBackground;
            set { _cardBackground = value; OnPropertyChanged(); }
        }

        private string _accentColor = "#00E676";
        public string AccentColor
        {
            get => _accentColor;
            set { _accentColor = value; OnPropertyChanged(); }
        }

        public ICommand ChangeStationLogoCommand { get; }

        public ThemeViewModel()
        {
            ChangeStationLogoCommand = new RelayCommand(o => ChangeStationLogo(), o => App.Auth.IsAuthorized(UserRole.Admin));
        }

        private void ApplyTheme(string themeName)
        {
            switch (themeName)
            {
                case "Cyan Broadcast":
                    WindowBackground = "#0A192F";
                    CardBackground = "#112240";
                    AccentColor = "#64FFDA";
                    break;
                case "Charcoal Pro":
                    WindowBackground = "#181818";
                    CardBackground = "#242424";
                    AccentColor = "#FF9800";
                    break;
                case "Deep Navy FM":
                    WindowBackground = "#0D1B2A";
                    CardBackground = "#1B263B";
                    AccentColor = "#48CAE4";
                    break;
                case "Studio Dark Neon":
                default:
                    WindowBackground = "#0F111A";
                    CardBackground = "#161B26";
                    AccentColor = "#00E676";
                    break;
            }
        }

        private void ChangeStationLogo()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de Imagen (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Todos los archivos (*.*)|*.*",
                Title = "Seleccionar Logo de la Emisora"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                StationLogoPath = openFileDialog.FileName;
                _logger.Information("Logo de estación cambiado: {Path}", StationLogoPath);
            }
        }
    }
}
