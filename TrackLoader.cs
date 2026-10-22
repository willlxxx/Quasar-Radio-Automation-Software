using System;
using System.IO;

namespace quasar_radio
{
    public static class TrackLoader
    {
        public static TrackItem CreateFromFile(string filePath)
        {
            var track = new TrackItem
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                Artist = "Artista Desconocido",
                Type = TrackType.Music,
                Status = TrackStatus.Queued
            };

            // Intentar separar "Artista - Título" si el archivo sigue ese patrón
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.Contains(" - "))
            {
                var parts = fileName.Split(new[] { " - " }, 2, StringSplitOptions.None);
                track.Artist = parts[0].Trim();
                track.Title = parts[1].Trim();
            }

            // Obtener duración exacta con NAudio
            try
            {
                using var reader = AudioSourceFactory.Open(filePath);
                track.Duration = reader.TotalTime;
            }
            catch
            {
                track.Duration = TimeSpan.Zero;
            }

            return track;
        }
    }
}
