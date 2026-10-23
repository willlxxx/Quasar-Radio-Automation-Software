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

        public ICommand TriggerCartPadCommand { get; }
        public ICommand AssignCartPadCommand { get; }
        public ICommand ClearCartPadCommand { get; }
        public ICommand StopAllCartPadsCommand { get; }

        public CartWallViewModel(CartwallEngine cartwallEngine)
        {
            _cartwallEngine = cartwallEngine ?? throw new ArgumentNullException(nameof(cartwallEngine));
            _cartwallEngine.PlaybackFailed += (_, message) => StatusText = message;

            bool CanOperate() => App.Auth.IsAuthorized(UserRole.Operator);

            // AUTH-1: disparar un pad es de operador y se hace sin clave. Asignarle un archivo
            // o quitarle el contenido cambia la botonera de toda la emisora, asi que sigue
            // siendo de administrador; lo que cambio es que la cuenta se pide al pulsar.
            bool Admin() => AuthGate.Admin();

            TriggerCartPadCommand = new RelayCommand(o => TriggerCartPad(o), o => CanOperate());
            AssignCartPadCommand = new RelayCommand(o => { if (Admin()) AssignCartPad(o as CartPadItem); });
            ClearCartPadCommand = new RelayCommand(o => { if (Admin()) ClearCartPad(o as CartPadItem); });
            StopAllCartPadsCommand = new RelayCommand(o => _cartwallEngine.StopAll(), o => CanOperate());

            InitializeCartPads();
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
                CartPads.Add(new CartPadItem
                {
                    PadId = i,
                    ShortcutText = $"F{i}",
                    Title = $"Efecto {i}",
                    ColorHex = presetColors[i - 1]
                });
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
        }

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

            if (pad != null)
            {
                if (!pad.HasAudio)
                {
                    AssignCartPad(pad);
                }
                else
                {
                    _cartwallEngine.TogglePad(pad);
                }
            }
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
                _logger.Information("Audio asignado a pad {PadId}: {Path}", pad.PadId, pad.FilePath);
            }
        }

        private void ClearCartPad(CartPadItem? pad)
        {
            if (pad == null) return;
            pad.FilePath = string.Empty;
            pad.Title = $"Efecto {pad.PadId}";
            _logger.Information("Pad {PadId} limpiado", pad.PadId);
        }
    }
}
