using System.Windows;
using Serilog;

namespace quasar_radio;

public partial class LoginWindow : Window
{
    private readonly AuthService _authService;

    public LoginWindow(AuthService authService)
    {
        Log.Information("LoginWindow: constructor iniciado.");
        _authService = authService;
        InitializeComponent();
        Log.Information("LoginWindow: InitializeComponent completado.");

        var isFirstRun = !_authService.HasUsers;
        SubtitleText.Text = isFirstRun
            ? "Configuración inicial. Crea la cuenta administradora para proteger esta instalación."
            : "Inicia sesión para abrir el estudio.";
        ConfirmPanel.Visibility = isFirstRun ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            Log.Information("LoginWindow: evento Loaded disparado.");
            Activate();
            Focus();
            UsernameBox.Focus();
        };
    }

    private void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (!_authService.HasUsers)
        {
            if (!string.Equals(password, ConfirmPasswordBox.Password, System.StringComparison.Ordinal))
            {
                ErrorText.Text = "Las contraseñas no coinciden.";
                return;
            }

            if (!_authService.CreateInitialAdmin(username, password, out var setupError))
            {
                ErrorText.Text = setupError;
                return;
            }
        }

        if (!_authService.Login(username, password, out var loginError))
        {
            ErrorText.Text = loginError;
            return;
        }

        DialogResult = true;
    }
}
