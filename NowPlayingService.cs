using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace quasar_radio
{
    public class NowPlayingInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string FormattedText { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public static class NowPlayingService
    {
        public static string TxtFilePath { get; set; } = "nowplaying.txt";
        public static string JsonFilePath { get; set; } = "nowplaying.json";

        public static event EventHandler<NowPlayingInfo>? NowPlayingUpdated;
        public static event EventHandler<string>? ExportFailed;

        public static void UpdateNowPlaying(TrackItem? track)
        {
            Task.Run(() =>
            {
                try
                {
                    var info = new NowPlayingInfo
                    {
                        Title = track?.Title ?? "Quasar Radio Pro",
                        Artist = track?.Artist ?? "En Vivo",
                        Duration = track?.DurationFormatted ?? "00:00",
                        FormattedText = track != null ? $"{track.Artist} - {track.Title}" : "Quasar Radio Pro - En Vivo",
                        UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };

                    // 1. Escribir nowplaying.txt para RDS y OBS
                    File.WriteAllText(TxtFilePath, info.FormattedText);

                    // 2. Escribir nowplaying.json para reproductores Web
                    string json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(JsonFilePath, json);

                    NowPlayingUpdated?.Invoke(null, info);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "No se pudo exportar la información de Now Playing");
                    ExportFailed?.Invoke(null, UserErrorMessages.For(ex, "exportar la información de la pista actual"));
                }
            });
        }
    }
}
