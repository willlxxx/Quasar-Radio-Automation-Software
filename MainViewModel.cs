using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Serilog;

namespace quasar_radio
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ILogger _logger = Log.ForContext<MainViewModel>();
        private readonly AudioEngine _audioEngine;
        private readonly CartwallEngine _cartwallEngine;
        private readonly CommercialScheduler _commercialScheduler;
        private readonly MicEngine _micEngine;
        private readonly SmartRotationEngine _rotationEngine;
        private readonly SilenceDetector _silenceDetector;
        private readonly StatisticsService _statisticsService;
        private readonly RecordingService _recordingService;
        private readonly AuxPlayerEngine _auxPlayerEngine;
        private readonly DispatcherTimer _clockTimer;
        private readonly AudioDatabaseService _databaseService;

        // Sub-ViewModels
        public ThemeViewModel ThemeViewModel { get; }
        public AudioPlayerViewModel AudioPlayerViewModel { get; }
        public StreamingViewModel StreamingViewModel { get; }
        public CartWallViewModel CartWallViewModel { get; }
        public LibraryViewModel LibraryViewModel { get; }

        // Colecciones expuestas (para compatibilidad con XAML existente)
        public ObservableCollection<TrackItem> Playlist => AudioPlayerViewModel.Playlist;
        public ObservableCollection<CartPadItem> CartPads => CartWallViewModel.CartPads;

        // Propiedades de AudioPlayerViewModel expuestas para compatibilidad
        public TrackItem? SelectedTrack
        {
            get => AudioPlayerViewModel.SelectedTrack;
            set => AudioPlayerViewModel.SelectedTrack = value;
        }
        public TrackItem? CurrentTrack => AudioPlayerViewModel.CurrentTrack;
        public TrackItem? NextTrack => AudioPlayerViewModel.NextTrack;
        public TimeSpan CurrentPosition => AudioPlayerViewModel.CurrentPosition;
        public TimeSpan CurrentDuration => AudioPlayerViewModel.CurrentDuration;
        public string CurrentPositionFormatted => AudioPlayerViewModel.CurrentPositionFormatted;
        public string CurrentDurationFormatted => AudioPlayerViewModel.CurrentDurationFormatted;
        public string RemainingTimeFormatted => AudioPlayerViewModel.RemainingTimeFormatted;
        public double ProgressPercentage => AudioPlayerViewModel.ProgressPercentage;
        public bool IsPlaying => AudioPlayerViewModel.IsPlaying;

        /// <summary>
        /// Hay algo en marcha (playout, micro, grabacion o stream). Lo usa el cierre
        /// protegido de la consola: apagar el programa con aire activo pide confirmacion.
        /// </summary>
        public bool IsAirActive() =>
            IsPlaying || IsMicActive || RecordingService.IsRecording || StreamingService.IsStreaming;

        public bool IsAutoMode
        {
            get => AudioPlayerViewModel.IsAutoMode;
            set => AudioPlayerViewModel.IsAutoMode = value;
        }
        public string AutoModeText => AudioPlayerViewModel.AutoModeText;
        public string AutoModeColor => AudioPlayerViewModel.AutoModeColor;
        public float MasterVolume
        {
            get => AudioPlayerViewModel.MasterVolume;
            set => AudioPlayerViewModel.MasterVolume = value;
        }
        public string VolumePercentageText => AudioPlayerViewModel.VolumePercentageText;
        public string ActiveDeckName => AudioPlayerViewModel.ActiveDeckName;
        public bool ShowOnAirButton => AudioPlayerViewModel.ShowOnAirButton;
        public string StatusText
        {
            get => AudioPlayerViewModel.StatusText;
            set => AudioPlayerViewModel.StatusText = value;
        }

        // Propiedades de Deck A expuestas
        public string DeckA_Title => AudioPlayerViewModel.DeckA_Title;
        public string DeckA_Artist => AudioPlayerViewModel.DeckA_Artist;
        public string DeckA_PositionFormatted => AudioPlayerViewModel.DeckA_PositionFormatted;
        public string DeckA_DurationFormatted => AudioPlayerViewModel.DeckA_DurationFormatted;
        public string DeckA_RemainingFormatted => AudioPlayerViewModel.DeckA_RemainingFormatted;
        public double DeckA_Progress => AudioPlayerViewModel.DeckA_Progress;
        public ObservableCollection<double> DeckA_Spectrum => AudioPlayerViewModel.DeckA_Spectrum;
        public bool DeckA_IsActive => AudioPlayerViewModel.DeckA_IsActive;
        public string DeckA_StatusText => AudioPlayerViewModel.DeckA_StatusText;
        public string DeckA_StatusColor => AudioPlayerViewModel.DeckA_StatusColor;

        // Propiedades de Deck B expuestas
        public string DeckB_Title => AudioPlayerViewModel.DeckB_Title;
        public string DeckB_Artist => AudioPlayerViewModel.DeckB_Artist;
        public string DeckB_PositionFormatted => AudioPlayerViewModel.DeckB_PositionFormatted;
        public string DeckB_DurationFormatted => AudioPlayerViewModel.DeckB_DurationFormatted;
        public string DeckB_RemainingFormatted => AudioPlayerViewModel.DeckB_RemainingFormatted;
        public double DeckB_Progress => AudioPlayerViewModel.DeckB_Progress;
        public ObservableCollection<double> DeckB_Spectrum => AudioPlayerViewModel.DeckB_Spectrum;
        public bool DeckB_IsActive => AudioPlayerViewModel.DeckB_IsActive;
        public string DeckB_StatusText => AudioPlayerViewModel.DeckB_StatusText;
        public string DeckB_StatusColor => AudioPlayerViewModel.DeckB_StatusColor;

        // Propiedades de ThemeViewModel expuestas
        public string StationLogoPath
        {
            get => ThemeViewModel.StationLogoPath;
            set => ThemeViewModel.StationLogoPath = value;
        }
        public bool HasCustomLogo => ThemeViewModel.HasCustomLogo;
        public string SelectedTheme
        {
            get => ThemeViewModel.SelectedTheme;
            set => ThemeViewModel.SelectedTheme = value;
        }
        public IReadOnlyList<string> AvailableThemes => ThemeViewModel.AvailableThemes;

        // Propiedades de CartWallViewModel expuestas
        public float CartwallMasterVolume
        {
            get => CartWallViewModel.CartwallMasterVolume;
            set => CartWallViewModel.CartwallMasterVolume = value;
        }
        public string CartwallVolumePercentageText => CartWallViewModel.CartwallVolumePercentageText;

        // Propiedades de StreamingViewModel expuestas
        public IcecastStreamingService StreamingService => StreamingViewModel.StreamingService;
        public string CurrentNowPlayingText
        {
            get => StreamingViewModel.CurrentNowPlayingText;
            set => StreamingViewModel.CurrentNowPlayingText = value;
        }

        // Propiedades de LibraryViewModel expuestas
        public TrackItem? SelectedDbTrack
        {
            get => LibraryViewModel.SelectedDbTrack;
            set => LibraryViewModel.SelectedDbTrack = value;
        }
        public AudioDatabaseService DatabaseService => _databaseService;

        // Comandos de AudioPlayerViewModel expuestos
        public ICommand PlayCommand => AudioPlayerViewModel.PlayCommand;
        public ICommand StopCommand => AudioPlayerViewModel.StopCommand;
        public ICommand PauseCommand => AudioPlayerViewModel.PauseCommand;
        public ICommand NextCommand => AudioPlayerViewModel.NextCommand;
        public ICommand ToggleAutoModeCommand => AudioPlayerViewModel.ToggleAutoModeCommand;
        public ICommand LoadFilesCommand => AudioPlayerViewModel.LoadFilesCommand;
        public ICommand InsertFilesCommand => AudioPlayerViewModel.InsertFilesCommand;
        public ICommand LoadFolderCommand => AudioPlayerViewModel.LoadFolderCommand;
        public ICommand RemoveTrackCommand => AudioPlayerViewModel.RemoveTrackCommand;
        public ICommand ClearPlayedTracksCommand => AudioPlayerViewModel.ClearPlayedTracksCommand;
        public ICommand MoveTrackUpCommand => AudioPlayerViewModel.MoveTrackUpCommand;
        public ICommand MoveTrackDownCommand => AudioPlayerViewModel.MoveTrackDownCommand;
        public ICommand ClearPlaylistCommand => AudioPlayerViewModel.ClearPlaylistCommand;
        public ICommand PlayTrackOnIdleDeckCommand => AudioPlayerViewModel.PlayTrackOnIdleDeckCommand;

        // Comandos de ThemeViewModel expuestos
        public ICommand ChangeStationLogoCommand => ThemeViewModel.ChangeStationLogoCommand;

        // Comandos de CartWallViewModel expuestos
        public ICommand TriggerCartPadCommand => CartWallViewModel.TriggerCartPadCommand;
        public ICommand AssignCartPadCommand => CartWallViewModel.AssignCartPadCommand;
        public ICommand ClearCartPadCommand => CartWallViewModel.ClearCartPadCommand;
        public ICommand StopAllCartPadsCommand => CartWallViewModel.StopAllCartPadsCommand;

        // Comandos de LibraryViewModel expuestos
        public ICommand AddTrackFromDbToPlaylistCommand => LibraryViewModel.AddTrackFromDbToPlaylistCommand;
        public ICommand GenerateAutoPlaylistCommand => LibraryViewModel.GenerateAutoPlaylistCommand;
        public ICommand ClearRotationHistoryCommand => LibraryViewModel.ClearRotationHistoryCommand;
        public ICommand GenerateSmartPlaylistCommand => LibraryViewModel.GenerateSmartPlaylistCommand;
        public ICommand OpenLibraryManagementCommand => LibraryViewModel.OpenLibraryManagementCommand;

        // Comandos de StreamingViewModel expuestos
        public ICommand ToggleStreamingCommand => StreamingViewModel.ToggleStreamingCommand;
        public ICommand TestIcecastConnectionCommand => StreamingViewModel.TestIcecastConnectionCommand;

        // Servicios expuestos
        public AudioDspProcessor DspProcessor => _audioEngine.DspProcessor;
        public SilenceDetector SilenceDetector => _silenceDetector;
        public RecordingService RecordingService => _recordingService;
        public StatisticsService StatisticsService => _statisticsService;
        public AuxPlayerEngine AuxPlayers => _auxPlayerEngine;
        public SmartRotationEngine RotationEngine => _rotationEngine;

        // Colecciones expuestas
        public ObservableCollection<CommercialBlock> CommercialBlocks => _commercialScheduler.Blocks;
        public ObservableCollection<string> RecordingFiles { get; } = new ObservableCollection<string>();

        // Estado global
        public bool IsAdmin => App.Auth.IsAuthorized(UserRole.Admin);
        public bool CanOperate => App.Auth.IsAuthorized(UserRole.Operator);

        public void RefreshAuthorizationState()
        {
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(CanOperate));
            CommandManager.InvalidateRequerySuggested();
        }

        // Reloj y timer
        private string _studioClock = DateTime.Now.ToString("HH:mm:ss");
        public string StudioClock
        {
            get => _studioClock;
            set
            {
                _studioClock = value;
                OnPropertyChanged();
            }
        }

        private string _nextCommercialCountdown = "SIN TANDAS";
        public string NextCommercialCountdown
        {
            get => _nextCommercialCountdown;
            set
            {
                _nextCommercialCountdown = value;
                OnPropertyChanged();
            }
        }

        // Micrófono
        private bool _isMicActive;
        public bool IsMicActive
        {
            get => _isMicActive;
            set
            {
                _isMicActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MicStatusText));
                OnPropertyChanged(nameof(MicButtonColor));
            }
        }

        public string MicStatusText => IsMicActive ? "MIC EN VIVO" : "MIC APAGADO";
        public string MicButtonColor => IsMicActive ? "#FF1744" : "#1E2330";

        // VU Meter
        private double _vuLeftLevel;
        public double VuLeftLevel
        {
            get => _vuLeftLevel;
            set
            {
                _vuLeftLevel = value;
                OnPropertyChanged();
            }
        }

        private double _vuRightLevel;
        public double VuRightLevel
        {
            get => _vuRightLevel;
            set
            {
                _vuRightLevel = value;
                OnPropertyChanged();
            }
        }

        // Detección de silencio
        private double _silenceDetectorLevel;
        public double SilenceDetectorLevel
        {
            get => _silenceDetectorLevel;
            set
            {
                _silenceDetectorLevel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SilenceLevelColor));
            }
        }

        public string SilenceLevelColor => SilenceDetectorLevel < 0.05 ? "#FF1744" : "#00E676";

        private bool _isSilenceDetectionEnabled = true;
        public bool IsSilenceDetectionEnabled
        {
            get => _isSilenceDetectionEnabled;
            set
            {
                _isSilenceDetectionEnabled = value;
                _silenceDetector.IsSilenceDetectionEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SilenceDetectionStatusText));
            }
        }

        public string SilenceDetectionStatusText => IsSilenceDetectionEnabled ? "SILENCIO: ACTIVO" : "SILENCIO: INACTIVO";

        private string _silenceStatus = "MONITOREANDO";
        public string SilenceStatus
        {
            get => _silenceStatus;
            set
            {
                _silenceStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SilenceStatusColor));
            }
        }

        public string SilenceStatusColor => SilenceStatus switch
        {
            "SILENCIO DETECTADO" => "#FF1744",
            "MONITOREANDO" => "#00E676",
            _ => "#888888"
        };

        // Comandos globales (que no están en sub-ViewModels)
        public ICommand ToggleMicCommand { get; private set; } = null!;
        public ICommand AnnounceTimeCommand { get; private set; } = null!;
        public ICommand ToggleSilenceDetectionCommand { get; private set; } = null!;
        public ICommand AdjustSilenceThresholdCommand { get; private set; } = null!;
        public ICommand ToggleRecordingCommand { get; private set; } = null!;
        public ICommand OpenRecordingFolderCommand { get; private set; } = null!;
        public ICommand DeleteRecordingCommand { get; private set; } = null!;
        public ICommand GenerateStatisticsReportCommand { get; private set; } = null!;
        public ICommand ClearStatisticsCommand { get; private set; } = null!;
        public ICommand SaveStatisticsCommand { get; private set; } = null!;
        public ICommand LoadStatisticsCommand { get; private set; } = null!;
        public ICommand TriggerCommercialBlockNowCommand { get; private set; } = null!;
        public ICommand AddSpotToBlockCommand { get; private set; } = null!;
        public ICommand PlayAux1Command { get; private set; } = null!;
        public ICommand PlayAux2Command { get; private set; } = null!;
        public ICommand OpenTrackEditorCommand { get; private set; } = null!;

        public MainViewModel(
            AudioEngine audioEngine,
            CartwallEngine cartwallEngine,
            CommercialScheduler commercialScheduler,
            MicEngine micEngine,
            SmartRotationEngine rotationEngine,
            SilenceDetector silenceDetector,
            StatisticsService statisticsService,
            RecordingService recordingService,
            AuxPlayerEngine auxPlayerEngine,
            AudioDatabaseService databaseService,
            ThemeViewModel themeViewModel,
            AudioPlayerViewModel audioPlayerViewModel,
            StreamingViewModel streamingViewModel,
            CartWallViewModel cartWallViewModel,
            LibraryViewModel libraryViewModel)
        {
            NowPlayingService.ExportFailed += OnNowPlayingExportFailed;

            // Inyectar servicios
            _audioEngine = audioEngine;
            _cartwallEngine = cartwallEngine;
            _commercialScheduler = commercialScheduler;
            _micEngine = micEngine;
            _rotationEngine = rotationEngine;
            _silenceDetector = silenceDetector;
            _statisticsService = statisticsService;
            _recordingService = recordingService;
            _auxPlayerEngine = auxPlayerEngine;
            _databaseService = databaseService;

            // Configurar eventos
            _audioEngine.MeterLevelsChanged += OnEngineMeterLevelsChanged;
            _commercialScheduler.BlockTriggered += OnCommercialBlockTriggered;
            _micEngine.MicStateChanged += (s, active) => IsMicActive = active;
            _silenceDetector.SilenceDetected += OnSilenceDetected;

            // Sub-ViewModels: singleton en DI, para que las ventanas de modulo
            // (UI-2) compartan el mismo estado que la consola.
            ThemeViewModel = themeViewModel;
            AudioPlayerViewModel = audioPlayerViewModel;
            StreamingViewModel = streamingViewModel;
            CartWallViewModel = cartWallViewModel;
            LibraryViewModel = libraryViewModel;

            // Configurar timer de reloj
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _clockTimer.Tick += OnClockTimerTick;
            _clockTimer.Start();

            // Configurar comandos
            ConfigureCommands();

            // Inicialización
            InitializeCommercialBlocks();
            LoadDefaultMusicFolder();

            // Exportar metadata inicial
            NowPlayingService.UpdateNowPlaying(null);

            _logger.Information("MainViewModel inicializado con DI");
        }

        private void ConfigureCommands()
        {
            bool CanOperate() => App.Auth.IsAuthorized(UserRole.Operator);
            bool IsAdmin() => App.Auth.IsAuthorized(UserRole.Admin);

            ToggleMicCommand = new RelayCommand(o =>
            {
                var wasActive = _micEngine.IsMicActive;
                var isActive = _micEngine.ToggleMic();
                AudioPlayerViewModel.StatusText = isActive
                    ? "Micrófono activado."
                    : wasActive
                        ? "Micrófono desactivado."
                        : _micEngine.LastError ?? "No se pudo activar el micrófono.";
            }, o => CanOperate());

            AnnounceTimeCommand = new RelayCommand(o => ExecuteAnnounceTime(), o => CanOperate());

            ToggleSilenceDetectionCommand = new RelayCommand(o => IsSilenceDetectionEnabled = !IsSilenceDetectionEnabled, o => CanOperate());
            AdjustSilenceThresholdCommand = new RelayCommand(o => AdjustSilenceThreshold(o), o => IsAdmin());

            ToggleRecordingCommand = new RelayCommand(o => ToggleRecording(), o => CanOperate());
            OpenRecordingFolderCommand = new RelayCommand(o =>
            {
                AudioPlayerViewModel.StatusText = _recordingService.OpenRecordingFolder()
                    ? "Carpeta de grabaciones abierta."
                    : _recordingService.LastError ?? "No se pudo abrir la carpeta de grabaciones.";
            }, o => CanOperate());
            DeleteRecordingCommand = new RelayCommand(o => DeleteRecording(o), o => IsAdmin());

            GenerateStatisticsReportCommand = new RelayCommand(o => GenerateStatisticsReport(), o => CanOperate());
            ClearStatisticsCommand = new RelayCommand(o => ClearStatistics(), o => IsAdmin());
            SaveStatisticsCommand = new RelayCommand(o => SaveStatistics(), o => IsAdmin());
            LoadStatisticsCommand = new RelayCommand(o => LoadStatistics(), o => IsAdmin());

            TriggerCommercialBlockNowCommand = new RelayCommand(o => InjectCommercialBlock(o as CommercialBlock, true), o => CanOperate());
            AddSpotToBlockCommand = new RelayCommand(o => AddSpotToBlock(o as CommercialBlock), o => IsAdmin());

            PlayAux1Command = new RelayCommand(o => _auxPlayerEngine.Aux1.PlayTrack(o as TrackItem ?? AudioPlayerViewModel.SelectedTrack ?? LibraryViewModel.SelectedDbTrack), o => CanOperate());
            PlayAux2Command = new RelayCommand(o => _auxPlayerEngine.Aux2.PlayTrack(o as TrackItem ?? AudioPlayerViewModel.SelectedTrack ?? LibraryViewModel.SelectedDbTrack), o => CanOperate());

            OpenTrackEditorCommand = new RelayCommand(o => OpenTrackEditor(o as TrackItem ?? AudioPlayerViewModel.SelectedTrack), o => IsAdmin());
        }

        private void OnNowPlayingExportFailed(object? sender, string message)
        {
            Application.Current.Dispatcher.BeginInvoke(() => AudioPlayerViewModel.StatusText = message);
        }

        private void OnClockTimerTick(object? sender, EventArgs e)
        {
            StudioClock = DateTime.Now.ToString("HH:mm:ss");

            TimeSpan timeToNext = _commercialScheduler.GetTimeUntilNextBlock();
            if (timeToNext.TotalSeconds > 0)
            {
                NextCommercialCountdown = "TANDA EN: " + timeToNext.ToString(@"mm\:ss");
            }
            else
            {
                NextCommercialCountdown = "SIN TANDAS";
            }

            if (_recordingService.IsRecording)
            {
                OnPropertyChanged(nameof(RecordingService.RecordingDurationFormatted));
            }
        }

        private void OnEngineMeterLevelsChanged(object? sender, (float Left, float Right) levels)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                VuLeftLevel = Math.Clamp(levels.Left * 100.0, 0.0, 100.0);
                VuRightLevel = Math.Clamp(levels.Right * 100.0, 0.0, 100.0);
                _silenceDetector.ProcessLevel(levels.Left, levels.Right);
                SilenceDetectorLevel = _silenceDetector.CurrentLevel;
            });
        }

        private void OnSilenceDetected(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                SilenceStatus = "SILENCIO DETECTADO";
                AudioPlayerViewModel.StatusText = "Silencio prolongado detectado - Auto-advance activado";

                if (AudioPlayerViewModel.IsAutoMode && AudioPlayerViewModel.IsPlaying && AudioPlayerViewModel.NextTrack != null)
                {
                    _audioEngine.TriggerCrossfadeToNext();
                }
                else if (!AudioPlayerViewModel.IsAutoMode && AudioPlayerViewModel.NextTrack != null)
                {
                    AudioPlayerViewModel.StatusText = "Silencio detectado (Modo Manual - No auto-advance)";
                }

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, args) =>
                {
                    SilenceStatus = "MONITOREANDO";
                    timer.Stop();
                };
                timer.Start();
            });
        }

        private void OnCommercialBlockTriggered(object? sender, CommercialBlock block)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                InjectCommercialBlock(block, block.Mode == TimingMode.Hard);
            });
        }

        private void InjectCommercialBlock(CommercialBlock? block, bool forceImmediate)
        {
            if (block == null || block.Spots.Count == 0) return;

            int insertIndex = AudioPlayerViewModel.CurrentTrack != null
                ? AudioPlayerViewModel.Playlist.IndexOf(AudioPlayerViewModel.CurrentTrack) + 1
                : 0;

            foreach (var spot in block.Spots)
            {
                var spotClone = new TrackItem
                {
                    Title = spot.Title,
                    Artist = "PUBLICIDAD",
                    FilePath = spot.FilePath,
                    Duration = spot.Duration,
                    Type = TrackType.Commercial,
                    Status = TrackStatus.Queued
                };

                AudioPlayerViewModel.Playlist.Insert(insertIndex++, spotClone);
            }

            AudioPlayerViewModel.StatusText = $"TANDA INYECTADA: {block.Name} ({block.Spots.Count} comerciales)";

            if (forceImmediate && AudioPlayerViewModel.IsPlaying)
            {
                _audioEngine.TriggerCrossfadeToNext();
            }
            else if (!AudioPlayerViewModel.IsPlaying && AudioPlayerViewModel.Playlist.Count > 0)
            {
                AudioPlayerViewModel.PlayCommand.Execute(null);
            }
        }

        private void AddSpotToBlock(CommercialBlock? block)
        {
            if (block == null) return;

            var openFileDialog = new OpenFileDialog
            {
                Filter = AudioSourceFactory.FileDialogFilter,
                Title = $"Añadir Spot Comercial a {block.Name}"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var track = TrackLoader.CreateFromFile(openFileDialog.FileName);
                track.Type = TrackType.Commercial;
                block.Spots.Add(track);
            }
        }

        private void InitializeCommercialBlocks()
        {
            var block15 = new CommercialBlock
            {
                Name = "Tanda Minuto 15",
                TriggerMinute = 15,
                Mode = TimingMode.Soft
            };

            var block45 = new CommercialBlock
            {
                Name = "Tanda Minuto 45",
                TriggerMinute = 45,
                Mode = TimingMode.Soft
            };

            string comFolder = "comerciales";
            if (Directory.Exists(comFolder))
            {
                var files = Directory.GetFiles(comFolder, "*.*")
                                     .Where(AudioSourceFactory.IsSupported).ToList();

                foreach (var file in files)
                {
                    var track = TrackLoader.CreateFromFile(file);
                    track.Type = TrackType.Commercial;
                    block15.Spots.Add(track);
                    block45.Spots.Add(track);
                }
            }

            CommercialBlocks.Add(block15);
            CommercialBlocks.Add(block45);
        }

        private void LoadDefaultMusicFolder()
        {
            AudioPlayerViewModel.LoadFolder("musica");
        }

        private void ExecuteAnnounceTime()
        {
            _audioEngine.IsDucked = true;
            CustomTimeEngine.AnnounceTime(
                onStart: () => Application.Current.Dispatcher.Invoke(() => AudioPlayerViewModel.StatusText = "LOCUCIÓN EN VIVO: Anunciando hora hablada..."),
                onEnd: () =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (!_micEngine.IsMicActive)
                        {
                            _audioEngine.IsDucked = false;
                        }
                    });
                },
                onError: message => Application.Current.Dispatcher.BeginInvoke(() => AudioPlayerViewModel.StatusText = message));
        }

        private void AdjustSilenceThreshold(object? parameter)
        {
            if (parameter != null && int.TryParse(parameter.ToString(), out int adjustment))
            {
                float currentThreshold = _silenceDetector.SilenceThreshold;
                float newThreshold = Math.Clamp(currentThreshold + (adjustment * 0.01f), 0.001f, 0.5f);
                _silenceDetector.SilenceThreshold = newThreshold;
                AudioPlayerViewModel.StatusText = $"Umbral de silencio ajustado: {newThreshold:F3} ({_silenceDetector.GetThresholdInDecibels():F1} dB)";
            }
        }

        private void ToggleRecording()
        {
            if (_recordingService.IsRecording)
            {
                AudioPlayerViewModel.StatusText = _recordingService.StopRecording()
                    ? $"Grabación guardada: {_recordingService.CurrentFileName}"
                    : _recordingService.LastError ?? "No se pudo detener la grabación.";
            }
            else
            {
                if (_recordingService.StartRecording())
                {
                    AudioPlayerViewModel.StatusText = "Grabación iniciada - Guardando emisión";
                }
                else
                {
                    AudioPlayerViewModel.StatusText = _recordingService.LastError ?? "No se pudo iniciar la grabación.";
                }
            }
        }

        private void DeleteRecording(object? parameter)
        {
            if (parameter is string filePath)
            {
                AudioPlayerViewModel.StatusText = _recordingService.DeleteRecording(filePath)
                    ? "Grabación eliminada."
                    : _recordingService.LastError ?? "No se pudo eliminar la grabación.";
            }
        }

        private void GenerateStatisticsReport()
        {
            try
            {
                string report = _statisticsService.GenerateReport();
                string reportPath = $"reporte_emision_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                if (!_statisticsService.ExportReportToFile(reportPath))
                {
                    AudioPlayerViewModel.StatusText = _statisticsService.LastError ?? "No se pudo exportar el reporte.";
                    return;
                }
                AudioPlayerViewModel.StatusText = $"Reporte generado: {reportPath}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = reportPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo generar el reporte de emisión");
                AudioPlayerViewModel.StatusText = UserErrorMessages.For(ex, "generar el reporte de emisión");
            }
        }

        private void ClearStatistics()
        {
            _statisticsService.ClearStatistics();
            AudioPlayerViewModel.StatusText = "Estadísticas limpiadas correctamente";
        }

        private void SaveStatistics()
        {
            AudioPlayerViewModel.StatusText = _statisticsService.SaveStatistics()
                ? "Estadísticas guardadas en archivo."
                : _statisticsService.LastError ?? "No se pudieron guardar las estadísticas.";
        }

        private void LoadStatistics()
        {
            AudioPlayerViewModel.StatusText = _statisticsService.LoadStatistics()
                ? "Estadísticas cargadas desde archivo."
                : _statisticsService.LastError ?? "No se pudieron cargar las estadísticas.";
        }

        private void OpenTrackEditor(TrackItem? track)
        {
            if (track == null) return;
            var dialog = new TrackEditorDialog(track)
            {
                Owner = Application.Current.MainWindow
            };
            if (dialog.ShowDialog() == true)
            {
                AudioPlayerViewModel.StatusText = $"Marcas CUE guardadas para: {track.Title}";
            }
        }
    }
}
