using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace quasar_radio
{
    /// <summary>
    /// Ventana del modulo BIBLIOTECA (UI-4). Se sigue llamando
    /// <c>LibraryManagementWindow</c> porque <see cref="ModuleNavigator"/> la registra con
    /// esa clave y el fichero XAML conserva su nombre de clase; lo que gestiona ya no es
    /// una suite de pestañas, es el espacio de trabajo de la libreria.
    /// </summary>
    public partial class LibraryManagementWindow : Window
    {
        private readonly LibraryViewModel _viewModel;

        public LibraryManagementWindow(LibraryViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
        }

        /// <summary>
        /// La seleccion multiple de un DataGrid no es enlazable en XAML (SelectedItems es
        /// de solo lectura), asi que entra por aqui y el ViewModel decide que puede hacer
        /// con ella. Es vista pura: no hay decision de negocio en esta linea.
        /// </summary>
        private void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid) _viewModel.UpdateSelection(grid.SelectedItems);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            // Guardar al cerrar era un MessageBox en la ventana vieja: cada cierre tapaba
            // la consola con un cuadro de "Metadatos guardados correctamente". Ahora queda
            // en la linea de estado y el error se registra en logs/.
            _viewModel.SaveSilently();
            base.OnClosing(e);
        }
    }
}
