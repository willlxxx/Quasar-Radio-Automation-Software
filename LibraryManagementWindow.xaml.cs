using System.Windows;

namespace quasar_radio
{
    public partial class LibraryManagementWindow : Window
    {
        private readonly LibraryManagementViewModel _viewModel;

        public LibraryManagementWindow(LibraryManagementViewModel? viewModel = null)
        {
            InitializeComponent();
            _viewModel = viewModel ?? new LibraryManagementViewModel();
            DataContext = _viewModel;

            // Conectar el comando de cerrar ventana
            _viewModel.CloseWindowCommand = new RelayCommand(_ => this.Close());
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Guardar metadatos automáticamente al cerrar
            try
            {
                _viewModel.SaveMetadataCommand.Execute(null);
            }
            catch
            {
                // Silenciar errores al cerrar
            }
        }
    }
}