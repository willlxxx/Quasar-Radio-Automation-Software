using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace quasar_radio
{
    /// <summary>
    /// El color actual del tema activo, como cadena hexadecimal.
    ///
    /// UI-9: un puñado de ViewModels y servicios exponen colores de estado como <c>string</c>
    /// y la vista los enlaza directo (<c>Foreground="{Binding SilenceStatusColor}"</c>). Mientras
    /// eran literales ("#FF1744") esos estados ignoraban el tema: en el claro el verde de
    /// "VIGILANDO" daba 1,7:1 sobre blanco — legalmente invisible. Aqui la cadena se resuelve
    /// contra la clave del token, asi que el ViewModel dice <em>que papel</em> cumple el color y
    /// el tema decide <em>cuál</em> es. Si aun no hay aplicación (diseñador, arranque temprano)
    /// se devuelve el literal histórico: el valor de reserva es siempre el que la vista tenía
    /// antes de UI-9, nunca un color nuevo.
    /// </summary>
    public static class ThemeHex
    {
        /// <summary>Devuelve "#RRGGBB" del brush <paramref name="brushKey"/> en el tema activo.</summary>
        public static string Of(string brushKey, string fallbackHex)
        {
            if (Application.Current?.TryFindResource(brushKey) is SolidColorBrush brush)
            {
                var c = brush.Color;
                return "#" + c.R.ToString("X2", CultureInfo.InvariantCulture)
                             + c.G.ToString("X2", CultureInfo.InvariantCulture)
                             + c.B.ToString("X2", CultureInfo.InvariantCulture);
            }
            return fallbackHex;
        }
    }

    /// <summary>
    /// Reavisa propiedades de un ViewModel cuando el tema cambia.
    ///
    /// Se suscribe el ThemeService una sola vez y cada ViewModel se enlaza por <em>referencia
    /// débil</em>: un <see cref="TrackItem"/> por fila de la biblioteca son cientos de objetos
    /// que nacen y mueren con cada reescaneo, y un manejador fuerte por cada uno sería una fuga
    /// que crece con la sesión. Los enlaces muertos se podan en el mismo evento.
    /// </summary>
    public static class ThemeRefresh
    {
        private static readonly List<WeakReference<Binding>> _bindings = new();
        private static readonly object _gate = new();
        private static bool _wired;

        private sealed record Binding(ViewModelBase Target, string[] PropertyNames);

        /// <summary>
        /// Atado: al cambiar el tema se re-lanza <paramref name="propertyNames"/> en el objetivo.
        /// Llamable desde el constructor del ViewModel; no hace falta desuscribirse.
        /// </summary>
        public static void Bind(ViewModelBase target, params string[] propertyNames)
        {
            if (target == null || propertyNames.Length == 0) return;

            lock (_gate)
            {
                if (!_wired)
                {
                    ThemeService.ThemeChanged += (_, _) => Notify();
                    _wired = true;
                }
                _bindings.Add(new WeakReference<Binding>(new Binding(target, propertyNames)));
            }
        }

        private static void Notify()
        {
            List<Binding> alive = new();
            lock (_gate)
            {
                _bindings.RemoveAll(reference =>
                {
                    if (reference.TryGetTarget(out var binding))
                    {
                        alive.Add(binding);
                        return false;
                    }
                    return true;
                });
            }

            // Fuera del candado: disparar PropertyChanged puede correr enlazadores y re-entrar.
            foreach (var binding in alive)
            {
                binding.Target.RaiseForTheme(binding.PropertyNames);
            }
        }
    }
}
