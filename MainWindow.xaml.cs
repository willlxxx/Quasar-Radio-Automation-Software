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

            // AUTH-1: quien se identifica desde el pie, desde la ventana de usuarios o desde
            // el sello de una operacion (AuthGate) tiene que verse reflejado al momento en el
            // pie de la consola, sin reabrir nada.
            App.Auth.SessionChanged += (_, _) => Dispatcher.BeginInvoke(UpdateUserDisplay);
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

            // AUTH-1: INVITADO es un estado normal, no un error. Se opera sin identificarse
            // (reproducir, encolar, curar la biblioteca, cambiar de tema); la identificacion
            // aparece cuando hace falta un nivel. Por eso el boton de identificarse se muestra
            // solo mientras no haya sesion, y el de cerrar solo cuando la hay.
            ManageUsersButton.Visibility = Visibility.Visible;
            IdentifyButton.Visibility = App.Auth.IsAuthenticated ? Visibility.Collapsed : Visibility.Visible;
            LogoutButton.Visibility = App.Auth.IsAuthenticated ? Visibility.Visible : Visibility.Collapsed;

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.RefreshAuthorizationState();
            }
        }

        /// <summary>
        /// Identificarse por voluntad propia, sin que lo exija una operacion. Acepta
        /// cualquier nivel: un operador que se registra ve como sus acciones quedan atribuidas.
        /// </summary>
        private void OnIdentifyClick(object sender, RoutedEventArgs e)
        {
            var login = new LoginWindow(App.Auth) { Owner = this };
            login.ShowDialog();
            UpdateUserDisplay();
        }

        private void OnManageUsersClick(object sender, RoutedEventArgs e)
        {
            // AUTH-1: la ventana de usuarios se abre SIN clave. Es el sitio donde se crean
            // las cuentas de nivel (administrador / operador), y si estuviera detras de un
            // login no se podria crear la primera. Lo que si lleva sello por nivel es crear
            // la segunda cuenta en adelante: UserManagementWindow usa AuthGate.
            var navigator = App.Services.GetRequiredService<IModuleNavigator>();

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