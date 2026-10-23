using System.Windows;

namespace quasar_radio
{
    /// <summary>
    /// Módulo de Procesador DSP: reemplaza a las pestañas "PROCESADOR DSP" y "AUX PFL"
    /// de la consola. La crea <see cref="ModuleNavigator"/> como ventana transient, le
    /// pone el Owner y le aplica la geometría guardada.
    ///
    /// El DataContext sigue siendo <see cref="MainViewModel"/> (singleton) porque todos
    /// los enlaces de la pestaña apuntaban a él; UI-5 los bajará a su sub-ViewModel.
    /// </summary>
    public partial class DspWindow : Window
    {
        public DspWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
