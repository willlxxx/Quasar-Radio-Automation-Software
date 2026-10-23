using System;
using System.Collections.Generic;
using System.IO;
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

        // UI-4 paso B: si se admiten datos heredados de track_metadata.json. Se apaga con
        // LIMPIAR HISTORIAL y solo vuelve con el siguiente arranque del programa.
        private bool _acceptsSeed = true;

        // Cuantas veces se apunto una salida al aire DESDE QUE ARRANCO EL PROGRAMA. El
        // contador de _playCount incluye lo sembrado desde track_metadata.json, asi que no
        // sirve para decir "hoy"; esta linea si, y es la que se ve en la cabecera de Biblioteca.
        private int _sessionPlays;
        
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

            _sessionPlays++;
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
            // Usar FilePath como identificador único.
            // UI-4 paso B: se resuelve la ruta antes de usarla de clave. El escaneo de las
            // carpetas guarda "musica\tema.wav" y la cola que arma la consola guarda la ruta
            // absoluta del mismo archivo; con la cadena cruda el motor veia dos pistas
            // distintas y la rotacion podia repetir la misma cancion creyendo que eran otras.
            try
            {
                return Path.GetFullPath(track.FilePath).ToLowerInvariant();
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return track.FilePath.ToLowerInvariant();
            }
        }
        
        /// <summary>
        /// UI-4 paso B: sembrar el motor con lo que ya esta publicado en la libreria
        /// (TrackItem.PlayCount y TrackItem.LastPlayed, que viven en track_metadata.json).
        /// Sin esto la rotacion "inteligente" empezaba cada arranque de cero y elegia al
        /// azar: el operador veia SMART PLAYLIST y salia lo mismo que AUTO.
        ///
        /// Solo sembras entradas que el motor NO conoce todavia, para que lo ocurrido en la
        /// sesion en curso tenga prioridad sobre la foto del disco.
        /// </summary>
        /// <param name="tracks">La libreria recien cargada.</param>
        /// <returns>Cuantas pistas quedaron sembradas (0 si el historial se acaban de limpiar).</returns>
        public int SeedFromLibrary(IEnumerable<TrackItem> tracks)
        {
            if (tracks == null || !_acceptsSeed) return 0;

            var seeded = 0;
            foreach (var track in tracks)
            {
                if (track == null || string.IsNullOrEmpty(track.FilePath)) continue;

                string trackKey = GetTrackKey(track);

                if (track.PlayCount > 0 && !_playCount.ContainsKey(trackKey))
                {
                    _playCount[trackKey] = track.PlayCount;
                    seeded++;
                }

                if (track.LastPlayed.HasValue && !_lastPlayedTimes.ContainsKey(trackKey))
                    _lastPlayedTimes[trackKey] = track.LastPlayed.Value;
            }

            return seeded;
        }

        public void ClearHistory()
        {
            _lastPlayedTimes.Clear();
            _playCount.Clear();
            _recentHistory.Clear();
            _sessionPlays = 0;

            // UI-4 paso B: tras limpiar, la siguiente recarga de libreria NO puede volver a
            // leer el historial persistido o el boton no habria hecho nada (se llama al
            // cerrar Biblioteca). Se vuelve a admitir el sembrado con el siguiente arranque.
            _acceptsSeed = false;
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
                SessionPlays = _sessionPlays,
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
        public int SessionPlays { get; set; }
        public KeyValuePair<string, int> MostPlayedTrack { get; set; }
        public double AveragePlaysPerTrack { get; set; }
        public int HistorySize { get; set; }
    }
}