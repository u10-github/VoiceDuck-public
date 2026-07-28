namespace VoiceDuck.Core.Tests;

public class ControlEligibilityResultTests
{
    [Fact]
    public void Eligible_results_with_the_same_values_are_equal()
    {
        Assert.Equal(
            new ControlEligibilityResult.Eligible(),
            new ControlEligibilityResult.Eligible());
    }

    [Fact]
    public void Rejected_result_preserves_typed_reason()
    {
        var result = new ControlEligibilityResult.Rejected(
            ControlEligibilityRejectionReason.TriggerApplication);

        Assert.Equal(
            ControlEligibilityRejectionReason.TriggerApplication,
            result.Reason);
    }
}
