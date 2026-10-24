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

        /// <summary>
        /// Re-lanza propiedades al cambiar el tema. UI-9: varios ViewModels exponen colores de
        /// estado como <c>string</c> ("#FF1744") y la vista los pinta tal cual; al resolverlos
        /// contra el token del tema activo hace falta avisar de que el valor viejo ya no es el
        /// del tema, o la aguja de silencio seguiría roja sobre blanco tras cambiar de piel.
        /// </summary>
        public void RaiseForTheme(params string[] propertyNames)
        {
            foreach (var name in propertyNames) OnPropertyChanged(name);
        }
    }
}