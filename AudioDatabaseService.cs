using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace quasar_radio
{
    public class AudioDatabaseService : ViewModelBase
    {
        public ObservableCollection<TrackItem> AllTracks { get; } = new ObservableCollection<TrackItem>();
        public ObservableCollection<TrackItem> FilteredTracks { get; } = new ObservableCollection<TrackItem>();

        private string _selectedCategory = "Todas";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private string _selectedGenre = "Todos";
        public string SelectedGenre
        {
            get => _selectedGenre;
            set
            {
                _selectedGenre = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private int? _filterYear;
        public int? FilterYear
        {
            get => _filterYear;
            set
            {
                _filterYear = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private string _filterArtist = string.Empty;
        public string FilterArtist
        {
            get => _filterArtist;
            set
            {
                _filterArtist = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private int? _filterPriority;
        public int? FilterPriority
        {
            get => _filterPriority;
            set
            {
                _filterPriority = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        // UI-4: Biblioteca filtra por rango de BPM. El filtro vivia en
        // AdvancedSearchService y ninguna vista lo llamaba; aqui queda junto al resto
        // de filtros, que son los de verdad que se aplican a la tabla.
        private double? _filterMinBpm;
        public double? FilterMinBpm
        {
            get => _filterMinBpm;
            set
            {
                _filterMinBpm = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private double? _filterMaxBpm;
        public double? FilterMaxBpm
        {
            get => _filterMaxBpm;
            set
            {
                _filterMaxBpm = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        // UI-4: "Archivos no encontrados" como nodo del arbol de categorias. No es un
        // filtro de metadatos, es el estado del disco, asi que se filtra aparte.
        private bool _showOnlyMissing;
        public bool ShowOnlyMissing
        {
            get => _showOnlyMissing;
            set
            {
                _showOnlyMissing = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private string _sortBy = "Title";
        public string SortBy
        {
            get => _sortBy;
            set
            {
                _sortBy = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private bool _sortAscending = true;
        public bool SortAscending
        {
            get => _sortAscending;
            set
            {
                _sortAscending = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        public List<string> Categories { get; } = new List<string>
        {
            "Todas",
            "Música",
            "Jingles",
            "Separadores",
            "Voces",
            "Publicidad",
            "Horarias"
        };

        public List<string> Genres { get; } = new List<string>
        {
            "Todos",
            "Pop",
            "Rock",
            "Salsa",
            "Reggaeton",
            "Electro",
            "Balada",
            "General"
        };

        // Gestión de géneros personalizados
        public void AddGenre(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre)) return;
            if (!Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
            {
                Genres.Add(genre);
            }
        }

        public void RemoveGenre(string genre)
        {
            if (genre == "Todos") return; // No eliminar "Todos"
            Genres.Remove(genre);
        }

        public void UpdateGenre(string oldGenre, string newGenre)
        {
            if (string.IsNullOrWhiteSpace(newGenre)) return;

            var index = Genres.FindIndex(g => g.Equals(oldGenre, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                Genres[index] = newGenre;
            }

            // UI-4: renombrar el genero tambien tiene que renombrar los tracks que lo
            // llevan. Antes solo se cambiaba la etiqueta de la lista y la libreria
            // segua diciendo lo mismo: el cambio no se veia en ningun lado.
            foreach (var track in AllTracks.Where(t => t.Genre.Equals(oldGenre, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                track.Genre = newGenre;
            }

            if (SelectedGenre.Equals(oldGenre, StringComparison.OrdinalIgnoreCase))
            {
                SelectedGenre = newGenre;
            }
            else
            {
                ApplyFilters();
            }
        }

        public List<string> SortOptions { get; } = new List<string>
        {
            "Title",
            "Artist",
            "Genre",
            "Year",
            "Priority",
            "Duration",
            "PlayCount",
            "LastPlayed"
        };

        public void LoadLibrary()
        {
            AllTracks.Clear();

            // 1. Música
            LoadFolderIntoCategory("musica", TrackType.Music, "Pop");
            // 2. Jingles
            LoadFolderIntoCategory("jingles", TrackType.Jingle, "Jingle");
            // 3. Separadores
            LoadFolderIntoCategory("separadores", TrackType.Sweeper, "Separador");
            // 4. Voces
            LoadFolderIntoCategory("voces", TrackType.Voice, "Locución");
            // 5. Publicidad
            LoadFolderIntoCategory("comerciales", TrackType.Commercial, "Publicidad");
            // 6. Horarias
            LoadFolderIntoCategory("horarias", TrackType.TimeAnnouncement, "Hora");

            // UI-4: el arbol de ESTADO y el glifo ⚠ se leen de FileAvailable, asi que el
            // escaneo tiene que preguntar por el disco y por NAudio antes de pintar. Sin
            // esto la ventana abria con "0 problematicos" y solo mentia hasta que alguien
            // pulsaba COMPROBAR ARCHIVOS.
            RecheckFiles();

            ApplyFilters();
        }

        private void LoadFolderIntoCategory(string folderName, TrackType type, string defaultGenre)
        {
            if (!Directory.Exists(folderName))
            {
                Directory.CreateDirectory(folderName);
                return;
            }

            var files = Directory.GetFiles(folderName, "*.*")
                                 .Where(AudioSourceFactory.IsSupported);

            foreach (var file in files)
            {
                var track = TrackLoader.CreateFromFile(file);
                track.Type = type;
                track.Genre = defaultGenre;
                AllTracks.Add(track);
            }
        }

        public void ApplyFilters()
        {
            FilteredTracks.Clear();

            var query = AllTracks.AsEnumerable();

            if (SelectedCategory != "Todas")
            {
                TrackType? targetType = SelectedCategory switch
                {
                    "Música" => TrackType.Music,
                    "Jingles" => TrackType.Jingle,
                    "Separadores" => TrackType.Sweeper,
                    "Voces" => TrackType.Voice,
                    "Publicidad" => TrackType.Commercial,
                    "Horarias" => TrackType.TimeAnnouncement,
                    _ => null
                };

                if (targetType.HasValue)
                {
                    query = query.Where(t => t.Type == targetType.Value);
                }
            }

            if (SelectedGenre != "Todos")
            {
                query = query.Where(t => t.Genre.Equals(SelectedGenre, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim().ToLower();
                query = query.Where(t => t.Title.ToLower().Contains(term) || 
                                         t.Artist.ToLower().Contains(term) ||
                                         t.Album.ToLower().Contains(term) ||
                                         t.Composer.ToLower().Contains(term));
            }

            if (FilterYear.HasValue)
            {
                query = query.Where(t => t.Year == FilterYear.Value);
            }

            if (!string.IsNullOrWhiteSpace(FilterArtist))
            {
                string artistTerm = FilterArtist.Trim().ToLower();
                query = query.Where(t => t.Artist.ToLower().Contains(artistTerm));
            }

            if (FilterPriority.HasValue)
            {
                query = query.Where(t => t.Priority >= FilterPriority.Value);
            }

            if (FilterMinBpm.HasValue)
            {
                query = query.Where(t => t.Bpm >= FilterMinBpm.Value);
            }

            if (FilterMaxBpm.HasValue)
            {
                query = query.Where(t => t.Bpm <= FilterMaxBpm.Value);
            }

            if (ShowOnlyMissing)
            {
                query = query.Where(t => !t.FileAvailable);
            }

            // Ordenamiento
            query = SortBy switch
            {
                "Title" => SortAscending ? query.OrderBy(t => t.Title) : query.OrderByDescending(t => t.Title),
                "Artist" => SortAscending ? query.OrderBy(t => t.Artist) : query.OrderByDescending(t => t.Artist),
                "Genre" => SortAscending ? query.OrderBy(t => t.Genre) : query.OrderByDescending(t => t.Genre),
                "Year" => SortAscending ? query.OrderBy(t => t.Year) : query.OrderByDescending(t => t.Year),
                "Priority" => SortAscending ? query.OrderBy(t => t.Priority) : query.OrderByDescending(t => t.Priority),
                "Duration" => SortAscending ? query.OrderBy(t => t.Duration) : query.OrderByDescending(t => t.Duration),
                "PlayCount" => SortAscending ? query.OrderBy(t => t.PlayCount) : query.OrderByDescending(t => t.PlayCount),
                "LastPlayed" => SortAscending ? 
                    query.OrderBy(t => t.LastPlayed ?? DateTime.MinValue) : 
                    query.OrderByDescending(t => t.LastPlayed ?? DateTime.MinValue),
                _ => query.OrderBy(t => t.Title)
            };

            foreach (var track in query)
            {
                FilteredTracks.Add(track);
            }
        }

        // Métodos de búsqueda avanzada
        public IEnumerable<TrackItem> SearchByTitle(string title)
        {
            return AllTracks.Where(t => t.Title.ToLower().Contains(title.ToLower()));
        }

        public IEnumerable<TrackItem> SearchByArtist(string artist)
        {
            return AllTracks.Where(t => t.Artist.ToLower().Contains(artist.ToLower()));
        }

        public IEnumerable<TrackItem> SearchByGenre(string genre)
        {
            return AllTracks.Where(t => t.Genre.Equals(genre, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<TrackItem> SearchByYear(int year)
        {
            return AllTracks.Where(t => t.Year == year);
        }

        public IEnumerable<TrackItem> SearchByYearRange(int startYear, int endYear)
        {
            return AllTracks.Where(t => t.Year >= startYear && t.Year <= endYear);
        }

        public IEnumerable<TrackItem> SearchByPriority(int minPriority)
        {
            return AllTracks.Where(t => t.Priority >= minPriority);
        }

        public IEnumerable<TrackItem> SearchByType(TrackType type)
        {
            return AllTracks.Where(t => t.Type == type);
        }

        // Métodos de gestión de tracks
        public void AddTrack(TrackItem track)
        {
            AllTracks.Add(track);
            ApplyFilters();
        }

        public void RemoveTrack(TrackItem track)
        {
            AllTracks.Remove(track);
            FilteredTracks.Remove(track);
        }

        public void UpdateTrack(TrackItem track)
        {
            ApplyFilters();
        }

        // Métodos de estadísticas
        public int GetTotalTracks() => AllTracks.Count;
        public int GetTracksByType(TrackType type) => AllTracks.Count(t => t.Type == type);
        public int GetTracksByGenre(string genre) => AllTracks.Count(t => t.Genre.Equals(genre, StringComparison.OrdinalIgnoreCase));
        public double GetAverageDuration() => AllTracks.Any() ? AllTracks.Average(t => t.Duration.TotalSeconds) : 0;
        public TimeSpan GetTotalDuration() => TimeSpan.FromSeconds(AllTracks.Sum(t => t.Duration.TotalSeconds));

        // Métodos de gestión de reproducción
        public void IncrementPlayCount(TrackItem track)
        {
            track.PlayCount++;
            track.LastPlayed = DateTime.Now;
        }

        public void ResetPlayCount(TrackItem track)
        {
            track.PlayCount = 0;
            track.LastPlayed = null;
        }

        // Métodos de utilidad
        public List<string> GetAllArtists()
        {
            return AllTracks.Select(t => t.Artist).Distinct().OrderBy(a => a).ToList();
        }

        public List<string> GetAllGenres()
        {
            return AllTracks.Select(t => t.Genre).Distinct().OrderBy(g => g).ToList();
        }

        public List<int> GetAllYears()
        {
            return AllTracks.Select(t => t.Year).Distinct().OrderBy(y => y).ToList();
        }

        public void ClearFilters()
        {
            ShowOnlyMissing = false;
            SelectedCategory = "Todas";
            SelectedGenre = "Todos";
            SearchText = string.Empty;
            FilterYear = null;
            FilterArtist = string.Empty;
            FilterPriority = null;
            FilterMinBpm = null;
            FilterMaxBpm = null;
            SortBy = "Title";
            SortAscending = true;
        }

        // ---------------------------------------------------------------- archivo

        /// <summary>
        /// Vuelve a preguntar qué tracks se pueden usar de verdad (UI-4): que el archivo
        /// siga en disco y que NAudio haya conseguido leer su duración. Devuelve los
        /// problemáticos y deja <see cref="TrackItem.FileAvailable"/> listo para el árbol
        /// de ESTADO y el glifo ⚠ de la tabla. Se llama al escanear la librería, al conectar
        /// un disco externo y desde el boton COMPROBAR ARCHIVOS de Biblioteca.
        /// </summary>
        public List<TrackItem> RecheckFiles()
        {
            var problematic = new List<TrackItem>();

            foreach (var track in AllTracks)
            {
                var onDisk = !string.IsNullOrWhiteSpace(track.FilePath) && File.Exists(track.FilePath);

                // Una duracion de cero significa "el archivo esta, pero NAudio no lo abrio":
                // truncado, formato que no es audio o extension enganosa. En cabina se oye
                // igual de mal que uno que falta, asi que comparte el aviso.
                track.FileAvailable = onDisk && track.Duration > TimeSpan.Zero;
                if (!track.FileAvailable) problematic.Add(track);
            }

            return problematic;
        }

        public int CountMissing() => AllTracks.Count(t => !t.FileAvailable);
    }
}
