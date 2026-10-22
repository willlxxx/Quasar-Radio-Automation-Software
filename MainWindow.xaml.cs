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
            if (!App.Auth.IsAuthorized(UserRole.Admin))
            {
                var loginWindow = new LoginWindow(App.Auth) { Owner = this };
                var dialogResult = loginWindow.ShowDialog();

                if (dialogResult != true || !App.Auth.IsAuthorized(UserRole.Admin))
                {
                    return;
                }
            }

            UpdateUserDisplay();
            new UserManagementWindow(App.Auth) { Owner = this }.ShowDialog();
            UpdateUserDisplay();
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