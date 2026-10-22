using System;
using System.Collections.Generic;
using System.Linq;

namespace quasar_radio
{
    public class CuePoint
    {
        public TimeSpan Position { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        
        public CuePoint(TimeSpan position, string name = "")
        {
            Position = position;
            Name = name;
        }
        
        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) 
                ? Position.ToString(@"mm\:ss\.fff") 
                : $"{Name} ({Position.ToString(@"mm\:ss\.fff")})";
        }
    }

    public class TrackEditorService
    {
        // Validación de puntos CUE
        public bool ValidateCuePoints(TrackItem track)
        {
            // Validar que CueIn < CueOut
            if (track.CueIn >= track.CueOut && track.CueOut > TimeSpan.Zero)
            {
                return false;
            }

            // Validar que los puntos estén dentro de la duración
            if (track.CueIn > track.Duration)
            {
                return false;
            }

            if (track.CueOut > track.Duration && track.CueOut > TimeSpan.Zero)
            {
                return false;
            }

            // Validar EndCue
            if (track.EndCue > track.Duration)
            {
                return false;
            }

            return true;
        }

        // Establecer puntos CUE de forma inteligente
        public void SetIntelligentCuePoints(TrackItem track)
        {
            // CueIn al inicio
            track.CueIn = TimeSpan.Zero;

            // CueOut 2 segundos antes del final si no está establecido
            if (track.CueOut == TimeSpan.Zero && track.Duration > TimeSpan.FromSeconds(5))
            {
                track.CueOut = track.Duration - TimeSpan.FromSeconds(2);
            }

            // Intro detectada (primeros 10 segundos)
            if (track.Intro == TimeSpan.Zero && track.Duration > TimeSpan.FromSeconds(15))
            {
                track.Intro = TimeSpan.FromSeconds(10);
            }

            // Outro detectada (últimos 5 segundos)
            if (track.Outro == TimeSpan.Zero && track.Duration > TimeSpan.FromSeconds(15))
            {
                track.Outro = TimeSpan.FromSeconds(5);
            }

            // Fade in por defecto (1 segundo)
            if (track.FadeIn == 0)
            {
                track.FadeIn = 1.0;
            }

            // Fade out por defecto (2 segundos)
            if (track.FadeOut == 0)
            {
                track.FadeOut = 2.0;
            }
        }

        // Resetear puntos CUE a valores predeterminados
        public void ResetCuePoints(TrackItem track)
        {
            track.CueIn = TimeSpan.Zero;
            track.CueOut = TimeSpan.Zero;
            track.Intro = TimeSpan.Zero;
            track.Outro = TimeSpan.Zero;
            track.EndCue = TimeSpan.Zero;
            track.FadeIn = 0;
            track.FadeOut = 0;
            track.CuePoints.Clear();
        }

        // Agregar punto CUE adicional
        public bool AddCuePoint(TrackItem track, TimeSpan position, string name = "")
        {
            if (position < TimeSpan.Zero || position > track.Duration)
            {
                return false;
            }

            // Verificar si ya existe un punto cercano (dentro de 100ms)
            if (track.CuePoints.Any(cp => Math.Abs((cp - position).TotalSeconds) < 0.1))
            {
                return false;
            }

            track.CuePoints.Add(position);
            track.CuePoints = track.CuePoints.OrderBy(cp => cp).ToList();
            
            return true;
        }

        // Eliminar punto CUE
        public bool RemoveCuePoint(TrackItem track, TimeSpan position)
        {
            var cuePoint = track.CuePoints.FirstOrDefault(cp => 
                Math.Abs((cp - position).TotalSeconds) < 0.1);

            if (cuePoint != TimeSpan.Zero)
            {
                track.CuePoints.Remove(cuePoint);
                return true;
            }

            return false;
        }

        // Eliminar todos los puntos CUE adicionales
        public void ClearAllCuePoints(TrackItem track)
        {
            track.CuePoints.Clear();
        }

        // Mover punto CUE
        public bool MoveCuePoint(TrackItem track, TimeSpan oldPosition, TimeSpan newPosition)
        {
            if (newPosition < TimeSpan.Zero || newPosition > track.Duration)
            {
                return false;
            }

            var index = track.CuePoints.FindIndex(cp => 
                Math.Abs((cp - oldPosition).TotalSeconds) < 0.1);

            if (index >= 0)
            {
                track.CuePoints[index] = newPosition;
                track.CuePoints = track.CuePoints.OrderBy(cp => cp).ToList();
                return true;
            }

            return false;
        }

        // Establecer Loop points
        public void SetLoopPoints(TrackItem track, TimeSpan loopStart, TimeSpan loopEnd)
        {
            if (loopStart < TimeSpan.Zero || loopEnd > track.Duration || loopStart >= loopEnd)
            {
                throw new ArgumentException("Puntos de loop inválidos");
            }

            // Usar CueIn como inicio de loop y CueOut como fin de loop
            track.CueIn = loopStart;
            track.CueOut = loopEnd;
        }

        // Calcular tiempo de reproducción efectivo
        public TimeSpan CalculateEffectiveDuration(TrackItem track)
        {
            if (track.CueOut > TimeSpan.Zero)
            {
                return track.CueOut - track.CueIn;
            }
            return track.Duration - track.CueIn;
        }

        // Calcular tiempo de intro
        public TimeSpan CalculateIntroTime(TrackItem track)
        {
            return track.Intro;
        }

        // Calcular tiempo de outro
        public TimeSpan CalculateOutroTime(TrackItem track)
        {
            return track.Outro;
        }

        // Normalizar puntos CUE (asegurar que estén dentro de límites)
        public void NormalizeCuePoints(TrackItem track)
        {
            // CueIn no puede ser negativo
            if (track.CueIn < TimeSpan.Zero)
            {
                track.CueIn = TimeSpan.Zero;
            }

            // CueIn no puede exceder duración
            if (track.CueIn > track.Duration)
            {
                track.CueIn = track.Duration;
            }

            // CueOut no puede exceder duración
            if (track.CueOut > track.Duration)
            {
                track.CueOut = track.Duration;
            }

            // CueOut debe ser mayor que CueIn
            if (track.CueOut > TimeSpan.Zero && track.CueOut <= track.CueIn)
            {
                track.CueOut = track.CueIn + TimeSpan.FromSeconds(1);
            }

            // Normalizar puntos CUE adicionales
            for (int i = track.CuePoints.Count - 1; i >= 0; i--)
            {
                if (track.CuePoints[i] < TimeSpan.Zero || track.CuePoints[i] > track.Duration)
                {
                    track.CuePoints.RemoveAt(i);
                }
            }

            // Ordenar puntos CUE
            track.CuePoints = track.CuePoints.OrderBy(cp => cp).ToList();
        }

        // Copiar puntos CUE de un track a otro
        public void CopyCuePoints(TrackItem source, TrackItem destination)
        {
            destination.CueIn = source.CueIn;
            destination.CueOut = source.CueOut;
            destination.Intro = source.Intro;
            destination.Outro = source.Outro;
            destination.EndCue = source.EndCue;
            destination.FadeIn = source.FadeIn;
            destination.FadeOut = source.FadeOut;
            destination.CuePoints = new List<TimeSpan>(source.CuePoints);
        }

        // Detectar silencio en puntos específicos (para edición asistida)
        public TimeSpan FindNearestSilence(TrackItem track, TimeSpan searchPosition, TimeSpan searchRange)
        {
            // Esta es una implementación básica. En una versión completa,
            // se usaría NAudio para analizar el audio y detectar silencio real.
            
            // Por ahora, retornamos el punto de búsqueda
            return searchPosition;
        }

        // Aplicar preset de edición
        public void ApplyEditPreset(TrackItem track, string presetName)
        {
            switch (presetName.ToLower())
            {
                case "radio":
                    // Preset para radio broadcasting
                    track.CueIn = TimeSpan.Zero;
                    track.CueOut = track.Duration - TimeSpan.FromSeconds(2);
                    track.Intro = TimeSpan.FromSeconds(10);
                    track.Outro = TimeSpan.FromSeconds(5);
                    track.FadeIn = 1.0;
                    track.FadeOut = 2.0;
                    break;

                case "club":
                    // Preset para club/DJ
                    track.CueIn = TimeSpan.Zero;
                    track.CueOut = track.Duration;
                    track.Intro = TimeSpan.FromSeconds(16); // 32 beats @ 120 BPM
                    track.Outro = TimeSpan.FromSeconds(32); // 64 beats @ 120 BPM
                    track.FadeIn = 0.5;
                    track.FadeOut = 0.5;
                    break;

                case "podcast":
                    // Preset para podcast
                    track.CueIn = TimeSpan.Zero;
                    track.CueOut = track.Duration;
                    track.Intro = TimeSpan.Zero;
                    track.Outro = TimeSpan.Zero;
                    track.FadeIn = 0.5;
                    track.FadeOut = 1.0;
                    break;

                case "clean":
                    // Preset limpio sin fades
                    track.CueIn = TimeSpan.Zero;
                    track.CueOut = track.Duration;
                    track.Intro = TimeSpan.Zero;
                    track.Outro = TimeSpan.Zero;
                    track.FadeIn = 0;
                    track.FadeOut = 0;
                    track.CuePoints.Clear();
                    break;

                default:
                    throw new ArgumentException($"Preset desconocido: {presetName}");
            }
        }

        // Obtener información de edición como string
        public string GetEditInfo(TrackItem track)
        {
            var info = new List<string>
            {
                $"Track: {track.Title} - {track.Artist}",
                $"Duración: {track.Duration:hh\\:mm\\:ss}",
                $"Cue In: {track.CueIn:hh\\:mm\\:ss\\.fff}",
                $"Cue Out: {(track.CueOut > TimeSpan.Zero ? track.CueOut.ToString(@"hh\:mm\:ss\.fff") : "No establecido")}",
                $"Intro: {track.Intro:hh\\:mm\\:ss}",
                $"Outro: {track.Outro:hh\\:mm\\:ss}",
                $"Fade In: {track.FadeIn:F2}s",
                $"Fade Out: {track.FadeOut:F2}s",
                $"Puntos CUE: {track.CuePoints.Count} ({track.CuePointsFormatted})",
                $"Duración efectiva: {CalculateEffectiveDuration(track):hh\\:mm\\:ss}"
            };

            return string.Join(Environment.NewLine, info);
        }

        // Validar configuración de fade
        public bool ValidateFadeSettings(TrackItem track)
        {
            // Fade in no puede exceder la mitad de la duración
            if (track.FadeIn > track.Duration.TotalSeconds / 2)
            {
                return false;
            }

            // Fade out no puede exceder la mitad de la duración
            if (track.FadeOut > track.Duration.TotalSeconds / 2)
            {
                return false;
            }

            // Fade in + fade out no puede exceder duración
            if (track.FadeIn + track.FadeOut > track.Duration.TotalSeconds)
            {
                return false;
            }

            return true;
        }

        // Calcular overlap de fades
        public double CalculateFadeOverlap(TrackItem track)
        {
            double effectiveDuration = CalculateEffectiveDuration(track).TotalSeconds;
            double totalFadeTime = track.FadeIn + track.FadeOut;
            
            if (totalFadeTime > effectiveDuration)
            {
                return totalFadeTime - effectiveDuration;
            }
            
            return 0;
        }
    }
}