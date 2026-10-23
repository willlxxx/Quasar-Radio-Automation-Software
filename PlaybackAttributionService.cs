using System;
using Serilog;

namespace quasar_radio
{
    /// <summary>
    /// UI-4 paso B: el unico sitio donde una pista que sale al aire deja de ser un
    /// recuerdo del reproductor y pasa a ser dato de la emisora.
    ///
    /// Hasta ahora los tres destinos que existen para ese dato no los llamaba nadie:
    /// <see cref="SmartRotationEngine.TrackPlayed"/> (el motor de rotacion), 
    /// <see cref="AudioDatabaseService.IncrementPlayCount"/> (las veces que se emitio, que
    /// se guardan en track_metadata.json) y <see cref="StatisticsService.RecordPlayback"/>
    /// (las cifras de Reportes y el reporte de emision). Por eso SMART PLAYLIST elegia al
    /// azar, la columna "Emitido" decia "Nunca" aunque sonara la pista, y las cuatro cifras
    /// de ESTADÍSTICAS se quedaban en 0 toda la vida.
    ///
    /// Va aparte del motor de audio y del ViewModel a proposito: el momento de empezar y el
    /// momento de terminar son dos eventos distintos de <see cref="AudioEngine"/>, y la
    /// atribucion no debe poder romper el aire. Ningun metodo de esta clase deja subir una
    /// excepcion: si un guardado falla se anota en el log y la musica sigue.
    /// </summary>
    public class PlaybackAttributionService
    {
        private readonly ILogger _logger = Log.ForContext<PlaybackAttributionService>();

        private readonly SmartRotationEngine _rotationEngine;
        private readonly AudioDatabaseService _database;
        private readonly StatisticsService _statistics;
        private readonly TrackMetadataService _metadata;

        private TrackItem? _airingTrack;
        private DateTime _airingStartedAt;

        public PlaybackAttributionService(
            SmartRotationEngine rotationEngine,
            AudioDatabaseService database,
            StatisticsService statistics,
            TrackMetadataService metadata)
        {
            _rotationEngine = rotationEngine ?? throw new ArgumentNullException(nameof(rotationEngine));
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        /// <summary>La pista que esta en el aire ahora mismo, o null si no hay ninguna.</summary>
        public TrackItem? AiringTrack => _airingTrack;

        /// <summary>
        /// Se llama en el instante en que una pista empieza a sonar (cambio de pista del
        /// motor, incluido el crossfade automatico). Apunta rotacion, contador y "ahora
        /// suena"; el tiempo de emision se cierra en <see cref="TrackEnded"/>.
        ///
        /// Del guardado solo escribe <c>playCount</c> y <c>lastPlayed</c>
        /// (<see cref="TrackMetadataService.RegisterPlay"/>). No manda el <see cref="TrackItem"/>
        /// completo porque el de la cola viene de leer el archivo, no de la Biblioteca: su
        /// genero es el de la etiqueta ID3, y volcarlo tapaba lo que el operador habia
        /// editado a mano.
        /// </summary>
        public void TrackStarted(TrackItem track, bool wasAutoPlay)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath)) return;

            _airingTrack = track;
            _airingStartedAt = DateTime.Now;

            try
            {
                _rotationEngine.TrackPlayed(track);
                _database.IncrementPlayCount(track);
                _metadata.RegisterPlay(track);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "No se pudo apuntar la reproduccion de {Title}", track.Title);
            }

            try
            {
                // nowplaying.txt / nowplaying.json: RDS, OBS y el reproductor web de la emisora.
                NowPlayingService.UpdateNowPlaying(track);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "No se pudo publicar la pista actual");
            }

            _logger.Information(
                "Atribuida reproduccion de {Title}: {Count} veces emitida ({AutoPlay})",
                track.Title, track.PlayCount, wasAutoPlay ? "auto" : "manual");
        }

        /// <summary>
        /// Cierra la pista que estaba en el aire y anota cuanto tiempo sonó de verdad en las
        /// estadísticas. Se llama al cambiar de pista, al parar y al terminar la lista, asi
        /// que un corte a los 20 segundos cuenta 20 segundos, no la duracion de la canción.
        /// </summary>
        public void TrackEnded(bool wasAutoPlay)
        {
            var track = _airingTrack;
            if (track == null) return;

            _airingTrack = null;

            var played = DateTime.Now - _airingStartedAt;
            if (track.Duration > TimeSpan.Zero && played > track.Duration) played = track.Duration;
            if (played < TimeSpan.Zero) played = TimeSpan.Zero;

            try
            {
                _statistics.RecordPlayback(track, played, wasAutoPlay);
                _logger.Information("Cerrada emision de {Title}: {Played:hh\\:mm\\:ss} al aire", track.Title, played);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "No se pudieron actualizar las estadísticas de {Title}", track.Title);
            }
        }

        /// <summary>
        /// "Ahora suena" sin pista: se usa al parar o al quedarse la lista vacia, para que
        /// nowplaying.txt no siga anunciando una canción que ya no esta en el aire.
        /// </summary>
        public void ClearNowPlaying()
        {
            try
            {
                NowPlayingService.UpdateNowPlaying(null);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "No se pudo limpiar la pista actual publicada");
            }
        }
    }
}
