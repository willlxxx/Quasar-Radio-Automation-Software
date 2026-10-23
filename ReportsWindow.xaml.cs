using System.Windows;

namespace quasar_radio
{
    /// <summary>
    /// Módulo de Reportes y estadísticas (antes pestaña ESTADÍSTICAS).
    /// </summary>
    public partial class ReportsWindow : Window
    {
        public ReportsWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
