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
        // AUTH-1: este dialogo ya no se abre al arrancar la emisora. Se abre en el momento
        // en que el operador pulsa una accion de administrador (AuthGate), o desde la
        // ventana de usuarios para identificarse. Por eso el texto habla de la accion, no
        // de "entrar al programa".
        Title = isFirstRun ? "Quasar Radio | Primera cuenta" : "Quasar Radio | Identifícate para esta acción";
        SubtitleText.Text = isFirstRun
            ? "Esta instalación todavía no tiene cuentas. Crea la primera para poder atribuir las operaciones."
            : "Esta operación cambia el estado compartido. Identifica a quién se le apunta.";
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

    /// <summary>AUTH-1: cancelar aqui no cierra nada del programa; solo devuelve la accion.</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Log.Information("LoginWindow: el usuario volvio sin identificarse; la accion no se hace.");
        DialogResult = false;
    }
}
