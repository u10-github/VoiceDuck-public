namespace VoiceDuck.Core.Tests;

public class DuckingPolicyTests
{
    [Fact]
    public void Create_with_defaults()
    {
        var policy = new DuckingPolicy();
        Assert.Equal(0.3, policy.DuckingRatio);
        Assert.Equal(10, policy.RestoreDelaySeconds);
    }

    [Fact]
    public void Create_with_custom_values()
    {
        var policy = new DuckingPolicy(0.5, 5);
        Assert.Equal(0.5, policy.DuckingRatio);
        Assert.Equal(5, policy.RestoreDelaySeconds);
    }

    [Fact]
    public void DuckingRatio_is_clamped_to_minimum()
    {
        var policy = new DuckingPolicy(-0.1, 10);
        Assert.Equal(0.0, policy.DuckingRatio);
    }

    [Fact]
    public void DuckingRatio_is_clamped_to_maximum()
    {
        var policy = new DuckingPolicy(1.5, 10);
        Assert.Equal(1.0, policy.DuckingRatio);
    }

    [Fact]
    public void Compute_ducked_volume_returns_ratio_of_original()
    {
        var policy = new DuckingPolicy(0.3, 10);
        Assert.Equal(0.3f, policy.ComputeDuckedVolume(1.0f));
    }

    [Theory]
    [InlineData(0.8, 0.5, 0.4)]
    [InlineData(1.0, 0.3, 0.3)]
    [InlineData(0.0, 0.5, 0.0)]
    public void Compute_ducked_volume_scenarios(double original, double ratio, double expected)
    {
        var policy = new DuckingPolicy(ratio, 10);
        Assert.Equal((float)expected, policy.ComputeDuckedVolume((float)original), 3);
    }

    [Fact]
    public void Compute_ducked_volume_clamps_negative_input()
    {
        var policy = new DuckingPolicy(0.5, 10);
        Assert.Equal(0.0f, policy.ComputeDuckedVolume(-0.5f));
    }

    [Fact]
    public void Compute_ducked_volume_clamps_overflow_input()
    {
        var policy = new DuckingPolicy(0.5, 10);
        Assert.Equal(0.5f, policy.ComputeDuckedVolume(2.0f));
    }
}
