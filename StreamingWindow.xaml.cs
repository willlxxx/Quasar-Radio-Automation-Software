using System.Windows;

namespace quasar_radio
{
    /// <summary>
    /// Módulo de Streaming y Grabación: junta las dos pestañas que en la consola vieja
    /// eran el mismo dominio (enlace Icecast + grabadora de emisión).
    /// </summary>
    public partial class StreamingWindow : Window
    {
        public StreamingWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Al abrir se lee el disco: el listado de grabaciones nunca se rellenaba, así
            // que la pestaña antigua no mostraba ningún archivo y ELIMINAR no tenía a qué.
            Loaded += (_, _) => viewModel.RefreshRecordingFiles();
        }
    }
}
