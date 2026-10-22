using System;
using System.Collections.Generic;
using System.IO;
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

                // Crear backup antes de guardar
                if (File.Exists(METADATA_FILE))
                {
                    File.Copy(METADATA_FILE, BACKUP_FILE, true);
                }

                string jsonString = JsonSerializer.Serialize(dtos, _jsonOptions);
                File.WriteAllText(METADATA_FILE, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error guardando metadatos: {ex.Message}");
                throw;
            }
        }

        public void LoadMetadata(AudioDatabaseService database)
        {
            if (!File.Exists(METADATA_FILE))
            {
                Console.WriteLine("No existe archivo de metadatos");
                return;
            }

            try
            {
                string jsonString = File.ReadAllText(METADATA_FILE);
                var dtos = JsonSerializer.Deserialize<List<TrackMetadataDto>>(jsonString, _jsonOptions);

                if (dtos == null || dtos.Count == 0)
                {
                    Console.WriteLine("Archivo de metadatos vacío o inválido");
                    return;
                }

                foreach (var dto in dtos)
                {
                    var existingTrack = database.AllTracks.FirstOrDefault(t => 
                        t.FilePath.Equals(dto.FilePath, StringComparison.OrdinalIgnoreCase));

                    if (existingTrack != null)
                    {
                        // Actualizar track existente con metadatos
                        existingTrack.Title = dto.Title;
                        existingTrack.Artist = dto.Artist;
                        existingTrack.Album = dto.Album;
                        existingTrack.Genre = dto.Genre;
                        existingTrack.Year = dto.Year;
                        existingTrack.Priority = dto.Priority;
                        existingTrack.Bpm = dto.Bpm;
                        existingTrack.Type = dto.Type;
                        existingTrack.CueInSeconds = dto.CueIn;
                        existingTrack.IntroSeconds = dto.Intro;
                        existingTrack.CueOutSeconds = dto.CueOut;
                        existingTrack.EndCueSeconds = dto.EndCue;
                        existingTrack.OutroSeconds = dto.Outro;
                        existingTrack.FadeIn = dto.FadeIn;
                        existingTrack.FadeOut = dto.FadeOut;
                        existingTrack.CuePoints = dto.CuePoints.Select(s => TimeSpan.FromSeconds(s)).ToList();
                        existingTrack.Composer = dto.Composer;
                        existingTrack.Copyright = dto.Copyright;
                        existingTrack.Comment = dto.Comment;
                        existingTrack.PlayCount = dto.PlayCount;
                        existingTrack.LastPlayed = dto.LastPlayed;
                    }
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
                        LoadMetadata(database); // Reintentar
                    }
                    catch (Exception backupEx)
                    {
                        Console.WriteLine($"Error recuperando backup: {backupEx.Message}");
                    }
                }
            }
        }

        public void SaveSingleTrackMetadata(TrackItem track)
        {
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

                // Buscar y actualizar o agregar el track
                var existingDto = dtos.FirstOrDefault(d => 
                    d.FilePath.Equals(track.FilePath, StringComparison.OrdinalIgnoreCase));

                if (existingDto != null)
                {
                    // Actualizar existente
                    existingDto.Title = track.Title;
                    existingDto.Artist = track.Artist;
                    existingDto.Album = track.Album;
                    existingDto.Genre = track.Genre;
                    existingDto.Year = track.Year;
                    existingDto.Priority = track.Priority;
                    existingDto.Bpm = track.Bpm;
                    existingDto.Type = track.Type;
                    existingDto.CueIn = track.CueInSeconds;
                    existingDto.Intro = track.IntroSeconds;
                    existingDto.CueOut = track.CueOutSeconds;
                    existingDto.EndCue = track.EndCueSeconds;
                    existingDto.Outro = track.OutroSeconds;
                    existingDto.FadeIn = track.FadeIn;
                    existingDto.FadeOut = track.FadeOut;
                    existingDto.CuePoints = track.CuePoints.Select(c => c.TotalSeconds).ToList();
                    existingDto.Composer = track.Composer;
                    existingDto.Copyright = track.Copyright;
                    existingDto.Comment = track.Comment;
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
                File.WriteAllText(METADATA_FILE, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error guardando metadatos del track: {ex.Message}");
                throw;
            }
        }

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
                    dtos.RemoveAll(d => d.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                    jsonString = JsonSerializer.Serialize(dtos, _jsonOptions);
                    File.WriteAllText(METADATA_FILE, jsonString);
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