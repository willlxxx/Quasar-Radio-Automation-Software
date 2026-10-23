using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace quasar_radio
{
    /// <summary>
    /// Ventanas que la consola puede tener abiertas al mismo tiempo.
    /// Se añaden aqui (una linea de <see cref="ModuleNavigator.Create"/>) a medida que
    /// UI-4 va trayendose el contenido de las antiguas pestañas de la consola.
    /// </summary>
    public enum ModuleKey
    {
        Library,
        Users,
        Schedule,
        Dsp,
        Streaming,
        Reports,
    }

    /// <summary>
    /// Punto unico de entrada para abrir una ventana de modulo.
    ///
    /// Reglas que resuelve y que cada ViewModels no deberia reimplementar:
    /// una sola instancia por modulo, no modal, duena de su geometria, y con el
    /// permiso comprobado antes de abrirla.
    /// </summary>
    public interface IModuleNavigator
    {
        bool IsOpen(ModuleKey key);

        /// <summary>Abre el modulo, o lo trae al frente si ya estaba abierto.</summary>
        void Open(ModuleKey key);

        /// <summary>Igual que <see cref="Open"/>: nunca cierra un modulo por atajo, para no perder trabajo en cabina.</summary>
        void Toggle(ModuleKey key);

        /// <summary>
        /// Devuelve true si hay un administrador autenticado. Si no los hay, abre el
        /// acceso modal y solo devuelve true si el usuario entra. Es la puerta comun de
        /// los modulos que antes eran un boton apagado sin cuenta de administrador.
        /// </summary>
        bool EnsureAdmin();

        /// <summary>Modulos que solo un administrador puede abrir (DSP y usuarios).</summary>
        bool RequiresAdmin(ModuleKey key);

        event EventHandler<ModuleKey>? ModuleOpened;
        event EventHandler<ModuleKey>? ModuleClosed;
    }

    public sealed class ModuleNavigator : IModuleNavigator
    {
        private static readonly ILogger _log = Log.ForContext<ModuleNavigator>();

        private readonly IServiceProvider _services;
        private readonly Dictionary<ModuleKey, Window> _open = new();
        private readonly Dictionary<ModuleKey, DispatcherTimer> _pendingSave = new();
        private readonly Dictionary<ModuleKey, WindowGeometry> _geometry = LoadGeometry();

        public ModuleNavigator(IServiceProvider services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public event EventHandler<ModuleKey>? ModuleOpened;
        public event EventHandler<ModuleKey>? ModuleClosed;

        public bool IsOpen(ModuleKey key) => _open.ContainsKey(key);

        /// <summary>
        /// Los modulos que tocan la señal o los permisos son de administrador. La puerta
        /// esta en <see cref="Open"/> para que ningun boton de la consola la pueda olvidar.
        /// </summary>
        public bool RequiresAdmin(ModuleKey key) => key switch
        {
            ModuleKey.Dsp => true,
            ModuleKey.Users => true,
            ModuleKey.Library => true,
            _ => false,
        };

        public void Toggle(ModuleKey key) => Open(key);

        public void Open(ModuleKey key)
        {
            var app = Application.Current;
            if (app == null)
            {
                _log.Warning("Sin Application en marcha; no se puede abrir {Key}", key);
                return;
            }

            if (_open.TryGetValue(key, out var existing))
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }

            // Asegurado aqui y no en cada llamada: EnsureAdmin es idempotente, asi que un
            // llamador que ya pidio acceso no vuelve a ver el dialogo.
            if (RequiresAdmin(key) && !EnsureAdmin())
            {
                _log.Information("Apertura cancelada de {Key}: hace falta administrador", key);
                return;
            }

            Window window;
            try
            {
                window = Create(key);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "No se pudo construir la ventana del modulo {Key}", key);
                return;
            }

            ApplyGeometry(window, key);
            Attach(window, key);

            if (app.MainWindow != null && !ReferenceEquals(app.MainWindow, window))
            {
                window.Owner = app.MainWindow;
            }

            window.Show();
            window.Activate();
            _open[key] = window;
            ModuleOpened?.Invoke(this, key);
            _log.Information("Modulo abierto: {Key}", key);
        }

        public bool EnsureAdmin()
        {
            if (App.Auth.IsAuthorized(UserRole.Admin)) return true;

            var owner = Application.Current?.MainWindow;
            var login = new LoginWindow(App.Auth)
            {
                Owner = owner
            };

            var accepted = login.ShowDialog() == true;
            if (!accepted || !App.Auth.IsAuthorized(UserRole.Admin))
            {
                _log.Information("Acceso denegado: hace falta una cuenta de administrador");
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ fabrica

        private Window Create(ModuleKey key) => key switch
        {
            ModuleKey.Library => _services.GetRequiredService<LibraryManagementWindow>(),
            ModuleKey.Users => new UserManagementWindow(App.Auth),
            ModuleKey.Schedule => _services.GetRequiredService<ScheduleWindow>(),
            ModuleKey.Dsp => _services.GetRequiredService<DspWindow>(),
            ModuleKey.Streaming => _services.GetRequiredService<StreamingWindow>(),
            ModuleKey.Reports => _services.GetRequiredService<ReportsWindow>(),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Modulo sin ventana registrada"),
        };

        // ------------------------------------------------------------- ciclo de vida

        private void Attach(Window window, ModuleKey key)
        {
            void OnGeometryChanged() => ScheduleSave(key);

            window.LocationChanged += (_, _) => OnGeometryChanged();
            window.SizeChanged += (_, _) => OnGeometryChanged();
            window.StateChanged += (_, _) => OnGeometryChanged();

            window.Closed += (_, _) =>
            {
                // SaveNow todavia ve la ventana en _open; despues se retira.
                SaveNow(key);

                if (_pendingSave.TryGetValue(key, out var timer)) timer.Stop();
                _pendingSave.Remove(key);
                _open.Remove(key);

                _log.Information("Modulo cerrado: {Key}", key);
                ModuleClosed?.Invoke(this, key);
            };
        }

        private void ScheduleSave(ModuleKey key)
        {
            if (!_open.TryGetValue(key, out var window) || window == null) return;

            if (!_pendingSave.TryGetValue(key, out var timer))
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    SaveNow(key);
                };
                _pendingSave[key] = timer;
            }

            timer.Stop();
            timer.Start();
        }

        // ----------------------------------------------------------------- geometria

        private void ApplyGeometry(Window window, ModuleKey key)
        {
            // Aunque la geometria guardada quepa en el virtual screen, el monitor pudo
            // cambiar de tamano o de barra de tareas: al aparecer se vuelve a encajar.
            window.FitWhenReady();

            if (!_geometry.TryGetValue(key, out var saved) || saved == null || !saved.IsSane())
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                return;
            }

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = saved.X;
            window.Top = saved.Y;
            window.Width = saved.W;
            window.Height = saved.H;
            if (saved.Max) window.WindowState = WindowState.Maximized;
        }

        private void SaveNow(ModuleKey key)
        {
            if (!_open.TryGetValue(key, out var window) || window == null) return;

            // RestoreBounds da el tamano real cuando la ventana esta maximizada o minimizada.
            var maximized = window.WindowState == WindowState.Maximized;
            var b = maximized || window.WindowState == WindowState.Minimized
                ? window.RestoreBounds
                : new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
            if (b.Width <= 0 || b.Height <= 0) return;

            _geometry[key] = new WindowGeometry
            {
                X = b.X,
                Y = b.Y,
                W = b.Width,
                H = b.Height,
                Max = maximized,
            };

            WriteGeometry();
        }

        private static string SettingsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuasarRadio");

        private static string LayoutFile => Path.Combine(SettingsDirectory, "layout.json");

        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        private static Dictionary<ModuleKey, WindowGeometry> LoadGeometry()
        {
            var result = new Dictionary<ModuleKey, WindowGeometry>();
            try
            {
                if (!File.Exists(LayoutFile)) return result;
                var stored = JsonSerializer.Deserialize<LayoutDocument>(File.ReadAllText(LayoutFile), ReadOptions);
                if (stored?.Windows == null) return result;

                foreach (var pair in stored.Windows)
                {
                    if (Enum.TryParse<ModuleKey>(pair.Key, true, out var key) && pair.Value != null)
                    {
                        result[key] = pair.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ForContext<ModuleNavigator>().Warning(ex, "No se pudo leer {File}", LayoutFile);
            }
            return result;
        }

        private void WriteGeometry()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                var payload = new LayoutDocument { Windows = new Dictionary<string, WindowGeometry>() };
                foreach (var pair in _geometry) payload.Windows[pair.Key.ToString()] = pair.Value;

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

                // Escritura atómica: el mismo patron de ThemeService y AuthService.
                var tmp = LayoutFile + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(LayoutFile)) File.Replace(tmp, LayoutFile, null);
                else File.Move(tmp, LayoutFile);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "No se pudo guardar la geometria de los modulos");
            }
        }

        private sealed class LayoutDocument
        {
            [JsonPropertyName("windows")]
            public Dictionary<string, WindowGeometry>? Windows { get; set; }
        }

        private sealed class WindowGeometry
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double W { get; set; }
            public double H { get; set; }
            public bool Max { get; set; }

            /// <summary>Descarta geometrias fuera de cualquier pantalla (monitor desconectado).</summary>
            public bool IsSane()
            {
                if (W < 200 || H < 120) return false;

                var left = SystemParameters.VirtualScreenLeft;
                var top = SystemParameters.VirtualScreenTop;
                var right = left + SystemParameters.VirtualScreenWidth;
                var bottom = top + SystemParameters.VirtualScreenHeight;

                // Tiene que quedar al menos una franja de 60 px visible para poder moverla.
                return X < right - 60 && X + W > left + 60
                    && Y < bottom - 60 && Y + H > top + 60;
            }
        }
    }
}
