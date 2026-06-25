namespace VoiceDuck.Core;

public record DuckingPolicy
{
    public double DuckingRatio { get; }
    public int RestoreDelaySeconds { get; }

    public DuckingPolicy()
        : this(0.3, 10)
    {
    }

    public DuckingPolicy(double duckingRatio, int restoreDelaySeconds)
    {
        DuckingRatio = Math.Clamp(duckingRatio, 0.0, 1.0);
        RestoreDelaySeconds = Math.Max(0, restoreDelaySeconds);
    }

    public float ComputeDuckedVolume(float originalVolume)
    {
        var safeVolume = Math.Clamp(originalVolume, 0.0f, 1.0f);
        return safeVolume * (float)DuckingRatio;
    }
}
