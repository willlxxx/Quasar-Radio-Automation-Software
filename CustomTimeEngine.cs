using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using Serilog;

namespace quasar_radio
{
    public static class CustomTimeEngine
    {
        private static readonly string[] AudioExtensions = [".mp3", ".wav", ".wma", ".flac", ".ogg", ".aac", ".m4a"];

        public static void AnnounceTime(Action? onStart = null, Action? onEnd = null, Action<string>? onError = null)
        {
            Task.Run(() =>
            {
                try
                {
                    DateTime now = DateTime.Now;
                    int hour = now.Hour;
                    int minute = now.Minute;

                    string? hourFile = minute == 0
                        ? FindClip($"HRS{hour:D2}_O") ?? FindClip($"HRS{hour:D2}")
                        : FindClip($"HRS{hour:D2}");
                    hourFile ??= FindClip(hour.ToString());

                    string? minFile = minute == 0
                        ? null
                        : FindClip($"MIN{minute:D2}") ?? FindClip($"min_{minute}");

                    if (File.Exists(hourFile))
                    {
                        onStart?.Invoke();
                        if (!PlayAudioFile(hourFile, out var error))
                        {
                            onError?.Invoke(error!);
                            onEnd?.Invoke();
                            return;
                        }
                        if (File.Exists(minFile) && !PlayAudioFile(minFile, out error))
                        {
                            onError?.Invoke(error!);
                        }
                        onEnd?.Invoke();
                    }
                    else
                    {
                        TimeAnnouncementService.SpeakCurrentTime(onStart, onEnd, onError);
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "No se pudo iniciar el anuncio de hora");
                    onError?.Invoke(UserErrorMessages.For(exception, "anunciar la hora"));
                    onEnd?.Invoke();
                }
            });
        }

        private static string? FindClip(string clipName)
        {
            string[] folders =
            [
                Path.Combine(AppContext.BaseDirectory, "horarias"),
                Path.Combine(Environment.CurrentDirectory, "horarias")
            ];

            foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var extension in AudioExtensions)
                {
                    var path = Path.Combine(folder, clipName + extension);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        private static bool PlayAudioFile(string filePath, out string? error)
        {
            error = null;
            try
            {
                using var reader = AudioSourceFactory.Open(filePath);
                using var waveOut = new WaveOut();
                waveOut.Init(reader);
                waveOut.Play();
                while (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    System.Threading.Thread.Sleep(100);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = UserErrorMessages.For(exception, "reproducir el clip horario");
                Log.Error(exception, "No se pudo reproducir el clip horario {FilePath}", filePath);
                return false;
            }
        }
    }
}
