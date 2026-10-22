using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace quasar_radio
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
            UpdateUserDisplay();
        }

        /// <summary>
        /// CenterScreen ignora la barra de tareas y los monitores con origen negativo,
        /// y dejaba la barra de titulo de la consola (900 px) fuera del panel de 900 px.
        /// Aquí se encaja la ventana en el area util del monitor que la recibe.
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowFitting.FitToWorkArea(this);
        }

        /// <summary>
        /// Unica ventana que puede apagar la aplicacion (ShutdownMode.OnMainWindowClose).
        /// Cerrar un modulo no cierra Quasar; cerrar Quasar con aire activo si pregunta.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.IsAirActive())
            {
                var answer = MessageBox.Show(
                    "Hay reproducción, micrófono, grabación o streaming en marcha.\n\n¿Cerrar Quasar Radio de todos modos?",
                    "Aire activo",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (answer != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }

        private void UpdateUserDisplay()
        {
            CurrentUserText.Text = App.Auth.IsAuthenticated
                ? $"{App.Auth.CurrentUserName} | {App.Auth.CurrentUserRole}"
                : "Invitado";

            ManageUsersButton.Visibility = Visibility.Visible;
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.RefreshAuthorizationState();
            }
        }

        private void OnManageUsersClick(object sender, RoutedEventArgs e)
        {
            var navigator = App.Services.GetRequiredService<IModuleNavigator>();
            if (!navigator.EnsureAdmin()) return;

            // Refresca IsAdmin en los comandos antes de abrir: la ventana de usuarios
            // ya no es modal, asi que lo que cambie aqui se ve en la consola al momento.
            UpdateUserDisplay();
            navigator.Open(ModuleKey.Users);
        }

        private void OnLogoutClick(object sender, RoutedEventArgs e)
        {
            App.Auth.Logout();
            UpdateUserDisplay();
        }

        private void OnPlaylistDrop(object sender, DragEventArgs e)
        {
            if (App.Auth.IsAuthorized(UserRole.Operator) && DataContext is MainViewModel vm)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null)
                    {
                        foreach (string file in files)
                        {
                            if (AudioSourceFactory.IsSupported(file))
                            {
                                vm.Playlist.Add(TrackLoader.CreateFromFile(file));
                            }
                        }
                    }
                }
            }
        }
    }
}