using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Serilog;

namespace quasar_radio
{
    public class LibraryViewModel : ViewModelBase
    {
        private readonly ILogger _logger = Log.ForContext<LibraryViewModel>();
        private readonly AudioDatabaseService _databaseService;
        private readonly SmartRotationEngine _rotationEngine;
        private readonly ObservableCollection<TrackItem> _playlist;

        public AudioDatabaseService DatabaseService => _databaseService;
        public SmartRotationEngine RotationEngine => _rotationEngine;

        private TrackItem? _selectedDbTrack;
        public TrackItem? SelectedDbTrack
        {
            get => _selectedDbTrack;
            set
            {
                _selectedDbTrack = value;
                OnPropertyChanged();
            }
        }

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

        public ICommand AddTrackFromDbToPlaylistCommand { get; }
        public ICommand GenerateAutoPlaylistCommand { get; }
        public ICommand ClearRotationHistoryCommand { get; }
        public ICommand GenerateSmartPlaylistCommand { get; }
        public ICommand OpenLibraryManagementCommand { get; }

        public LibraryViewModel(
            AudioDatabaseService databaseService,
            SmartRotationEngine rotationEngine,
            ObservableCollection<TrackItem> playlist)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _rotationEngine = rotationEngine ?? throw new ArgumentNullException(nameof(rotationEngine));
            _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));

            bool CanOperate() => App.Auth.IsAuthorized(UserRole.Operator);
            bool IsAdmin() => App.Auth.IsAuthorized(UserRole.Admin);

            AddTrackFromDbToPlaylistCommand = new RelayCommand(o => AddDbTrackToPlaylist(o as TrackItem ?? SelectedDbTrack), o => CanOperate());
            GenerateAutoPlaylistCommand = new RelayCommand(o => GenerateAutoPlaylist(), o => CanOperate());
            ClearRotationHistoryCommand = new RelayCommand(o => ClearRotationHistory(), o => IsAdmin());
            GenerateSmartPlaylistCommand = new RelayCommand(o => GenerateSmartPlaylist(), o => CanOperate());
            OpenLibraryManagementCommand = new RelayCommand(o => OpenLibraryManagement(), o => IsAdmin());

            _databaseService.LoadLibrary();
        }

        private void AddDbTrackToPlaylist(TrackItem? track)
        {
            if (track == null) return;
            _playlist.Add(track);
            StatusText = $"Añadido a la cola: {track.Title}";
            _logger.Information("Track añadido desde DB: {Title}", track.Title);
        }

        private void GenerateAutoPlaylist()
        {
            if (_databaseService.AllTracks.Count == 0) return;

            var musicTracks = _databaseService.AllTracks.Where(t => t.Type == TrackType.Music).ToList();
            var jingles = _databaseService.AllTracks.Where(t => t.Type == TrackType.Jingle).ToList();

            if (musicTracks.Count == 0) return;

            _playlist.Clear();

            var rand = new Random();
            for (int i = 0; i < Math.Min(15, musicTracks.Count); i++)
            {
                _playlist.Add(musicTracks[rand.Next(musicTracks.Count)]);

                if (i % 3 == 0 && jingles.Count > 0)
                {
                    _playlist.Add(jingles[rand.Next(jingles.Count)]);
                }
            }

            StatusText = $"Generada lista automática con {_playlist.Count} elementos.";
            _logger.Information("Generada lista automática con {Count} elementos", _playlist.Count);
        }

        private void GenerateSmartPlaylist()
        {
            if (_databaseService.AllTracks.Count == 0) return;

            var musicTracks = _databaseService.AllTracks.Where(t => t.Type == TrackType.Music).ToList();
            var jingles = _databaseService.AllTracks.Where(t => t.Type == TrackType.Jingle).ToList();

            if (musicTracks.Count == 0) return;

            _playlist.Clear();

            var smartMusicTracks = _rotationEngine.GetSmartRotation(musicTracks, Math.Min(15, musicTracks.Count));

            foreach (var track in smartMusicTracks)
            {
                _playlist.Add(track);
            }

            var smartJingles = _rotationEngine.GetSmartRotation(jingles, Math.Min(5, jingles.Count));
            int jingleIndex = 0;
            for (int i = 0; i < _playlist.Count && jingleIndex < smartJingles.Count; i++)
            {
                if (i % 3 == 0)
                {
                    _playlist.Insert(i, smartJingles[jingleIndex++]);
                }
            }

            StatusText = $"Generada lista inteligente con {_playlist.Count} elementos (sin repeticiones).";
            _logger.Information("Generada lista inteligente con {Count} elementos", _playlist.Count);
        }

        private void ClearRotationHistory()
        {
            _rotationEngine.ClearHistory();
            StatusText = "Historial de rotación limpiado correctamente.";
            _logger.Information("Historial de rotación limpiado");
        }

        private void OpenLibraryManagement()
        {
            var libraryWindow = new LibraryManagementWindow
            {
                Owner = Application.Current.MainWindow
            };
            libraryWindow.ShowDialog();

            _databaseService.LoadLibrary();
            StatusText = "Gestión de librería cerrada. Base de datos actualizada.";
            _logger.Information("Gestión de librería cerrada");
        }
    }
}
