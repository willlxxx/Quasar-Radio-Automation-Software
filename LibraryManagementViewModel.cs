using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;

namespace quasar_radio
{
    public class LibraryManagementViewModel : ViewModelBase
    {
        private readonly AudioDatabaseService _databaseService;
        private readonly TrackMetadataService _metadataService;
        private readonly TrackEditorService _editorService;
        private readonly AdvancedSearchService _searchService;
        private readonly AudioAnalysisService? _analysisService;

        // Servicios públicos
        public AudioDatabaseService DatabaseService => _databaseService;
        public TrackEditorService EditorService => _editorService;
        public AdvancedSearchService SearchService => _searchService;
        public AudioAnalysisService? AnalysisService => _analysisService;

        // Selección actual
        private TrackItem? _selectedTrack;
        public TrackItem? SelectedTrack
        {
            get => _selectedTrack;
            set { _selectedTrack = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTrackSelected)); }
        }

        public bool IsTrackSelected => _selectedTrack != null;

        // Filtros avanzados
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); ApplyAdvancedFilters(); }
        }

        private int? _filterYear;
        public int? FilterYear
        {
            get => _filterYear;
            set { _filterYear = value; OnPropertyChanged(); ApplyAdvancedFilters(); }
        }

        private string _filterArtist = string.Empty;
        public string FilterArtist
        {
            get => _filterArtist;
            set { _filterArtist = value; OnPropertyChanged(); ApplyAdvancedFilters(); }
        }

        private int? _filterMinPriority;
        public int? FilterMinPriority
        {
            get => _filterMinPriority;
            set { _filterMinPriority = value; OnPropertyChanged(); ApplyAdvancedFilters(); }
        }

        private int? _filterMaxPriority;
        public int? FilterMaxPriority
        {
            get => _filterMaxPriority;
            set { _filterMaxPriority = value; OnPropertyChanged(); ApplyAdvancedFilters(); }
        }

        private double? _filterMinBpm;
        public double? FilterMinBpm
        {
            get => _filterMinBpm;
            set { _filterMinBpm = value; OnPropertyChanged(); ApplyAdvancedFilters(); }
        }

        private double? _filterMaxBpm;
        public double? FilterMaxBpm
        {
            get => _filterMaxBpm;
            set { _filterMaxBpm = value; OnPropertyChanged(); ApplyAdvancedFilters(); }
        }

        // Ordenamiento
        private string _sortBy = "Title";
        public string SortBy
        {
            get => _sortBy;
            set { _sortBy = value; OnPropertyChanged(); ApplyAdvancedFilters(); }
        }

        private bool _sortAscending = true;
        public bool SortAscending
        {
            get => _sortAscending;
            set { _sortAscending = value; OnPropertyChanged(); ApplyAdvancedFilters(); }
        }

        // Resultados de búsqueda
        public ObservableCollection<TrackItem> SearchResults { get; } = new ObservableCollection<TrackItem>();

        // Estadísticas
        private string _statisticsText = string.Empty;
        public string StatisticsText
        {
            get => _statisticsText;
            set { _statisticsText = value; OnPropertyChanged(); }
        }

        // Estado de edición
        private string _editInfo = string.Empty;
        public string EditInfo
        {
            get => _editInfo;
            set { _editInfo = value; OnPropertyChanged(); }
        }

        // Preset seleccionado
        private string _selectedPreset = "Radio";
        public string SelectedPreset
        {
            get => _selectedPreset;
            set { _selectedPreset = value; OnPropertyChanged(); }
        }

        // Gestión de géneros
        private string _newGenreName = string.Empty;
        public string NewGenreName
        {
            get => _newGenreName;
            set { _newGenreName = value; OnPropertyChanged(); }
        }

        private string _selectedGenreForEdit = string.Empty;
        public string SelectedGenreForEdit
        {
            get => _selectedGenreForEdit;
            set { _selectedGenreForEdit = value; OnPropertyChanged(); }
        }

        // Análisis de audio
        private string _analysisStatus = string.Empty;
        public string AnalysisStatus
        {
            get => _analysisStatus;
            set { _analysisStatus = value; OnPropertyChanged(); }
        }

        private bool _isAnalyzing = false;
        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set { _isAnalyzing = value; OnPropertyChanged(); }
        }

        private AudioAnalysisResult? _currentAnalysisResult;
        public AudioAnalysisResult? CurrentAnalysisResult
        {
            get => _currentAnalysisResult;
            set { _currentAnalysisResult = value; OnPropertyChanged(); }
        }

        // Comandos
        public ICommand RefreshLibraryCommand { get; }
        public ICommand ApplyFiltersCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        public ICommand SaveMetadataCommand { get; }
        public ICommand LoadMetadataCommand { get; }
        public ICommand ExportToCsvCommand { get; }
        public ICommand EditTrackCommand { get; }
        public ICommand ApplyPresetCommand { get; }
        public ICommand ResetCuePointsCommand { get; }
        public ICommand IntelligentCuePointsCommand { get; }
        public ICommand NormalizeCuePointsCommand { get; }
        public ICommand CopyCuePointsCommand { get; }
        public ICommand AddCuePointCommand { get; }
        public ICommand RemoveCuePointCommand { get; }
        public ICommand DeleteTrackCommand { get; }
        public ICommand CloseWindowCommand { get; set; }
        public ICommand SearchCommand { get; }
        public ICommand SmartSearchCommand { get; }
        public ICommand UpdateStatisticsCommand { get; }
        
        // Comandos de gestión de géneros
        public ICommand AddGenreCommand { get; }
        public ICommand RemoveGenreCommand { get; }
        public ICommand UpdateGenreCommand { get; }
        
        // Comandos de análisis de audio
        public ICommand AnalyzeAudioCommand { get; }
        public ICommand ApplyAutoCueCommand { get; }
        public ICommand ApplyAutoGainCommand { get; }
        public ICommand ApplyAudioEnhancementCommand { get; }
        public ICommand ImportTracksCommand { get; }
        public ICommand ImportFolderCommand { get; }

        public LibraryManagementViewModel(AudioDatabaseService? databaseService = null)
        {
            _databaseService = databaseService ?? App.Services?.GetService<AudioDatabaseService>() ?? new AudioDatabaseService();
            _metadataService = new TrackMetadataService();
            _editorService = new TrackEditorService();
            _searchService = new AdvancedSearchService(_databaseService);
            try 
            {
                _analysisService = new AudioAnalysisService();
            }
            catch
            {
                _analysisService = null; // Manejar error de inicialización
            }

            // Inicializar comandos
            RefreshLibraryCommand = new RelayCommand(_ => RefreshLibrary());
            ApplyFiltersCommand = new RelayCommand(_ => ApplyAdvancedFilters());
            ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
            SaveMetadataCommand = new RelayCommand(_ => SaveMetadata());
            LoadMetadataCommand = new RelayCommand(_ => LoadMetadata());
            ExportToCsvCommand = new RelayCommand(_ => ExportToCsv());
            EditTrackCommand = new RelayCommand(_ => EditTrack(), _ => IsTrackSelected);
            ApplyPresetCommand = new RelayCommand(param => ApplyPreset(param?.ToString() ?? "Radio"));
            ResetCuePointsCommand = new RelayCommand(_ => ResetCuePoints(), _ => IsTrackSelected);
            IntelligentCuePointsCommand = new RelayCommand(_ => SetIntelligentCuePoints(), _ => IsTrackSelected);
            NormalizeCuePointsCommand = new RelayCommand(_ => NormalizeCuePoints(), _ => IsTrackSelected);
            CopyCuePointsCommand = new RelayCommand(_ => CopyCuePoints(), _ => IsTrackSelected);
            AddCuePointCommand = new RelayCommand(_ => AddCuePoint(), _ => IsTrackSelected);
            RemoveCuePointCommand = new RelayCommand(_ => RemoveCuePoint(), _ => IsTrackSelected);
            DeleteTrackCommand = new RelayCommand(_ => DeleteTrack(), _ => IsTrackSelected);
            CloseWindowCommand = new RelayCommand(_ => { });
            SearchCommand = new RelayCommand(_ => ExecuteSearch());
            SmartSearchCommand = new RelayCommand(_ => ExecuteSmartSearch());
            UpdateStatisticsCommand = new RelayCommand(_ => UpdateStatistics());
            
            // Comandos de gestión de géneros
            AddGenreCommand = new RelayCommand(_ => AddGenre());
            RemoveGenreCommand = new RelayCommand(_ => RemoveGenre());
            UpdateGenreCommand = new RelayCommand(_ => UpdateGenre());
            
            // Comandos de análisis de audio
            AnalyzeAudioCommand = new RelayCommand(_ => AnalyzeAudio(), _ => IsTrackSelected && !IsAnalyzing);
            ApplyAutoCueCommand = new RelayCommand(_ => ApplyAutoCue(), _ => IsTrackSelected && CurrentAnalysisResult != null);
            ApplyAutoGainCommand = new RelayCommand(_ => ApplyAutoGain(), _ => IsTrackSelected && CurrentAnalysisResult != null);
            ApplyAudioEnhancementCommand = new RelayCommand(_ => ApplyAudioEnhancement(), _ => IsTrackSelected && CurrentAnalysisResult != null);
            ImportTracksCommand = new RelayCommand(_ => ImportTracks());
            ImportFolderCommand = new RelayCommand(_ => ImportFolder());

            // Cargar biblioteca inicial
            RefreshLibrary();
            UpdateStatistics();
        }

        private void RefreshLibrary()
        {
            _databaseService.LoadLibrary();
            ApplyAdvancedFilters();
            UpdateStatistics();
        }

        private void ApplyAdvancedFilters()
        {
            SearchResults.Clear();

            var criteria = new SearchCriteria
            {
                Title = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                Artist = string.IsNullOrWhiteSpace(FilterArtist) ? null : FilterArtist,
                Year = FilterYear,
                MinPriority = FilterMinPriority,
                MaxPriority = FilterMaxPriority,
                MinBpm = FilterMinBpm,
                MaxBpm = FilterMaxBpm
            };

            var results = _searchService.AdvancedSearch(criteria);
            var sortedResults = _searchService.Sort(results, SortBy, SortAscending);

            foreach (var track in sortedResults)
            {
                SearchResults.Add(track);
            }

            UpdateStatistics();
        }

        private void ClearFilters()
        {
            SearchText = string.Empty;
            FilterYear = null;
            FilterArtist = string.Empty;
            FilterMinPriority = null;
            FilterMaxPriority = null;
            FilterMinBpm = null;
            FilterMaxBpm = null;
            SortBy = "Title";
            SortAscending = true;
            ApplyAdvancedFilters();
        }

        private void SaveMetadata()
        {
            try
            {
                _metadataService.SaveMetadata(_databaseService.AllTracks);
                MessageBox.Show("Metadatos guardados correctamente", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar metadatos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadMetadata()
        {
            try
            {
                _metadataService.LoadMetadata(_databaseService);
                ApplyAdvancedFilters();
                MessageBox.Show("Metadatos cargados correctamente", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar metadatos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToCsv()
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"library_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    _metadataService.ExportMetadataToCsv(saveFileDialog.FileName);
                    MessageBox.Show($"Exportación completada: {saveFileDialog.FileName}", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditTrack()
        {
            if (SelectedTrack == null) return;
            EditInfo = _editorService.GetEditInfo(SelectedTrack);
        }

        private void ApplyPreset(string presetName)
        {
            if (SelectedTrack == null) return;
            try
            {
                _editorService.ApplyEditPreset(SelectedTrack, presetName);
                EditInfo = _editorService.GetEditInfo(SelectedTrack);
                OnPropertyChanged(nameof(SelectedTrack));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al aplicar preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetCuePoints()
        {
            if (SelectedTrack == null) return;
            _editorService.ResetCuePoints(SelectedTrack);
            EditInfo = _editorService.GetEditInfo(SelectedTrack);
            OnPropertyChanged(nameof(SelectedTrack));
        }

        private void SetIntelligentCuePoints()
        {
            if (SelectedTrack == null) return;
            _editorService.SetIntelligentCuePoints(SelectedTrack);
            EditInfo = _editorService.GetEditInfo(SelectedTrack);
            OnPropertyChanged(nameof(SelectedTrack));
        }

        private void NormalizeCuePoints()
        {
            if (SelectedTrack == null) return;
            _editorService.NormalizeCuePoints(SelectedTrack);
            EditInfo = _editorService.GetEditInfo(SelectedTrack);
            OnPropertyChanged(nameof(SelectedTrack));
        }

        private void CopyCuePoints()
        {
            if (SelectedTrack == null) return;
            MessageBox.Show("Función de copiar puntos CUE entre tracks aún no implementada en la interfaz", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddCuePoint()
        {
            if (SelectedTrack == null) return;
            MessageBox.Show("Función de agregar puntos CUE aún no implementada en la interfaz", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RemoveCuePoint()
        {
            if (SelectedTrack == null) return;
            MessageBox.Show("Función de eliminar puntos CUE aún no implementada en la interfaz", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteTrack()
        {
            if (SelectedTrack == null) return;

            var result = MessageBox.Show(
                $"¿Está seguro de eliminar el track '{SelectedTrack.Title}' de la base de datos?",
                "Confirmar eliminación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _databaseService.RemoveTrack(SelectedTrack);
                _metadataService.DeleteTrackMetadata(SelectedTrack.FilePath);
                SelectedTrack = null;
                EditInfo = string.Empty;
                UpdateStatistics();
            }
        }

        private void ExecuteSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;
            ApplyAdvancedFilters();
        }

        private void ExecuteSmartSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            SearchResults.Clear();
            var results = _searchService.SmartSearch(SearchText);

            foreach (var track in results)
            {
                SearchResults.Add(track);
            }

            UpdateStatistics();
        }

        private void UpdateStatistics()
        {
            var stats = new List<string>
            {
                $"Total tracks: {_databaseService.GetTotalTracks()}",
                $"En resultados: {SearchResults.Count}",
                $"Duración total: {_databaseService.GetTotalDuration():hh\\:mm\\:ss}",
                $"Duración promedio: {TimeSpan.FromSeconds(_databaseService.GetAverageDuration()):mm\\:ss}",
                $"Artistas únicos: {_databaseService.GetAllArtists().Count}",
                $"Géneros únicos: {_databaseService.GetAllGenres().Count}",
                $"Años en biblioteca: {_databaseService.GetAllYears().Count}"
            };

            StatisticsText = string.Join(" | ", stats);
        }

        // Métodos auxiliares para la UI
        public List<string> GetSortOptions() => new List<string> { "Title", "Artist", "Genre", "Year", "Priority", "Bpm", "Duration", "PlayCount" };
        public List<string> GetPresetOptions() => new List<string> { "Radio", "Club", "Podcast", "Clean" };
        public List<int> GetPriorityRange() => Enumerable.Range(1, 10).ToList();
        public List<int> GetYearRange() => Enumerable.Range(1900, DateTime.Now.Year - 1899 + 1).Reverse().ToList();

        // Métodos de gestión de géneros
        private void AddGenre()
        {
            if (string.IsNullOrWhiteSpace(NewGenreName)) return;
            
            _databaseService.AddGenre(NewGenreName);
            NewGenreName = string.Empty;
            OnPropertyChanged(nameof(DatabaseService.Genres));
            
            MessageBox.Show($"Género '{NewGenreName}' agregado correctamente", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RemoveGenre()
        {
            if (string.IsNullOrWhiteSpace(SelectedGenreForEdit)) return;
            if (SelectedGenreForEdit == "Todos") return;

            var result = MessageBox.Show(
                $"¿Está seguro de eliminar el género '{SelectedGenreForEdit}'?",
                "Confirmar eliminación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _databaseService.RemoveGenre(SelectedGenreForEdit);
                SelectedGenreForEdit = string.Empty;
                OnPropertyChanged(nameof(DatabaseService.Genres));
                
                MessageBox.Show("Género eliminado correctamente", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateGenre()
        {
            if (string.IsNullOrWhiteSpace(SelectedGenreForEdit) || string.IsNullOrWhiteSpace(NewGenreName)) return;
            if (SelectedGenreForEdit == "Todos") return;

            _databaseService.UpdateGenre(SelectedGenreForEdit, NewGenreName);
            var oldName = SelectedGenreForEdit;
            SelectedGenreForEdit = NewGenreName;
            NewGenreName = string.Empty;
            OnPropertyChanged(nameof(DatabaseService.Genres));
            
            MessageBox.Show($"Género renombrado de '{oldName}' a '{SelectedGenreForEdit}'", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Métodos de análisis de audio
        private void AnalyzeAudio()
        {
            if (SelectedTrack == null || _analysisService == null) return;

            try
            {
                IsAnalyzing = true;
                AnalysisStatus = "Analizando audio... por favor espere";
                
                // Análisis en background para no bloquear la UI
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var result = _analysisService.AnalyzeAudioFile(SelectedTrack.FilePath);
                        
                        // Actualizar UI en el thread principal
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            CurrentAnalysisResult = result;
                            AnalysisStatus = $"Análisis completado: BPM={result.Bpm:F0}, Pico={result.PeakAmplitude:F2}, RMS={result.RmsAmplitude:F2}";
                            IsAnalyzing = false;
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            AnalysisStatus = $"Error en análisis: {ex.Message}";
                            IsAnalyzing = false;
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                AnalysisStatus = $"Error iniciando análisis: {ex.Message}";
                IsAnalyzing = false;
            }
        }

        private void ApplyAutoCue()
        {
            if (SelectedTrack == null || CurrentAnalysisResult == null || _analysisService == null) return;

            try
            {
                SelectedTrack.CueIn = CurrentAnalysisResult.AutoCueIn ?? TimeSpan.Zero;
                SelectedTrack.CueOut = CurrentAnalysisResult.AutoCueOut ?? SelectedTrack.Duration;
                SelectedTrack.Intro = CurrentAnalysisResult.IntroPoint ?? TimeSpan.Zero;
                SelectedTrack.Outro = CurrentAnalysisResult.OutroPoint ?? TimeSpan.Zero;
                
                EditInfo = _editorService.GetEditInfo(SelectedTrack);
                OnPropertyChanged(nameof(SelectedTrack));
                
                MessageBox.Show("Puntos CUE automáticos aplicados correctamente", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error aplicando AutoCue: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyAutoGain()
        {
            if (SelectedTrack == null || CurrentAnalysisResult == null) return;

            try
            {
                // Calcular ganancia basada en el análisis
                var gainDb = 20 * Math.Log10(CurrentAnalysisResult.RecommendedGain);
                SelectedTrack.FadeIn = Math.Max(0, gainDb); // Usar FadeIn como ganancia temporal
                SelectedTrack.FadeOut = 0; // Resetear fade out
                
                MessageBox.Show($"Ganancia automática aplicada: {gainDb:F1}dB", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error aplicando ganancia automática: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyAudioEnhancement()
        {
            if (SelectedTrack == null || CurrentAnalysisResult == null || _analysisService == null) return;

            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "WAV Files (*.wav)|*.wav|MP3 Files (*.mp3)|*.mp3",
                    DefaultExt = "wav",
                    FileName = $"enhanced_{Path.GetFileNameWithoutExtension(SelectedTrack.FilePath)}.wav"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    _analysisService.ApplyAudioEnhancement(SelectedTrack.FilePath, saveFileDialog.FileName, CurrentAnalysisResult);
                    MessageBox.Show($"Mejora de audio aplicada. Archivo guardado en: {saveFileDialog.FileName}", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error aplicando mejora de audio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Métodos de importación
        private void ImportTracks()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = AudioSourceFactory.FileDialogFilter,
                Multiselect = true,
                Title = "Seleccionar archivos de audio para importar"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ImportAudioFiles(openFileDialog.FileNames);
            }
        }

        private void ImportFolder()
        {
            try
            {
                // Usar OpenFileDialog para seleccionar carpeta (enfoque mejorado)
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = AudioSourceFactory.FileDialogFilter,
                    Title = "IMPORTACIÓN MASIVA - Selecciona cualquier archivo en la carpeta deseada",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var folderPath = Path.GetDirectoryName(openFileDialog.FileName);
                    if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                    {
                        // Buscar archivos de audio en todas las subcarpetas (recursivo)
                        var allAudioFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                            .Where(AudioSourceFactory.IsSupported)
                            .ToArray();

                        if (allAudioFiles.Length > 0)
                        {
                            var confirmResult = MessageBox.Show(
                                $"📁 Carpeta: {Path.GetFileName(folderPath)}\n\n" +
                                $"🎵 Se encontraron {allAudioFiles.Length} archivos de audio en la carpeta y sus subcarpetas.\n\n" +
                                $"¿Desea importar todos estos archivos?",
                                "Importación Masiva de Carpeta",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (confirmResult == MessageBoxResult.Yes)
                            {
                                ImportAudioFiles(allAudioFiles);
                            }
                        }
                        else
                        {
                            MessageBox.Show("No se encontraron archivos de audio en la carpeta seleccionada ni en sus subcarpetas", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        MessageBox.Show("No se pudo determinar la carpeta del archivo seleccionado", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en importación de carpeta: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportAudioFiles(string[] filePaths)
        {
            try
            {
                var importedCount = 0;
                var genre = SelectedGenreForEdit ?? "General";

                foreach (var filePath in filePaths)
                {
                    try
                    {
                        var track = TrackLoader.CreateFromFile(filePath);
                        track.Genre = genre;
                        _databaseService.AddTrack(track);
                        importedCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error importando {filePath}: {ex.Message}");
                    }
                }

                ApplyAdvancedFilters();
                UpdateStatistics();
                
                MessageBox.Show($"{importedCount} de {filePaths.Length} archivos importados correctamente como '{genre}'", 
                    "Importación completada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error durante la importación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}