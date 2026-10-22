using System;
using System.Collections.Generic;
using System.Linq;

namespace quasar_radio
{
    public class SmartRotationEngine
    {
        private readonly Dictionary<string, DateTime> _lastPlayedTimes = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, int> _playCount = new Dictionary<string, int>();
        private readonly Queue<string> _recentHistory = new Queue<string>();
        
        private int _historySize = 50; // Mantener historial de últimos 50 tracks
        private TimeSpan _minIntervalBetweenRepeats = TimeSpan.FromHours(2); // Mínimo 2 horas entre repeticiones
        private int _maxRotationCycles = 3; // Rotar 3 veces antes de permitir repetición
        
        public int HistorySize
        {
            get => _historySize;
            set => _historySize = Math.Max(10, value);
        }
        
        public TimeSpan MinIntervalBetweenRepeats
        {
            get => _minIntervalBetweenRepeats;
            set => _minIntervalBetweenRepeats = TimeSpan.FromMinutes(Math.Max(30, value.TotalMinutes));
        }
        
        public int MaxRotationCycles
        {
            get => _maxRotationCycles;
            set => _maxRotationCycles = Math.Max(1, value);
        }
        
        public void TrackPlayed(TrackItem track)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath)) return;
            
            string trackKey = GetTrackKey(track);
            
            // Actualizar tiempo de última reproducción
            _lastPlayedTimes[trackKey] = DateTime.Now;
            
            // Incrementar contador de reproducciones
            if (!_playCount.ContainsKey(trackKey))
                _playCount[trackKey] = 0;
            _playCount[trackKey]++;
            
            // Agregar al historial reciente
            _recentHistory.Enqueue(trackKey);
            if (_recentHistory.Count > _historySize)
                _recentHistory.Dequeue();
        }
        
        public List<TrackItem> GetSmartRotation(List<TrackItem> availableTracks, int count)
        {
            if (availableTracks == null || availableTracks.Count == 0)
                return new List<TrackItem>();
            
            var result = new List<TrackItem>();
            var available = new List<TrackItem>(availableTracks);
            var random = new Random();
            
            for (int i = 0; i < count && available.Count > 0; i++)
            {
                var selectedTrack = SelectNextTrack(available, random);
                if (selectedTrack != null)
                {
                    result.Add(selectedTrack);
                    available.Remove(selectedTrack);
                }
            }
            
            return result;
        }
        
        private TrackItem? SelectNextTrack(List<TrackItem> available, Random random)
        {
            // Agrupar tracks por prioridad basada en frecuencia de reproducción
            var priorityGroups = new Dictionary<int, List<TrackItem>>();
            
            foreach (var track in available)
            {
                string trackKey = GetTrackKey(track);
                int priority = CalculateTrackPriority(trackKey);
                
                if (!priorityGroups.ContainsKey(priority))
                    priorityGroups[priority] = new List<TrackItem>();
                priorityGroups[priority].Add(track);
            }
            
            // Priorizar tracks con menor prioridad (menos reproducidos)
            var minPriority = priorityGroups.Keys.Min();
            var candidateTracks = priorityGroups[minPriority];
            
            // Selección aleatoria entre los tracks de mayor prioridad
            return candidateTracks[random.Next(candidateTracks.Count)];
        }
        
        private int CalculateTrackPriority(string trackKey)
        {
            int priority = 0;
            
            // Factor 1: Historial reciente (mayor penalización si está en historial reciente)
            if (_recentHistory.Contains(trackKey))
            {
                priority += 100; // Penalización alta
            }
            
            // Factor 2: Tiempo desde última reproducción
            if (_lastPlayedTimes.ContainsKey(trackKey))
            {
                var timeSinceLastPlay = DateTime.Now - _lastPlayedTimes[trackKey];
                if (timeSinceLastPlay < _minIntervalBetweenRepeats)
                {
                    priority += 50; // Penalización media si se reprodució recientemente
                }
            }
            
            // Factor 3: Contador de reproducciones (menos penalización para tracks menos reproducidos)
            int playCount = _playCount.ContainsKey(trackKey) ? _playCount[trackKey] : 0;
            priority += playCount * 10;
            
            return priority;
        }
        
        private string GetTrackKey(TrackItem track)
        {
            // Usar FilePath como identificador único
            return track.FilePath.ToLowerInvariant();
        }
        
        public void ClearHistory()
        {
            _lastPlayedTimes.Clear();
            _playCount.Clear();
            _recentHistory.Clear();
        }
        
        public void RemoveTrackFromHistory(TrackItem track)
        {
            if (track == null) return;
            
            string trackKey = GetTrackKey(track);
            _lastPlayedTimes.Remove(trackKey);
            _playCount.Remove(trackKey);
            
            // Reconstruir queue sin el track
            var newQueue = new Queue<string>();
            foreach (var item in _recentHistory)
            {
                if (item != trackKey)
                    newQueue.Enqueue(item);
            }
            _recentHistory.Clear();
            foreach (var item in newQueue)
                _recentHistory.Enqueue(item);
        }
        
        public RotationStatistics GetStatistics()
        {
            return new RotationStatistics
            {
                TotalTracksTracked = _playCount.Count,
                TotalPlays = _playCount.Values.Sum(),
                MostPlayedTrack = _playCount.OrderByDescending(x => x.Value).FirstOrDefault(),
                AveragePlaysPerTrack = _playCount.Count > 0 ? (double)_playCount.Values.Sum() / _playCount.Count : 0,
                HistorySize = _recentHistory.Count
            };
        }
    }
    
    public class RotationStatistics
    {
        public int TotalTracksTracked { get; set; }
        public int TotalPlays { get; set; }
        public KeyValuePair<string, int> MostPlayedTrack { get; set; }
        public double AveragePlaysPerTrack { get; set; }
        public int HistorySize { get; set; }
    }
}