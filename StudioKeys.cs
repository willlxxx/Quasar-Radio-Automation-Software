using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;

namespace quasar_radio
{
    /// <summary>
    /// Tecla F1..F12 = pad 1..12 de la cartwall, en CUALQUIER ventana de la suite.
    ///
    /// Antes eran 12 <c>KeyBinding</c> copiados en <c>MainWindow.xaml</c> apuntando al
    /// DataContext de la consola: en cuanto la Biblioteca o el DSP tienen el foco, la
    /// botonera dejaba de responder. Con la propiedad adjunta basta con declarar
    /// <c>local:StudioKeys.CartWall="True"</c> en la ventana.
    /// </summary>
    public static class StudioKeys
    {
        public static readonly DependencyProperty CartWallProperty =
            DependencyProperty.RegisterAttached(
                "CartWall",
                typeof(bool),
                typeof(StudioKeys),
                new PropertyMetadata(false, OnCartWallChanged));

        public static bool GetCartWall(DependencyObject obj) =>
            (bool)obj.GetValue(CartWallProperty);

        public static void SetCartWall(System.Windows.DependencyObject obj, bool value) =>
            obj.SetValue(CartWallProperty, value);

        private static void OnCartWallChanged(System.Windows.DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window) return;
            if (e.NewValue is not true) return;

            // Una sola instancia de comando para las 12 teclas: resuelve el VM de la
            // cartwall en el momento de pulsar, no al parsear el XAML.
            var command = new CartWallShortcutCommand();

            for (var pad = 1; pad <= 12; pad++)
            {
                window.InputBindings.Add(new KeyBinding(
                    command,
                    new KeyGesture(Key.F1 + (pad - 1), ModifierKeys.None, pad.ToString())));
            }
        }

        /// <summary>
        /// Reenvia al comando real de la cartwall, que vive en un singleton de DI
        /// (<see cref="CartWallViewModel"/>), de modo que la tecla funciona con el foco
        /// puesto en la ventana que esté delante.
        /// </summary>
        private sealed class CartWallShortcutCommand : ICommand
        {
            public event EventHandler? CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }

            private static ICommand? Resolve()
            {
                try
                {
                    return App.Services
                        .GetRequiredService<MainViewModel>()
                        .CartWallViewModel
                        .TriggerCartPadCommand;
                }
                catch (Exception ex)
                {
                    Serilog.Log.ForContext<CartWallShortcutCommand>()
                              .Warning(ex, "Cartwall todavia no disponible; la tecla no hace nada");
                    return null;
                }
            }

            public bool CanExecute(object? parameter)
            {
                var inner = Resolve();
                return inner?.CanExecute(parameter) ?? false;
            }

            public void Execute(object? parameter) => Resolve()?.Execute(parameter);
        }
    }
}
