using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace quasar_radio
{
    /// <summary>
    /// Una linea de submenu con su mando ya atado al pad del menu. Se construye en el
    /// ViewModel y no en el XAML por el mismo motivo que <see cref="CartPadMenu"/>: dentro de
    /// un ContextMenu no se puede Enlazar contra la ventana, asi que lo que se enlaza es el dato.
    /// </summary>
    public sealed class PadValueItem : ViewModelBase
    {
        private string _header = string.Empty;
        public string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(); }
        }

        public ICommand Command { get; set; } = null!;

        /// <summary>El valor que representa la linea (porcentaje, o el codigo del modo).</summary>
        public int Value { get; init; }
    }

    /// <summary>
    /// El menu contextual de un pad de la botonera SFX.
    ///
    /// UI-8 lo pide porque el menu que habia en el XAML no hacia nada: sus dos lineas
    /// <c>MenuItem</c> enlazaban el mando con <c>RelativeSource AncestorType=Window</c>, y un
    /// ContextMenu vive en una ventana Popup aparte que no cuelga de la consola, de modo que el
    /// enlace no resolvia, el mando era nulo y la linea salia apagada. El operador veia un menu
    /// con dos opciones inertes. Aqui cada pad lleva sus mandos puestos, atados a si mismo, y el
    /// XAML solo tiene que decir <c>{Binding Menu.TriggerCommand}</c>.
    /// </summary>
    public sealed class CartPadMenu
    {
        /// <summary>Pasos de volumen del menu. De diez en diez: se acertar con el raton y se lee.</summary>
        public static readonly int[] VolumeSteps = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

        /// <summary>
        /// Profundidades de ducking. Se queda en 0 sin bajar nada, y el 90 en vez del 100 porque
        /// cortar la musica de golpe suena a error de emision; si hace falta silencio total se usa
        /// el PARAR del deck.
        /// </summary>
        public static readonly int[] DuckSteps = { 0, 25, 50, 75, 90 };

        private readonly CartWallViewModel _owner;
        private readonly CartPadItem _pad;

        public ICommand TriggerCommand { get; }
        public ICommand PreListenCommand { get; }
        public ICommand AssignCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand RenameCommand { get; }

        public IReadOnlyList<PadValueItem> VolumeItems { get; }
        public IReadOnlyList<PadValueItem> DuckItems { get; }
        public IReadOnlyList<PadValueItem> ModeItems { get; }

        public CartPadMenu(CartWallViewModel owner, CartPadItem pad)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _pad = pad ?? throw new ArgumentNullException(nameof(pad));

            // Disparar, pre-escuchar y mover volumen o ducking son de operador y no piden clave
            // (AUTH-1): son los movimientos que se hacen en el aire. Asignar un clip, quitarlo y
            // rebautizar el pad cambian la botonera de toda la emisora, asi que piden nivel.
            TriggerCommand = new RelayCommand(_ => _owner.TriggerPad(_pad), _ => _owner.CanOperate());
            PreListenCommand = new RelayCommand(_ => _owner.TogglePreListen(_pad), _ => _owner.CanOperate());

            AssignCommand = new RelayCommand(_ => _owner.AssignWithGate(_pad), _ => _owner.CanOperate());
            ClearCommand = new RelayCommand(_ => _owner.ClearWithGate(_pad), _ => _owner.CanOperate());
            RenameCommand = new RelayCommand(_ => _owner.RenameWithGate(_pad), _ => _owner.CanOperate());

            var volume = new List<PadValueItem>();
            foreach (var percent in VolumeSteps)
            {
                int captured = percent;
                volume.Add(new PadValueItem
                {
                    Value = captured,
                    Command = new RelayCommand(_ => _owner.SetPadVolume(_pad, captured), _ => _owner.CanOperate())
                });
            }
            VolumeItems = volume;

            var duck = new List<PadValueItem>();
            foreach (var percent in DuckSteps)
            {
                int captured = percent;
                duck.Add(new PadValueItem
                {
                    Value = captured,
                    Command = new RelayCommand(_ => _owner.SetPadDuck(_pad, captured), _ => _owner.CanOperate())
                });
            }
            DuckItems = duck;

            var modes = new List<PadValueItem>();
            foreach (CartPlayMode mode in new[] { CartPlayMode.Toggle, CartPlayMode.OneShot })
            {
                CartPlayMode captured = mode;
                modes.Add(new PadValueItem
                {
                    Value = (int)mode,
                    Command = new RelayCommand(_ => _owner.SetPadMode(_pad, captured), _ => _owner.CanOperate())
                });
            }
            ModeItems = modes;

            RefreshValueLabels();
        }

        /// <summary>
        /// Vuelve a escribir las etiquetas de los tres submenues. Lo hace el ViewModel despues de
        /// cualquier cambio, para que la opcion vigente se vea marcada dentro del propio menu: en
        /// cabina se necesita reconocer al instante donde esta el pad sin abrir el menu de al lado.
        /// </summary>
        public void RefreshValueLabels()
        {
            int currentVolume = _pad.VolumePercent;
            foreach (var item in VolumeItems)
            {
                item.Header = (item.Value == currentVolume ? "✓ " : "    ") + item.Value + " %";
            }

            int currentDuck = _pad.DuckPercent;
            foreach (var item in DuckItems)
            {
                string label = item.Value == 0 ? "sin bajar la música"
                    : item.Value == 90 ? "90 % (casi muda)"
                    : item.Value + " %";
                item.Header = (item.Value == currentDuck ? "✓ " : "    ") + label;
            }

            foreach (var item in ModeItems)
            {
                var mode = (CartPlayMode)item.Value;
                string label = mode == CartPlayMode.OneShot
                    ? "disparar y dejar"
                    : "inicio/parada";
                item.Header = (_pad.Mode == mode ? "✓ " : "    ") + label;
            }
        }
    }
}
