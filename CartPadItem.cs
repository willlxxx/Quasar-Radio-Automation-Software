using System;
using System.IO;

namespace quasar_radio
{
    public class CartPadItem : ViewModelBase
    {
        private int _padId;
        public int PadId
        {
            get => _padId;
            set { _padId = value; OnPropertyChanged(); }
        }

        private string _shortcutText = string.Empty;
        public string ShortcutText
        {
            get => _shortcutText;
            set { _shortcutText = value; OnPropertyChanged(); }
        }

        private string _title = "Vacío";
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private string _filePath = string.Empty;
        public string FilePath
        {
            get => _filePath;
            set 
            { 
                _filePath = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(HasAudio));
            }
        }

        public bool HasAudio => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

        private string _colorHex = "#262D3D";
        public string ColorHex
        {
            get => _colorHex;
            set { _colorHex = value; OnPropertyChanged(); }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set 
            { 
                _isPlaying = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(StatusBorderColor));
            }
        }

        public string StatusBorderColor => IsPlaying ? "#00E676" : "#262D3D";

        private float _volume = 1.0f;
        public float Volume
        {
            get => _volume;
            set { _volume = Math.Clamp(value, 0.0f, 1.0f); OnPropertyChanged(); }
        }
    }
}
