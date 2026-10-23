using System.Windows;

namespace quasar_radio
{
    /// <summary>
    /// Módulo de Programación (antes pestaña TANDAS de la consola).
    /// </summary>
    public partial class ScheduleWindow : Window
    {
        public ScheduleWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
