using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Serilog;

namespace quasar_radio
{
    /// <summary>
    /// ViewModel del modulo BIBLIOTECA (UI-4).
    ///
    /// Antes de UI-4 esto era un botoncito de "encolar" pegado a la consola
    /// (ViewModels/LibraryViewModel) y la gestion real vivia en otra ventana con cuatro
    /// pestanas y su propio ViewModel de 737 lineas, que se constrvia sus servicios con
    /// <c>new</c> y por eso no veia la misma libreria que la consola. Los dos papeles se
    /// unifican aqui: una sola ventana, sin pestanas, un singleton compartido con la
    /// consola (misma cola, mismo stream, mismo historial de rotacion).
    /// </summary>
    public class LibraryViewModel : ViewModelBase
    {
        private readonly ILogger _logger = Log.ForContext<LibraryViewModel>();

        private readonly AudioDatabaseService _databaseService;
        private readonly SmartRotationEngine _rotationEngine;
        private readonly TrackMetadataService _metadataService;
        private readonly TrackEditorService _editorService;
        private readonly AuxPlayerEngine _auxEngine;
        private readonly ObservableCollection<TrackItem> _playlist;
        private readonly IModuleNavigator _navigator;

        public AudioDatabaseService DatabaseService => _databaseService;
        public SmartRotationEngine RotationEngine => _rotationEngine;

        // ------------------------------------------------------------ estado de vista

        private string _statusText = "ESTADO: LISTO";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        /// <summary>La fila enfocada: manda en el inspector y en el PFL.</summary>
        private TrackItem? _anchorTrack;
        public TrackItem? AnchorTrack
        {
            get => _anchorTrack;
            set { _anchorTrack = value; OnPropertyChanged(); }
        }

        /// <summary>Todas las filas seleccionadas; vacia hasta que la tabla avisa.</summary>
        private IReadOnlyList<TrackItem> _selectedTracks = Array.Empty<TrackItem>();

        public int SelectedCount => _selectedTracks.Count;
        public bool HasSelection => _selectedTracks.Count > 0;
        public bool IsSingleSelection => _selectedTracks.Count == 1;
        public bool IsBatchSelection => _selectedTracks.Count > 1;

        private string _selectionSummary = "Ningun track seleccionado";
        public string SelectionSummary
        {
            get => _selectionSummary;
            set { _selectionSummary = value; OnPropertyChanged(); }
        }

        // ------------------------------------------------------------- arbol de categorias

        public ObservableCollection<LibraryNode> TreeNodes { get; } = new();

        private LibraryNode? _selectedNode;
        public LibraryNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode == value) return;
                _selectedNode = value;
                OnPropertyChanged();
                // UI-4 paso B: la tarjeta de genero (renombrar / quitar) vive de esta
                // seleccion, y tiene que rellenarse tambien cuando se va a otra categoria.
                RefreshGenreWorkspace();
                if (_selectedNode != null) ApplyNode(_selectedNode);
            }
        }

        private string _newGenreName = string.Empty;
        public string NewGenreName
        {
            get => _newGenreName;
            set { _newGenreName = value; OnPropertyChanged(); }
        }

        /// <summary>Generos que existen en la libreria, para el combo del inspector.</summary>
        public List<string> GenreOptions => _databaseService.GetAllGenres();

        // Filtros numericos de la barra. Van como texto porque un TextBox enlazado
        // directo a int? o double? no puede representar "vacio": el usuario borra el
        // numero, el binding no lo convierte y el filtro se queda puesto sin avisar.
        // Aqui vacio o texto imposible significa "sin filtro", que es lo que parece.

        private string _yearText = string.Empty;
        public string YearText
        {
            get => _yearText;
            set
            {
                _yearText = value;
                OnPropertyChanged();
                _databaseService.FilterYear = int.TryParse(_yearText.Trim(), out var year) ? year : null;
            }
        }

        private string _bpmMinText = string.Empty;
        public string BpmMinText
        {
            get => _bpmMinText;
            set
            {
                _bpmMinText = value;
                OnPropertyChanged();
                _databaseService.FilterMinBpm = double.TryParse(ParseNumber(_bpmMinText), out var bpm) ? bpm : null;
            }
        }

        private string _bpmMaxText = string.Empty;
        public string BpmMaxText
        {
            get => _bpmMaxText;
            set
            {
                _bpmMaxText = value;
                OnPropertyChanged();
                _databaseService.FilterMaxBpm = double.TryParse(ParseNumber(_bpmMaxText), out var bpm) ? bpm : null;
            }
        }

        public List<string> SortOptions => _databaseService.SortOptions;
        public List<int> PriorityOptions => Enumerable.Range(1, 10).ToList();
        public string[] PresetOptions { get; } = { "Radio", "Club", "Podcast", "Clean" };

        /// <summary>
        /// Los tipos de audio con su nombre en español, para los combos del inspector y
        /// del lote. El enumerado se llama Music/Jingle/Sweeper y en cabina se lee
        /// "Música/Jingle/Separador", que es como estan las carpetas de la libreria.
        /// </summary>
        public List<KeyValuePair<TrackType, string>> TypeChoices { get; } = new()
        {
            new(TrackType.Music, "Música"),
            new(TrackType.Jingle, "Jingle"),
            new(TrackType.Sweeper, "Separador"),
            new(TrackType.Voice, "Voz"),
            new(TrackType.Commercial, "Publicidad"),
            new(TrackType.TimeAnnouncement, "Horaria"),
        };

        // ------------------------------------------------------------- editor de un track

        private string _editTitle = string.Empty;
        public string EditTitle { get => _editTitle; set { _editTitle = value; OnPropertyChanged(); } }

        private string _editArtist = string.Empty;
        public string EditArtist { get => _editArtist; set { _editArtist = value; OnPropertyChanged(); } }

        private string _editAlbum = string.Empty;
        public string EditAlbum { get => _editAlbum; set { _editAlbum = value; OnPropertyChanged(); } }

        private string _editGenre = string.Empty;
        public string EditGenre { get => _editGenre; set { _editGenre = value; OnPropertyChanged(); } }

        private string _editYearText = string.Empty;
        public string EditYearText { get => _editYearText; set { _editYearText = value; OnPropertyChanged(); } }

        private int? _editPriority;
        public int? EditPriority { get => _editPriority; set { _editPriority = value; OnPropertyChanged(); } }

        private string _editBpmText = string.Empty;
        public string EditBpmText { get => _editBpmText; set { _editBpmText = value; OnPropertyChanged(); } }

        private string _editCueInText = string.Empty;
        public string EditCueInText { get => _editCueInText; set { _editCueInText = value; OnPropertyChanged(); } }

        private string _editCueOutText = string.Empty;
        public string EditCueOutText { get => _editCueOutText; set { _editCueOutText = value; OnPropertyChanged(); } }

        private string _editIntroText = string.Empty;
        public string EditIntroText { get => _editIntroText; set { _editIntroText = value; OnPropertyChanged(); } }

        private string _editOutroText = string.Empty;
        public string EditOutroText { get => _editOutroText; set { _editOutroText = value; OnPropertyChanged(); } }

        private string _editFadeInText = string.Empty;
        public string EditFadeInText { get => _editFadeInText; set { _editFadeInText = value; OnPropertyChanged(); } }

        private string _editFadeOutText = string.Empty;
        public string EditFadeOutText { get => _editFadeOutText; set { _editFadeOutText = value; OnPropertyChanged(); } }

        private string _editComment = string.Empty;
        public string EditComment { get => _editComment; set { _editComment = value; OnPropertyChanged(); } }

        private TrackType _editType = TrackType.Music;
        public TrackType EditType { get => _editType; set { _editType = value; OnPropertyChanged(); } }

        private string _editCuePointsText = "Ninguno";
        public string EditCuePointsText { get => _editCuePointsText; set { _editCuePointsText = value; OnPropertyChanged(); } }

        // ----------------------------------------------------------- edicion en lote

        private bool _batchGenreEnabled;
        public bool BatchGenreEnabled { get => _batchGenreEnabled; set { _batchGenreEnabled = value; OnPropertyChanged(); } }

        private string _batchGenre = string.Empty;
        public string BatchGenre { get => _batchGenre; set { _batchGenre = value; OnPropertyChanged(); } }

        private bool _batchPriorityEnabled;
        public bool BatchPriorityEnabled { get => _batchPriorityEnabled; set { _batchPriorityEnabled = value; OnPropertyChanged(); } }

        private int? _batchPriority;
        public int? BatchPriority { get => _batchPriority; set { _batchPriority = value; OnPropertyChanged(); } }

        private bool _batchYearEnabled;
        public bool BatchYearEnabled { get => _batchYearEnabled; set { _batchYearEnabled = value; OnPropertyChanged(); } }

        private string _batchYearText = string.Empty;
        public string BatchYearText { get => _batchYearText; set { _batchYearText = value; OnPropertyChanged(); } }

        private bool _batchFadeEnabled;
        public bool BatchFadeEnabled { get => _batchFadeEnabled; set { _batchFadeEnabled = value; OnPropertyChanged(); } }

        private string _batchFadeInText = string.Empty;
        public string BatchFadeInText { get => _batchFadeInText; set { _batchFadeInText = value; OnPropertyChanged(); } }

        private string _batchFadeOutText = string.Empty;
        public string BatchFadeOutText { get => _batchFadeOutText; set { _batchFadeOutText = value; OnPropertyChanged(); } }

        private bool _batchTypeEnabled;
        public bool BatchTypeEnabled { get => _batchTypeEnabled; set { _batchTypeEnabled = value; OnPropertyChanged(); } }

        private TrackType _batchType = TrackType.Music;
        public TrackType BatchType { get => _batchType; set { _batchType = value; OnPropertyChanged(); } }

        // ----------------------------------------------------------------- importacion

        private string _importGenre = "General";
        public string ImportGenre { get => _importGenre; set { _importGenre = value; OnPropertyChanged(); } }

        private TrackType _importType = TrackType.Music;
        public TrackType ImportType { get => _importType; set { _importType = value; OnPropertyChanged(); } }

        private string _selectedPreset = "Radio";
        public string SelectedPreset { get => _selectedPreset; set { _selectedPreset = value; OnPropertyChanged(); } }

        // ----------------------------------------------------------------- cabecera

        private string _librarySummary = string.Empty;
        public string LibrarySummary { get => _librarySummary; set { _librarySummary = value; OnPropertyChanged(); } }

        private string _rotationSummary = string.Empty;
        public string RotationSummary { get => _rotationSummary; set { _rotationSummary = value; OnPropertyChanged(); } }

        // ------------------------------------------------------------------ comandos

        public ICommand ReloadLibraryCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        public ICommand RecheckFilesCommand { get; }
        public ICommand QueueSelectionCommand { get; }
        public ICommand GenerateAutoPlaylistCommand { get; }
        public ICommand GenerateSmartPlaylistCommand { get; }
        public ICommand ClearRotationHistoryCommand { get; }
        public ICommand PlayAux1Command { get; }
        public ICommand PlayAux2Command { get; }
        public ICommand ApplyEditsCommand { get; }
        public ICommand RevertEditsCommand { get; }
        public ICommand ApplyBatchCommand { get; }
        public ICommand ApplyPresetCommand { get; }
        public ICommand IntelligentCueCommand { get; }
        public ICommand NormalizeCueCommand { get; }
        public ICommand ResetCueCommand { get; }
        public ICommand CopyCueToSelectionCommand { get; }
        public ICommand ClearExtraCuePointsCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand AddGenreCommand { get; }
        public ICommand RenameGenreCommand { get; }
        public ICommand DeleteGenreCommand { get; }
        public ICommand ImportFilesCommand { get; }
        public ICommand ImportFolderCommand { get; }
        public ICommand SaveMetadataCommand { get; }
        public ICommand LoadMetadataCommand { get; }
        public ICommand ExportCsvCommand { get; }

        public LibraryViewModel(
            AudioDatabaseService databaseService,
            SmartRotationEngine rotationEngine,
            TrackMetadataService metadataService,
            TrackEditorService editorService,
            AuxPlayerEngine auxEngine,
            ObservableCollection<TrackItem> playlist,
            IModuleNavigator navigator)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _rotationEngine = rotationEngine ?? throw new ArgumentNullException(nameof(rotationEngine));
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
            _editorService = editorService ?? throw new ArgumentNullException(nameof(editorService));
            _auxEngine = auxEngine ?? throw new ArgumentNullException(nameof(auxEngine));
            _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));

            bool CanOperate() => App.Auth.IsAuthorized(UserRole.Operator);

            // AUTH-1: los NIVELES se quedan, lo que cambia es el MOMENTO de pedir la clave.
            // Antes estas operaciones iban con `_ => IsAdmin()` en el CanExecute, asi que un
            // invitado veia seis botones muertos y no podia hacer nada mas que pedirle la
            // contraseña al jefe. Ahora el boton responde y `AuthGate.Admin()` pregunta quien
            // es en el momento del clic: si la sesion abierta cubre el nivel, se hace; si no,
            // sale el dialogo y al cancelar no se toca nada. La clasificacion de que es de
            // administrador no cambia: solo el sitio de la puerta.
            bool Admin() => AuthGate.Admin();

            ReloadLibraryCommand = new RelayCommand(_ => { if (Admin()) ReloadLibrary(); });
            ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
            RecheckFilesCommand = new RelayCommand(_ => { if (Admin()) RecheckFiles(); });

            QueueSelectionCommand = new RelayCommand(_ => QueueSelection(), _ => CanOperate() && HasSelection);
            GenerateAutoPlaylistCommand = new RelayCommand(_ => GenerateAutoPlaylist(), _ => CanOperate());
            GenerateSmartPlaylistCommand = new RelayCommand(_ => GenerateSmartPlaylist(), _ => CanOperate());
            ClearRotationHistoryCommand = new RelayCommand(_ => { if (Admin()) ClearRotationHistory(); });
            PlayAux1Command = new RelayCommand(o => PlayAux(1, o), _ => CanOperate() && AnchorTrack != null);
            PlayAux2Command = new RelayCommand(o => PlayAux(2, o), _ => CanOperate() && AnchorTrack != null);

            ApplyEditsCommand = new RelayCommand(_ => ApplyEdits(), _ => IsSingleSelection);
            RevertEditsCommand = new RelayCommand(_ => LoadEditorFields(AnchorTrack), _ => IsSingleSelection);
            ApplyBatchCommand = new RelayCommand(_ => ApplyBatch(), _ => CanOperate() && HasSelection);

            ApplyPresetCommand = new RelayCommand(_ => ApplyPreset(), _ => CanOperate() && HasSelection);
            IntelligentCueCommand = new RelayCommand(_ => RunOnSelection(_editorService.SetIntelligentCuePoints, "CUE inteligente"), _ => CanOperate() && HasSelection);
            NormalizeCueCommand = new RelayCommand(_ => RunOnSelection(_editorService.NormalizeCuePoints, "CUE normalizado"), _ => CanOperate() && HasSelection);
            ResetCueCommand = new RelayCommand(_ => RunOnSelection(_editorService.ResetCuePoints, "CUE reseteado"), _ => CanOperate() && HasSelection);
            CopyCueToSelectionCommand = new RelayCommand(_ => CopyCueToSelection(), _ => CanOperate() && IsBatchSelection);
            ClearExtraCuePointsCommand = new RelayCommand(_ => RunOnSelection(_editorService.ClearAllCuePoints, "Puntos CUE extra borrados"), _ => CanOperate() && HasSelection);

            DeleteSelectedCommand = new RelayCommand(_ => { if (Admin()) DeleteSelected(); }, _ => HasSelection);
            AddGenreCommand = new RelayCommand(_ => { if (Admin()) AddGenre(); });
            // UI-4 paso B: renombrar y quitar genero son de OPERADOR, no de administrador,
            // igual que la edicion en lote (ambas cambian el genero de varias filas de golpe;
            // lo unico de nivel es lo que toca archivos: RECARGAR, IMPORTAR, QUITAR pistas).
            // Y van sin cuadro de confirmacion: en cabina un MessageBox tapando el aire es
            // peor que un boton que avisa. QUITAR pide el clic dos veces (ver DeleteGenre).
            RenameGenreCommand = new RelayCommand(_ => RenameGenre(), _ => CanOperate() && IsGenreNodeSelected);
            DeleteGenreCommand = new RelayCommand(_ => DeleteGenre(), _ => CanOperate() && IsGenreNodeSelected);
            ImportFilesCommand = new RelayCommand(_ => { if (Admin()) ImportFiles(); });
            ImportFolderCommand = new RelayCommand(_ => { if (Admin()) ImportFolder(); });
            SaveMetadataCommand = new RelayCommand(_ => { if (Admin() && SaveSilently()) Notify("Metadatos guardados"); });
            LoadMetadataCommand = new RelayCommand(_ => { if (Admin()) LoadMetadata(); });
            ExportCsvCommand = new RelayCommand(_ => { if (Admin()) ExportCsv(); });

            // Cerrar Gestion de libreria recarga la base: es lo que hacia el antiguo
            // suscriptor y lo que sigue necesitando la consola (la cola y el aire tienen
            // que ver los tracks nuevos sin reiniciar la aplicacion).
            _navigator.ModuleClosed += (_, key) =>
            {
                if (key != ModuleKey.Library) return;
                // UI-4 paso B: por el mismo camino que RECARGAR, para que el escaneo no
                // se quede con los generos de fabrica ni pierda lo importado.
                var restored = RescanAndRestore();
                StatusText = "Biblioteca cerrada. La consola ya ve los cambios. " + restored;
                _logger.Information("Biblioteca cerrada: base de datos recargada");
            };

            ReloadLibrary(silent: true);
        }

        // ---------------------------------------------------------------- selection

        /// <summary>
        /// Lo llama la vista (DataGrid.SelectionChanged). WPF no permite enlazar
        /// SelectedItems en modo multiple, asi que la seleccion entra por aqui.
        /// </summary>
        public void UpdateSelection(System.Collections.IList items)
        {
            _selectedTracks = items?.OfType<TrackItem>().ToList() ?? new List<TrackItem>();
            AnchorTrack = _selectedTracks.Count > 0 ? _selectedTracks[0] : null;

            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsSingleSelection));
            OnPropertyChanged(nameof(IsBatchSelection));
            SelectionSummary = BuildSummary();
            LoadEditorFields(AnchorTrack);
        }

        private string BuildSummary()
        {
            if (_selectedTracks.Count == 0) return "Ningun track seleccionado";

            var total = TimeSpan.FromSeconds(_selectedTracks.Sum(t => t.Duration.TotalSeconds));
            var rotos = _selectedTracks.Count(t => !t.FileAvailable);
            var text = $"{_selectedTracks.Count} seleccionados · duración {total:hh\\:mm\\:ss}";
            if (rotos > 0) text += $" · {rotos} con problema de archivo";
            return text;
        }

        // ------------------------------------------------------------------- cabecera

        private void UpdateHeader()
        {
            var total = _databaseService.AllTracks.Count;
            var visibles = _databaseService.FilteredTracks.Count;
            var dur = _databaseService.GetTotalDuration();
            var rotos = _databaseService.AllTracks.Count(t => !t.FileAvailable);

            LibrarySummary = $"{total} tracks · {visibles} a la vista · duración {dur:hh\\:mm\\:ss}"
                + (rotos > 0 ? $" · {rotos} con archivo faltante o ilegible" : " · todos los archivos presentes");

            var stats = _rotationEngine.GetStatistics();
            RotationSummary = $"Rotación: {stats.TotalTracksTracked} pistas seguidas · "
                + $"{stats.SessionPlays} salida(s) al aire en esta sesión"
                + (stats.SessionPlays == 0 && stats.TotalTracksTracked == 0
                    ? " (SMART PLAYLIST todavía elige al azar: nada ha sonado)"
                    : string.Empty);
        }

        // ----------------------------------------------------------------------- arbol

        /// <summary>
        /// Reconstruye el arbol con los conteos reales. Se llama tras recargar, importar,
        /// borrar o editar en lote: los numeros tienen que cuadrar con la tabla.
        /// </summary>
        public void RefreshTree()
        {
            var previous = _selectedNode?.Key;
            var tracks = _databaseService.AllTracks;

            TreeNodes.Clear();
            TreeNodes.Add(new LibraryNode("all", "🎧", "Toda la biblioteca", tracks.Count));

            TreeNodes.Add(new LibraryNode("sep-tipos", "", "TIPO DE AUDIO", -1, isHeader: true));
            foreach (var (label, type) in new[]
            {
                ("Música", TrackType.Music), ("Jingles", TrackType.Jingle),
                ("Separadores", TrackType.Sweeper), ("Voces", TrackType.Voice),
                ("Publicidad", TrackType.Commercial), ("Horarias", TrackType.TimeAnnouncement),
            })
            {
                TreeNodes.Add(new LibraryNode($"tipo-{type}", TypeGlyph(type), label,
                    tracks.Count(t => t.Type == type), category: CategoryFor(type)));
            }

            var missing = tracks.Count(t => !t.FileAvailable && !File.Exists(t.FilePath));
            var unreadable = tracks.Count(t => !t.FileAvailable && File.Exists(t.FilePath));
            TreeNodes.Add(new LibraryNode("sep-estado", "", "ESTADO DE LOS ARCHIVOS", -1, isHeader: true));
            TreeNodes.Add(new LibraryNode("missing", "🚫", "Faltan en disco", missing, missingOnly: true));
            TreeNodes.Add(new LibraryNode("unreadable", "🔇", "No se pueden leer", unreadable, missingOnly: true));

            TreeNodes.Add(new LibraryNode("sep-generos", "", "GÉNERO", -1, isHeader: true));
            foreach (var group in tracks.GroupBy(t => t.Genre).OrderBy(g => g.Key))
            {
                TreeNodes.Add(new LibraryNode($"gen-{group.Key}", "🏷", group.Key, group.Count(), genre: group.Key));
            }

            UpdateHeader();
            // El combo de genero del inspector se alimenta de aqui: si un lote mete un
            // genero nuevo, tiene que aparecer sin cerrar y reabrir la ventana.
            OnPropertyChanged(nameof(GenreOptions));

            if (previous != null)
            {
                _selectedNode = TreeNodes.FirstOrDefault(n => n.Key == previous);
                OnPropertyChanged(nameof(SelectedNode));
            }

            // UI-4 paso B: el arbol es el que manda en la tarjeta de genero, asi que se
            // refresca aqui y no solo cuando se hace clic en un nodo.
            RefreshGenreWorkspace();
        }

        private static string TypeGlyph(TrackType type) => type switch
        {
            TrackType.Jingle => "📻",
            TrackType.Commercial => "📢",
            TrackType.Sweeper => "⚡",
            TrackType.TimeAnnouncement => "🕒",
            TrackType.Voice => "🗣",
            _ => "🎵",
        };

        private static string CategoryFor(TrackType type) => type switch
        {
            TrackType.Music => "Música",
            TrackType.Jingle => "Jingles",
            TrackType.Sweeper => "Separadores",
            TrackType.Voice => "Voces",
            TrackType.Commercial => "Publicidad",
            _ => "Horarias",
        };

        /// <summary>
        /// Un nodo del arbol fija los filtros de la base. No sustituye a la barra de
        /// filtros: los escribe, para que ambas vistas sigan diciendo lo mismo.
        /// </summary>
        private void ApplyNode(LibraryNode node)
        {
            if (node.IsHeader) return;

            _databaseService.ShowOnlyMissing = node.MissingOnly;
            _databaseService.SelectedCategory = node.Category ?? "Todas";
            _databaseService.SelectedGenre = node.Genre ?? "Todos";
            RefreshTree();
            StatusText = node.MissingOnly
                ? $"Filtro por estado: {node.Label} ({node.Count})."
                : $"Filtro: {node.Label} ({node.Count}).";
        }

        private void ClearFilters()
        {
            // Los textos de filtro van primero: vaciarlos ya deja FilterYear/FilterMinBpm
            // en null por su propia cuenta, y despues ClearFilters hace el resto.
            YearText = string.Empty;
            BpmMinText = string.Empty;
            BpmMaxText = string.Empty;
            _databaseService.ClearFilters();
            _selectedNode = null;
            OnPropertyChanged(nameof(SelectedNode));
            RefreshTree();
            StatusText = "Filtros limpiados.";
        }

        // -------------------------------------------------------------------- libreria

        private void ReloadLibrary(bool silent = false)
        {
            try
            {
                var restored = RescanAndRestore();
                StatusText = silent
                    ? restored
                    : $"Biblioteca recargada: {_databaseService.AllTracks.Count} tracks. {restored}";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fallo al recargar la biblioteca");
                StatusText = UserErrorMessages.For(ex, "recargar la biblioteca");
            }
        }

        /// <summary>
        /// La secuencia de carga de la libreria, en el unico orden que es correcto
        /// (UI-4 paso B):
        ///
        /// 1. Escanear las carpetas del disco. Eso da la lista bruta, con el genero de
        ///    fabrica de cada carpeta (todo lo que esta en musica/ nace como "Pop").
        /// 2. Volver a aplicar lo guardado en track_metadata.json encima del escaneo. Sin
        ///    este paso las ediciones del operador se perdia al reiniciar y las pistas
        ///    importadas desde otra carpeta desaparecian de la biblioteca.
        /// 3. Sembrar el motor de rotacion con los contadores que acaban de volver del
        ///    JSON: es lo que hace que SMART PLAYLIST no repita lo de ayer.
        ///
        /// Se llama en el arranque, con RECARGAR y al cerrar la ventana (la consola tiene
        /// que ver los cambios), asi que los tres pasos comparten un solo sitio.
        /// </summary>
        private string RescanAndRestore()
        {
            _databaseService.LoadLibrary();

            var result = _metadataService.LoadMetadata(_databaseService);
            if (result.FileFound && (result.Applied > 0 || result.RestoredImports > 0))
            {
                _databaseService.RecheckFiles();
            }

            var seeded = _rotationEngine.SeedFromLibrary(_databaseService.AllTracks);

            RefreshTree();
            _logger.Information(
                "Libreria cargada: {Total} tracks, {Aplicados} fichas restauradas, {Importados} importados recuperados, {Sembrados} en la rotacion",
                _databaseService.AllTracks.Count, result.Applied, result.RestoredImports, seeded);

            if (!result.FileFound) return "Biblioteca cargada (aun no hay metadatos guardados).";

            var partes = new List<string>();
            if (result.Applied > 0) partes.Add($"{result.Applied} fichas guardadas aplicadas");
            if (result.RestoredImports > 0)
            {
                var aviso = result.MissingFiles > 0 ? $"({result.MissingFiles} sin el archivo delante)" : string.Empty;
                partes.Add($"{result.RestoredImports} importados recuperados{aviso}");
            }
            if (result.Duplicated > 0) partes.Add($"{result.Duplicated} fichas repetidas del mismo archivo, se conservó la primera");
            if (seeded > 0) partes.Add($"{seeded} tracks con historial de rotación");

            return partes.Count == 0
                ? "Biblioteca cargada."
                : "Recuperado de track_metadata.json: " + string.Join(", ", partes) + ".";
        }

        private void RecheckFiles()
        {
            try
            {
                var missing = _databaseService.RecheckFiles();
                _databaseService.ApplyFilters();
                RefreshTree();
                StatusText = missing.Count == 0
                    ? "Comprobados " + _databaseService.AllTracks.Count + " archivos: todos presentes y legibles."
                    : $"Faltan o no se pueden leer {missing.Count} archivos. Marcados en rojo en la tabla.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fallo al comprobar archivos");
                StatusText = UserErrorMessages.For(ex, "comprobar los archivos de la librería");
            }
        }

        // --------------------------------------------------------------------- encolar

        private void QueueSelection()
        {
            var nuevos = 0;
            foreach (var track in _selectedTracks)
            {
                if (_playlist.Contains(track)) continue;
                _playlist.Add(track);
                nuevos++;
            }

            StatusText = $"Encolados {nuevos} tracks. La cola tiene {_playlist.Count} elementos.";
            _logger.Information("Encolados {Count} tracks desde Biblioteca", nuevos);
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
                if (i % 3 == 0 && jingles.Count > 0) _playlist.Add(jingles[rand.Next(jingles.Count)]);
            }

            StatusText = $"Lista automática: {_playlist.Count} elementos (puede repetir tracks).";
            _logger.Information("Generada lista automatica con {Count} elementos", _playlist.Count);
        }

        private void GenerateSmartPlaylist()
        {
            if (_databaseService.AllTracks.Count == 0) return;

            var musicTracks = _databaseService.AllTracks.Where(t => t.Type == TrackType.Music).ToList();
            var jingles = _databaseService.AllTracks.Where(t => t.Type == TrackType.Jingle).ToList();
            if (musicTracks.Count == 0) return;

            _playlist.Clear();
            foreach (var track in _rotationEngine.GetSmartRotation(musicTracks, Math.Min(15, musicTracks.Count)))
                _playlist.Add(track);

            var smartJingles = _rotationEngine.GetSmartRotation(jingles, Math.Min(5, jingles.Count));
            int jingleIndex = 0;
            for (int i = 0; i < _playlist.Count && jingleIndex < smartJingles.Count; i++)
            {
                if (i % 3 == 0) _playlist.Insert(i, smartJingles[jingleIndex++]);
            }

            UpdateHeader();
            StatusText = $"Lista inteligente: {_playlist.Count} elementos sin repetir dentro de la lista. "
                + RotationSummary;
            _logger.Information("Generada lista inteligente con {Count} elementos", _playlist.Count);
        }

        private void ClearRotationHistory()
        {
            _rotationEngine.ClearHistory();
            UpdateHeader();
            // UI-4 paso B: desde que el motor se siembra con lo guardado, limpiar no puede
            // prometer mas de lo que hace. Lo del JSON vuelve al reiniciar; se dice aqui para
            // que el operador no crea que ha borrado el historial de la emisora.
            StatusText = "El motor olvidó lo emitido en esta sesión. Al reiniciar el programa "
                + "vuelve a leer los contadores de track_metadata.json. " + RotationSummary;
            _logger.Information("Historial de rotación limpiado (sesión)");
        }

        private void PlayAux(int channel, object? parameter)
        {
            var track = parameter as TrackItem ?? AnchorTrack;
            if (track == null) return;

            var aux = channel == 1 ? _auxEngine.Aux1 : _auxEngine.Aux2;
            var ok = aux.PlayTrack(track);
            StatusText = ok
                ? $"PFL AUX {channel}: {track.Title} (no sale al aire)."
                : $"AUX {channel} no pudo abrir {Path.GetFileName(track.FilePath)}.";
            _logger.Information("PFL AUX {Channel}: {Title} ({Ok})", channel, track.Title, ok);
        }

        // -------------------------------------------------------------------- inspector

        private void LoadEditorFields(TrackItem? track)
        {
            if (track == null)
            {
                EditTitle = EditArtist = EditAlbum = EditGenre = EditComment = string.Empty;
                EditYearText = EditBpmText = EditCueInText = EditCueOutText = string.Empty;
                EditIntroText = EditOutroText = EditFadeInText = EditFadeOutText = string.Empty;
                EditPriority = null;
                EditCuePointsText = "Ninguno";
                return;
            }

            EditTitle = track.Title;
            EditArtist = track.Artist;
            EditAlbum = track.Album;
            EditGenre = track.Genre;
            EditYearText = track.Year.ToString();
            EditPriority = track.Priority;
            EditBpmText = track.Bpm.ToString("0.#");
            EditCueInText = track.CueIn.TotalSeconds.ToString("0.###");
            EditCueOutText = track.CueOut.TotalSeconds.ToString("0.###");
            EditIntroText = track.Intro.TotalSeconds.ToString("0.###");
            EditOutroText = track.Outro.TotalSeconds.ToString("0.###");
            EditFadeInText = track.FadeIn.ToString("0.##");
            EditFadeOutText = track.FadeOut.ToString("0.##");
            EditType = track.Type;
            EditComment = track.Comment;
            EditCuePointsText = track.CuePointsFormatted;
        }

        private void ApplyEdits()
        {
            var track = AnchorTrack;
            if (track == null) return;

            if (int.TryParse(EditYearText.Trim(), out var year)) track.Year = year;
            if (double.TryParse(ParseNumber(EditBpmText), out var bpm)) track.Bpm = bpm;
            if (double.TryParse(ParseNumber(EditCueInText), out var cueIn)) track.CueIn = TimeSpan.FromSeconds(Math.Max(0, cueIn));
            if (double.TryParse(ParseNumber(EditCueOutText), out var cueOut)) track.CueOut = TimeSpan.FromSeconds(Math.Max(0, cueOut));
            if (double.TryParse(ParseNumber(EditIntroText), out var intro)) track.Intro = TimeSpan.FromSeconds(Math.Max(0, intro));
            if (double.TryParse(ParseNumber(EditOutroText), out var outro)) track.Outro = TimeSpan.FromSeconds(Math.Max(0, outro));
            if (double.TryParse(ParseNumber(EditFadeInText), out var fadeIn)) track.FadeIn = Math.Max(0, fadeIn);
            if (double.TryParse(ParseNumber(EditFadeOutText), out var fadeOut)) track.FadeOut = Math.Max(0, fadeOut);

            track.Title = EditTitle;
            track.Artist = EditArtist;
            track.Album = EditAlbum;
            track.Comment = EditComment;
            track.Type = EditType;
            if (EditPriority.HasValue) track.Priority = EditPriority.Value;
            if (!string.IsNullOrWhiteSpace(EditGenre)) track.Genre = EditGenre.Trim();

            _editorService.NormalizeCuePoints(track);
            if (!_editorService.ValidateCuePoints(track))
                StatusText = $"Ojo: {track.Title} tiene puntos CUE fuera de la duración. Normalizados.";
            else
                StatusText = $"Guardados los datos de {track.Title}.";

            if (!string.IsNullOrWhiteSpace(EditGenre)) _databaseService.AddGenre(EditGenre.Trim());

            LoadEditorFields(track);
            SaveSilently();
            RefreshTree();
        }

        private void ApplyBatch()
        {
            if (_selectedTracks.Count == 0) return;

            int? year = null;
            if (BatchYearEnabled)
            {
                if (!int.TryParse(BatchYearText.Trim(), out var parsedYear))
                {
                    StatusText = "El año del lote no es un número. No se ha aplicado nada.";
                    return;
                }
                year = parsedYear;
            }

            double? fadeIn = null, fadeOut = null;
            if (BatchFadeEnabled)
            {
                if (!string.IsNullOrWhiteSpace(BatchFadeInText))
                {
                    if (!double.TryParse(ParseNumber(BatchFadeInText), out var fi)) { StatusText = "Fade in del lote no es un número."; return; }
                    fadeIn = Math.Max(0, fi);
                }
                if (!string.IsNullOrWhiteSpace(BatchFadeOutText))
                {
                    if (!double.TryParse(ParseNumber(BatchFadeOutText), out var fo)) { StatusText = "Fade out del lote no es un número."; return; }
                    fadeOut = Math.Max(0, fo);
                }
            }

            var touched = 0;
            foreach (var track in _selectedTracks)
            {
                if (BatchGenreEnabled && !string.IsNullOrWhiteSpace(BatchGenre)) track.Genre = BatchGenre.Trim();
                if (BatchPriority.HasValue && BatchPriorityEnabled) track.Priority = BatchPriority.Value;
                if (year.HasValue) track.Year = year.Value;
                if (BatchTypeEnabled) track.Type = BatchType;
                if (fadeIn.HasValue) track.FadeIn = fadeIn.Value;
                if (fadeOut.HasValue) track.FadeOut = fadeOut.Value;
                touched++;
            }

            var applied = new List<string>();
            if (BatchGenreEnabled && !string.IsNullOrWhiteSpace(BatchGenre)) { applied.Add("género"); _databaseService.AddGenre(BatchGenre.Trim()); }
            if (BatchPriorityEnabled && BatchPriority.HasValue) applied.Add("prioridad");
            if (year.HasValue) applied.Add("año");
            if (BatchTypeEnabled) applied.Add("tipo");
            if (fadeIn.HasValue || fadeOut.HasValue) applied.Add("fades");

            StatusText = applied.Count == 0
                ? "No has marcado ningún campo del lote: no se ha cambiado nada."
                : $"{applied.Count} campo(s) aplicados a {touched} tracks: {string.Join(", ", applied)}. Guardado.";

            _logger.Information("Edición en lote de {Count} tracks: {Fields}", touched, string.Join(",", applied));
            if (applied.Count > 0)
            {
                SaveSilently();
                RefreshTree();
            }
        }

        // ------------------------------------------------------------------- puntos CUE

        private void ApplyPreset()
        {
            var name = PresetOptions.Contains(SelectedPreset) ? SelectedPreset : "Radio";
            RunOnSelection(t => _editorService.ApplyEditPreset(t, name), $"Preset {name}");
        }

        /// <summary>
        /// Ejecuta una edicion de la misma forma sobre todas las filas seleccionadas: es
        /// lo que no tenia la ventana vieja, que solo dejaba editar una fila.
        /// </summary>
        private void RunOnSelection(Action<TrackItem> edit, string descripcion)
        {
            if (_selectedTracks.Count == 0) return;

            foreach (var track in _selectedTracks) edit(track);
            LoadEditorFields(AnchorTrack);
            SaveSilently();
            StatusText = $"{descripcion}: aplicado a {_selectedTracks.Count} track(s) y guardado.";
        }

        private void CopyCueToSelection()
        {
            if (_selectedTracks.Count < 2) return;
            var source = _selectedTracks[0];
            foreach (var target in _selectedTracks.Skip(1)) _editorService.CopyCuePoints(source, target);

            LoadEditorFields(AnchorTrack);
            SaveSilently();
            StatusText = $"Puntos CUE de «{source.Title}» copiados a {_selectedTracks.Count - 1} track(s).";
        }

        // ----------------------------------------------------------------- borrar / generar

        private void DeleteSelected()
        {
            if (_selectedTracks.Count == 0) return;

            var confirm = MessageBox.Show(
                $"¿Quitar {_selectedTracks.Count} track(s) de la base de datos?\n\n"
                + "Solo los quita de la biblioteca; no borra los archivos del disco.",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var quitados = 0;
            foreach (var track in _selectedTracks.ToList())
            {
                _databaseService.RemoveTrack(track);
                _metadataService.DeleteTrackMetadata(track.FilePath);
                quitados++;
            }

            UpdateSelection(new List<TrackItem>());
            _databaseService.ApplyFilters();
            RefreshTree();
            StatusText = $"Quitados {quitados} track(s) de la base de datos.";
            _logger.Information("Quitados {Count} tracks de la base de datos", quitados);
        }

        private void AddGenre()
        {
            var name = NewGenreName.Trim();
            if (name.Length == 0) return;

            _databaseService.AddGenre(name);
            NewGenreName = string.Empty;
            OnPropertyChanged(nameof(GenreOptions));
            RefreshTree();
            StatusText = $"Género «{name}» añadido a la lista. Asígnalo en lote para que aparezca en el árbol.";
        }

        // ------------------------------------------------------- renombrar / quitar genero

        /// <summary>
        /// El genero se gestiona desde el propio arbol: al elegir un nodo de GÉNERO, la caja
        /// de abajo se rellena con su nombre (UI-4 paso B). Antes <c>UpdateGenre</c> y
        /// <c>RemoveGenre</c> existian en el servicio pero ninguna vista los llamaba, asi que
        /// una emisora que queria escribir "Bachata" en vez de "Pop" tenia que re-marcar
        /// pista por pista con la edicion en lote.
        /// </summary>
        public bool IsGenreNodeSelected => _selectedNode?.Genre != null;

        private string _genreRenameText = string.Empty;
        public string GenreRenameText
        {
            get => _genreRenameText;
            set { _genreRenameText = value; OnPropertyChanged(); }
        }

        private string _genreDeleteTarget = "General";
        public string GenreDeleteTarget
        {
            get => _genreDeleteTarget;
            set { _genreDeleteTarget = value; OnPropertyChanged(); }
        }

        /// <summary>El genero con el que se relleno la caja por ultima vez.</summary>
        private string? _genreWorkspaceFor;

        // segundo clic de confirmacion de QUITAR genero (sin cuadro modal)
        private string? _quitarArmadoPara;
        private DateTime _quitarArmadoHasta = DateTime.MinValue;

        public string DeleteGenreLabel =>
            _quitarArmadoPara != null && DateTime.Now <= _quitarArmadoHasta ? "⚠ ¿OTRA VEZ?" : "✖ QUITAR";

        public string DeleteGenreHelp =>
            _quitarArmadoPara != null && DateTime.Now <= _quitarArmadoHasta
                ? "pulsa otra vez para confirmar"
                : "quita el género: sus pistas pasan al género de abajo";

        /// <summary>Cuantas pistas levaria el genero elegido en el arbol.</summary>
        public string GenreNodeSummary => IsGenreNodeSelected
            ? $"{_selectedNode!.Genre} · {_selectedNode.Count} pista(s)"
            : "Elige un genero del arbol para renombrarlo o quitarlo";

        public List<string> GenreTargetOptions
        {
            get
            {
                var actual = _selectedNode?.Genre;
                var opciones = GenreOptions.Where(g => !g.Equals(actual, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!opciones.Contains("General")) opciones.Insert(0, "General");
                return opciones;
            }
        }

        private void RefreshGenreWorkspace()
        {
            OnPropertyChanged(nameof(IsGenreNodeSelected));
            OnPropertyChanged(nameof(GenreNodeSummary));
            OnPropertyChanged(nameof(GenreTargetOptions));

            // Se rellena solo cuando cambia el nodo: si no, cualquier refresco del arbol
            // (despues de un lote, de importar) taparia el nombre que se esta escribiendo.
            var genero = _selectedNode?.Genre;
            if (!string.Equals(genero, _genreWorkspaceFor, StringComparison.Ordinal))
            {
                if (genero != null) GenreRenameText = genero;
                // Cambiar de genero desarma el segundo clic: nunca debe quedar armado para
                // un nodo distinto del que el operador vio.
                _quitarArmadoPara = null;
                OnPropertyChanged(nameof(DeleteGenreLabel));
                OnPropertyChanged(nameof(DeleteGenreHelp));
            }
            _genreWorkspaceFor = genero;
        }

        private void RenameGenre()
        {
            var node = _selectedNode;
            if (node?.Genre == null)
            {
                StatusText = "Renombrar un género exige tener su nodo del árbol seleccionado.";
                return;
            }

            var from = node.Genre;
            var to = GenreRenameText.Trim();

            if (to.Length == 0)
            {
                StatusText = "Escribe el nombre nuevo del género; con la caja vacía no se cambia nada.";
                return;
            }

            if (to.Equals(from, StringComparison.OrdinalIgnoreCase))
            {
                StatusText = $"El género ya se llama «{from}».";
                return;
            }

            var afectados = _databaseService.AllTracks
                .Count(t => t.Genre.Equals(from, StringComparison.OrdinalIgnoreCase));

            _databaseService.UpdateGenre(from, to);
            _databaseService.AddGenre(to);

            OnPropertyChanged(nameof(GenreOptions));
            RefreshTree();

            // El nodo viejo se llamo «from» y ya no existe: se sigue la pista renombrada para
            // que la tarjeta de genero no se quede en blanco despues de cambiar el nombre.
            var nodoNuevo = TreeNodes.FirstOrDefault(n => n.Key == "gen-" + to);
            if (nodoNuevo != null)
            {
                _selectedNode = nodoNuevo;
                OnPropertyChanged(nameof(SelectedNode));
                RefreshGenreWorkspace();
            }

            SaveSilently();
            UpdateSelection(_selectedTracks.ToList());

            StatusText = $"Género «{from}» renombrado a «{to}»: {afectados} pista(s) actualizadas y guardadas.";
            _logger.Information("Género renombrado de {From} a {To} en {Count} tracks", from, to, afectados);
        }

        private void DeleteGenre()
        {
            var node = _selectedNode;
            if (node?.Genre == null)
            {
                StatusText = "Para quitar un género, selecciona antes su nodo del árbol.";
                return;
            }

            var from = node.Genre;
            var destino = string.IsNullOrWhiteSpace(GenreDeleteTarget) ? "General" : GenreDeleteTarget.Trim();

            if (destino.Equals(from, StringComparison.OrdinalIgnoreCase))
            {
                StatusText = $"Un género no puede pasar a llamarse igual: elige otro destino para sus {node.Count} pista(s).";
                return;
            }

            // Segundo clic: el primero solo arma el boton. Es la forma de confirmar sin
            // cuadro modal, que en cabina se pone encima del aire.
            if (_quitarArmadoPara != from || DateTime.Now > _quitarArmadoHasta)
            {
                _quitarArmadoPara = from;
                _quitarArmadoHasta = DateTime.Now.AddSeconds(12);
                OnPropertyChanged(nameof(DeleteGenreLabel));
                OnPropertyChanged(nameof(DeleteGenreHelp));
                StatusText = $"Pulsa otra vez en 12 segundos para quitar «{from}»: sus {node.Count} pista(s) pasarán a «{destino}». Ningún archivo se borra.";
                return;
            }

            _quitarArmadoPara = null;
            OnPropertyChanged(nameof(DeleteGenreLabel));
            OnPropertyChanged(nameof(DeleteGenreHelp));

            var movidos = 0;
            foreach (var track in _databaseService.AllTracks
                         .Where(t => t.Genre.Equals(from, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                track.Genre = destino;
                movidos++;
            }

            _databaseService.RemoveGenre(from);
            _databaseService.AddGenre(destino);

            if (_databaseService.SelectedGenre.Equals(from, StringComparison.OrdinalIgnoreCase))
                _databaseService.SelectedGenre = "Todos";
            else
                _databaseService.ApplyFilters();

            UpdateSelection(new List<TrackItem>());
            OnPropertyChanged(nameof(GenreOptions));
            RefreshTree();
            SaveSilently();

            StatusText = $"Género «{from}» quitado: {movidos} pista(s) ahora son «{destino}».";
            _logger.Information("Género {Genre} quitado, {Count} tracks pasan a {Destino}", from, movidos, destino);
        }

        // ------------------------------------------------------------------- importacion

        private void ImportFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = AudioSourceFactory.FileDialogFilter,
                Multiselect = true,
                Title = "Seleccionar archivos de audio para importar"
            };

            if (dialog.ShowDialog() == true) ImportAudioFiles(dialog.FileNames);
        }

        private void ImportFolder()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = AudioSourceFactory.FileDialogFilter,
                    Title = "IMPORTACIÓN MASIVA: elige cualquier archivo de la carpeta",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true) return;

                var folder = Path.GetDirectoryName(dialog.FileName);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    StatusText = "No se ha podido determinar la carpeta del archivo elegido.";
                    return;
                }

                var allAudioFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(AudioSourceFactory.IsSupported).ToArray();
                if (allAudioFiles.Length == 0)
                {
                    StatusText = $"No hay archivos de audio en {folder} ni en sus subcarpetas.";
                    return;
                }

                var confirm = MessageBox.Show(
                    $"Carpeta: {folder}\n\n{allAudioFiles.Length} archivos de audio encontrados.\n"
                    + "Se añaden a la biblioteca con el tipo y género elegidos abajo. ¿Importar?",
                    "Importación masiva de carpeta", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes) ImportAudioFiles(allAudioFiles);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fallo en importación de carpeta");
                StatusText = UserErrorMessages.For(ex, "importar la carpeta");
            }
        }

        private void ImportAudioFiles(string[] filePaths)
        {
            try
            {
                var importados = 0;
                var fallidos = 0;
                var genre = string.IsNullOrWhiteSpace(ImportGenre) ? "General" : ImportGenre.Trim();

                foreach (var filePath in filePaths)
                {
                    try
                    {
                        var track = TrackLoader.CreateFromFile(filePath);
                        track.Genre = genre;
                        track.Type = ImportType;
                        track.FileAvailable = File.Exists(filePath) && track.Duration > TimeSpan.Zero;
                        _databaseService.AddTrack(track);
                        importados++;
                    }
                    catch (Exception ex)
                    {
                        fallidos++;
                        _logger.Warning(ex, "No se pudo importar {File}", filePath);
                    }
                }

                _databaseService.ApplyFilters();
                RefreshTree();
                StatusText = $"Importados {importados}/{filePaths.Length} como «{ImportType} · {genre}»"
                    + (fallidos > 0 ? $" · {fallidos} no se pudieron leer." : " · guarda los metadatos al cerrar.");
                SaveSilently();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fallo en importación");
                StatusText = UserErrorMessages.For(ex, "importar los archivos elegidos");
            }
        }

        // -------------------------------------------------------------------- metadatos

        /// <summary>
        /// Guarda sin ventanitas emergentes: la ventana vieja avisaba con un MessageBox
        /// cada vez que se cerraba, y en cabina eso es un cuadro tapando el aire.
        /// </summary>
        public bool SaveSilently()
        {
            try
            {
                _metadataService.SaveMetadata(_databaseService.AllTracks);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "No se pudieron guardar los metadatos");
                StatusText = UserErrorMessages.For(ex, "guardar los metadatos");
                return false;
            }
        }

        private void Notify(string message) => StatusText = message;

        private void LoadMetadata()
        {
            try
            {
                var result = _metadataService.LoadMetadata(_databaseService);
                _databaseService.RecheckFiles();
                _databaseService.ApplyFilters();
                var sembrados = _rotationEngine.SeedFromLibrary(_databaseService.AllTracks);
                RefreshTree();
                UpdateSelection(new List<TrackItem>());
                StatusText = !result.FileFound
                    ? "No hay track_metadata.json en la carpeta del programa: nada que cargar."
                    : $"Metadatos aplicados sobre {result.Applied} pista(s)"
                        + (result.RestoredImports > 0 ? $" y {result.RestoredImports} importado(s) recuperado(s)" : string.Empty)
                        + (sembrados > 0 ? $" y {sembrados} con historial de rotación" : string.Empty)
                        + ". " + RotationSummary;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "No se pudieron cargar los metadatos");
                StatusText = UserErrorMessages.For(ex, "cargar los metadatos guardados");
            }
        }

        private void ExportCsv()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"library_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (dialog.ShowDialog() != true) return;

                _metadataService.ExportMetadataToCsv(dialog.FileName);
                StatusText = $"Exportado a {dialog.FileName}";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "No se pudo exportar a CSV");
                StatusText = UserErrorMessages.For(ex, "exportar la biblioteca a CSV");
            }
        }

        // --------------------------------------------------------------------- numerica

        private static string ParseNumber(string? text)
            => (text ?? string.Empty).Trim().Replace(',', '.');
    }

    /// <summary>
    /// Una fila del arbol de categorias de Biblioteca. Es plana y con nivel en vez de
    /// TreeViewItem anidado: la seleccion de un TreeView anidado enlaza fatal con
    /// MVVM y el arbol de la libreria solo tiene dos alturas de verdad.
    /// </summary>
    public sealed class LibraryNode
    {
        public LibraryNode(
            string key, string glyph, string label, int count,
            bool isHeader = false, string? category = null, string? genre = null,
            bool missingOnly = false)
        {
            Key = key;
            Glyph = glyph;
            Label = label;
            Count = count;
            IsHeader = isHeader;
            Category = category;
            Genre = genre;
            MissingOnly = missingOnly;
        }

        public string Key { get; }
        public string Glyph { get; }
        public string Label { get; }
        public int Count { get; }
        public bool IsHeader { get; }
        public string? Category { get; }
        public string? Genre { get; }
        public bool MissingOnly { get; }

        public int Indent => IsHeader ? 0 : 8;
        public Thickness IconIndent => new Thickness(IsHeader ? 0 : 8, 0, 0, 0);
        public string CountText => Count < 0 ? string.Empty : Count.ToString();
        public bool IsCountVisible => Count >= 0;
        public string AutomationName => IsHeader ? Label : $"{Label}, {Count} tracks";
    }
}
