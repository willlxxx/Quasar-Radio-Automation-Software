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
        private readonly IModuleNavigator _navigator;
        private readonly WeeklyScheduleService _weeklySchedule;

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

        private string _lastPautaText = string.Empty;

        /// <summary>
        /// El aviso de la última pauta que se cumplió, FIJO en el pie de la consola. La línea de
        /// estado normal la pisa el propio arranque de emisión que la pauta provoca —dura menos
        /// de 100 ms—, así que el operador no llegaba a ver qué se había disparado (medido en
        /// vivo 2026-10-23: «Pauta horaria: viernes 05:31» en el log y nada en la pantalla).
        /// </summary>
        public string LastPautaText
        {
            get => _lastPautaText;
            private set
            {
                if (_lastPautaText == value) return;
                _lastPautaText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPauta));
            }
        }

        public bool HasPauta => !string.IsNullOrEmpty(_lastPautaText);

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

        // UI-4: aqui antes se reenviaban SelectedDbTrack y DatabaseService para el panel
        // BASE DE DATOS de la consola. El panel se fue a la ventana de Biblioteca, que es
        // el mismo singleton LibraryViewModel, asi que el reenvio sobraba: la consola ya
        // no toca la base de datos, abre el modulo.

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

        // ---------------------------------------------------------------- modulos (UI-3b)

        /// <summary>
        /// La consola ya no es un TabControl: cada modulo es una ventana y esta franja
        /// marca cual esta abierta. El navigator es la unica fuente de verdad; aqui solo
        /// se traduce a propiedades de enlace.
        /// </summary>
        public bool IsLibraryOpen => _navigator.IsOpen(ModuleKey.Library);
        public bool IsScheduleOpen => _navigator.IsOpen(ModuleKey.Schedule);
        public bool IsDspOpen => _navigator.IsOpen(ModuleKey.Dsp);
        public bool IsStreamingOpen => _navigator.IsOpen(ModuleKey.Streaming);
        public bool IsReportsOpen => _navigator.IsOpen(ModuleKey.Reports);

        /// <summary>Abre un modulo por nombre: CommandParameter="Dsp", "Streaming", ...</summary>
        public ICommand OpenModuleCommand { get; private set; } = null!;

        public void OpenModule(string? keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return;

            if (!Enum.TryParse<ModuleKey>(keyName, ignoreCase: true, out var key))
            {
                _logger.Warning("Modulo desconocido: {Key}", keyName);
                return;
            }

            _navigator.Open(key);
        }

        private void RaiseModuleOpenStates()
        {
            OnPropertyChanged(nameof(IsLibraryOpen));
            OnPropertyChanged(nameof(IsScheduleOpen));
            OnPropertyChanged(nameof(IsDspOpen));
            OnPropertyChanged(nameof(IsStreamingOpen));
            OnPropertyChanged(nameof(IsReportsOpen));
        }

        /// <summary>
        /// Vuelve a listar los WAV de la carpeta de grabaciones. La pestaña GRABACIÓN de la
        /// consola vieja nunca llamaba a esto, asi que su listado estaba siempre vacio y el
        /// boton de borrar no recibia parametro.
        /// </summary>
        public void RefreshRecordingFiles()
        {
            RecordingFiles.Clear();
            foreach (var path in _recordingService.GetRecordingFiles())
                RecordingFiles.Add(Path.GetFileName(path));
        }

        // Comandos globales (que no están en sub-ViewModels)
        public ICommand ToggleMicCommand { get; private set; } = null!;
        public ICommand AnnounceTimeCommand { get; private set; } = null!;
        public ICommand ToggleSilenceDetectionCommand { get; private set; } = null!;
        public ICommand AdjustSilenceThresholdCommand { get; private set; } = null!;
        public ICommand ToggleRecordingCommand { get; private set; } = null!;
        public ICommand OpenRecordingFolderCommand { get; private set; } = null!;
        public ICommand RefreshRecordingFilesCommand { get; private set; } = null!;
        public ICommand DeleteRecordingCommand { get; private set; } = null!;
        public ICommand GenerateStatisticsReportCommand { get; private set; } = null!;
        public ICommand ClearStatisticsCommand { get; private set; } = null!;
        public ICommand SaveStatisticsCommand { get; private set; } = null!;
        public ICommand LoadStatisticsCommand { get; private set; } = null!;
        public ICommand TriggerCommercialBlockNowCommand { get; private set; } = null!;
        public ICommand AddSpotToBlockCommand { get; private set; } = null!;
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
            LibraryViewModel libraryViewModel,
            WeeklyScheduleService weeklySchedule,
            IModuleNavigator navigator)
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
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));

            // UI-4 paso C: la rejilla horaria vive aqui, no en su ventana. Se carga al arrancar
            // y se cumple con Programación cerrada, porque lo que programa el operador es aire,
            // no una vista.
            _weeklySchedule = weeklySchedule ?? throw new ArgumentNullException(nameof(weeklySchedule));
            _weeklySchedule.SlotDue += OnScheduleSlotDue;

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

            // La franja de modulos de la consola marca la ventana que esta abierta; el
            // navigator ya es singleton, asi que el estado es el mismo que ve la consola.
            _navigator.ModuleOpened += (_, _) => RaiseModuleOpenStates();
            _navigator.ModuleClosed += (_, _) => RaiseModuleOpenStates();

            // Configurar timer de reloj
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _clockTimer.Tick += OnClockTimerTick;
            _clockTimer.Start();

            // Configurar comandos
            ConfigureCommands();

            // Inicialización
            InitializeCommercialBlocks();
            string scheduleLoaded = _weeklySchedule.Load();
            _logger.Information("Rejilla horaria: {Message} ({Count} celdas)", scheduleLoaded,
                _weeklySchedule.ProgrammedCount);
            LoadDefaultMusicFolder();

            // Exportar metadata inicial
            NowPlayingService.UpdateNowPlaying(null);

            // AUTH-1: la identificacion puede llegar en mitad de una sesion (la pide AuthGate
            // al pulsar una operacion, o el usuario en la ventana de usuarios). Al cambiar la
            // sesion hay que re-evaluar los CanExecute y repintar el pie de la consola, si no
            // el operador se queda con botones del estado viejo aunque ya este identificado.
            App.Auth.SessionChanged += (_, _) =>
                Application.Current?.Dispatcher.BeginInvoke(RefreshAuthorizationState);

            _logger.Information("MainViewModel inicializado con DI");
        }

        private void ConfigureCommands()
        {
            bool CanOperate() => App.Auth.IsAuthorized(UserRole.Operator);

            // AUTH-1: el nivel se sigue exigiendo, pero se pide AL PULSAR (AuthGate) y no
            // dejando el boton apagado desde el arranque. Ninguna ventana de la emisora se
            // abre ya detras de una clave; lo que si queda atribuido por nivel es esto:
            // umbral de silencio, borrar grabaciones, y el estadistico.
            bool Admin() => AuthGate.Admin();

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
            AdjustSilenceThresholdCommand = new RelayCommand(o => { if (Admin()) AdjustSilenceThreshold(o); });

            ToggleRecordingCommand = new RelayCommand(o => ToggleRecording(), o => CanOperate());
            OpenRecordingFolderCommand = new RelayCommand(o =>
            {
                AudioPlayerViewModel.StatusText = _recordingService.OpenRecordingFolder()
                    ? "Carpeta de grabaciones abierta."
                    : _recordingService.LastError ?? "No se pudo abrir la carpeta de grabaciones.";
            }, o => CanOperate());
            DeleteRecordingCommand = new RelayCommand(o => { if (Admin()) DeleteRecording(o); });

            GenerateStatisticsReportCommand = new RelayCommand(o => GenerateStatisticsReport(), o => CanOperate());
            ClearStatisticsCommand = new RelayCommand(o => { if (Admin()) ClearStatistics(); });
            SaveStatisticsCommand = new RelayCommand(o => { if (Admin()) SaveStatistics(); });
            LoadStatisticsCommand = new RelayCommand(o => { if (Admin()) LoadStatistics(); });

            TriggerCommercialBlockNowCommand = new RelayCommand(o => InjectCommercialBlock(o as CommercialBlock, true), o => CanOperate());
            AddSpotToBlockCommand = new RelayCommand(o => { if (Admin()) AddSpotToBlock(o as CommercialBlock); });

            // UI-4: PlayAux1/PlayAux2Command vivian aqui para el boton "PFL AUX 1" del panel
            // BASE DE DATOS. El PFL se elige sobre un track concreto, y ese track se elige en
            // Biblioteca, asi que los mandos se fueron con el boton (LibraryViewModel).
            // DspWindow conserva parada y volumen de AUX 1/2, que es monitorizacion.

            OpenTrackEditorCommand = new RelayCommand(o => { if (Admin()) OpenTrackEditor(o as TrackItem ?? AudioPlayerViewModel.SelectedTrack); });

            RefreshRecordingFilesCommand = new RelayCommand(o => RefreshRecordingFiles(), o => CanOperate());
            OpenModuleCommand = new RelayCommand(o => OpenModule(o as string));
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

        /// <summary>
        /// Ejecuta la pauta de una celda de la rejilla (UI-4 paso C). El reloj del servicio ya
        /// llama en el hilo de UI; aquí solo se decide <b>qué</b> hace cada tipo, y siempre con
        /// una operación que la consola ya ofrecía al operador: no hay nada en la rejilla que
        /// un humano no pueda hacer a mano desde la consola.
        /// </summary>
        private void OnScheduleSlotDue(int day, int hour, ScheduleSlot slot)
        {
            string where = string.Format("{0} {1:00}:{2:00}",
                WeeklyScheduleService.DayName(day), hour, slot.Minute);

            // Primero el aviso, y en su sitio fijo: lo que viene debajo (regenerar la lista,
            // arrancar la emisión) pisa StatusText en cuanto empieza.
            LastPautaText = string.Format("PAUTA {0}: {1}", where, WeeklyScheduleService.Describe(slot));

            switch (slot.Kind)
            {
                case ScheduleKind.Hora:
                    StatusText = $"PAUTA {where}: hora hablada.";
                    ExecuteAnnounceTime();
                    break;

                case ScheduleKind.Cart:
                    var pad = CartPads.FirstOrDefault(p =>
                        string.Equals(p.ShortcutText, slot.Ref, StringComparison.OrdinalIgnoreCase));
                    if (pad is null)
                    {
                        StatusText = $"PAUTA {where}: sin cart ({slot.Ref}) en la botonera.";
                        _logger.Warning("Pauta horaria con cart inexistente {Ref}", slot.Ref);
                        break;
                    }

                    StatusText = $"PAUTA {where}: cart {pad.ShortcutText} · {pad.Title}.";
                    CartWallViewModel.TriggerCartPadCommand.Execute(pad);
                    break;

                case ScheduleKind.Tanda:
                    var block = CommercialBlocks.FirstOrDefault(b =>
                        string.Equals(b.BlockId, slot.Ref, StringComparison.OrdinalIgnoreCase));
                    if (block is null || block.Spots.Count == 0)
                    {
                        StatusText = $"PAUTA {where}: la tanda programada no existe o no tiene cuñas.";
                        _logger.Warning("Pauta horaria con tanda inutilizable {Ref}", slot.Ref);
                        break;
                    }

                    StatusText = $"PAUTA {where}: tanda {block.Name} ({block.Spots.Count} comerciales).";
                    InjectCommercialBlock(block, block.Mode == TimingMode.Hard);
                    break;

                case ScheduleKind.AutoPlaylist:
                    LibraryViewModel.GenerateAutoPlaylistCommand.Execute(null);
                    StatusText = $"PAUTA {where}: lista automática regenerada ({Playlist.Count} elementos).";
                    if (!IsPlaying && Playlist.Count > 0)
                    {
                        AudioPlayerViewModel.PlayCommand.Execute(null);
                    }
                    break;
            }
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
                AudioPlayerViewModel.StatusText = _recordingService.StartRecording()
                    ? "Grabación iniciada - Guardando emisión"
                    : _recordingService.LastError ?? "No se pudo iniciar la grabación.";
            }

            // La ventana de Streaming muestra el listado: que refleje el archivo recien
            // abierto o cerrado sin tener que pulsar ACTUALIZAR.
            RefreshRecordingFiles();
        }

        private void DeleteRecording(object? parameter)
        {
            if (parameter is string fileNameOrPath && !string.IsNullOrWhiteSpace(fileNameOrPath))
            {
                // RecordingFiles lista nombres, no rutas: la carpeta la aporta el servicio.
                var path = Path.IsPathRooted(fileNameOrPath)
                    ? fileNameOrPath
                    : Path.Combine(_recordingService.RecordingFolder, fileNameOrPath);

                AudioPlayerViewModel.StatusText = _recordingService.DeleteRecording(path)
                    ? "Grabación eliminada."
                    : _recordingService.LastError ?? "No se pudo eliminar la grabación.";

                RefreshRecordingFiles();
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
