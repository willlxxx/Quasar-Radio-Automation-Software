using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Serilog;

namespace quasar_radio
{
    public enum UserRole
    {
        Viewer,
        Operator,
        Admin
    }

    public sealed class UserAccount
    {
        public UserAccount(string username, string passwordHash, string passwordSalt, UserRole role)
        {
            Username = username;
            PasswordHash = passwordHash;
            PasswordSalt = passwordSalt;
            Role = role;
        }

        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string PasswordSalt { get; set; }
        public UserRole Role { get; set; }
    }

    public class AuthService
    {
        private const int PasswordIterations = 210_000;
        private readonly string _usersFilePath;
        private readonly Dictionary<string, UserAccount> _users;
        private UserAccount? _currentUser;

        public AuthService()
        {
            var dataDirectory = DataDirectory();
            Directory.CreateDirectory(dataDirectory);
            _usersFilePath = Path.Combine(dataDirectory, "users.json");
            _users = LoadUsers();
            Log.Information("Base de usuarios en {Path}", _usersFilePath);
        }

        /// <summary>
        /// Carpeta donde vive <c>users.json</c>. Por defecto es el dato local de Windows.
        /// Existe la variable <c>QUASAR_RADIO_DATA_DIR</c> por dos motivos: montar una
        /// instalacion portable, y -el que lo pidio- poder verificar en vivo el login y la
        /// creacion de cuentas SIN tocar las cuentas reales del fundador. Los scripts de
        /// interfaz la apuntan a su sandbox de %TEMP%.
        /// </summary>
        private static string DataDirectory()
        {
            var custom = Environment.GetEnvironmentVariable("QUASAR_RADIO_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(custom))
            {
                return custom;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuasarRadio");
        }

        public bool HasUsers => _users.Count > 0;
        public string CurrentUserName => _currentUser?.Username ?? string.Empty;
        public UserRole? CurrentUserRole => _currentUser?.Role;
        public IReadOnlyList<UserAccount> Users => _users.Values.OrderBy(user => user.Username).ToArray();

        /// <summary>
        /// AUTH-1: se dispara al identificar o al salir de una sesion. Como la clave ya no
        /// abre ventanas sino que sella acciones, la interfaz tiene que reaccionar en el
        /// momento: los botones que dependian del nivel cambian de estado sin reiniciar nada,
        /// y el pie de la consola pasa de «Invitado» a «willlxxx | Admin».
        /// </summary>
        public event EventHandler? SessionChanged;

        public bool CreateInitialAdmin(string username, string password, out string error)
        {
            if (HasUsers)
            {
                error = "Ya existe una cuenta; inicia sesión para continuar.";
                return false;
            }

            return CreateUserCore(username, password, UserRole.Admin, out error);
        }

        public bool CreateUser(string username, string password, UserRole role, out string error)
        {
            // AUTH-1: el programa no pide login al arrancar, asi que la unica forma de montar
            // una instalacion nueva es poder crear la primera cuenta sin estar dentro. Con esa
            // excepcion cerrada, crear usuarios si es una atribucion de nivel: la segunda
            // cuenta en adelante exige sesion de administrador, y quien la abre desde la
            // ventana de usuarios recibe el dialogo de identificacion (AuthGate).
            if (HasUsers && !IsAuthorized(UserRole.Admin))
            {
                error = "Hace falta una sesion de administrador para crear mas usuarios.";
                return false;
            }

            return CreateUserCore(username, password, role, out error);
        }

        public bool Login(string username, string password, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                error = "Escribe el usuario y la contraseña.";
                return false;
            }

            if (!_users.TryGetValue(username, out var user))
            {
                error = "Usuario o contraseña incorrectos.";
                Log.Warning("Intento de acceso fallido para usuario {Username}", username);
                return false;
            }

            var salt = Convert.FromBase64String(user.PasswordSalt);
            var actualHash = DerivePasswordHash(password, salt);
            var expectedHash = Convert.FromBase64String(user.PasswordHash);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            {
                error = "Usuario o contraseña incorrectos.";
                Log.Warning("Intento de acceso fallido para usuario {Username}", username);
                return false;
            }

            _currentUser = user;
            Log.Information("Usuario autenticado: {Username} con rol {Role}", user.Username, user.Role);
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Logout()
        {
            if (_currentUser != null)
            {
                Log.Information("Sesión cerrada para {Username}", _currentUser.Username);
            }

            _currentUser = null;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool IsAuthenticated => _currentUser != null;

        public bool IsAuthorized(UserRole requiredRole)
        {
            if (_currentUser == null)
            {
                return requiredRole != UserRole.Admin;
            }

            return _currentUser.Role >= requiredRole;
        }

        private bool CreateUserCore(string username, string password, UserRole role, out string error)
        {
            error = string.Empty;
            username = username?.Trim() ?? string.Empty;
            if (username.Length < 3 || username.Length > 40)
            {
                error = "El usuario debe tener entre 3 y 40 caracteres.";
                return false;
            }

            if (password.Length < 10)
            {
                error = "La contraseña debe tener al menos 10 caracteres.";
                return false;
            }

            if (_users.ContainsKey(username))
            {
                error = "Ese nombre de usuario ya existe.";
                return false;
            }

            var salt = RandomNumberGenerator.GetBytes(16);
            var user = new UserAccount(
                username,
                Convert.ToBase64String(DerivePasswordHash(password, salt)),
                Convert.ToBase64String(salt),
                role);
            _users.Add(username, user);
            SaveUsers();
            Log.Information("Cuenta creada para {Username} con rol {Role}", username, role);
            return true;
        }

        private Dictionary<string, UserAccount> LoadUsers()
        {
            if (!File.Exists(_usersFilePath))
            {
                return new Dictionary<string, UserAccount>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var accounts = JsonSerializer.Deserialize<List<UserAccount>>(File.ReadAllText(_usersFilePath)) ?? [];
                return accounts.ToDictionary(account => account.Username, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                Log.Error(exception, "No se pudo leer el archivo local de usuarios.");
                throw new InvalidDataException("No se pudo cargar la base local de usuarios. El archivo debe repararse antes de iniciar.", exception);
            }
        }

        private void SaveUsers()
        {
            var temporaryPath = _usersFilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_users.Values));
            File.Move(temporaryPath, _usersFilePath, overwrite: true);
        }

        private static byte[] DerivePasswordHash(string password, byte[] salt) =>
            Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
    }
}