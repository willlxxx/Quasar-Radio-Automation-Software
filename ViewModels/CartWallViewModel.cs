using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Serilog;

namespace quasar_radio
{
    public class CartWallViewModel : ViewModelBase
    {
        private readonly ILogger _logger = Log.ForContext<CartWallViewModel>();
        private readonly CartwallEngine _cartwallEngine;
        private readonly CartPadService _padService;
        private readonly AuxPlayerEngine? _auxEngine;

        public ObservableCollection<CartPadItem> CartPads { get; } = new ObservableCollection<CartPadItem>();

        private float _cartwallMasterVolume = 1.0f;
        public float CartwallMasterVolume
        {
            get => _cartwallMasterVolume;
            set
            {
                _cartwallMasterVolume = value;
                _cartwallEngine.CartwallVolume = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CartwallVolumePercentageText));
            }
        }

        public string CartwallVolumePercentageText => $"{(int)(CartwallMasterVolume * 100)}%";

        private string _statusText = "ESTADO: LISTO";
        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// El resumen que se lee en la cabecera de la botonera. Con doce pads vacios por delante no
        /// hay manera de saber si alguien monto algo, y esa duda en el aire se resuelve a palos.
        /// </summary>
        public string PadInventoryText => string.Format("{0} de {1} con clip",
            CartPads.Count(p => p.HasAudio), CartPads.Count);

        /// <summary>Que pad esta sonando por el monitor, para el estado de la consola.</summary>
        private CartPadItem? _preListeningPad;

        public ICommand TriggerCartPadCommand { get; }
        public ICommand AssignCartPadCommand { get; }
        public ICommand ClearCartPadCommand { get; }
        public ICommand StopAllCartPadsCommand { get; }
        public ICommand SaveCartPadsCommand { get; }

        public CartWallViewModel(CartwallEngine cartwallEngine, CartPadService? padService = null,
            AuxPlayerEngine? auxEngine = null)
        {
            _cartwallEngine = cartwallEngine ?? throw new ArgumentNullException(nameof(cartwallEngine));
            _padService = padService ?? new CartPadService();
            _auxEngine = auxEngine;
            _cartwallEngine.PlaybackFailed += (_, message) => StatusText = message;

            bool CanOperate() => App.Auth.IsAuthorized(UserRole.Operator);

            // AUTH-1: disparar un pad es de operador y se hace sin clave. Asignarle un archivo,
            // quitarle el contenido o rebautizarlo cambia la botonera de toda la emisora, asi que
            // sigue siendo de administrador; lo que cambio es que la cuenta se pide al pulsar.
            bool Admin() => AuthGate.Admin();

            TriggerCartPadCommand = new RelayCommand(o => TriggerCartPad(o), o => CanOperate());
            AssignCartPadCommand = new RelayCommand(o => { if (Admin()) AssignCartPad(o as CartPadItem); });
            ClearCartPadCommand = new RelayCommand(o => { if (Admin()) ClearCartPad(o as CartPadItem); });
            StopAllCartPadsCommand = new RelayCommand(o => StopAllPads(), o => CanOperate());

            // Guardar la botonera es de administrador, pero el CanExecute no puede pedir la clave:
            // WPF re-evalua los mandos con cada entrada de raton, y un CanExecute que abre un
            // dialogo de login convierte cada clic de la consola en una ventana inesperada.
            SaveCartPadsCommand = new RelayCommand(o => { if (Admin()) StatusText = _padService.Save(CartPads); });

            InitializeCartPads();

            // Un solo suscriptor para el monitor de pads. Atarlo dentro de TogglePreListen hacia
            // que cada pre-escucha anadiera su propio delegado y el evento se quedaba con una
            // lista de manipuladores que ya no apuntan a ningun pad.
            if (_auxEngine != null) _auxEngine.Aux3.PropertyChanged += OnAux3PropertyChanged;
        }

        /// <summary>La pre-escucha se apaga sola cuando el clip del monitor termina.</summary>
        private void OnAux3PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AuxPlayer.IsPlaying)) return;
            if (_auxEngine != null && _auxEngine.Aux3.IsPlaying) return;

            var pad = _preListeningPad;
            _preListeningPad = null;
            if (pad == null) return;

            pad.IsPreListening = false;
            StatusText = $"Terminada la pre-escucha de {pad.ShortcutText}: «{pad.Title}».";
        }

        private void InitializeCartPads()
        {
            string[] presetColors = new string[]
            {
                "#FF1744", "#FF9100", "#FFEA00", "#00E676",
                "#00B0FF", "#651FFF", "#F50057", "#00E5FF",
                "#76FF03", "#FF3D00", "#3D5AFE", "#D500F9"
            };

            for (int i = 1; i <= 12; i++)
            {
                var pad = new CartPadItem
                {
                    PadId = i,
                    ShortcutText = $"F{i}",
                    Title = $"Efecto {i}",
                    ColorHex = presetColors[i - 1]
                };
                pad.Menu = new CartPadMenu(this, pad);
                CartPads.Add(pad);
            }

            string sfxFolder = "efectos";
            if (Directory.Exists(sfxFolder))
            {
                var files = Directory.GetFiles(sfxFolder, "*.*")
                                     .Where(AudioSourceFactory.IsSupported)
                                     .ToArray();

                for (int i = 0; i < Math.Min(files.Length, 12); i++)
                {
                    CartPads[i].FilePath = files[i];
                    CartPads[i].Title = Path.GetFileNameWithoutExtension(files[i]);
                }
            }

            // UI-8: lo que hay en cart_pads.json manda sobre el escaneo de la carpeta. El escaneo
            // queda como material de arranque (la primera vez que se abre el programa en una
            // maquina), pero el operador ya puede mover, renombrar y ajustar niveles sin que el
            // siguiente arranque le devuelva la botonera al desorden de los nombres de archivo.
            StatusText = _padService.Load(CartPads);
            foreach (var pad in CartPads)
            {
                pad.Menu?.RefreshValueLabels();
            }
            OnPropertyChanged(nameof(PadInventoryText));
        }

        // ------------------------------------------------------------------ el aire

        public bool CanOperate() => App.Auth.IsAuthorized(UserRole.Operator);

        private void TriggerCartPad(object? param)
        {
            CartPadItem? pad = null;

            if (param is CartPadItem p)
            {
                pad = p;
            }
            else if (param != null && int.TryParse(param.ToString(), out int padId))
            {
                pad = CartPads.FirstOrDefault(x => x.PadId == padId);
            }

            if (pad != null) TriggerPad(pad);
        }

        /// <summary>
        /// Sacar o cortar el pad. Con el pad vacio ya NO abre el dialogo de archivo: hasta UI-8 una
        /// pulsacion sobre un pad sin clip abria un cuadro de seleccion modal encima de la consola,
        /// que es la peor manera de enterarse de que se equivoco de tecla estando al aire.
        /// </summary>
        public void TriggerPad(CartPadItem pad)
        {
            if (pad == null) return;

            if (!pad.HasAudio)
            {
                StatusText = pad.IsFileMissing
                    ? $"El pad {pad.ShortcutText} apunta a un archivo que ya no esta: {Path.GetFileName(pad.FilePath)}."
                    : $"El pad {pad.ShortcutText} no tiene clip montado. Clic derecho → Asignar audio.";
                return;
            }

            if (_preListeningPad == pad) StopPreListen();

            _cartwallEngine.TogglePad(pad);

            if (pad.IsPlaying)
            {
                StatusText = pad.DuckPercent > 0
                    ? $"Pad {pad.ShortcutText} al aire: «{pad.Title}» (música −{pad.DuckPercent}%)."
                    : $"Pad {pad.ShortcutText} al aire: «{pad.Title}».";
            }
            else
            {
                StatusText = $"Pad {pad.ShortcutText} cortado: «{pad.Title}».";
            }
        }

        public void StopAllPads()
        {
            _cartwallEngine.StopAll();
            StopPreListen();
            StatusText = "Parados los pads de la botonera.";
        }

        // ------------------------------------------------------------------ pre-escucha

        /// <summary>
        /// Pre-escucha del pad por el monitor (AUX 3), sin abrir el aire ni duckear la musica: es
        /// oir el clip antes de sacarlo. Va por un AUX aparte para no pisar la pre-escucha de los
        /// decks, que tiene su propio boton.
        /// </summary>
        public void TogglePreListen(CartPadItem pad)
        {
            if (pad == null) return;

            if (_preListeningPad == pad && _auxEngine != null && _auxEngine.Aux3.IsPlaying)
            {
                StopPreListen();
                StatusText = $"Pre-escucha quitada: «{pad.Title}».";
                return;
            }

            if (_auxEngine == null)
            {
                StatusText = "La pre-escucha de pads necesita el monitor auxiliar, y este motor no lo tiene.";
                return;
            }

            if (!pad.HasAudio)
            {
                StatusText = $"El pad {pad.ShortcutText} no tiene ningun clip que pre-escuchar.";
                return;
            }

            StopPreListen();

            var ok = _auxEngine.Aux3.PlayTrack(new TrackItem
            {
                Title = pad.Title,
                FilePath = pad.FilePath
            });

            if (!ok)
            {
                StatusText = $"No se pudo pre-escuchar «{pad.Title}»: el archivo no se abre.";
                return;
            }

            pad.IsPreListening = true;
            _preListeningPad = pad;
            StatusText = $"Pre-escucha {pad.ShortcutText}: «{pad.Title}» (por el monitor, no sale al aire).";
        }

        private void StopPreListen()
        {
            if (_auxEngine != null) _auxEngine.Aux3.Stop();
            if (_preListeningPad != null) _preListeningPad.IsPreListening = false;
            _preListeningPad = null;
        }

        // ------------------------------------------------------------------ ajustes por pad

        /// <summary>
        /// Volumen del pad, 0..100. Se aplica en la cadena de este clip y en vivo: si el pad esta
        /// sonando, el cambio se oye en ese momento en vez de esperar al siguiente disparo.
        /// </summary>
        public void SetPadVolume(CartPadItem pad, int percent)
        {
            if (pad == null) return;

            pad.Volume = Math.Clamp(percent, 0, 100) / 100f;
            _cartwallEngine.ApplyPadVolume(pad);
            pad.Menu?.RefreshValueLabels();
            StatusText = $"Pad {pad.ShortcutText} a {pad.VolumePercent} % de volumen.";
            SaveQuietly();
        }

        /// <summary>
        /// Cuesta abajo de la musica mientras este pad esta en el aire, en porcentaje. Es el
        /// "superpuesto, bajando la musica" de la ronda 2, aplicado clip a clip.
        /// </summary>
        public void SetPadDuck(CartPadItem pad, int percent)
        {
            if (pad == null) return;

            pad.DuckDepth = Math.Clamp(percent, 0, 100) / 100f;

            // Si el pad ya esta sonando, la nueva profundidad vale para lo que queda de clip:
            // el motor vuelve a pedir el ducking con el factor de este pad.
            if (pad.IsPlaying) _cartwallEngine.ReapplyPadDuck(pad);

            pad.Menu?.RefreshValueLabels();
            StatusText = percent == 0
                ? $"Pad {pad.ShortcutText}: la musica no baja mientras suene."
                : $"Pad {pad.ShortcutText}: música −{pad.DuckPercent}% mientras suene.";
            SaveQuietly();
        }

        public void SetPadMode(CartPadItem pad, CartPlayMode mode)
        {
            if (pad == null || pad.Mode == mode) return;

            pad.Mode = mode;
            pad.Menu?.RefreshValueLabels();
            StatusText = $"Pad {pad.ShortcutText}: modo {(mode == CartPlayMode.OneShot ? "disparar y dejar" : "inicio/parada")}.";
            SaveQuietly();
        }

        // ------------------------------------------------------------------ contenido del pad

        public void AssignWithGate(CartPadItem pad)
        {
            if (AuthGate.Admin()) AssignCartPad(pad);
        }

        public void ClearWithGate(CartPadItem pad)
        {
            if (AuthGate.Admin()) ClearCartPad(pad);
        }

        public void RenameWithGate(CartPadItem pad)
        {
            if (pad == null) return;
            if (!AuthGate.Admin()) return;

            var owner = Application.Current?.MainWindow;
            var accepted = InputDialog.Ask(owner, $"Nombre del pad {pad.ShortcutText}",
                pad.HasAudio ? Path.GetFileName(pad.FilePath) : "El pad no tiene clip montado.",
                "Es el nombre que se ve en la botonera y en el estado del aire.", pad.Title);

            if (accepted == null)
            {
                StatusText = $"Renombrar {pad.ShortcutText} cancelado.";
                return;
            }

            pad.Title = accepted;
            pad.Menu?.RefreshValueLabels();
            OnPropertyChanged(nameof(PadInventoryText));
            StatusText = $"Pad {pad.ShortcutText} renombrado a «{accepted}».";
            SaveQuietly();
        }

        private void AssignCartPad(CartPadItem? pad)
        {
            if (pad == null) return;

            var openFileDialog = new OpenFileDialog
            {
                Filter = AudioSourceFactory.FileDialogFilter,
                Title = $"Asignar Audio a Pad {pad.ShortcutText}"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                pad.FilePath = openFileDialog.FileName;
                pad.Title = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                pad.Menu?.RefreshValueLabels();
                OnPropertyChanged(nameof(PadInventoryText));
                _logger.Information("Audio asignado a pad {PadId}: {Path}", pad.PadId, pad.FilePath);
                StatusText = $"Pad {pad.ShortcutText} montado con «{pad.Title}».";
                SaveQuietly();
            }
        }

        private void ClearCartPad(CartPadItem? pad)
        {
            if (pad == null) return;
            pad.FilePath = string.Empty;
            pad.Title = pad.FallbackTitle;
            OnPropertyChanged(nameof(PadInventoryText));
            _logger.Information("Pad {PadId} limpiado", pad.PadId);
            StatusText = $"Pad {pad.ShortcutText} vaciado.";
            SaveQuietly();
        }

        /// <summary>
        /// Guarda la botonera sin mover el estado: se llama despues de cada ajuste. Lo que dice el
        /// pie es lo que hizo el operador (volumen, ducking, nombre), y un disco que falla no puede
        /// tapar ese aviso; se anota en el log y se queda el mensaje de la accion.
        /// </summary>
        private void SaveQuietly()
        {
            var result = _padService.Save(CartPads);
            if (result.StartsWith("No se pudo", StringComparison.Ordinal))
            {
                _logger.Warning("La botonera no se guardo: {Message}", result);
            }
        }
    }
}
