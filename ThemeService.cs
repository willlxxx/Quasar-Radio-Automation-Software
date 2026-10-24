using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using Serilog;

namespace quasar_radio
{
    /// <summary>
    /// Dueño del tema activo de la interfaz.
    ///
    /// El tema NO es un conjunto de propiedades enlazadas: es el diccionario de
    /// recursos que ocupa el hueco <see cref="ThemeSlotIndex"/> en
    /// Application.Resources.MergedDictionaries. Como todas las vistas consumen
    /// los brushes por DynamicResource, sustituir ese diccionario refresca cada
    /// ventana abierta sin reconstruir nada.
    /// </summary>
    public static class ThemeService
    {
        /// <summary>Posición del hueco de tema: [0] Tokens, [1] Controls, [2] Tema.</summary>
        public const int ThemeSlotIndex = 2;

        public const string DefaultTheme = "Studio Dark Neon";

        private static readonly Dictionary<string, string> ThemeFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Studio Dark Neon", "Theme.StudioDarkNeon.xaml" },
            { "Cyan Broadcast", "Theme.CyanBroadcast.xaml" },
            { "Charcoal Pro", "Theme.CharcoalPro.xaml" },
            { "Deep Navy FM", "Theme.DeepNavyFM.xaml" },
            { "Studio Light", "Theme.StudioLight.xaml" },
        };

        private static readonly ILogger _log = Log.ForContext(typeof(ThemeService));

        public static IReadOnlyList<string> AvailableThemes { get; } =
            new List<string> { "Studio Dark Neon", "Cyan Broadcast", "Charcoal Pro", "Deep Navy FM", "Studio Light" };

        public static string CurrentTheme { get; private set; } = DefaultTheme;

        /// <summary>Se dispara después de cambiar el diccionario, para lo que pinte en código (VU metros).</summary>
        public static event EventHandler? ThemeChanged;

        /// <summary>
        /// Aplica un tema por su nombre visible y lo guarda para el próximo arranque.
        /// Si el nombre no existe, no hace nada y lo registra.
        /// </summary>
        public static void Apply(string themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName)) return;
            if (!ThemeFiles.TryGetValue(themeName.Trim(), out var file))
            {
                _log.Warning("Tema desconocido solicitado: {Theme}", themeName);
                return;
            }

            var app = Application.Current;
            if (app == null)
            {
                _log.Warning("No hay Application en marcha; el tema {Theme} no se puede aplicar", themeName);
                return;
            }

            var merged = app.Resources.MergedDictionaries;
            var alreadyInSlot = merged.Count > ThemeSlotIndex
                                && string.Equals(merged[ThemeSlotIndex].Source?.OriginalString,
                                                 $"pack://application:,,,/Themes/{file}",
                                                 StringComparison.OrdinalIgnoreCase);
            if (alreadyInSlot)
            {
                // El hueco ya carga ese diccionario: solo queda fijar el nombre y guardarlo.
                CurrentTheme = themeName.Trim();
                Save(CurrentTheme);
                return;
            }

            try
            {
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/Themes/{file}", UriKind.Absolute)
                };

                if (merged.Count > ThemeSlotIndex) merged[ThemeSlotIndex] = dictionary;
                else merged.Add(dictionary);

                CurrentTheme = themeName.Trim();
                Save(CurrentTheme);
                ThemeChanged?.Invoke(null, EventArgs.Empty);
                _log.Information("Tema aplicado: {Theme}", CurrentTheme);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "No se pudo aplicar el tema {Theme}", themeName);
            }
        }

        /// <summary>Restaura el tema elegido la última vez. Se llama antes de mostrar la ventana principal.</summary>
        public static void RestoreSaved()
        {
            Apply(Load());
        }

        // ---------------------------------------------------------------- guardado

        private static string SettingsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuasarRadio");

        private static string SettingsFile => Path.Combine(SettingsDirectory, "appearance.json");

        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        private static string Load()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return DefaultTheme;
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppearanceSettings>(json, ReadOptions);
                var value = settings?.Theme;
                if (!string.IsNullOrWhiteSpace(value) && ThemeFiles.ContainsKey(value.Trim())) return value.Trim();
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "No se pudo leer {File}; se usa el tema predeterminado", SettingsFile);
            }
            return DefaultTheme;
        }

        private static void Save(string theme)
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                var payload = JsonSerializer.Serialize(new AppearanceSettings { Theme = theme });

                // Escritura atómica: temporal + reemplazo, el patron que ya usa AuthService.
                var tmp = SettingsFile + ".tmp";
                File.WriteAllText(tmp, payload);
                if (File.Exists(SettingsFile)) File.Replace(tmp, SettingsFile, null);
                else File.Move(tmp, SettingsFile);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "El tema {Theme} se aplico pero no se pudo guardar la eleccion", theme);
            }
        }

        private sealed class AppearanceSettings
        {
            [System.Text.Json.Serialization.JsonPropertyName("theme")]
            public string Theme { get; set; } = DefaultTheme;
        }
    }
}
