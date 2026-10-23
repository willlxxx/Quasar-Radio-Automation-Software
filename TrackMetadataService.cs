using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace quasar_radio
{
    public class TrackMetadataDto
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = "Artista Desconocido";
        public string Album { get; set; } = string.Empty;
        public string Genre { get; set; } = "General";
        public int Year { get; set; } = DateTime.Now.Year;
        public int Priority { get; set; } = 5;
        public double Bpm { get; set; } = 120.0;
        public TrackType Type { get; set; } = TrackType.Music;
        
        // Puntos CUE
        public double CueIn { get; set; } = 0.0;
        public double Intro { get; set; } = 0.0;
        public double CueOut { get; set; } = 0.0;
        public double EndCue { get; set; } = 0.0;
        public double Outro { get; set; } = 0.0;
        
        // Fade
        public double FadeIn { get; set; } = 0.0;
        public double FadeOut { get; set; } = 0.0;
        
        // Puntos CUE adicionales
        public List<double> CuePoints { get; set; } = new List<double>();
        
        // Metadatos adicionales
        public string Composer { get; set; } = string.Empty;
        public string Copyright { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        
        // Estadísticas de reproducción
        public int PlayCount { get; set; } = 0;
        public DateTime? LastPlayed { get; set; } = null;
    }

    /// <summary>
    /// Resultado de aplicar <c>track_metadata.json</c> sobre la libreria recien escaneada
    /// (UI-4 paso B). Se muestra en la barra de estado de Biblioteca para que el operador
    /// sepa si lo que ve es lo que dej la sesión anterior o un disco recién conectado.
    /// </summary>
    public sealed class MetadataRestoreResult
    {
        public int Applied { get; set; }
        public int RestoredImports { get; set; }
        public int MissingFiles { get; set; }

        /// <summary>
        /// Fichas del JSON que apuntaban a un archivo ya resuelto en esta misma carga (la misma
        /// pista escrita como ruta relativa y como absoluta). Van aparte para que el aviso de
        /// "fichas recuperadas" no cuente doble.
        /// </summary>
        public int Duplicated { get; set; }

        public bool FileFound { get; set; }
    }

    public class TrackMetadataService
    {
        private const string METADATA_FILE = "track_metadata.json";
        private const string BACKUP_FILE = "track_metadata_backup.json";
        
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Escritura atómica (problema A11 del plan de arquitectura): se escribe un
        /// <c>.tmp</c>, se pide al sistema operativo que lo lleve a disco y solo entonces se
        /// cambia por el archivo bueno, guardando el anterior como copia de seguridad.
        /// Desde UI-4 paso B esto deja de ser un detalle: el JSON se reescribe solo, cada vez
        /// que una pista sale al aire, asi que un corte de luz a mitad de escritura se habría
        /// comido los cue points y los contadores de toda la librería.
        /// </summary>
        private static void WriteAtomic(string path, string content)
        {
            var tmp = path + ".tmp";
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, BACKUP_FILE); return; }
                catch (IOException)
                {
                    // FAT/exFAT de un disco externo no soportan REPLACE: se pierde el rename
                    // atomico pero el contenido ya esta escrito en el .tmp, que es lo grave.
                }
            }

            File.Move(tmp, path, true);
        }

        public void SaveMetadata(IEnumerable<TrackItem> tracks)
        {
            try
            {
                var dtos = tracks.Select(track => new TrackMetadataDto
                {
                    FilePath = track.FilePath,
                    Title = track.Title,
                    Artist = track.Artist,
                    Album = track.Album,
                    Genre = track.Genre,
                    Year = track.Year,
                    Priority = track.Priority,
                    Bpm = track.Bpm,
                    Type = track.Type,
                    CueIn = track.CueInSeconds,
                    Intro = track.IntroSeconds,
                    CueOut = track.CueOutSeconds,
                    EndCue = track.EndCueSeconds,
                    Outro = track.OutroSeconds,
                    FadeIn = track.FadeIn,
                    FadeOut = track.FadeOut,
                    CuePoints = track.CuePoints.Select(c => c.TotalSeconds).ToList(),
                    Composer = track.Composer,
                    Copyright = track.Copyright,
                    Comment = track.Comment,
                    PlayCount = track.PlayCount,
                    LastPlayed = track.LastPlayed
                }).ToList();

                string jsonString = JsonSerializer.Serialize(dtos, _jsonOptions);
                WriteAtomic(METADATA_FILE, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error guardando metadatos: {ex.Message}");
                throw;
            }
        }

        public MetadataRestoreResult LoadMetadata(AudioDatabaseService database)
        {
            var result = new MetadataRestoreResult();

            if (!File.Exists(METADATA_FILE))
            {
                Console.WriteLine("No existe archivo de metadatos");
                return result;
            }

            result.FileFound = true;

            try
            {
                string jsonString = File.ReadAllText(METADATA_FILE);
                var dtos = JsonSerializer.Deserialize<List<TrackMetadataDto>>(jsonString, _jsonOptions);

                if (dtos == null || dtos.Count == 0)
                {
                    Console.WriteLine("Archivo de metadatos vacío o inválido");
                    return result;
                }

                // UI-4 paso B: el mismo archivo puede estar escrito de dos formas en el JSON.
                // El escaneo de las carpetas guarda rutas relativas ("musica\tema.wav") y lo que
                // se importa o se carga a la cola desde MainViewModel guarda la ruta absoluta.
                // Comparar las cadenas tal cual hacia que la ficha absoluta no encontrara a la
                // pista escaneada: la biblioteca duplicaba la fila en cada arranque, con dos
                // contadores de emision y la rotacion repartida entre las dos mitades. Se
                // compara la ruta resuelta, que es identica para las dos escrituras.
                var porRuta = new Dictionary<string, TrackItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in database.AllTracks)
                {
                    var k = FullPathOf(t.FilePath);
                    if (k != null && !porRuta.ContainsKey(k)) porRuta[k] = t;
                }

                // Que ficha se aplico ya sobre cada archivo: la segunda escritura del mismo
                // archivo (relativa + absoluta) no vuelve a pisar lo que dice la primera.
                var aplicadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var dto in dtos)
                {
                    var clave = FullPathOf(dto.FilePath);
                    if (clave == null) continue;

                    if (!porRuta.TryGetValue(clave, out var existingTrack))
                    {
                        // UI-4 paso B: el escaneo del disco solo encuentra lo que vive en
                        // musica/, jingles/, separadores/, voces/, comerciales/ y horarias/.
                        // Una pista importada desde otra carpeta (o desde un disco externo)
                        // se perdia al reiniciar, aunque su ficha estuviera guardada: la
                        // biblioteca perdia el genero, los cue points y el contador de
                        // reproducciones que el operador habia dejado escritos. Se recrea.
                        if (!AudioSourceFactory.IsSupported(dto.FilePath))
                        {
                            continue;
                        }

                        var imported = TrackLoader.CreateFromFile(dto.FilePath);
                        ApplyDto(imported, dto);
                        database.AllTracks.Add(imported);
                        database.AddGenre(dto.Genre);
                        result.RestoredImports++;

                        // A partir de aqui las fichas que senalen a este mismo archivo se
                        // aplican sobre la pista recreada en vez de crear una segunda fila.
                        porRuta[clave] = imported;
                        aplicadas.Add(clave);

                        if (!File.Exists(dto.FilePath)) result.MissingFiles++;
                        continue;
                    }

                    if (!aplicadas.Add(clave))
                    {
                        // El JSON tiene dos fichas del mismo archivo (una relativa y una
                        // absoluta, normalmente por pistas que se importaron y ademas viven en
                        // una de las carpetas del escaneo). Se conserva la primera aplicada.
                        result.Duplicated++;
                        continue;
                    }

                    ApplyDto(existingTrack, dto);
                    result.Applied++;
                }

                database.ApplyFilters();
                Console.WriteLine($"Metadatos cargados correctamente para {dtos.Count} tracks");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cargando metadatos: {ex.Message}");
                
                // Intentar recuperar del backup
                if (File.Exists(BACKUP_FILE))
                {
                    try
                    {
                        Console.WriteLine("Intentando recuperar del backup...");
                        File.Copy(BACKUP_FILE, METADATA_FILE, true);
                        return LoadMetadata(database); // Reintentar
                    }
                    catch (Exception backupEx)
                    {
                        Console.WriteLine($"Error recuperando backup: {backupEx.Message}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Ruta resuelta contra el directorio de la emisora, o null si la ficha no trae ruta.
        /// Es la clave con la que se cruzan el escaneo de disco (guarda "musica\tema.wav") y las
        /// importaciones o la cola (guardan "C:\...\musica\tema.wav"): ambas escrituras dan la
        /// misma clave. Si el sistema no puede resolverla, se usa el texto recortado, que es lo
        /// mejor que se puede hacer sin romper la ficha.
        /// </summary>
        private static string? FullPathOf(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try { return Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return path.Trim();
            }
        }

        /// <summary>
        /// Vuelca una ficha guardada sobre un track ya existente. Antes estaba inlineado en
        /// el bucle de carga y la rama de importados no la tenia: ahora ambas rutas aplican
        /// exactamente los mismos campos.
        /// </summary>
        private static void ApplyDto(TrackItem track, TrackMetadataDto dto)
        {
            track.Title = dto.Title;
            track.Artist = dto.Artist;
            track.Album = dto.Album;
            track.Genre = dto.Genre;
            track.Year = dto.Year;
            track.Priority = dto.Priority;
            track.Bpm = dto.Bpm;
            track.Type = dto.Type;
            track.CueInSeconds = dto.CueIn;
            track.IntroSeconds = dto.Intro;
            track.CueOutSeconds = dto.CueOut;
            track.EndCueSeconds = dto.EndCue;
            track.OutroSeconds = dto.Outro;
            track.FadeIn = dto.FadeIn;
            track.FadeOut = dto.FadeOut;
            track.CuePoints = dto.CuePoints.Select(s => TimeSpan.FromSeconds(s)).ToList();
            track.Composer = dto.Composer;
            track.Copyright = dto.Copyright;
            track.Comment = dto.Comment;
            track.PlayCount = dto.PlayCount;
            track.LastPlayed = dto.LastPlayed;
        }

        /// <summary>
        /// Apunta al aire que una pista se emitió: <b>solo</b> <c>playCount</c> y
        /// <c>lastPlayed</c> sobre la ficha guardada.
        ///
        /// Existe porque el camino viejo los pisaba. Esto lo llamaba únicamente
        /// <see cref="PlaybackAttributionService"/> en el instante del PLAY, y volcaba los
        /// dieciocho campos del <see cref="TrackItem"/> de la cola encima de la ficha: la cola
        /// de la consola se llena leyendo el archivo, así que su género es el que traiga la
        /// etiqueta ID3 (o <c>"General"</c> si no trae), y con eso borraba el género que el
        /// operador escribió a mano en Biblioteca. Pasó de verdad con una pista real —«Pop» se
        /// convirtió en «General» sin tocar Biblioteca— y ahí quedó la lección:
        /// <b>reproducir no es editar</b>.
        ///
        /// Los campos editorados (título, artista, álbum, género, año, prioridad, BPM, tipo,
        /// cue in/out, intro/outro, fades, puntos de cue, compositor, copyright y comentario)
        /// solo se reescriben por la vía de Biblioteca, que manda el objeto completo
        /// (<c>SaveMetadata</c>). Aquí, si la pista no tiene ficha todavía, se crea con todo lo
        /// que se sabe de ella: es lo que hace falta para que una importación fuera de las seis
        /// carpetas sobreviva al reinicio en cuanto suena.
        ///
        /// La ficha se cruza por <see cref="FullPathOf"/> y en minúsculas, la misma clave que
        /// usa la carga: sin eso, el escaneo (<c>musica\tema.wav</c>) y la cola
        /// (<c>C:\...\musica\tema.wav</c>) son dos entradas distintas y el contador se
        /// quedaría en la otra mitad de la ficha.
        /// </summary>
        public void RegisterPlay(TrackItem track)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.FilePath)) return;

            try
            {
                List<TrackMetadataDto> dtos;

                if (File.Exists(METADATA_FILE))
                {
                    string existingJson = File.ReadAllText(METADATA_FILE);
                    dtos = JsonSerializer.Deserialize<List<TrackMetadataDto>>(existingJson, _jsonOptions) ?? new List<TrackMetadataDto>();
                }
                else
                {
                    dtos = new List<TrackMetadataDto>();
                }

                var clave = Clave(track.FilePath);
                var existingDto = dtos.FirstOrDefault(d => Clave(d.FilePath) == clave);

                if (existingDto != null)
                {
                    // Actualizar existente: SOLO lo que apunta la emisión. Nada más.
                    existingDto.PlayCount = track.PlayCount;
                    existingDto.LastPlayed = track.LastPlayed;
                }
                else
                {
                    // Agregar nuevo
                    dtos.Add(new TrackMetadataDto
                    {
                        FilePath = track.FilePath,
                        Title = track.Title,
                        Artist = track.Artist,
                        Album = track.Album,
                        Genre = track.Genre,
                        Year = track.Year,
                        Priority = track.Priority,
                        Bpm = track.Bpm,
                        Type = track.Type,
                        CueIn = track.CueInSeconds,
                        Intro = track.IntroSeconds,
                        CueOut = track.CueOutSeconds,
                        EndCue = track.EndCueSeconds,
                        Outro = track.OutroSeconds,
                        FadeIn = track.FadeIn,
                        FadeOut = track.FadeOut,
                        CuePoints = track.CuePoints.Select(c => c.TotalSeconds).ToList(),
                        Composer = track.Composer,
                        Copyright = track.Copyright,
                        Comment = track.Comment,
                        PlayCount = track.PlayCount,
                        LastPlayed = track.LastPlayed
                    });
                }

                string jsonString = JsonSerializer.Serialize(dtos, _jsonOptions);
                WriteAtomic(METADATA_FILE, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error guardando metadatos del track: {ex.Message}");
                throw;
            }
        }

        /// <summary>Clave con la que dos escrituras de la misma pista son la misma ficha.</summary>
        private static string Clave(string path) => (FullPathOf(path) ?? path.Trim()).ToLowerInvariant();

        public void DeleteTrackMetadata(string filePath)
        {
            if (!File.Exists(METADATA_FILE))
                return;

            try
            {
                string jsonString = File.ReadAllText(METADATA_FILE);
                var dtos = JsonSerializer.Deserialize<List<TrackMetadataDto>>(jsonString, _jsonOptions);

                if (dtos != null)
                {
                    // La misma clave que usa la carga y el apuntar de emisiones: el escaneo guarda
                    // "musica\tema.wav" y la Biblioteca "C:\...\musica\tema.wav". Comparando el
                    // texto literal se quedaba una ficha huérfana de la otra mitad de la pista.
                    var clave = Clave(filePath);
                    dtos.RemoveAll(d => Clave(d.FilePath) == clave);
                    jsonString = JsonSerializer.Serialize(dtos, _jsonOptions);
                    WriteAtomic(METADATA_FILE, jsonString);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error eliminando metadatos del track: {ex.Message}");
            }
        }

        public bool MetadataExists()
        {
            return File.Exists(METADATA_FILE);
        }

        public void ExportMetadataToCsv(string outputPath)
        {
            if (!File.Exists(METADATA_FILE))
            {
                throw new FileNotFoundException("No existe archivo de metadatos");
            }

            try
            {
                string jsonString = File.ReadAllText(METADATA_FILE);
                var dtos = JsonSerializer.Deserialize<List<TrackMetadataDto>>(jsonString, _jsonOptions);

                if (dtos == null || dtos.Count == 0)
                {
                    throw new InvalidOperationException("No hay metadatos para exportar");
                }

                var csvLines = new List<string>
                {
                    "FilePath,Title,Artist,Album,Genre,Year,Priority,Bpm,Type,CueIn,Intro,CueOut,EndCue,Outro,FadeIn,FadeOut,Composer,Comment,PlayCount,LastPlayed"
                };

                foreach (var dto in dtos)
                {
                    var line = $"{EscapeCsv(dto.FilePath)},{EscapeCsv(dto.Title)},{EscapeCsv(dto.Artist)},{EscapeCsv(dto.Album)},{EscapeCsv(dto.Genre)},{dto.Year},{dto.Priority},{dto.Bpm},{dto.Type},{dto.CueIn},{dto.Intro},{dto.CueOut},{dto.EndCue},{dto.Outro},{dto.FadeIn},{dto.FadeOut},{EscapeCsv(dto.Composer)},{EscapeCsv(dto.Comment)},{dto.PlayCount},{dto.LastPlayed?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""}";
                    csvLines.Add(line);
                }

                File.WriteAllLines(outputPath, csvLines);
                Console.WriteLine($"Metadatos exportados a CSV: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exportando a CSV: {ex.Message}");
                throw;
            }
        }

        private string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "";
            
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }
    }
}