using System;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using Serilog;

namespace quasar_radio
{
    public static class TimeAnnouncementService
    {
        public static void SpeakCurrentTime(Action? onSpeakingStart = null, Action? onSpeakingEnd = null, Action<string>? onError = null)
        {
            Task.Run(() =>
            {
                try
                {
                    DateTime now = DateTime.Now;
                    int hour = now.Hour;
                    int minute = now.Minute;

                    string text = $"Es la hora {hour} con {minute:D2} minutos";

                    onSpeakingStart?.Invoke();

                    using var synth = new SpeechSynthesizer();
                    
                    // Buscar voz en español si está instalada
                    foreach (var voice in synth.GetInstalledVoices())
                    {
                        if (voice.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase))
                        {
                            synth.SelectVoice(voice.VoiceInfo.Name);
                            break;
                        }
                    }

                    synth.Volume = 100;
                    synth.Rate = 0;
                    synth.Speak(text);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "No se pudo anunciar la hora mediante síntesis de voz");
                    onError?.Invoke(UserErrorMessages.For(ex, "anunciar la hora por voz"));
                }
                finally
                {
                    onSpeakingEnd?.Invoke();
                }
            });
        }
    }
}
