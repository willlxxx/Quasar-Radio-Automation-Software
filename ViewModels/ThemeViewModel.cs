using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using Serilog;

namespace quasar_radio
{
    /// <summary>
    /// ViewModel de la barra de apariencia: logo de estación y tema activo.
    ///
    /// El tema real lo gobierna <see cref="ThemeService"/> sustituyendo el
    /// diccionario de recursos de la aplicación. Como las vistas consumen los
    /// brushes por DynamicResource, cambiar de tema refresca todas las ventanas
    /// abiertas sin reconstruir nada y sin duplicar la paleta aquí.
    /// </summary>
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

        private string _selectedTheme = ThemeService.CurrentTheme;
        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (string.Equals(_selectedTheme, value, StringComparison.OrdinalIgnoreCase)) return;
                _selectedTheme = value;
                OnPropertyChanged();
                ThemeService.Apply(value);
            }
        }

        public IReadOnlyList<string> AvailableThemes => ThemeService.AvailableThemes;

        public ICommand ChangeStationLogoCommand { get; }

        public ThemeViewModel()
        {
            ChangeStationLogoCommand = new RelayCommand(o => ChangeStationLogo(), o => App.Auth.IsAuthorized(UserRole.Admin));
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
