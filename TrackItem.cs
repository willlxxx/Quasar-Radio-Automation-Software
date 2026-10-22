using System;

namespace quasar_radio
{
    public enum TrackType
    {
        Music,
        Jingle,
        Commercial,
        Sweeper,
        TimeAnnouncement,
        Voice
    }

    public enum TrackStatus
    {
        Queued,
        Next,
        Playing,
        Played
    }

    public class TrackItem : ViewModelBase
    {
        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private string _artist = "Artista Desconocido";
        public string Artist
        {
            get => _artist;
            set { _artist = value; OnPropertyChanged(); }
        }

        private string _album = string.Empty;
        public string Album
        {
            get => _album;
            set { _album = value; OnPropertyChanged(); }
        }

        private string _genre = "General";
        public string Genre
        {
            get => _genre;
            set { _genre = value; OnPropertyChanged(); }
        }

        private int _year = DateTime.Now.Year;
        public int Year
        {
            get => _year;
            set { _year = Math.Clamp(value, 1900, DateTime.Now.Year + 1); OnPropertyChanged(); }
        }

        private int _priority = 5;
        public int Priority
        {
            get => _priority;
            set { _priority = Math.Clamp(value, 1, 10); OnPropertyChanged(); OnPropertyChanged(nameof(PriorityFormatted)); }
        }

        public string PriorityFormatted => Priority switch
        {
            >= 9 => "🔴 Alta",
            >= 7 => "🟠 Media-Alta",
            >= 5 => "🟡 Media",
            >= 3 => "🟢 Baja",
            _ => "⚪ Muy Baja"
        };

        private double _bpm = 120.0;
        public double Bpm
        {
            get => _bpm;
            set 
            { 
                _bpm = Math.Clamp(value, 0.0, 300.0); 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(BpmFormatted));
            }
        }

        public string BpmFormatted => Bpm > 0 ? $"{Bpm:0} BPM" : "-- BPM";

        private string _filePath = string.Empty;
        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        private TimeSpan _duration = TimeSpan.Zero;
        public TimeSpan Duration
        {
            get => _duration;
            set 
            { 
                _duration = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(DurationFormatted));
            }
        }

        public string DurationFormatted => Duration.ToString(@"mm\:ss");

        private TrackType _type = TrackType.Music;
        public TrackType Type
        {
            get => _type;
            set 
            { 
                _type = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IconSymbol));
            }
        }

        public string IconSymbol => Type switch
        {
            TrackType.Jingle => "📻",
            TrackType.Commercial => "📢",
            TrackType.Sweeper => "⚡",
            TrackType.TimeAnnouncement => "🕒",
            TrackType.Voice => "🗣",
            _ => "🎵"
        };

        private TrackStatus _status = TrackStatus.Queued;
        public TrackStatus Status
        {
            get => _status;
            set 
            { 
                _status = value; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public string StatusText => Status switch
        {
            TrackStatus.Playing => "AL AIRE",
            TrackStatus.Next => "SIGUIENTE",
            TrackStatus.Played => "EMITIDO",
            _ => "EN COLA"
        };

        public string StatusColor => Status switch
        {
            TrackStatus.Playing => "#FF3333", // Rojo vivo
            TrackStatus.Next => "#00CC66",    // Verde emisión
            TrackStatus.Played => "#555555",   // Gris apagado
            _ => "#888888"                    // Gris neutro
        };

        private DateTime? _estimatedPlayTime;
        public DateTime? EstimatedPlayTime
        {
            get => _estimatedPlayTime;
            set 
            { 
                _estimatedPlayTime = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(EstimatedPlayTimeFormatted));
            }
        }

        public string EstimatedPlayTimeFormatted => EstimatedPlayTime?.ToString("HH:mm:ss") ?? "--:--:--";

        // Puntos CUE (en segundos o TimeSpan)
        private TimeSpan _cueIn = TimeSpan.Zero;
        public TimeSpan CueIn
        {
            get => _cueIn;
            set { _cueIn = value; OnPropertyChanged(); OnPropertyChanged(nameof(CueInSeconds)); }
        }

        public double CueInSeconds
        {
            get => CueIn.TotalSeconds;
            set => CueIn = TimeSpan.FromSeconds(Math.Max(0, value));
        }

        private TimeSpan _intro = TimeSpan.Zero;
        public TimeSpan Intro
        {
            get => _intro;
            set { _intro = value; OnPropertyChanged(); OnPropertyChanged(nameof(IntroSeconds)); }
        }

        public double IntroSeconds
        {
            get => Intro.TotalSeconds;
            set => Intro = TimeSpan.FromSeconds(Math.Max(0, value));
        }

        private TimeSpan _cueOut = TimeSpan.Zero;
        public TimeSpan CueOut
        {
            get => _cueOut;
            set { _cueOut = value; OnPropertyChanged(); OnPropertyChanged(nameof(CueOutSeconds)); }
        }

        public double CueOutSeconds
        {
            get => CueOut.TotalSeconds;
            set => CueOut = TimeSpan.FromSeconds(Math.Max(0, value));
        }

        private TimeSpan _endCue = TimeSpan.Zero;
        public TimeSpan EndCue
        {
            get => _endCue;
            set { _endCue = value; OnPropertyChanged(); OnPropertyChanged(nameof(EndCueSeconds)); }
        }

        public double EndCueSeconds
        {
            get => EndCue.TotalSeconds;
            set => EndCue = TimeSpan.FromSeconds(Math.Max(0, value));
        }

        // Fade In/Out (en segundos)
        private double _fadeIn = 0.0;
        public double FadeIn
        {
            get => _fadeIn;
            set { _fadeIn = Math.Max(0, value); OnPropertyChanged(); OnPropertyChanged(nameof(FadeInFormatted)); }
        }

        public string FadeInFormatted => FadeIn > 0 ? $"{FadeIn:F2}s" : "Off";

        private double _fadeOut = 0.0;
        public double FadeOut
        {
            get => _fadeOut;
            set { _fadeOut = Math.Max(0, value); OnPropertyChanged(); OnPropertyChanged(nameof(FadeOutFormatted)); }
        }

        public string FadeOutFormatted => FadeOut > 0 ? $"{FadeOut:F2}s" : "Off";

        // Outro (en segundos)
        private TimeSpan _outro = TimeSpan.Zero;
        public TimeSpan Outro
        {
            get => _outro;
            set { _outro = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutroSeconds)); }
        }

        public double OutroSeconds
        {
            get => Outro.TotalSeconds;
            set => Outro = TimeSpan.FromSeconds(Math.Max(0, value));
        }

        // Puntos CUE adicionales para múltiples puntos de referencia
        private List<TimeSpan> _cuePoints = new List<TimeSpan>();
        public List<TimeSpan> CuePoints
        {
            get => _cuePoints;
            set { _cuePoints = value; OnPropertyChanged(); OnPropertyChanged(nameof(CuePointsFormatted)); }
        }

        public string CuePointsFormatted => CuePoints.Count > 0 
            ? string.Join(", ", CuePoints.Select(c => c.ToString(@"mm\:ss"))) 
            : "Ninguno";

        // Metadatos adicionales
        private string _composer = string.Empty;
        public string Composer
        {
            get => _composer;
            set { _composer = value; OnPropertyChanged(); }
        }

        private string _copyright = string.Empty;
        public string Copyright
        {
            get => _copyright;
            set { _copyright = value; OnPropertyChanged(); }
        }

        private string _comment = string.Empty;
        public string Comment
        {
            get => _comment;
            set { _comment = value; OnPropertyChanged(); }
        }

        private int _playCount = 0;
        public int PlayCount
        {
            get => _playCount;
            set { _playCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(LastPlayedFormatted)); }
        }

        private DateTime? _lastPlayed;
        public DateTime? LastPlayed
        {
            get => _lastPlayed;
            set { _lastPlayed = value; OnPropertyChanged(); OnPropertyChanged(nameof(LastPlayedFormatted)); }
        }

        public string LastPlayedFormatted => LastPlayed?.ToString("dd/MM/yyyy HH:mm") ?? "Nunca";

        public override string ToString()
        {
            return $"{IconSymbol} {Title} - {Artist} ({DurationFormatted})";
        }
    }
}
