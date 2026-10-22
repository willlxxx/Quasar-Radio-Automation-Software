using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace quasar_radio
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            // Usamos un IF clásico en lugar del operador ?. 
            // Esto es mucho más difícil que el compilador falle al leerlo.
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}