using System;
using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;

namespace quasar_radio;

public static class AudioSourceFactory
{
    public const string Extensions = "*.mp3;*.wav;*.wma;*.flac;*.ogg;*.aac;*.m4a";
    public const string FileDialogFilter = "Archivos de audio (*.mp3;*.wav;*.wma;*.flac;*.ogg;*.aac;*.m4a)|*.mp3;*.wav;*.wma;*.flac;*.ogg;*.aac;*.m4a|Todos los archivos (*.*)|*.*";

    public static bool IsSupported(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wma", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase);
    }

    public static WaveStream Open(string filePath)
    {
        if (!IsSupported(filePath))
        {
            throw new NotSupportedException($"Formato de audio no admitido: {Path.GetExtension(filePath)}");
        }

        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".ogg" => new VorbisWaveReader(filePath),
            ".flac" or ".aac" or ".m4a" => new MediaFoundationReader(filePath),
            _ => new AudioFileReader(filePath)
        };
    }
}