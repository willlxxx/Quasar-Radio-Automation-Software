using System;
using System.Windows;

namespace quasar_radio
{
    /// <summary>
    /// Un cuadro de texto con ACEPTAR / CANCELAR. Nace en UI-8 para renombrar pads de la botonera,
    /// y esta hecho ventana aparte (no campo dentro del menu) porque un menu contextual se cierra
    /// en cuanto se pulsa fuera, y escribir un nombre con el menu abierto es imposible.
    /// </summary>
    public partial class InputDialog : Window
    {
        public string TitleText { get; set; } = "Entrada de texto";
        public string PromptText { get; set; } = string.Empty;
        public string HintText { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

        /// <summary>Lo que se acepto. Vacio si el operador cancelo o no escribio nada.</summary>
        public string AcceptedText { get; private set; } = string.Empty;

        public InputDialog()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += (_, _) =>
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            };
        }

        /// <summary>
        /// Muestra el cuadro modal sobre <paramref name="owner"/> y devuelve el texto aceptado, o
        /// <c>null</c> si se cancelo. Se cierra con Enter (Aceptar) y con Esc (Cancelar).
        /// </summary>
        public static string? Ask(Window? owner, string title, string prompt, string hint, string initial)
        {
            var dialog = new InputDialog
            {
                TitleText = title,
                PromptText = prompt,
                HintText = hint,
                Value = initial ?? string.Empty,
                Owner = owner ?? Application.Current?.MainWindow
            };

            return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.AcceptedText)
                ? dialog.AcceptedText
                : null;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            AcceptedText = (Value ?? string.Empty).Trim();

            // Un pad sin nombre no se guarda: el titulo es lo que se lee en el aire y en el menu.
            if (string.IsNullOrWhiteSpace(AcceptedText))
            {
                ValueBox.Focus();
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
