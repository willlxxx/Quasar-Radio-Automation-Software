using System;
using System.Windows;
using System.Windows.Media;
using Serilog;

namespace quasar_radio;

/// <summary>
/// AUTH-1: esta ventana se abre SIN clave. Ahi es donde se crean las cuentas con nivel
/// (administrador / operador / espectador), que son las que despues firman una accion
/// atribuida. El unico permiso que se sigue pidiendo es crear una cuenta cuando ya
/// existen: ahi sale el dialogo de identificacion, y si quien se identifica no es
/// administrador, la creacion no se hace.
/// </summary>
public partial class UserManagementWindow : Window
{
    private readonly AuthService _authService;

    public UserManagementWindow(AuthService authService)
    {
        _authService = authService;
        InitializeComponent();
        RoleBox.ItemsSource = new[] { UserRole.Admin, UserRole.Operator, UserRole.Viewer };

        // AUTH-1: el nivel por defecto acompana al momento de la instalacion. Sin cuentas
        // creadas la primera tiene que poder ser de administrador (es quien manda despues);
        // habiendo ya cuentas, el defecto pasa a Operador: nadie crea un administrador por
        // dejar el combo donde esta.
        RoleBox.SelectedIndex = _authService.HasUsers ? 1 : 0;
        _authService.SessionChanged += OnSessionChanged;
        Closed += (_, _) => _authService.SessionChanged -= OnSessionChanged;
        RefreshUsers();
    }

    private void OnSessionChanged(object? sender, EventArgs e) => RefreshSession();

    private void RefreshSession()
    {
        // Corta a proposito: vive en la cabecera, junto al boton de identificarse, y el texto
        // largo recortaba el boton en una ventana de 620 px.
        SessionText.Text = _authService.IsAuthenticated
            ? $"Sesión: {_authService.CurrentUserName} · {_authService.CurrentUserRole}"
            : "Sesión: invitado (las cuentas definen quién manda)";
        IdentifyButton.Visibility = _authService.IsAuthenticated ? Visibility.Collapsed : Visibility.Visible;
        LogoutButton.Visibility = _authService.IsAuthenticated ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshUsers()
    {
        UsersGrid.ItemsSource = _authService.Users;
        RefreshSession();
    }

    /// <summary>Identificarse sin hacer otra cosa: para que un nivel quede apuntado en la consola.</summary>
    private void OnIdentifyClick(object sender, RoutedEventArgs e)
    {
        var login = new LoginWindow(_authService) { Owner = this };
        if (login.ShowDialog() == true)
        {
            Say($"Identificado como {App.Auth.CurrentUserName} · nivel {App.Auth.CurrentUserRole}", good: true);
        }
        RefreshUsers();
    }

    private void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        _authService.Logout();
        Say("Sesión cerrada. Vuelves a operar como invitado.", good: true);
        RefreshUsers();
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (RoleBox.SelectedItem is not UserRole role)
        {
            Say("Selecciona un nivel válido.");
            return;
        }

        // El sello por nivel, en el momento del clic: sin cuentas todavia se puede crear la
        // primera (es la unica forma de arrancar la instalacion sin login previo); con
        // cuentas creadas, hace falta una sesion de administrador.
        if (_authService.HasUsers && !AuthGate.Admin())
        {
            Say("Sin sesión de administrador no se pueden crear más cuentas.");
            return;
        }

        if (!_authService.CreateUser(UsernameBox.Text, PasswordBox.Password, role, out var error))
        {
            Say(error);
            return;
        }

        Say($"Cuenta creada con nivel {role}.", good: true);
        UsernameBox.Clear();
        PasswordBox.Clear();

        // Si la ventana se abrio antes de que existiera nadie, el combo aun ofrece Admin.
        // Habiendo ya cuentas, el siguiente defecto baja a Operador: un administrador nuevo
        // se elige a proposito, no por no haber movido el combo.
        if (_authService.HasUsers && RoleBox.SelectedIndex == 0) RoleBox.SelectedIndex = 1;

        RefreshUsers();
    }

    /// <summary>Estado en el pie. El color sale de los tokens del tema, nunca de un pincel fijo.</summary>
    private void Say(string message, bool good = false)
    {
        var key = good ? "Brush.Accent.Success" : "Brush.Accent.Coral";
        StatusText.Foreground = TryFindResource(key) as Brush
            ?? SystemColors.ControlBrush;
        StatusText.Text = message;
        Log.Information("Ventana de usuarios: {Message}", message);
    }
}
