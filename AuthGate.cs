using System.Windows;
using Serilog;

namespace quasar_radio;

/// <summary>
/// AUTH-1 (2026-10-22, decisión del fundador): el programa NO pide clave para usarse.
/// Ninguna ventana de modulo se abre ya tras un login, y el arranque es directo. La
/// contraseña queda solo para <b>atribuir</b> acciones por nivel: cuando el operador
/// pulsa una operacion de administrador, se le pide aqui mismo que se identifique, y
/// la accion se hace (o no) segun el rol de la cuenta que escribio.
///
/// Es el mismo dialogo de siempre (<see cref="LoginWindow"/>), pero cambiado de sitio:
/// antes era una puerta por delante de la ventana, ahora es un sello por detras de la
/// accion. Diferencia practica en cabina: un invitado puede abrir Biblioteca, curar su
/// musica, encolar y regenerar la auto-playlist; lo que no puede es borrar tracks de la
/// base de datos sin decir quien es.
///
/// Idempotente: si la sesion abierta ya tiene el rol, no vuelve a preguntar, asi que
/// encadenar diez operaciones de administrador cuesta una sola identificacion.
/// </summary>
public static class AuthGate
{
    /// <summary>Pide identificacion si hace falta y dice si el nivel queda cubierto.</summary>
    public static bool Ensure(UserRole required)
    {
        if (App.Auth.IsAuthorized(required)) return true;

        var owner = Application.Current?.MainWindow;
        var login = new LoginWindow(App.Auth)
        {
            Owner = owner
        };

        var accepted = login.ShowDialog() == true;
        if (!accepted || App.Auth.CurrentUserRole is not { } role || role < required)
        {
            Log.Information(
                "Accion sin permiso: hace falta {Role} y no se identifico nadie (sesion actual: {Session})",
                required,
                App.Auth.IsAuthenticated ? $"{App.Auth.CurrentUserName}/{App.Auth.CurrentUserRole}" : "invitado");
            return false;
        }

        Log.Information("Accion atribuida a {User} con rol {Role}", App.Auth.CurrentUserName, role);
        return true;
    }

    /// <summary>Operaciones que cambian el estado compartido: base de datos, usuarios, estadisticas.</summary>
    public static bool Admin() => Ensure(UserRole.Admin);

    /// <summary>Operaciones de emision: reproducir, encolar, tocar el aire.</summary>
    public static bool Operator() => Ensure(UserRole.Operator);
}
