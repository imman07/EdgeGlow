namespace EdgeGlow;

/// <summary>Single definition of "a song is playing".</summary>
public static class Activity
{
    public static bool IsPlaying(MediaColorProvider media, AudioAnalyzer audio)
        => media.IsPlaying || audio.IsAudioActive();
}
