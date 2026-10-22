using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace quasar_radio
{
    public class SearchCriteria
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Genre { get; set; }
        public int? Year { get; set; }
        public int? YearFrom { get; set; }
        public int? YearTo { get; set; }
        public int? MinPriority { get; set; }
        public int? MaxPriority { get; set; }
        public TrackType? Type { get; set; }
        public double? MinBpm { get; set; }
        public double? MaxBpm { get; set; }
        public TimeSpan? MinDuration { get; set; }
        public TimeSpan? MaxDuration { get; set; }
        public int? MinPlayCount { get; set; }
        public int? MaxPlayCount { get; set; }
        public DateTime? PlayedAfter { get; set; }
        public DateTime? PlayedBefore { get; set; }
        public bool? HasCuePoints { get; set; }
        public bool? HasFadeSettings { get; set; }
        public string? Comment { get; set; }
        public string? Composer { get; set; }
    }

    public enum SearchMode
    {
        Exact,
        Contains,
        StartsWith,
        EndsWith,
        Fuzzy
    }

    public class AdvancedSearchService
    {
        private readonly AudioDatabaseService _database;

        public AdvancedSearchService(AudioDatabaseService database)
        {
            _database = database;
        }

        // Búsqueda básica con modo
        public IEnumerable<TrackItem> Search(string searchTerm, SearchMode mode = SearchMode.Contains)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return Enumerable.Empty<TrackItem>();

            var term = searchTerm.Trim().ToLower();

            return _database.AllTracks.Where(track =>
            {
                var title = track.Title.ToLower();
                var artist = track.Artist.ToLower();
                var album = track.Album.ToLower();

                return mode switch
                {
                    SearchMode.Exact => title == term || artist == term || album == term,
                    SearchMode.Contains => title.Contains(term) || artist.Contains(term) || album.Contains(term),
                    SearchMode.StartsWith => title.StartsWith(term) || artist.StartsWith(term) || album.StartsWith(term),
                    SearchMode.EndsWith => title.EndsWith(term) || artist.EndsWith(term) || album.EndsWith(term),
                    SearchMode.Fuzzy => FuzzyMatch(title, term) || FuzzyMatch(artist, term) || FuzzyMatch(album, term),
                    _ => title.Contains(term) || artist.Contains(term) || album.Contains(term)
                };
            });
        }

        // Búsqueda avanzada con múltiples criterios
        public IEnumerable<TrackItem> AdvancedSearch(SearchCriteria criteria)
        {
            var query = _database.AllTracks.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(criteria.Title))
            {
                query = query.Where(t => t.Title.ToLower().Contains(criteria.Title.ToLower()));
            }

            if (!string.IsNullOrWhiteSpace(criteria.Artist))
            {
                query = query.Where(t => t.Artist.ToLower().Contains(criteria.Artist.ToLower()));
            }

            if (!string.IsNullOrWhiteSpace(criteria.Album))
            {
                query = query.Where(t => t.Album.ToLower().Contains(criteria.Album.ToLower()));
            }

            if (!string.IsNullOrWhiteSpace(criteria.Genre))
            {
                query = query.Where(t => t.Genre.Equals(criteria.Genre, StringComparison.OrdinalIgnoreCase));
            }

            if (criteria.Year.HasValue)
            {
                query = query.Where(t => t.Year == criteria.Year.Value);
            }

            if (criteria.YearFrom.HasValue)
            {
                query = query.Where(t => t.Year >= criteria.YearFrom.Value);
            }

            if (criteria.YearTo.HasValue)
            {
                query = query.Where(t => t.Year <= criteria.YearTo.Value);
            }

            if (criteria.MinPriority.HasValue)
            {
                query = query.Where(t => t.Priority >= criteria.MinPriority.Value);
            }

            if (criteria.MaxPriority.HasValue)
            {
                query = query.Where(t => t.Priority <= criteria.MaxPriority.Value);
            }

            if (criteria.Type.HasValue)
            {
                query = query.Where(t => t.Type == criteria.Type.Value);
            }

            if (criteria.MinBpm.HasValue)
            {
                query = query.Where(t => t.Bpm >= criteria.MinBpm.Value);
            }

            if (criteria.MaxBpm.HasValue)
            {
                query = query.Where(t => t.Bpm <= criteria.MaxBpm.Value);
            }

            if (criteria.MinDuration.HasValue)
            {
                query = query.Where(t => t.Duration >= criteria.MinDuration.Value);
            }

            if (criteria.MaxDuration.HasValue)
            {
                query = query.Where(t => t.Duration <= criteria.MaxDuration.Value);
            }

            if (criteria.MinPlayCount.HasValue)
            {
                query = query.Where(t => t.PlayCount >= criteria.MinPlayCount.Value);
            }

            if (criteria.MaxPlayCount.HasValue)
            {
                query = query.Where(t => t.PlayCount <= criteria.MaxPlayCount.Value);
            }

            if (criteria.PlayedAfter.HasValue)
            {
                query = query.Where(t => t.LastPlayed.HasValue && t.LastPlayed >= criteria.PlayedAfter.Value);
            }

            if (criteria.PlayedBefore.HasValue)
            {
                query = query.Where(t => t.LastPlayed.HasValue && t.LastPlayed <= criteria.PlayedBefore.Value);
            }

            if (criteria.HasCuePoints.HasValue)
            {
                if (criteria.HasCuePoints.Value)
                {
                    query = query.Where(t => t.CuePoints.Count > 0);
                }
                else
                {
                    query = query.Where(t => t.CuePoints.Count == 0);
                }
            }

            if (criteria.HasFadeSettings.HasValue)
            {
                if (criteria.HasFadeSettings.Value)
                {
                    query = query.Where(t => t.FadeIn > 0 || t.FadeOut > 0);
                }
                else
                {
                    query = query.Where(t => t.FadeIn == 0 && t.FadeOut == 0);
                }
            }

            if (!string.IsNullOrWhiteSpace(criteria.Comment))
            {
                query = query.Where(t => t.Comment.ToLower().Contains(criteria.Comment.ToLower()));
            }

            if (!string.IsNullOrWhiteSpace(criteria.Composer))
            {
                query = query.Where(t => t.Composer.ToLower().Contains(criteria.Composer.ToLower()));
            }

            return query;
        }

        // Búsqueda por patrón regex
        public IEnumerable<TrackItem> SearchByRegex(string pattern, string field = "title")
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);

                return field.ToLower() switch
                {
                    "title" => _database.AllTracks.Where(t => regex.IsMatch(t.Title)),
                    "artist" => _database.AllTracks.Where(t => regex.IsMatch(t.Artist)),
                    "album" => _database.AllTracks.Where(t => regex.IsMatch(t.Album)),
                    "genre" => _database.AllTracks.Where(t => regex.IsMatch(t.Genre)),
                    "comment" => _database.AllTracks.Where(t => regex.IsMatch(t.Comment)),
                    _ => _database.AllTracks.Where(t => regex.IsMatch(t.Title) || regex.IsMatch(t.Artist))
                };
            }
            catch (ArgumentException)
            {
                return Enumerable.Empty<TrackItem>();
            }
        }

        // Búsqueda por duración
        public IEnumerable<TrackItem> SearchByDuration(TimeSpan minDuration, TimeSpan maxDuration)
        {
            return _database.AllTracks.Where(t => t.Duration >= minDuration && t.Duration <= maxDuration);
        }

        // Búsqueda por BPM
        public IEnumerable<TrackItem> SearchByBpm(double minBpm, double maxBpm)
        {
            return _database.AllTracks.Where(t => t.Bpm >= minBpm && t.Bpm <= maxBpm);
        }

        // Búsqueda de tracks no reproducidos recientemente
        public IEnumerable<TrackItem> SearchNotPlayedSince(DateTime date)
        {
            return _database.AllTracks.Where(t => !t.LastPlayed.HasValue || t.LastPlayed < date);
        }

        // Búsqueda de tracks más reproducidos
        public IEnumerable<TrackItem> SearchMostPlayed(int topN = 10)
        {
            return _database.AllTracks.OrderByDescending(t => t.PlayCount).Take(topN);
        }

        // Búsqueda de tracks menos reproducidos
        public IEnumerable<TrackItem> SearchLeastPlayed(int topN = 10)
        {
            return _database.AllTracks.OrderBy(t => t.PlayCount).Take(topN);
        }

        // Búsqueda de tracks duplicados (por título y artista)
        public IEnumerable<IGrouping<string, TrackItem>> FindDuplicates()
        {
            return _database.AllTracks
                .GroupBy(t => $"{t.Title.ToLower()}_{t.Artist.ToLower()}")
                .Where(g => g.Count() > 1);
        }

        // Búsqueda de tracks con metadatos incompletos
        public IEnumerable<TrackItem> FindIncompleteMetadata()
        {
            return _database.AllTracks.Where(t =>
                string.IsNullOrWhiteSpace(t.Title) ||
                t.Artist == "Artista Desconocido" ||
                string.IsNullOrWhiteSpace(t.Genre) ||
                t.Genre == "General" ||
                t.Year == DateTime.Now.Year ||
                t.Priority == 5);
        }

        // Búsqueda de tracks sin puntos CUE
        public IEnumerable<TrackItem> FindTracksWithoutCuePoints()
        {
            return _database.AllTracks.Where(t =>
                t.CueIn == TimeSpan.Zero &&
                t.CueOut == TimeSpan.Zero &&
                t.CuePoints.Count == 0);
        }

        // Búsqueda de tracks con configuración de fade
        public IEnumerable<TrackItem> FindTracksWithFadeSettings()
        {
            return _database.AllTracks.Where(t => t.FadeIn > 0 || t.FadeOut > 0);
        }

        // Búsqueda por tags en comentarios
        public IEnumerable<TrackItem> SearchByCommentTag(string tag)
        {
            var searchTerm = $"#{tag.ToLower()}";
            return _database.AllTracks.Where(t => t.Comment.ToLower().Contains(searchTerm));
        }

        // Búsqueda inteligente (interpreta la intención)
        public IEnumerable<TrackItem> SmartSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Enumerable.Empty<TrackItem>();

            var lowerQuery = query.Trim().ToLower();

            // Detectar búsqueda por año
            if (int.TryParse(lowerQuery, out int year) && year >= 1900 && year <= DateTime.Now.Year + 1)
            {
                return _database.AllTracks.Where(t => t.Year == year);
            }

            // Detectar búsqueda por rango de años (ej: "2010-2020")
            var yearRangeMatch = Regex.Match(lowerQuery, @"(\d{4})\s*-\s*(\d{4})");
            if (yearRangeMatch.Success)
            {
                int startYear = int.Parse(yearRangeMatch.Groups[1].Value);
                int endYear = int.Parse(yearRangeMatch.Groups[2].Value);
                return _database.AllTracks.Where(t => t.Year >= startYear && t.Year <= endYear);
            }

            // Detectar búsqueda por BPM (ej: "120 bpm")
            var bpmMatch = Regex.Match(lowerQuery, @"(\d+)\s*bpm");
            if (bpmMatch.Success)
            {
                int bpm = int.Parse(bpmMatch.Groups[1].Value);
                return _database.AllTracks.Where(t => Math.Abs(t.Bpm - bpm) < 5); // ±5 BPM tolerance
            }

            // Detectar búsqueda por duración (ej: ">3:00", "<2:30")
            var durationMatch = Regex.Match(lowerQuery, @"([<>])\s*(\d+):(\d+)");
            if (durationMatch.Success)
            {
                int minutes = int.Parse(durationMatch.Groups[2].Value);
                int seconds = int.Parse(durationMatch.Groups[3].Value);
                TimeSpan duration = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);

                if (durationMatch.Groups[1].Value == ">")
                {
                    return _database.AllTracks.Where(t => t.Duration > duration);
                }
                else
                {
                    return _database.AllTracks.Where(t => t.Duration < duration);
                }
            }

            // Búsqueda por tipo
            if (lowerQuery.Contains("jingle") || lowerQuery.Contains("identificación"))
            {
                return _database.AllTracks.Where(t => t.Type == TrackType.Jingle);
            }
            if (lowerQuery.Contains("comercial") || lowerQuery.Contains("publicidad"))
            {
                return _database.AllTracks.Where(t => t.Type == TrackType.Commercial);
            }
            if (lowerQuery.Contains("voz") || lowerQuery.Contains("locución"))
            {
                return _database.AllTracks.Where(t => t.Type == TrackType.Voice);
            }

            // Búsqueda por género
            var genreMatch = _database.GetAllGenres().FirstOrDefault(g => 
                lowerQuery.Contains(g.ToLower()));
            if (genreMatch != null)
            {
                return _database.AllTracks.Where(t => t.Genre.Equals(genreMatch, StringComparison.OrdinalIgnoreCase));
            }

            // Búsqueda general por título/artista
            return Search(lowerQuery, SearchMode.Contains);
        }

        // Algoritmo de coincidencia fuzzy (distancia de Levenshtein simplificada)
        private bool FuzzyMatch(string source, string pattern)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(pattern))
                return false;

            if (source.Contains(pattern))
                return true;

            // Implementación simplificada de coincidencia aproximada
            int maxDistance = Math.Max(2, pattern.Length / 3);
            int distance = CalculateLevenshteinDistance(source, pattern);
            
            return distance <= maxDistance;
        }

        private int CalculateLevenshteinDistance(string source, string target)
        {
            int[,] matrix = new int[source.Length + 1, target.Length + 1];

            for (int i = 0; i <= source.Length; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= target.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    int cost = (source[i - 1] == target[j - 1]) ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[source.Length, target.Length];
        }

        // Sugerencias de búsqueda autocompletado
        public List<string> GetSearchSuggestions(string partialTerm, int maxSuggestions = 10)
        {
            if (string.IsNullOrWhiteSpace(partialTerm))
                return new List<string>();

            var term = partialTerm.ToLower();
            var suggestions = new HashSet<string>();

            // Sugerencias de títulos
            foreach (var title in _database.AllTracks
                .Where(t => t.Title.ToLower().StartsWith(term))
                .Select(t => t.Title)
                .Take(maxSuggestions / 2))
            {
                suggestions.Add(title);
            }

            // Sugerencias de artistas
            foreach (var artist in _database.AllTracks
                .Where(t => t.Artist.ToLower().StartsWith(term))
                .Select(t => t.Artist)
                .Take(maxSuggestions / 2))
            {
                suggestions.Add(artist);
            }

            return suggestions.Take(maxSuggestions).OrderBy(s => s).ToList();
        }

        // Ordenamiento personalizado
        public IEnumerable<TrackItem> Sort(IEnumerable<TrackItem> tracks, string sortBy, bool ascending = true)
        {
            return sortBy.ToLower() switch
            {
                "title" => ascending ? tracks.OrderBy(t => t.Title) : tracks.OrderByDescending(t => t.Title),
                "artist" => ascending ? tracks.OrderBy(t => t.Artist) : tracks.OrderByDescending(t => t.Artist),
                "album" => ascending ? tracks.OrderBy(t => t.Album) : tracks.OrderByDescending(t => t.Album),
                "genre" => ascending ? tracks.OrderBy(t => t.Genre) : tracks.OrderByDescending(t => t.Genre),
                "year" => ascending ? tracks.OrderBy(t => t.Year) : tracks.OrderByDescending(t => t.Year),
                "priority" => ascending ? tracks.OrderBy(t => t.Priority) : tracks.OrderByDescending(t => t.Priority),
                "bpm" => ascending ? tracks.OrderBy(t => t.Bpm) : tracks.OrderByDescending(t => t.Bpm),
                "duration" => ascending ? tracks.OrderBy(t => t.Duration) : tracks.OrderByDescending(t => t.Duration),
                "playcount" => ascending ? tracks.OrderBy(t => t.PlayCount) : tracks.OrderByDescending(t => t.PlayCount),
                "lastplayed" => ascending ? 
                    tracks.OrderBy(t => t.LastPlayed ?? DateTime.MinValue) : 
                    tracks.OrderByDescending(t => t.LastPlayed ?? DateTime.MinValue),
                _ => tracks.OrderBy(t => t.Title)
            };
        }

        // Búsqueda combinada con ordenamiento
        public IEnumerable<TrackItem> SearchAndSort(SearchCriteria criteria, string sortBy, bool ascending = true)
        {
            var results = AdvancedSearch(criteria);
            return Sort(results, sortBy, ascending);
        }
    }
}