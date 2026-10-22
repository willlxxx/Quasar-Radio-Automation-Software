using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using Serilog;

namespace quasar_radio
{
    public class PlaybackRecord
    {
        public DateTime Timestamp { get; set; }
        public string TrackTitle { get; set; } = string.Empty;
        public string TrackArtist { get; set; } = string.Empty;
        public string TrackFilePath { get; set; } = string.Empty;
        public TrackType TrackType { get; set; }
        public string Genre { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public TimeSpan PlayTime { get; set; }
        public bool WasAutoPlay { get; set; }
    }

    public class TrackStatistics
    {
        public string TrackTitle { get; set; } = string.Empty;
        public string TrackArtist { get; set; } = string.Empty;
        public string TrackFilePath { get; set; } = string.Empty;
        public int PlayCount { get; set; }
        public TimeSpan TotalPlayTime { get; set; }
        public DateTime FirstPlayed { get; set; }
        public DateTime LastPlayed { get; set; }
        public TrackType TrackType { get; set; }
        public string Genre { get; set; } = string.Empty;
    }

    public class HourlyStatistics
    {
        public int Hour { get; set; }
        public int PlayCount { get; set; }
        public TimeSpan TotalPlayTime { get; set; }
    }

    public class GenreStatistics
    {
        public string Genre { get; set; } = string.Empty;
        public int PlayCount { get; set; }
        public TimeSpan TotalPlayTime { get; set; }
    }

    public class DailyStatistics
    {
        public DateTime Date { get; set; }
        public int TotalTracksPlayed { get; set; }
        public TimeSpan TotalPlayTime { get; set; }
        public TimeSpan TotalOnAirTime { get; set; }
    }

    public class StatisticsService
    {
        private readonly ConcurrentQueue<PlaybackRecord> _playbackHistory = new ConcurrentQueue<PlaybackRecord>();
        private readonly Dictionary<string, TrackStatistics> _trackStats = new Dictionary<string, TrackStatistics>();
        private readonly Dictionary<int, HourlyStatistics> _hourlyStats = new Dictionary<int, HourlyStatistics>();
        private readonly Dictionary<string, GenreStatistics> _genreStats = new Dictionary<string, GenreStatistics>();
        private readonly Dictionary<DateTime, DailyStatistics> _dailyStats = new Dictionary<DateTime, DailyStatistics>();
        private readonly object _syncLock = new object();

        /// <summary>
        /// Límite máximo de registros históricos en memoria (ventana deslizante) para evitar crecimiento infinito.
        /// </summary>
        public int MaxHistoryRecords { get; set; } = 5000;

        private DateTime _sessionStartTime = DateTime.Now;
        private TimeSpan _totalSessionPlayTime = TimeSpan.Zero;
        private int _sessionTrackCount = 0;
        public string? LastError { get; private set; }

        private const string StatisticsFile = "statistics.json";
        private const string PlaybackHistoryFile = "playback_history.json";

        public event EventHandler? StatisticsUpdated;

        public DateTime SessionStartTime => _sessionStartTime;
        public TimeSpan TotalSessionPlayTime => _totalSessionPlayTime;
        public int SessionTrackCount => _sessionTrackCount;
        public int TotalPlaybackRecords => _playbackHistory.Count;
        public int TotalUniqueTracks
        {
            get
            {
                lock (_syncLock) return _trackStats.Count;
            }
        }

        public void RecordPlayback(TrackItem track, TimeSpan playTime, bool wasAutoPlay = true)
        {
            if (track == null) return;

            var record = new PlaybackRecord
            {
                Timestamp = DateTime.Now,
                TrackTitle = track.Title,
                TrackArtist = track.Artist,
                TrackFilePath = track.FilePath,
                TrackType = track.Type,
                Genre = track.Genre,
                Duration = track.Duration,
                PlayTime = playTime,
                WasAutoPlay = wasAutoPlay
            };

            // Encolar de forma segura en ConcurrentQueue
            _playbackHistory.Enqueue(record);

            // Ventana deslizante FIFO: descartar excedentes de memoria
            while (_playbackHistory.Count > MaxHistoryRecords && _playbackHistory.TryDequeue(out _))
            {
            }

            lock (_syncLock)
            {
                _totalSessionPlayTime += playTime;
                _sessionTrackCount++;

                // Actualizar estadísticas del track
                UpdateTrackStatistics(record);

                // Actualizar estadísticas por hora
                UpdateHourlyStatistics(record);

                // Actualizar estadísticas por género
                UpdateGenreStatistics(record);

                // Actualizar estadísticas diarias
                UpdateDailyStatistics(record);
            }

            StatisticsUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateTrackStatistics(PlaybackRecord record)
        {
            string key = record.TrackFilePath.ToLowerInvariant();

            if (!_trackStats.ContainsKey(key))
            {
                _trackStats[key] = new TrackStatistics
                {
                    TrackTitle = record.TrackTitle,
                    TrackArtist = record.TrackArtist,
                    TrackFilePath = record.TrackFilePath,
                    TrackType = record.TrackType,
                    Genre = record.Genre,
                    FirstPlayed = record.Timestamp,
                    LastPlayed = record.Timestamp
                };
            }

            var stats = _trackStats[key];
            stats.PlayCount++;
            stats.TotalPlayTime += record.PlayTime;
            stats.LastPlayed = record.Timestamp;
        }

        private void UpdateHourlyStatistics(PlaybackRecord record)
        {
            int hour = record.Timestamp.Hour;

            if (!_hourlyStats.ContainsKey(hour))
            {
                _hourlyStats[hour] = new HourlyStatistics { Hour = hour };
            }

            var stats = _hourlyStats[hour];
            stats.PlayCount++;
            stats.TotalPlayTime += record.PlayTime;
        }

        private void UpdateGenreStatistics(PlaybackRecord record)
        {
            string genre = record.Genre ?? "General";

            if (!_genreStats.ContainsKey(genre))
            {
                _genreStats[genre] = new GenreStatistics { Genre = genre };
            }

            var stats = _genreStats[genre];
            stats.PlayCount++;
            stats.TotalPlayTime += record.PlayTime;
        }

        private void UpdateDailyStatistics(PlaybackRecord record)
        {
            DateTime date = record.Timestamp.Date;

            if (!_dailyStats.ContainsKey(date))
            {
                _dailyStats[date] = new DailyStatistics { Date = date };
            }

            var stats = _dailyStats[date];
            stats.TotalTracksPlayed++;
            stats.TotalPlayTime += record.PlayTime;
            stats.TotalOnAirTime = DateTime.Now - _sessionStartTime;
        }

        public List<TrackStatistics> GetMostPopularTracks(int count = 10)
        {
            lock (_syncLock)
            {
                return _trackStats.Values
                           .OrderByDescending(t => t.PlayCount)
                           .Take(count)
                           .ToList();
            }
        }

        public List<TrackStatistics> GetMostPlayedByTime(int count = 10)
        {
            lock (_syncLock)
            {
                return _trackStats.Values
                           .OrderByDescending(t => t.TotalPlayTime)
                           .Take(count)
                           .ToList();
            }
        }

        public List<HourlyStatistics> GetHourlyStatistics()
        {
            lock (_syncLock)
            {
                return _hourlyStats.Values.OrderBy(h => h.Hour).ToList();
            }
        }

        public List<GenreStatistics> GetGenreStatistics()
        {
            lock (_syncLock)
            {
                return _genreStats.Values.OrderByDescending(g => g.PlayCount).ToList();
            }
        }

        public List<DailyStatistics> GetDailyStatistics(int days = 30)
        {
            DateTime cutoffDate = DateTime.Now.AddDays(-days);
            lock (_syncLock)
            {
                return _dailyStats.Values
                           .Where(d => d.Date >= cutoffDate)
                           .OrderByDescending(d => d.Date)
                           .ToList();
            }
        }

        /// <summary>
        /// Obtiene un subconjunto paginado del historial de reproducciones almacenado en la ventana deslizante.
        /// </summary>
        public (IReadOnlyList<PlaybackRecord> Items, int TotalCount) GetPagedHistory(int pageIndex, int pageSize)
        {
            var snapshot = _playbackHistory.ToArray();
            int total = snapshot.Length;
            int safePage = Math.Max(1, pageIndex);
            int safeSize = Math.Max(1, pageSize);

            var items = snapshot
                .OrderByDescending(p => p.Timestamp)
                .Skip((safePage - 1) * safeSize)
                .Take(safeSize)
                .ToList();

            return (items, total);
        }

        public List<PlaybackRecord> GetRecentPlays(int count = 20)
        {
            return _playbackHistory
                       .ToArray()
                       .OrderByDescending(p => p.Timestamp)
                       .Take(count)
                       .ToList();
        }

        public PlaybackRecord? GetLastPlayback()
        {
            return _playbackHistory.ToArray().LastOrDefault();
        }

        public TimeSpan GetAverageTrackDuration()
        {
            var snapshot = _playbackHistory.ToArray();
            if (snapshot.Length == 0) return TimeSpan.Zero;

            return TimeSpan.FromTicks(
                (long)snapshot.Average(p => p.Duration.Ticks)
            );
        }

        public double GetAveragePlaysPerHour()
        {
            lock (_syncLock)
            {
                if (_totalSessionPlayTime.TotalHours == 0) return 0;
                return _sessionTrackCount / _totalSessionPlayTime.TotalHours;
            }
        }

        public List<TrackStatistics> GetRecentlyPlayedTracks(int count = 10)
        {
            lock (_syncLock)
            {
                return _trackStats.Values
                           .OrderByDescending(t => t.LastPlayed)
                           .Take(count)
                           .ToList();
            }
        }

        public List<TrackStatistics> GetLeastPlayedTracks(int count = 10)
        {
            lock (_syncLock)
            {
                return _trackStats.Values
                           .OrderBy(t => t.PlayCount)
                           .Take(count)
                           .ToList();
            }
        }

        public Dictionary<string, int> GetPlayCountByType()
        {
            return _playbackHistory
                       .ToArray()
                       .GroupBy(p => p.TrackType)
                       .ToDictionary(g => g.Key.ToString(), g => g.Count());
        }

        public TimeSpan GetTotalPlayTimeByType(TrackType type)
        {
            return _playbackHistory
                       .ToArray()
                       .Where(p => p.TrackType == type)
                       .Aggregate(TimeSpan.Zero, (sum, p) => sum + p.PlayTime);
        }

        public void ClearStatistics()
        {
            lock (_syncLock)
            {
                while (_playbackHistory.TryDequeue(out _)) { }
                _trackStats.Clear();
                _hourlyStats.Clear();
                _genreStats.Clear();
                _dailyStats.Clear();
                _sessionStartTime = DateTime.Now;
                _totalSessionPlayTime = TimeSpan.Zero;
                _sessionTrackCount = 0;
            }

            StatisticsUpdated?.Invoke(this, EventArgs.Empty);
        }

        public bool SaveStatistics()
        {
            LastError = null;
            try
            {
                object data;
                lock (_syncLock)
                {
                    data = new
                    {
                        PlaybackHistory = _playbackHistory.ToArray(),
                        TrackStats = _trackStats,
                        HourlyStats = _hourlyStats,
                        GenreStats = _genreStats,
                        DailyStats = _dailyStats,
                        SessionStartTime = _sessionStartTime,
                        TotalSessionPlayTime = _totalSessionPlayTime,
                        SessionTrackCount = _sessionTrackCount
                    };
                }

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(StatisticsFile, json);
                return true;
            }
            catch (Exception ex)
            {
                LastError = UserErrorMessages.For(ex, "guardar las estadísticas");
                Log.Error(ex, "No se pudieron guardar las estadísticas en {StatisticsFile}", StatisticsFile);
                return false;
            }
        }

        public bool LoadStatistics()
        {
            LastError = null;
            try
            {
                if (!File.Exists(StatisticsFile))
                {
                    LastError = "Todavía no hay estadísticas guardadas para cargar.";
                    return false;
                }

                string json = File.ReadAllText(StatisticsFile);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                if (data.TryGetProperty("PlaybackHistory", out var history))
                {
                    lock (_syncLock)
                    {
                        while (_playbackHistory.TryDequeue(out _)) { }
                        foreach (var item in history.EnumerateArray())
                        {
                            if (JsonSerializer.Deserialize<PlaybackRecord>(item) is { } record)
                            {
                                _playbackHistory.Enqueue(record);
                                while (_playbackHistory.Count > MaxHistoryRecords && _playbackHistory.TryDequeue(out _)) { }
                            }
                        }
                    }
                }

                if (data.TryGetProperty("TrackStats", out var trackStats))
                {
                    _trackStats.Clear();
                    foreach (var item in trackStats.EnumerateObject())
                    {
                        if (JsonSerializer.Deserialize<TrackStatistics>(item.Value) is { } stats)
                        {
                            _trackStats[item.Name] = stats;
                        }
                    }
                }

                if (data.TryGetProperty("HourlyStats", out var hourlyStats))
                {
                    _hourlyStats.Clear();
                    foreach (var item in hourlyStats.EnumerateObject())
                    {
                        if (JsonSerializer.Deserialize<HourlyStatistics>(item.Value) is { } stats)
                        {
                            _hourlyStats[int.Parse(item.Name)] = stats;
                        }
                    }
                }

                if (data.TryGetProperty("GenreStats", out var genreStats))
                {
                    _genreStats.Clear();
                    foreach (var item in genreStats.EnumerateObject())
                    {
                        if (JsonSerializer.Deserialize<GenreStatistics>(item.Value) is { } stats)
                        {
                            _genreStats[item.Name] = stats;
                        }
                    }
                }

                if (data.TryGetProperty("DailyStats", out var dailyStats))
                {
                    _dailyStats.Clear();
                    foreach (var item in dailyStats.EnumerateObject())
                    {
                        if (JsonSerializer.Deserialize<DailyStatistics>(item.Value) is { } stats)
                        {
                            _dailyStats[DateTime.Parse(item.Name)] = stats;
                        }
                    }
                }

                if (data.TryGetProperty("SessionStartTime", out var sessionStart))
                {
                    _sessionStartTime = sessionStart.GetDateTime();
                }

                if (data.TryGetProperty("TotalSessionPlayTime", out var totalPlayTime))
                {
                    if (TimeSpan.TryParse(totalPlayTime.GetString(), out var parsedPlayTime))
                    {
                        _totalSessionPlayTime = parsedPlayTime;
                    }
                }

                if (data.TryGetProperty("SessionTrackCount", out var trackCount))
                {
                    _sessionTrackCount = trackCount.GetInt32();
                }

                StatisticsUpdated?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                LastError = UserErrorMessages.For(ex, "cargar las estadísticas");
                Log.Error(ex, "No se pudieron cargar las estadísticas desde {StatisticsFile}", StatisticsFile);
                return false;
            }
        }

        public string GenerateReport()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== REPORTE DE EMISIÓN ===");
            report.AppendLine($"Fecha: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            report.AppendLine($"Sesión iniciada: {_sessionStartTime:dd/MM/yyyy HH:mm:ss}");
            report.AppendLine();

            report.AppendLine("--- ESTADÍSTICAS GENERALES ---");
            report.AppendLine($"Tracks reproducidos: {_sessionTrackCount}");
            report.AppendLine($"Tiempo total de reproducción: {_totalSessionPlayTime:hh\\:mm\\:ss}");
            report.AppendLine($"Promedio por hora: {GetAveragePlaysPerHour():F1} tracks/hora");
            report.AppendLine($"Promedio de duración: {GetAverageTrackDuration():mm\\:ss}");
            report.AppendLine();

            report.AppendLine("--- TRACKS MÁS POPULARES ---");
            var topTracks = GetMostPopularTracks(5);
            foreach (var track in topTracks)
            {
                report.AppendLine($"{track.PlayCount}x - {track.TrackArtist} - {track.TrackTitle} ({track.TotalPlayTime:hh\\:mm\\:ss})");
            }
            report.AppendLine();

            report.AppendLine("--- ESTADÍSTICAS POR GÉNERO ---");
            var genreStats = GetGenreStatistics();
            foreach (var genre in genreStats)
            {
                report.AppendLine($"{genre.Genre}: {genre.PlayCount} tracks ({genre.TotalPlayTime:hh\\:mm\\:ss})");
            }
            report.AppendLine();

            report.AppendLine("--- HORARIOS MÁS ACTIVOS ---");
            var hourlyStats = GetHourlyStatistics().OrderByDescending(h => h.PlayCount).Take(5);
            foreach (var hour in hourlyStats)
            {
                report.AppendLine($"{hour.Hour:00}:00 - {hour.PlayCount} tracks ({hour.TotalPlayTime:hh\\:mm\\:ss})");
            }

            return report.ToString();
        }

        public bool ExportReportToFile(string filePath)
        {
            LastError = null;
            try
            {
                string report = GenerateReport();
                File.WriteAllText(filePath, report);
                return true;
            }
            catch (Exception ex)
            {
                LastError = UserErrorMessages.For(ex, "exportar el reporte de emisión");
                Log.Error(ex, "No se pudo exportar el reporte a {ReportPath}", filePath);
                return false;
            }
        }
    }
}