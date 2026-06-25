namespace VoiceDuck.Core.Tests;

public class DuckingTargetDecisionTests
{
    [Fact]
    public void Duck_outcome()
    {
        var decision = DuckingTargetDecision.Duck("not a trigger app");
        Assert.Equal(DuckingOutcome.Duck, decision.Outcome);
        Assert.Equal("not a trigger app", decision.Reason);
    }

    [Fact]
    public void Protect_outcome()
    {
        var decision = DuckingTargetDecision.Protect("is a trigger app");
        Assert.Equal(DuckingOutcome.Protect, decision.Outcome);
        Assert.Equal("is a trigger app", decision.Reason);
    }

    [Fact]
    public void Two_decisions_with_same_values_are_equal()
    {
        var a = DuckingTargetDecision.Duck("reason");
        var b = DuckingTargetDecision.Duck("reason");
        Assert.Equal(a, b);
    }
}
