using System;
using System.Windows;

namespace quasar_radio;

public partial class UserManagementWindow : Window
{
    private readonly AuthService _authService;

    public UserManagementWindow(AuthService authService)
    {
        _authService = authService;
        InitializeComponent();
        RoleBox.ItemsSource = new[] { UserRole.Operator, UserRole.Viewer };
        RoleBox.SelectedIndex = 0;
        RefreshUsers();
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (RoleBox.SelectedItem is not UserRole role)
        {
            StatusText.Text = "Selecciona un rol válido.";
            return;
        }

        if (!_authService.CreateUser(UsernameBox.Text, PasswordBox.Password, role, out var error))
        {
            StatusText.Text = error;
            return;
        }

        StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        StatusText.Text = "Cuenta creada.";
        UsernameBox.Clear();
        PasswordBox.Clear();
        RefreshUsers();
    }

    private void RefreshUsers() => UsersGrid.ItemsSource = _authService.Users;
}
