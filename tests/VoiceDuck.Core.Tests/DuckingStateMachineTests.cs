namespace VoiceDuck.Core.Tests;

public class DuckingStateMachineTests
{
    [Fact]
    public void Initial_phase_is_Idle()
    {
        var machine = new DuckingStateMachine();
        Assert.Equal(DuckingPhase.Idle, machine.Phase);
    }

    [Fact]
    public void Initial_active_set_is_empty()
    {
        var machine = new DuckingStateMachine();
        Assert.Empty(machine.ActiveTriggerApps);
    }

    [Fact]
    public void Idle_transitions_to_Ducking_when_trigger_app_active()
    {
        var machine = new DuckingStateMachine();
        var phase = machine.NotifyTriggerAppActive("Discord.exe");
        Assert.Equal(DuckingPhase.Ducking, phase);
        Assert.Equal(DuckingPhase.Ducking, machine.Phase);
    }

    [Fact]
    public void Idle_transitions_to_Ducking_when_multiple_trigger_apps_active()
    {
        var machine = new DuckingStateMachine();
        machine.NotifyTriggerAppActive("Discord.exe");
        var phase = machine.NotifyTriggerAppActive("DiscordCanary.exe");
        Assert.Equal(DuckingPhase.Ducking, phase);
        Assert.Equal(2, machine.ActiveTriggerApps.Count);
    }

    [Fact]
    public void Ducking_stays_Ducking_when_another_trigger_app_becomes_active()
    {
        var machine = new DuckingStateMachine();
        machine.NotifyTriggerAppActive("Discord.exe");
        var phase = machine.NotifyTriggerAppActive("DiscordCanary.exe");
        Assert.Equal(DuckingPhase.Ducking, phase);
    }

    [Fact]
    public void Ducking_stays_Ducking_when_one_of_many_trigger_apps_becomes_inactive()
    {
        var machine = new DuckingStateMachine();
        machine.NotifyTriggerAppActive("Discord.exe");
        machine.NotifyTriggerAppActive("DiscordCanary.exe");
        var phase = machine.NotifyTriggerAppInactive("Discord.exe");
        Assert.Equal(DuckingPhase.Ducking, phase);
        Assert.Single(machine.ActiveTriggerApps);
    }

    [Fact]
    public void Ducking_transitions_to_WaitingForRestore_when_all_trigger_apps_inactive()
    {
        var machine = new DuckingStateMachine();
        machine.NotifyTriggerAppActive("Discord.exe");
        var phase = machine.NotifyTriggerAppInactive("Discord.exe");
        Assert.Equal(DuckingPhase.WaitingForRestore, phase);
        Assert.Equal(DuckingPhase.WaitingForRestore, machine.Phase);
        Assert.Empty(machine.ActiveTriggerApps);
    }

    [Fact]
    public void Ducking_transitions_to_WaitingForRestore_when_multiple_all_become_inactive()
    {
        var machine = new DuckingStateMachine();
        machine.NotifyTriggerAppActive("Discord.exe");
        machine.NotifyTriggerAppActive("DiscordCanary.exe");
        machine.NotifyTriggerAppInactive("Discord.exe");
        var phase = machine.NotifyTriggerAppInactive("DiscordCanary.exe");
        Assert.Equal(DuckingPhase.WaitingForRestore, phase);
    }

    [Fact]
    public void WaitingForRestore_returns_to_Ducking_when_trigger_app_reactivates()
    {
        var machine = new DuckingStateMachine();
        machine.NotifyTriggerAppActive("Discord.exe");
        machine.NotifyTriggerAppInactive("Discord.exe");
        var phase = machine.NotifyTriggerAppActive("Discord.exe");
        Assert.Equal(DuckingPhase.Ducking, phase);
        Assert.Single(machine.ActiveTriggerApps);
    }

    [Fact]
    public void WaitingForRestore_transitions_to_Restoring_when_delay_elapses()
    {
        var machine = new DuckingStateMachine();
        machine.NotifyTriggerAppActive("Discord.exe");
        machine.NotifyTriggerAppInactive("Discord.exe");
        var phase = machine.NotifyRestoreDelayElapsed();
        Assert.Equal(DuckingPhase.Restoring, phase);
        Assert.Equal(DuckingPhase.Restoring, machine.Phase);
    }

    [Fact]
    public void Restoring_transitions_to_Idle_when_restore_completed()
    {
        var machine = new DuckingStateMachine();
        machine.NotifyTriggerAppActive("Discord.exe");
        machine.NotifyTriggerAppInactive("Discord.exe");
        machine.NotifyRestoreDelayElapsed();
        var phase = machine.NotifyRestoreCompleted();
        Assert.Equal(DuckingPhase.Idle, phase);
        Assert.Equal(DuckingPhase.Idle, machine.Phase);
        Assert.Empty(machine.ActiveTriggerApps);
    }

    [Fact]
    public void Full_cycle_single_trigger_app()
    {
        var machine = new DuckingStateMachine();
        Assert.Equal(DuckingPhase.Idle, machine.Phase);

        machine.NotifyTriggerAppActive("Discord.exe");
        Assert.Equal(DuckingPhase.Ducking, machine.Phase);

        machine.NotifyTriggerAppInactive("Discord.exe");
        Assert.Equal(DuckingPhase.WaitingForRestore, machine.Phase);

        machine.NotifyRestoreDelayElapsed();
        Assert.Equal(DuckingPhase.Restoring, machine.Phase);

        machine.NotifyRestoreCompleted();
        Assert.Equal(DuckingPhase.Idle, machine.Phase);
    }

    [Fact]
    public void Restoring_returns_to_Ducking_when_trigger_app_reactivates()
    {
        var machine = new DuckingStateMachine();
        machine.NotifyTriggerAppActive("Discord.exe");
        machine.NotifyTriggerAppInactive("Discord.exe");
        machine.NotifyRestoreDelayElapsed();

        var phase = machine.NotifyTriggerAppActive("Discord.exe");

        Assert.Equal(DuckingPhase.Ducking, phase);
        Assert.True(machine.IsDucking);
        Assert.Single(machine.ActiveTriggerApps);
    }

    [Fact]
    public void IsDucking_is_true_when_Ducking_or_WaitingForRestore()
    {
        var machine = new DuckingStateMachine();
        Assert.False(machine.IsDucking);

        machine.NotifyTriggerAppActive("Discord.exe");
        Assert.True(machine.IsDucking);

        machine.NotifyTriggerAppInactive("Discord.exe");
        Assert.True(machine.IsDucking);

        machine.NotifyRestoreDelayElapsed();
        Assert.False(machine.IsDucking);

        machine.NotifyRestoreCompleted();
        Assert.False(machine.IsDucking);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void NotifyTriggerAppActive_throws_on_invalid_process_name(string? invalid)
    {
        var machine = new DuckingStateMachine();
        Assert.Throws<ArgumentException>(() => machine.NotifyTriggerAppActive(invalid!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void NotifyTriggerAppInactive_throws_on_invalid_process_name(string? invalid)
    {
        var machine = new DuckingStateMachine();
        Assert.Throws<ArgumentException>(() => machine.NotifyTriggerAppInactive(invalid!));
    }
}
