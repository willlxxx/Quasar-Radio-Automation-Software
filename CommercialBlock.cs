using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace quasar_radio
{
    public enum TimingMode
    {
        Soft, // Espera a que termine el tema actual
        Hard  // Interrumpe inmediatamente al segundo exacto
    }

    public class CommercialBlock : ViewModelBase
    {
        private string _blockId = Guid.NewGuid().ToString("N")[..8];
        public string BlockId
        {
            get => _blockId;
            set { _blockId = value; OnPropertyChanged(); }
        }

        private string _name = "Tanda Comercial";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private int _triggerMinute = 15; // Minuto de cada hora (ej. 15, 30, 45, 0)
        public int TriggerMinute
        {
            get => _triggerMinute;
            set { _triggerMinute = Math.Clamp(value, 0, 59); OnPropertyChanged(); OnPropertyChanged(nameof(ScheduleText)); }
        }

        private TimingMode _mode = TimingMode.Soft;
        public TimingMode Mode
        {
            get => _mode;
            set { _mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModeText)); }
        }

        public string ModeText => Mode == TimingMode.Hard ? "RÍGIDO (HARD)" : "SUAVE (SOFT)";

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TrackItem> Spots { get; } = new ObservableCollection<TrackItem>();

        public TimeSpan TotalDuration => TimeSpan.FromSeconds(Spots.Sum(s => s.Duration.TotalSeconds));
        public string TotalDurationFormatted => TotalDuration.ToString(@"mm\:ss");

        public string ScheduleText => $"Cada hora a los :{TriggerMinute:D2} min";

        public CommercialBlock()
        {
            Spots.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(TotalDuration));
                OnPropertyChanged(nameof(TotalDurationFormatted));
            };
        }
    }
}
