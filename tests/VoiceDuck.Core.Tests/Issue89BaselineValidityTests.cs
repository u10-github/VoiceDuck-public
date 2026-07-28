namespace VoiceDuck.Core.Tests;

public class Issue89BaselineValidityTests
{
    private const string Device = "default-device";
    private const string PathA = @"C:\Apps\a.exe";
    private const string PathB = @"C:\Apps\b.exe";

    private static readonly VoiceDuckSettings Settings = new(
        new DuckingPolicy(0.5, 10),
        new[] { new TriggerApp("Discord.exe") },
        Array.Empty<ExcludeApp>());

    [Fact]
    public void Policy_classifies_empty_boundary_and_conflicting_candidates()
    {
        Assert.IsType<BaselineSelectionResult.NoCandidates>(
            BaselineSelectionPolicy.Select(Array.Empty<float>()));

        var exactBoundary = Assert.IsType<BaselineSelectionResult.Selected>(
            BaselineSelectionPolicy.Select(new[] { 0.50f, 0.51f }));
        Assert.Equal(0.51f, exactBoundary.Baseline);

        var epsilonBoundary = Assert.IsType<BaselineSelectionResult.Selected>(
            BaselineSelectionPolicy.Select(new[] { 0.50f, 0.5100009f }));
        Assert.Equal(0.5100009f, epsilonBoundary.Baseline);

        Assert.IsType<BaselineSelectionResult.Conflict>(
            BaselineSelectionPolicy.Select(new[] { 0.50f, 0.510002f }));
    }

    [Fact]
    public void Policy_selects_maximum_regardless_of_candidate_order()
    {
        var forward = Assert.IsType<BaselineSelectionResult.Selected>(
            BaselineSelectionPolicy.Select(new[] { 0.995f, 1.0f }));
        var reverse = Assert.IsType<BaselineSelectionResult.Selected>(
            BaselineSelectionPolicy.Select(new[] { 1.0f, 0.995f }));

        Assert.Equal(1.0f, forward.Baseline);
        Assert.Equal(forward.Baseline, reverse.Baseline);
        Assert.Equal(forward.Spread, reverse.Spread);
    }

    [Fact]
    public void Rejected_new_identity_has_no_state_persistence_or_write()
    {
        var fixture = new Fixture();

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 1.0f), Session(2, PathA, 0.5f) },
            Settings,
            "VoiceDuck.exe");

        Assert.Equal(0, fixture.Store.Count);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
    }

    [Fact]
    public void Valid_and_conflicting_identities_are_processed_independently()
    {
        var fixture = new Fixture();

        fixture.Service.ApplyDucking(
            new[]
            {
                Session(1, PathA, 0.80f),
                Session(2, PathA, 0.805f),
                Session(3, PathB, 1.0f),
                Session(4, PathB, 0.5f),
            },
            Settings,
            "VoiceDuck.exe");

        var validIdentity = new ApplicationAudioIdentity(Device, PathA);
        var rejectedIdentity = new ApplicationAudioIdentity(Device, PathB);
        Assert.True(fixture.Store.TryGet(validIdentity, out var state));
        Assert.Equal(0.805f, state!.BaselineVolume);
        Assert.False(fixture.Store.TryGet(rejectedIdentity, out _));
        Assert.Single(fixture.Repository.Saved, o => o.Identity.Equals(validIdentity));
        Assert.Equal(2, fixture.Writer.Calls.Count);
        Assert.All(fixture.Writer.Calls, call => Assert.Contains(call.Identity.ProcessId, new uint[] { 1, 2 }));
    }

    [Fact]
    public void Persistence_precedes_writes_and_failure_prevents_writes()
    {
        var fixture = new Fixture();
        fixture.Repository.Events = fixture.Events;
        fixture.Writer.Events = fixture.Events;

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.80f), Session(2, PathA, 0.805f) },
            Settings,
            "VoiceDuck.exe");

        Assert.Equal("save", fixture.Events[0]);
        Assert.All(fixture.Events.Skip(1).Take(2), e => Assert.Equal("write", e));

        var failing = new Fixture();
        failing.Repository.ThrowOnSave = true;
        failing.Service.ApplyDucking(new[] { Session(3, PathA, 0.8f) }, Settings, "VoiceDuck.exe");
        Assert.Empty(failing.Writer.Calls);
        Assert.Equal(0, failing.Store.Count);
    }

    [Fact]
    public void Durable_baseline_wins_over_conflicting_observations_after_restart()
    {
        var identity = new ApplicationAudioIdentity(Device, PathA);
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture();
        fixture.Repository.Existing.Add(new RestorationObligation(
            identity, 1.0f, RestorationStatus.Ducked, now, now));

        fixture.Service.LoadAndPopulateStartupState();
        fixture.Writer.Calls.Clear();
        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.5f), Session(2, PathA, 0.25f) },
            Settings,
            "VoiceDuck.exe");

        Assert.True(fixture.Store.TryGet(identity, out var state));
        Assert.Equal(1.0f, state!.BaselineVolume);
        Assert.All(fixture.Writer.Calls, call => Assert.Equal(0.5f, call.Volume));
        Assert.Single(fixture.Repository.Saved, o =>
            o.Identity.Equals(identity) && o.BaselineVolume == 1.0f);
    }

    [Fact]
    public void Rejection_diagnostics_are_complete_deduplicated_and_change_sensitive()
    {
        var fixture = new Fixture();
        var first = new[] { Session(1, PathA, 1.0f), Session(2, PathA, 0.5f) };

        fixture.Service.ApplyDucking(first, Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(first.Reverse().ToArray(), Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 1.0f), Session(2, PathA, 0.4f) },
            Settings,
            "VoiceDuck.exe");

        var decisions = fixture.Logger.Messages
            .Where(m => m.StartsWith("BaselineDecision:", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, decisions.Count);
        Assert.All(decisions, message =>
        {
            Assert.Contains($"identity={Device}|{PathA}", message);
            Assert.Contains("candidates=[", message);
            Assert.Contains("spread=", message);
            Assert.Contains("outcome=rejected", message);
            Assert.Contains("reason=volume_conflict", message);
        });
    }

    [Fact]
    public void Empty_poll_has_no_side_effects_and_deduplicates_complete_no_candidate_diagnostic()
    {
        var fixture = new Fixture();

        fixture.Service.ApplyDucking(Array.Empty<AudioSessionInfo>(), Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(Array.Empty<AudioSessionInfo>(), Settings, "VoiceDuck.exe");

        Assert.Equal(0, fixture.Store.Count);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
        var firstEmptyDecisions = BaselineDecisions(fixture);
        var empty = Assert.Single(firstEmptyDecisions);
        Assert.Equal(
            "BaselineDecision: identity=(none) candidates=[] spread=0 outcome=rejected reason=no_candidates baseline=none endpoint_relevance=none relevance_reason=no_eligible_session_after_filter",
            empty);

        fixture.Service.ApplyDucking(new[] { Session(1, PathA, 0.8f) }, Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(Array.Empty<AudioSessionInfo>(), Settings, "VoiceDuck.exe");

        Assert.Equal(2, BaselineDecisions(fixture).Count(message =>
            message.Contains("reason=no_candidates", StringComparison.Ordinal)));
    }

    [Fact]
    public void New_consistent_selection_diagnostic_contains_complete_decision_fields()
    {
        var fixture = new Fixture();

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.80f), Session(2, PathA, 0.805f) },
            Settings,
            "VoiceDuck.exe");

        var decision = Assert.Single(BaselineDecisions(fixture));
        Assert.Equal(
            $"BaselineDecision: identity={Device}|{PathA} candidates=[0.8,0.805] spread=0.004999995 outcome=selected reason=consistent_candidates baseline=0.805 endpoint_relevance=relevant relevance_reason=default_multimedia",
            decision);
    }

    [Fact]
    public void Existing_state_and_durable_obligation_diagnostics_report_baseline_source()
    {
        var identity = new ApplicationAudioIdentity(Device, PathA);
        var stateFixture = new Fixture();
        stateFixture.Store.Add(new ApplicationVolumeState(identity, 1.0f, isDucked: true));

        stateFixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.5f), Session(2, PathA, 0.25f) },
            Settings,
            "VoiceDuck.exe");

        Assert.Equal(
            $"BaselineDecision: identity={Device}|{PathA} candidates=[0.25,0.5] spread=0.25 outcome=selected reason=existing_state baseline=1 endpoint_relevance=relevant relevance_reason=default_multimedia",
            Assert.Single(BaselineDecisions(stateFixture)));

        var durableFixture = new Fixture();
        var now = DateTimeOffset.UtcNow;
        durableFixture.Repository.Existing.Add(new RestorationObligation(
            identity, 1.0f, RestorationStatus.Ducked, now, now));

        durableFixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.5f), Session(2, PathA, 0.25f) },
            Settings,
            "VoiceDuck.exe");

        Assert.Equal(
            $"BaselineDecision: identity={Device}|{PathA} candidates=[0.25,0.5] spread=0.25 outcome=selected reason=existing_obligation baseline=1 endpoint_relevance=relevant relevance_reason=default_multimedia",
            Assert.Single(BaselineDecisions(durableFixture)));
    }

    [Fact]
    public void Durable_obligation_reconciles_disagreeing_in_memory_baseline_before_persist_and_write()
    {
        var identity = new ApplicationAudioIdentity(Device, PathA);
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture();
        fixture.Store.Add(new ApplicationVolumeState(identity, 0.8f, isDucked: true));
        fixture.Repository.Existing.Add(new RestorationObligation(
            identity, 1.0f, RestorationStatus.Ducked, now, now));

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.5f), Session(2, PathA, 0.25f) },
            Settings,
            "VoiceDuck.exe");

        Assert.True(fixture.Store.TryGet(identity, out var state));
        Assert.Equal(1.0f, state!.BaselineVolume);
        Assert.Single(fixture.Repository.Saved, obligation =>
            obligation.Identity.Equals(identity) && obligation.BaselineVolume == 1.0f);
        Assert.Equal(2, fixture.Writer.Calls.Count);
        Assert.All(fixture.Writer.Calls, call => Assert.Equal(0.5f, call.Volume));
        Assert.Equal(
            $"BaselineDecision: identity={Device}|{PathA} candidates=[0.25,0.5] spread=0.25 outcome=selected reason=reconciled_durable_obligation baseline=1 endpoint_relevance=relevant relevance_reason=default_multimedia",
            Assert.Single(BaselineDecisions(fixture)));
    }

    [Fact]
    public void Conflict_to_selected_and_managed_transitions_emit_once_per_changed_decision()
    {
        var fixture = new Fixture();

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 1.0f), Session(2, PathA, 0.5f) },
            Settings,
            "VoiceDuck.exe");
        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.8f), Session(2, PathA, 0.805f) },
            Settings,
            "VoiceDuck.exe");
        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.4025f), Session(2, PathA, 0.4025f) },
            Settings,
            "VoiceDuck.exe");
        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.4025f), Session(2, PathA, 0.4025f) },
            Settings,
            "VoiceDuck.exe");

        var decisions = BaselineDecisions(fixture);
        Assert.Equal(3, decisions.Count);
        Assert.Contains("outcome=rejected reason=volume_conflict", decisions[0]);
        Assert.Contains("outcome=selected reason=consistent_candidates", decisions[1]);
        Assert.Contains("outcome=selected reason=existing_obligation", decisions[2]);
    }

    [Fact]
    public void Corrupt_load_poll_prunes_decisions_for_identities_not_in_current_group_set()
    {
        var fixture = new Fixture();
        var firstPoll = new[]
        {
            Session(1, PathA, 1.0f),
            Session(2, PathA, 0.5f),
            Session(3, PathB, 1.0f),
            Session(4, PathB, 0.5f),
        };
        fixture.Service.ApplyDucking(firstPoll, Settings, "VoiceDuck.exe");

        fixture.Repository.WasCorrupt = true;
        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 1.0f), Session(2, PathA, 0.5f) },
            Settings,
            "VoiceDuck.exe");
        fixture.Repository.WasCorrupt = false;

        fixture.Service.ApplyDucking(
            new[] { Session(3, PathB, 1.0f), Session(4, PathB, 0.5f) },
            Settings,
            "VoiceDuck.exe");

        Assert.Equal(2, BaselineDecisions(fixture).Count(message =>
            message.Contains($"identity={Device}|{PathB}", StringComparison.Ordinal)));
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
        Assert.Equal(0, fixture.Store.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_durable_obligations_fail_closed_with_order_stable_diagnostic(bool reverse)
    {
        var identity = new ApplicationAudioIdentity(Device, PathA);
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture();
        var originalState = new ApplicationVolumeState(identity, 0.8f, isDucked: true);
        fixture.Store.Add(originalState);
        var duplicates = new[]
        {
            new RestorationObligation(identity, 1.0f, RestorationStatus.Ducked, now, now),
            new RestorationObligation(identity, 0.6f, RestorationStatus.RestorePending, now, now),
        };
        fixture.Repository.Existing.AddRange(reverse ? duplicates.Reverse() : duplicates);

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.4f) },
            Settings,
            "VoiceDuck.exe");

        Assert.True(fixture.Store.TryGet(identity, out var state));
        Assert.Same(originalState, state);
        Assert.Equal(0.8f, state!.BaselineVolume);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
        Assert.Equal(
            $"BaselineDecision: identity={Device}|{PathA} candidates=[0.6,1] spread=0.39999998 outcome=rejected reason=duplicate_obligation_conflict baseline=none endpoint_relevance=unknown relevance_reason=durable_repository_validation",
            Assert.Single(BaselineDecisions(fixture)));

        fixture.Repository.Existing.Reverse();
        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.4f) },
            Settings,
            "VoiceDuck.exe");
        Assert.Single(BaselineDecisions(fixture));
    }

    [Fact]
    public void Equal_baseline_duplicate_durable_obligations_still_fail_uniqueness_contract()
    {
        var identity = new ApplicationAudioIdentity(Device, PathA);
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture();
        fixture.Repository.Existing.AddRange(
        [
            new RestorationObligation(identity, 1.0f, RestorationStatus.Ducked, now, now),
            new RestorationObligation(identity, 1.0f, RestorationStatus.Ducked, now, now),
        ]);

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.4f) },
            Settings,
            "VoiceDuck.exe");

        Assert.Equal(0, fixture.Store.Count);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
        Assert.Contains(
            "candidates=[1,1] spread=0 outcome=rejected reason=duplicate_obligation_conflict",
            Assert.Single(BaselineDecisions(fixture)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Duplicate_durable_identity_is_rejected_locally_while_valid_neighbor_is_persisted_before_write(
        bool duplicateFirst)
    {
        var duplicateIdentity = new ApplicationAudioIdentity(Device, PathA);
        var validIdentity = new ApplicationAudioIdentity(Device, PathB);
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture();
        fixture.Repository.Events = fixture.Events;
        fixture.Writer.Events = fixture.Events;
        var originalState = new ApplicationVolumeState(
            duplicateIdentity, 0.8f, isDucked: true);
        fixture.Store.Add(originalState);
        var duplicateRecords = new[]
        {
            new RestorationObligation(
                duplicateIdentity,
                1.0f,
                RestorationStatus.Ducked,
                now.AddMinutes(-2),
                now.AddMinutes(-1)),
            new RestorationObligation(
                duplicateIdentity,
                0.6f,
                RestorationStatus.RestorePending,
                now.AddMinutes(-4),
                now.AddMinutes(-3)),
        };
        fixture.Repository.Existing.AddRange(duplicateRecords);

        var sessions = duplicateFirst
            ? new[]
            {
                Session(1, PathA, 0.4f),
                Session(2, PathB, 0.8f),
                Session(3, PathB, 0.805f),
            }
            : new[]
            {
                Session(2, PathB, 0.8f),
                Session(3, PathB, 0.805f),
                Session(1, PathA, 0.4f),
            };

        fixture.Service.ApplyDucking(
            sessions,
            Settings,
            "VoiceDuck.exe");

        Assert.True(fixture.Store.TryGet(duplicateIdentity, out var duplicateState));
        Assert.Same(originalState, duplicateState);
        Assert.Equal(0.8f, duplicateState!.BaselineVolume);
        Assert.True(fixture.Store.TryGet(validIdentity, out var validState));
        Assert.Equal(0.805f, validState!.BaselineVolume);

        Assert.Equal(2, fixture.Repository.SaveCount);
        Assert.Equal(3, fixture.Repository.Saved.Count);
        Assert.Equal(duplicateRecords, fixture.Repository.Saved.Take(2));
        Assert.Same(duplicateRecords[0], fixture.Repository.Saved[0]);
        Assert.Same(duplicateRecords[1], fixture.Repository.Saved[1]);
        Assert.Equal(
            new[]
            {
                "save",
                "write",
                "write",
                "save",
            },
            fixture.Events);

        Assert.Equal(2, fixture.Writer.Calls.Count);
        Assert.All(
            fixture.Writer.Calls,
            call =>
            {
                Assert.Contains(call.Identity.ProcessId, new uint[] { 2, 3 });
                Assert.Equal(0.4025f, call.Volume);
            });
        var savedNeighbor = Assert.Single(
            fixture.Repository.Saved,
            obligation => obligation.Identity.Equals(validIdentity));
        Assert.Equal(0.805f, savedNeighbor.BaselineVolume);
        Assert.Equal(RestorationStatus.Ducked, savedNeighbor.Status);

        var duplicateDiagnostics = BaselineDecisions(fixture)
            .Where(message => message.Contains(
                "reason=duplicate_obligation_conflict",
                StringComparison.Ordinal))
            .ToList();
        Assert.Contains(
            $"identity={Device}|{PathA}",
            Assert.Single(duplicateDiagnostics));
        Assert.DoesNotContain(
            BaselineDecisions(fixture),
            message => message.Contains($"identity={Device}|{PathB}", StringComparison.Ordinal)
                && message.Contains(
                    "reason=duplicate_obligation_conflict",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_identity_remains_unchanged_when_valid_neighbor_prewrite_persistence_fails()
    {
        var duplicateIdentity = new ApplicationAudioIdentity(Device, PathA);
        var validIdentity = new ApplicationAudioIdentity(Device, PathB);
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture();
        var originalState = new ApplicationVolumeState(
            duplicateIdentity, 0.8f, isDucked: true);
        fixture.Store.Add(originalState);
        var duplicateRecords = new[]
        {
            new RestorationObligation(
                duplicateIdentity,
                1.0f,
                RestorationStatus.Ducked,
                now.AddMinutes(-2),
                now.AddMinutes(-1)),
            new RestorationObligation(
                duplicateIdentity,
                0.6f,
                RestorationStatus.RestorePending,
                now.AddMinutes(-4),
                now.AddMinutes(-3)),
        };
        fixture.Repository.Existing.AddRange(duplicateRecords);
        fixture.Repository.ThrowOnSave = true;

        fixture.Service.ApplyDucking(
            new[]
            {
                Session(2, PathB, 0.8f),
                Session(3, PathB, 0.805f),
                Session(1, PathA, 0.4f),
            },
            Settings,
            "VoiceDuck.exe");

        Assert.True(fixture.Store.TryGet(duplicateIdentity, out var duplicateState));
        Assert.Same(originalState, duplicateState);
        Assert.False(fixture.Store.TryGet(validIdentity, out _));
        Assert.Equal(duplicateRecords, fixture.Repository.Existing);
        Assert.Same(duplicateRecords[0], fixture.Repository.Existing[0]);
        Assert.Same(duplicateRecords[1], fixture.Repository.Existing[1]);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
        Assert.Contains(
            $"identity={Device}|{PathA}",
            Assert.Single(
                BaselineDecisions(fixture),
                message => message.Contains(
                    "reason=duplicate_obligation_conflict",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void Duplicate_durable_identity_absent_from_current_groups_is_bounded_until_candidates_change()
    {
        var duplicateIdentity = new ApplicationAudioIdentity(Device, PathB);
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture();
        fixture.Repository.Existing.AddRange(
        [
            new RestorationObligation(duplicateIdentity, 1.0f, RestorationStatus.Ducked, now, now),
            new RestorationObligation(duplicateIdentity, 0.6f, RestorationStatus.Ducked, now, now),
        ]);
        var currentSessions = new[]
        {
            Session(1, PathA, 1.0f),
            Session(2, PathA, 0.5f),
        };

        fixture.Service.ApplyDucking(currentSessions, Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(currentSessions, Settings, "VoiceDuck.exe");

        Assert.Single(BaselineDecisions(fixture), message =>
            message.Contains($"identity={Device}|{PathB}", StringComparison.Ordinal)
            && message.Contains("reason=duplicate_obligation_conflict", StringComparison.Ordinal));
        fixture.Repository.Existing[1] = new RestorationObligation(
            duplicateIdentity, 0.5f, RestorationStatus.Ducked, now, now);
        fixture.Service.ApplyDucking(currentSessions, Settings, "VoiceDuck.exe");

        var decisions = BaselineDecisions(fixture)
            .Where(message => message.Contains(
                "reason=duplicate_obligation_conflict",
                StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, decisions.Count);
        Assert.Contains("candidates=[0.6,1]", decisions[0]);
        Assert.Contains("candidates=[0.5,1]", decisions[1]);
        Assert.Equal(0, fixture.Store.Count);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
    }

    [Fact]
    public void Multiple_duplicate_durable_identities_log_in_deterministic_identity_order()
    {
        var identityA = new ApplicationAudioIdentity(Device, PathA);
        var identityB = new ApplicationAudioIdentity(Device, PathB);
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture();
        fixture.Repository.Existing.AddRange(
        [
            new RestorationObligation(identityB, 1.0f, RestorationStatus.Ducked, now, now),
            new RestorationObligation(identityA, 0.9f, RestorationStatus.Ducked, now, now),
            new RestorationObligation(identityB, 0.6f, RestorationStatus.Ducked, now, now),
            new RestorationObligation(identityA, 0.7f, RestorationStatus.Ducked, now, now),
        ]);

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.8f) },
            Settings,
            "VoiceDuck.exe");

        var decisions = BaselineDecisions(fixture);
        Assert.Equal(2, decisions.Count);
        Assert.Contains($"identity={Device}|{PathA}", decisions[0]);
        Assert.Contains($"identity={Device}|{PathB}", decisions[1]);
    }

    [Fact]
    public void Successful_unique_load_prunes_absent_duplicate_fingerprint_before_reappearance()
    {
        var duplicateIdentity = new ApplicationAudioIdentity(Device, PathB);
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture();
        var duplicates = new[]
        {
            new RestorationObligation(duplicateIdentity, 1.0f, RestorationStatus.Ducked, now, now),
            new RestorationObligation(duplicateIdentity, 0.6f, RestorationStatus.Ducked, now, now),
        };
        fixture.Repository.Existing.AddRange(duplicates);
        var conflictingCurrentSessions = new[]
        {
            Session(1, PathA, 1.0f),
            Session(2, PathA, 0.5f),
        };

        fixture.Service.ApplyDucking(conflictingCurrentSessions, Settings, "VoiceDuck.exe");

        fixture.Repository.Existing.Clear();
        fixture.Service.ApplyDucking(conflictingCurrentSessions, Settings, "VoiceDuck.exe");

        fixture.Repository.Existing.AddRange(duplicates);
        fixture.Service.ApplyDucking(conflictingCurrentSessions, Settings, "VoiceDuck.exe");

        Assert.Equal(2, BaselineDecisions(fixture).Count(message =>
            message.Contains($"identity={Device}|{PathB}", StringComparison.Ordinal)
            && message.Contains("reason=duplicate_obligation_conflict", StringComparison.Ordinal)));
        Assert.Equal(0, fixture.Store.Count);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Startup_duplicate_durable_obligations_fail_closed_before_deferred_restore(bool reverse)
    {
        var identity = new ApplicationAudioIdentity(Device, PathA);
        var now = DateTimeOffset.UtcNow;
        var fixture = new Fixture();
        var duplicates = new[]
        {
            new RestorationObligation(identity, 1.0f, RestorationStatus.Ducked, now, now),
            new RestorationObligation(identity, 0.6f, RestorationStatus.RestorePending, now, now),
        };
        fixture.Repository.Existing.AddRange(reverse ? duplicates.Reverse() : duplicates);

        var result = fixture.Service.LoadAndPopulateStartupState();
        fixture.Service.ApplyDeferredRestores(new[] { Session(1, PathA, 0.5f) });

        Assert.False(result.WasCorrupt);
        Assert.False(result.Saved);
        Assert.Equal(0, result.LoadedCount);
        Assert.Equal(0, fixture.Store.Count);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
        Assert.Equal(
            $"BaselineDecision: identity={Device}|{PathA} candidates=[0.6,1] spread=0.39999998 outcome=rejected reason=duplicate_obligation_conflict baseline=none endpoint_relevance=unknown relevance_reason=durable_repository_validation",
            Assert.Single(BaselineDecisions(fixture)));
    }

    private static List<string> BaselineDecisions(Fixture fixture) =>
        fixture.Logger.Messages
            .Where(message => message.StartsWith("BaselineDecision:", StringComparison.Ordinal))
            .ToList();

    private static AudioSessionInfo Session(uint pid, string path, float volume) =>
        new(new AudioSessionIdentity(pid, "App.exe", Device, $"instance-{pid}"), volume, false, path);

    private sealed class Fixture
    {
        public List<string> Events { get; } = new();
        public Writer Writer { get; } = new();
        public ApplicationVolumeStateStore Store { get; } = new();
        public Repository Repository { get; } = new();
        public TestLogger Logger { get; } = new();
        public VolumeDuckingService Service { get; }

        public Fixture()
        {
            Service = new VolumeDuckingService(
                Writer,
                new DuckingSessionClassifier(),
                Store,
                Repository,
                new EndpointSelector(),
                Logger);
        }
    }

    private sealed class EndpointSelector : IAudioEndpointSelector
    {
        public string? GetDefaultMultimediaEndpointId() => Device;
    }

    private sealed class Writer : IAudioSessionVolumeWriter
    {
        public List<(AudioSessionIdentity Identity, float Volume)> Calls { get; } = new();
        public List<string>? Events { get; set; }

        public VolumeWriteResult SetVolume(AudioSessionIdentity identity, float volume)
        {
            Events?.Add("write");
            Calls.Add((identity, volume));
            return VolumeWriteResult.Succeeded;
        }
    }

    private sealed class Repository : IRestorationObligationRepository
    {
        public List<RestorationObligation> Existing { get; } = new();
        public List<RestorationObligation> Saved { get; private set; } = new();
        public int SaveCount { get; private set; }
        public bool ThrowOnSave { get; set; }
        public bool WasCorrupt { get; set; }
        public List<string>? Events { get; set; }

        public RestorationObligationLoadResult LoadAll() => new(Existing.ToArray(), WasCorrupt);

        public void SaveAll(IReadOnlyList<RestorationObligation> obligations)
        {
            Events?.Add("save");
            if (ThrowOnSave)
                throw new InvalidOperationException("save failed");
            Saved = obligations.ToList();
            Existing.Clear();
            Existing.AddRange(obligations);
            SaveCount++;
        }

        public void DeleteAll()
        {
            Existing.Clear();
            Saved.Clear();
        }
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }
}
