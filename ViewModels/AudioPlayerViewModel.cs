using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Serilog;

namespace quasar_radio
{
    public class AudioPlayerViewModel : ViewModelBase
    {
        private readonly ILogger _logger = Log.ForContext<AudioPlayerViewModel>();
        private readonly AudioEngine _audioEngine;
        private readonly PlaybackAttributionService _attribution;
        private readonly DispatcherTimer _playbackTimer;
        private const int SpectrumBarCount = 18;

        public ObservableCollection<TrackItem> Playlist { get; } = new ObservableCollection<TrackItem>();
        public ObservableCollection<double> DeckA_Spectrum { get; } = new(Enumerable.Repeat(2.0, SpectrumBarCount));
        public ObservableCollection<double> DeckB_Spectrum { get; } = new(Enumerable.Repeat(2.0, SpectrumBarCount));

        // DECK A PROPIEDADES
        public string DeckA_Title => _audioEngine.DeckA_Track?.Title ?? "DECK A - VACÍO";
        public string DeckA_Artist => _audioEngine.DeckA_Track?.Artist ?? "Seleccione pista";
        public string DeckA_PositionFormatted => _audioEngine.DeckA_Position.ToString(@"mm\:ss");
        public string DeckA_DurationFormatted => _audioEngine.DeckA_Duration.ToString(@"mm\:ss");
        public string DeckA_RemainingFormatted => "-" + (_audioEngine.DeckA_Duration - _audioEngine.DeckA_Position).ToString(@"mm\:ss");
        public string DeckA_CueInfo => _audioEngine.DeckA_Track == null
            ? "CUE 00:00 / END"
            : $"CUE {(_audioEngine.DeckA_Track.CueIn > TimeSpan.Zero ? _audioEngine.DeckA_Track.CueIn.ToString(@"mm\:ss") : "00:00")} / {(_audioEngine.DeckA_Track.CueOut > TimeSpan.Zero ? _audioEngine.DeckA_Track.CueOut.ToString(@"mm\:ss") : "END")}";
        public double DeckA_Progress => _audioEngine.DeckA_Duration.TotalMilliseconds > 0 ? (_audioEngine.DeckA_Position.TotalMilliseconds / _audioEngine.DeckA_Duration.TotalMilliseconds) * 100.0 : 0;
        public double DeckA_VuLevel => _audioEngine.DeckA_VuLevel;
        public bool DeckA_IsActive => _audioEngine.DeckA_IsActive;
        public string DeckA_StatusText => DeckA_IsActive ? "REPRODUCIENDO" : (_audioEngine.DeckA_Track != null ? "PRE-LOADED (LISTO)" : "EN ESPERA");
        public string DeckA_StatusColor => DeckA_IsActive ? "#00E676" : (_audioEngine.DeckA_Track != null ? "#00E676" : "#555555");

        // DECK B PROPIEDADES
        public string DeckB_Title => _audioEngine.DeckB_Track?.Title ?? "DECK B - VACÍO";
        public string DeckB_Artist => _audioEngine.DeckB_Track?.Artist ?? "Seleccione pista";
        public string DeckB_PositionFormatted => _audioEngine.DeckB_Position.ToString(@"mm\:ss");
        public string DeckB_DurationFormatted => _audioEngine.DeckB_Duration.ToString(@"mm\:ss");
        public string DeckB_RemainingFormatted => "-" + (_audioEngine.DeckB_Duration - _audioEngine.DeckB_Position).ToString(@"mm\:ss");
        public string DeckB_CueInfo => _audioEngine.DeckB_Track == null
            ? "CUE 00:00 / END"
            : $"CUE {(_audioEngine.DeckB_Track.CueIn > TimeSpan.Zero ? _audioEngine.DeckB_Track.CueIn.ToString(@"mm\:ss") : "00:00")} / {(_audioEngine.DeckB_Track.CueOut > TimeSpan.Zero ? _audioEngine.DeckB_Track.CueOut.ToString(@"mm\:ss") : "END")}";
        public double DeckB_Progress => _audioEngine.DeckB_Duration.TotalMilliseconds > 0 ? (_audioEngine.DeckB_Position.TotalMilliseconds / _audioEngine.DeckB_Duration.TotalMilliseconds) * 100.0 : 0;
        public double DeckB_VuLevel => _audioEngine.DeckB_VuLevel;
        public bool DeckB_IsActive => _audioEngine.DeckB_IsActive;
        public string DeckB_StatusText => DeckB_IsActive ? "REPRODUCIENDO" : (_audioEngine.DeckB_Track != null ? "PRE-LOADED (LISTO)" : "EN ESPERA");
        public string DeckB_StatusColor => DeckB_IsActive ? "#00E676" : (_audioEngine.DeckB_Track != null ? "#00E676" : "#555555");

        private TrackItem? _selectedTrack;
        public TrackItem? SelectedTrack
        {
            get => _selectedTrack;
            set
            {
                _selectedTrack = value;
                OnPropertyChanged();
            }
        }

        private TrackItem? _currentTrack;
        public TrackItem? CurrentTrack
        {
            get => _currentTrack;
            set
            {
                _currentTrack = value;
                OnPropertyChanged();
            }
        }

        private TrackItem? _nextTrack;
        public TrackItem? NextTrack
        {
            get => _nextTrack;
            set
            {
                _nextTrack = value;
                OnPropertyChanged();
            }
        }

        private TimeSpan _currentPosition = TimeSpan.Zero;
        public TimeSpan CurrentPosition
        {
            get => _currentPosition;
            set
            {
                _currentPosition = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPositionFormatted));
                OnPropertyChanged(nameof(RemainingTimeFormatted));
                OnPropertyChanged(nameof(ProgressPercentage));
            }
        }

        public string CurrentPositionFormatted => CurrentPosition.ToString(@"mm\:ss");

        private TimeSpan _currentDuration = TimeSpan.Zero;
        public TimeSpan CurrentDuration
        {
            get => _currentDuration;
            set
            {
                _currentDuration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentDurationFormatted));
                OnPropertyChanged(nameof(RemainingTimeFormatted));
                OnPropertyChanged(nameof(ProgressPercentage));
            }
        }

        public string CurrentDurationFormatted => CurrentDuration.ToString(@"mm\:ss");

        public TimeSpan RemainingTime
        {
            get
            {
                TimeSpan rem = CurrentDuration - CurrentPosition;
                return rem < TimeSpan.Zero ? TimeSpan.Zero : rem;
            }
        }

        public string RemainingTimeFormatted => "-" + RemainingTime.ToString(@"mm\:ss");

        public double ProgressPercentage
        {
            get
            {
                if (CurrentDuration.TotalMilliseconds <= 0) return 0;
                return Math.Clamp((CurrentPosition.TotalMilliseconds / CurrentDuration.TotalMilliseconds) * 100.0, 0, 100);
            }
        }

        private bool _isAutoMode = true;
        public bool IsAutoMode
        {
            get => _isAutoMode;
            set
            {
                _isAutoMode = value;
                _audioEngine.IsAutoMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutoModeText));
                OnPropertyChanged(nameof(AutoModeColor));
            }
        }

        public string AutoModeText => IsAutoMode ? "MODO: AUTO" : "MODO: MANUAL";
        public string AutoModeColor => IsAutoMode ? "#00CC66" : "#FF9900";

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

        private float _masterVolume = 1.0f;
        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = value;
                _audioEngine.MasterVolume = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumePercentageText));
            }
        }

        public string VolumePercentageText => $"{(int)(MasterVolume * 100)}%";

        private string _activeDeckName = "DECK A";
        public string ActiveDeckName
        {
            get => _activeDeckName;
            set
            {
                _activeDeckName = value;
                OnPropertyChanged();
            }
        }

        public bool IsPlaying => _audioEngine.IsPlaying;
        public bool ShowOnAirButton => IsPlaying;

        // COMANDOS
        public ICommand PlayCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand ToggleAutoModeCommand { get; }
        public ICommand LoadFilesCommand { get; }
        public ICommand InsertFilesCommand { get; }
        public ICommand LoadFolderCommand { get; }
        public ICommand RemoveTrackCommand { get; }
        public ICommand ClearPlayedTracksCommand { get; }
        public ICommand MoveTrackUpCommand { get; }
        public ICommand MoveTrackDownCommand { get; }
        public ICommand ClearPlaylistCommand { get; }
        public ICommand PlayTrackOnIdleDeckCommand { get; }
        public ICommand DeckAPlayCommand { get; }
        public ICommand DeckAPauseCommand { get; }
        public ICommand DeckACueCommand { get; }
        public ICommand DeckBPlayCommand { get; }
        public ICommand DeckBPauseCommand { get; }
        public ICommand DeckBCueCommand { get; }

        public AudioPlayerViewModel(AudioEngine audioEngine, PlaybackAttributionService attribution)
        {
            _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
            _attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
            _audioEngine.GetNextTrackProvider = GetNextTrackForEngine;
            _audioEngine.TrackChanged += OnEngineTrackChanged;
            _audioEngine.PositionChanged += OnEnginePositionChanged;
            _audioEngine.PlaybackFinished += OnEnginePlaybackFinished;

            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _playbackTimer.Tick += OnPlaybackTimerTick;
            _playbackTimer.Start();

            bool CanOperate() => App.Auth.IsAuthorized(UserRole.Operator);

            PlayCommand = new RelayCommand(o => ExecutePlay(), o => CanOperate() && Playlist.Count > 0);
            StopCommand = new RelayCommand(o => ExecuteStop(), o => CanOperate() && IsPlaying);
            PauseCommand = new RelayCommand(o => ExecutePause(), o => CanOperate() && CurrentTrack != null);
            NextCommand = new RelayCommand(o => ExecuteNext(), o => CanOperate() && IsPlaying && NextTrack != null);
            ToggleAutoModeCommand = new RelayCommand(o => IsAutoMode = !IsAutoMode, o => CanOperate());
            LoadFilesCommand = new RelayCommand(o => LoadFiles(), o => CanOperate());
            InsertFilesCommand = new RelayCommand(o => InsertFiles(), o => CanOperate());
            LoadFolderCommand = new RelayCommand(o => LoadFolder(), o => CanOperate());
            RemoveTrackCommand = new RelayCommand(o => RemoveSelectedTrack(), o => CanOperate() && SelectedTrack != null);
            ClearPlayedTracksCommand = new RelayCommand(o => ClearPlayedTracks(), o => CanOperate() && Playlist.Any(t => t.Status == TrackStatus.Played));
            MoveTrackUpCommand = new RelayCommand(o => MoveSelectedTrackUp(), o => CanOperate() && CanMoveTrackUp());
            MoveTrackDownCommand = new RelayCommand(o => MoveSelectedTrackDown(), o => CanOperate() && CanMoveTrackDown());
            ClearPlaylistCommand = new RelayCommand(o => ClearPlaylist(), o => CanOperate() && Playlist.Count > 0);
            PlayTrackOnIdleDeckCommand = new RelayCommand(o => PlayTrackOnIdleDeck(o), o => CanOperate());
            DeckAPlayCommand = new RelayCommand(o => PlayDeck("A", SelectedTrack ?? CurrentTrack ?? Playlist.FirstOrDefault()), o => CanOperate() && (SelectedTrack != null || CurrentTrack != null || Playlist.Count > 0));
            DeckAPauseCommand = new RelayCommand(o => PauseDeck("A"), o => CanOperate());
            DeckACueCommand = new RelayCommand(o => CueDeck("A"), o => CanOperate());
            DeckBPlayCommand = new RelayCommand(o => PlayDeck("B", SelectedTrack ?? CurrentTrack ?? Playlist.FirstOrDefault()), o => CanOperate() && (SelectedTrack != null || CurrentTrack != null || Playlist.Count > 0));
            DeckBPauseCommand = new RelayCommand(o => PauseDeck("B"), o => CanOperate());
            DeckBCueCommand = new RelayCommand(o => CueDeck("B"), o => CanOperate());
        }

        private void OnPlaybackTimerTick(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(DeckA_Title));
            OnPropertyChanged(nameof(DeckA_Artist));
            OnPropertyChanged(nameof(DeckA_PositionFormatted));
            OnPropertyChanged(nameof(DeckA_DurationFormatted));
            OnPropertyChanged(nameof(DeckA_RemainingFormatted));
            OnPropertyChanged(nameof(DeckA_CueInfo));
            OnPropertyChanged(nameof(DeckA_Progress));
            OnPropertyChanged(nameof(DeckA_VuLevel));
            UpdateSpectrum(DeckA_Spectrum, _audioEngine.DeckA_Spectrum);
            OnPropertyChanged(nameof(DeckA_IsActive));
            OnPropertyChanged(nameof(DeckA_StatusText));
            OnPropertyChanged(nameof(DeckA_StatusColor));

            OnPropertyChanged(nameof(DeckB_Title));
            OnPropertyChanged(nameof(DeckB_Artist));
            OnPropertyChanged(nameof(DeckB_PositionFormatted));
            OnPropertyChanged(nameof(DeckB_DurationFormatted));
            OnPropertyChanged(nameof(DeckB_RemainingFormatted));
            OnPropertyChanged(nameof(DeckB_CueInfo));
            OnPropertyChanged(nameof(DeckB_Progress));
            OnPropertyChanged(nameof(DeckB_VuLevel));
            UpdateSpectrum(DeckB_Spectrum, _audioEngine.DeckB_Spectrum);
            OnPropertyChanged(nameof(DeckB_IsActive));
            OnPropertyChanged(nameof(DeckB_StatusText));
            OnPropertyChanged(nameof(DeckB_StatusColor));
        }

        private static void UpdateSpectrum(ObservableCollection<double> bars, float[] spectrum)
        {
            for (var index = 0; index < bars.Count; index++)
            {
                var level = index < spectrum.Length ? spectrum[index] : 0f;
                var targetHeight = 2.0 + level * 22.0;
                bars[index] += (targetHeight - bars[index]) * 0.55;
            }
        }

        private TrackItem? GetNextTrackForEngine()
        {
            if (Playlist.Count == 0) return null;

            int currentIndex = -1;
            if (CurrentTrack != null)
            {
                currentIndex = Playlist.IndexOf(CurrentTrack);
            }

            if (currentIndex >= 0 && currentIndex < Playlist.Count - 1)
            {
                return Playlist[currentIndex + 1];
            }

            return null;
        }

        private void OnEngineTrackChanged(object? sender, TrackItem? newTrack)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (CurrentTrack != null)
                {
                    CurrentTrack.Status = TrackStatus.Played;
                    // UI-4 paso B: la que deja el aire se cierra con el tiempo que sonó de
                    // verdad, y la que entra se apunta en rotacion, contador y "ahora suena".
                    _attribution.TrackEnded(IsAutoMode);
                }

                CurrentTrack = newTrack;
                ActiveDeckName = _audioEngine.ActiveDeckName;

                if (CurrentTrack != null)
                {
                    CurrentTrack.Status = TrackStatus.Playing;
                    CurrentDuration = CurrentTrack.Duration;
                    _attribution.TrackStarted(CurrentTrack, IsAutoMode);
                }

                UpdateNextTrackAndStatuses();
                UpdateStatusText();
                OnPropertyChanged(nameof(IsPlaying));
            });
        }

        private void OnEnginePositionChanged(object? sender, TimeSpan pos)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentPosition = pos;
                if (_audioEngine.CurrentDuration > TimeSpan.Zero)
                {
                    CurrentDuration = _audioEngine.CurrentDuration;
                }
            });
        }

        private void OnEnginePlaybackFinished(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (CurrentTrack != null)
                {
                    CurrentTrack.Status = TrackStatus.Played;
                    _attribution.TrackEnded(IsAutoMode);
                }
                _attribution.ClearNowPlaying();
                CurrentTrack = null;
                NextTrack = null;
                CurrentPosition = TimeSpan.Zero;
                CurrentDuration = TimeSpan.Zero;
                UpdateStatusText();
                OnPropertyChanged(nameof(IsPlaying));
            });
        }

        private void ExecutePlay()
        {
            try
            {
                TrackItem? trackToPlay = SelectedTrack ?? Playlist.FirstOrDefault(t => t.Status != TrackStatus.Played) ?? Playlist.FirstOrDefault();
                if (trackToPlay == null)
                {
                    StatusText = "ESTADO: No hay pistas en la lista";
                    return;
                }

                if (_audioEngine.PlayTrack(trackToPlay))
                {
                    UpdateStatusText();
                    _logger.Information("Reproduciendo pista: {Title}", trackToPlay.Title);
                }
                else
                {
                    StatusText = _audioEngine.LastError ?? "No se pudo reproducir la pista seleccionada.";
                    _logger.Warning("No se pudo reproducir la pista: {Title}", trackToPlay.Title);
                }
            }
            catch (Exception ex)
            {
                StatusText = "ERROR CRÍTICO en reproducción";
                _logger.Error(ex, "Error al intentar reproducir pista");
            }
        }

        private void ExecuteStop()
        {
            try
            {
                _audioEngine.StopAll();
                if (CurrentTrack != null)
                {
                    CurrentTrack.Status = TrackStatus.Played;
                    // UI-4 paso B: parar tambien cierra la emision, con el tiempo que llego
                    // a sonar. Un corte a los 15 segundos apunta 15 segundos, no 3 minutos.
                    _attribution.TrackEnded(IsAutoMode);
                }
                _attribution.ClearNowPlaying();
                CurrentTrack = null;
                NextTrack = null;
                CurrentPosition = TimeSpan.Zero;
                CurrentDuration = TimeSpan.Zero;
                UpdateStatusText();
                OnPropertyChanged(nameof(IsPlaying));
                _logger.Information("Emisión detenida correctamente");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al detener la emisión");
                StatusText = "ERROR: No se pudo detener la emisión";
            }
        }

        private void ExecutePause()
        {
            _audioEngine.Pause();
            UpdateStatusText();
            OnPropertyChanged(nameof(IsPlaying));
        }

        private void PlayDeck(string deckName, TrackItem? track)
        {
            var deckTrack = deckName.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? _audioEngine.DeckB_Track
                : _audioEngine.DeckA_Track;

            var chosenTrack = track ?? deckTrack ?? SelectedTrack ?? Playlist.FirstOrDefault();

            if (chosenTrack == null)
            {
                StatusText = "Selecciona una pista para cargar en el deck.";
                return;
            }

            if (deckTrack != null && !string.Equals(deckTrack.FilePath, chosenTrack.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                chosenTrack = deckTrack;
            }

            if (_audioEngine.PlayTrackOnDeck(deckName, chosenTrack))
            {
                StatusText = $"Deck {deckName}: {chosenTrack.Title}";
                _logger.Information("Track cargada en {DeckName}: {Title}", deckName, chosenTrack.Title);
            }
            else
            {
                StatusText = _audioEngine.LastError ?? "No se pudo cargar la pista.";
            }
        }

        private void PauseDeck(string deckName)
        {
            _audioEngine.PauseDeck(deckName);
            UpdateStatusText();
            OnPropertyChanged(nameof(IsPlaying));
        }

        private void CueDeck(string deckName)
        {
            _audioEngine.CueDeck(deckName);
            StatusText = $"Cue {deckName} aplicado.";
        }

        private void ExecuteNext()
        {
            try
            {
                _audioEngine.TriggerCrossfadeToNext();
                _logger.Information("Crossfade al siguiente track iniciado");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al hacer crossfade al siguiente track");
                StatusText = "ERROR: No se pudo avanzar al siguiente track";
            }
        }

        private void PlayTrackOnIdleDeck(object? parameter)
        {
            if (parameter is not TrackItem track)
            {
                return;
            }

            if (_audioEngine.PlayTrackOnIdleDeck(track))
            {
                StatusText = $"Deck libre: {track.Title}";
            }
            else
            {
                StatusText = _audioEngine.LastError ?? $"No se pudo cargar {track.Title} en el deck libre.";
            }
        }

        private void LoadFiles()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = AudioSourceFactory.FileDialogFilter,
                    Multiselect = true,
                    Title = "Seleccionar Música para Quasar Radio"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    foreach (string file in openFileDialog.FileNames)
                    {
                        Playlist.Add(TrackLoader.CreateFromFile(file));
                    }
                    UpdateNextTrackAndStatuses();
                    StatusText = $"Cargadas {openFileDialog.FileNames.Length} pistas a la lista.";
                    _logger.Information("Cargadas {Count} pistas desde archivo", openFileDialog.FileNames.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al cargar archivos de audio");
                StatusText = "ERROR: No se pudieron cargar los archivos";
            }
        }

        private void InsertFiles()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = AudioSourceFactory.FileDialogFilter,
                    Multiselect = true,
                    Title = "Insertar Música en Posición Selección"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    int insertIndex = SelectedTrack != null ? Playlist.IndexOf(SelectedTrack) + 1 : (CurrentTrack != null ? Playlist.IndexOf(CurrentTrack) + 1 : 0);

                    foreach (string file in openFileDialog.FileNames)
                    {
                        Playlist.Insert(insertIndex++, TrackLoader.CreateFromFile(file));
                    }
                    UpdateNextTrackAndStatuses();
                    StatusText = $"Insertadas {openFileDialog.FileNames.Length} pistas a la lista.";
                    _logger.Information("Insertadas {Count} pistas en posición selección", openFileDialog.FileNames.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al insertar archivos de audio");
                StatusText = "ERROR: No se pudieron insertar los archivos";
            }
        }

        public void LoadFolder(string? defaultPath = null)
        {
            string folderPath = defaultPath ?? string.Empty;

            if (string.IsNullOrEmpty(folderPath))
            {
                var folderDialog = new OpenFolderDialog
                {
                    Title = "Seleccionar carpeta de música"
                };

                if (folderDialog.ShowDialog() == true)
                {
                    folderPath = folderDialog.FolderName;
                }
                else
                {
                    return;
                }
            }

            if (!Directory.Exists(folderPath))
            {
                if (defaultPath != null) Directory.CreateDirectory(folderPath);
                return;
            }

            var files = Directory.GetFiles(folderPath, "*.*")
                                 .Where(AudioSourceFactory.IsSupported);

            int count = 0;
            foreach (var file in files)
            {
                Playlist.Add(TrackLoader.CreateFromFile(file));
                count++;
            }

            UpdateNextTrackAndStatuses();
            StatusText = $"Cargadas {count} canciones desde carpeta.";
        }

        private void UpdateNextTrackAndStatuses()
        {
            if (Playlist.Count == 0)
            {
                NextTrack = null;
                return;
            }

            int currentIndex = CurrentTrack != null ? Playlist.IndexOf(CurrentTrack) : -1;

            if (currentIndex >= 0 && currentIndex < Playlist.Count - 1)
            {
                NextTrack = Playlist[currentIndex + 1];
            }
            else if (currentIndex == -1)
            {
                NextTrack = Playlist.FirstOrDefault();
            }
            else
            {
                NextTrack = null;
            }

            if (NextTrack != null)
            {
                _audioEngine.PreloadNextTrack(NextTrack);
            }

            foreach (var item in Playlist)
            {
                if (item == CurrentTrack)
                    item.Status = TrackStatus.Playing;
                else if (item == NextTrack)
                    item.Status = TrackStatus.Next;
                else if (Playlist.IndexOf(item) < currentIndex)
                    item.Status = TrackStatus.Played;
                else
                    item.Status = TrackStatus.Queued;
            }
        }

        private void RemoveSelectedTrack()
        {
            if (SelectedTrack == null) return;

            int index = Playlist.IndexOf(SelectedTrack);
            Playlist.Remove(SelectedTrack);

            if (Playlist.Count > 0)
            {
                SelectedTrack = Playlist[Math.Min(index, Playlist.Count - 1)];
            }

            UpdateNextTrackAndStatuses();
        }

        private void ClearPlayedTracks()
        {
            var playedList = Playlist.Where(t => t.Status == TrackStatus.Played).ToList();
            foreach (var track in playedList)
            {
                Playlist.Remove(track);
            }
            UpdateNextTrackAndStatuses();
            StatusText = $"Limpiados {playedList.Count} temas emitidos.";
        }

        private bool CanMoveTrackUp() => SelectedTrack != null && Playlist.IndexOf(SelectedTrack) > 0;

        private void MoveSelectedTrackUp()
        {
            if (SelectedTrack == null) return;
            int index = Playlist.IndexOf(SelectedTrack);
            if (index > 0)
            {
                Playlist.Move(index, index - 1);
                UpdateNextTrackAndStatuses();
            }
        }

        private bool CanMoveTrackDown() => SelectedTrack != null && Playlist.IndexOf(SelectedTrack) < Playlist.Count - 1;

        private void MoveSelectedTrackDown()
        {
            if (SelectedTrack == null) return;
            int index = Playlist.IndexOf(SelectedTrack);
            if (index < Playlist.Count - 1)
            {
                Playlist.Move(index, index + 1);
                UpdateNextTrackAndStatuses();
            }
        }

        private void ClearPlaylist()
        {
            ExecuteStop();
            Playlist.Clear();
            SelectedTrack = null;
            CurrentTrack = null;
            NextTrack = null;
            StatusText = "ESTADO: LISTA VACÍA";
        }

        private void UpdateStatusText()
        {
            if (IsPlaying)
            {
                StatusText = $"AL AIRE ({ActiveDeckName}): {CurrentTrack?.Title} [{AutoModeText}]";
            }
            else
            {
                StatusText = $"ESTADO: DETENIDO [{AutoModeText}]";
            }
        }
    }
}
