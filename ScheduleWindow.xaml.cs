using System.ComponentModel;
using System.Windows;

namespace quasar_radio
{
    /// <summary>
    /// Módulo de Programación (antes pestaña TANDAS de la consola). Desde UI-4 paso C es la
    /// rejilla horaria editable en vista día y vista semana, más las tandas que la rejilla
    /// puede programar.
    /// </summary>
    public partial class ScheduleWindow : Window
    {
        private readonly ScheduleViewModel _viewModel;

        public ScheduleWindow(ScheduleViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        /// <summary>
        /// Cerrar no puede tirar el trabajo: si hay celdas cambiadas y la sesión es de
        /// administrador, se guarda en silencio (sin cuadro que tape el aire). Si no lo es,
        /// queda dicho en la línea de estado.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            _viewModel.SaveIfDirty();
            base.OnClosing(e);
        }
    }
}
