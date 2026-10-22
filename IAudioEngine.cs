using System;
using NAudio.Wave;

namespace quasar_radio;

public interface IAudioEngine : IDisposable
{
    float MasterVolume { get; set; }
    double CrossfadeSeconds { get; set; }
    bool IsAutoMode { get; set; }
    bool IsPlaying { get; }
    string? LastError { get; }
    AudioDspProcessor DspProcessor { get; }
    bool IsDucked { get; set; }
    Func<TrackItem?>? GetNextTrackProvider { get; set; }

    TrackItem? DeckA_Track { get; }
    TimeSpan DeckA_Position { get; }
    TimeSpan DeckA_Duration { get; }
    bool DeckA_IsActive { get; }
    float DeckA_Volume { get; set; }

    TrackItem? DeckB_Track { get; }
    TimeSpan DeckB_Position { get; }
    TimeSpan DeckB_Duration { get; }
    bool DeckB_IsActive { get; }
    float DeckB_Volume { get; set; }

    TrackItem? CurrentTrack { get; }
    string ActiveDeckName { get; }
    TimeSpan CurrentPosition { get; }
    TimeSpan CurrentDuration { get; }

    event EventHandler<TrackItem?>? TrackChanged;
    event EventHandler<TimeSpan>? PositionChanged;
    event EventHandler? PlaybackFinished;
    event EventHandler<(float Left, float Right)>? MeterLevelsChanged;

    bool PlayTrack(TrackItem track);
    void PreloadNextTrack(TrackItem? track);
    void TriggerCrossfadeToNext();
    void Pause();
    void StopAll();
}
