using Content.IntegrationTests.Fixtures;
using Content.Server.Ashfall.CharacterGen;
using Content.Shared.Ashfall.CharacterGen;
using Content.Shared.Ashfall.CharacterGen.Lifepath;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.IntegrationTests.Tests.Ashfall;

/// <summary>
///     Wake-up priority slots: pin candidate + job pairs, verify they survive pool rerolls,
///     verify moving a pin between slots, and that the confirmed selection keeps the pinned
///     candidate's identity instead of a pool index.
/// </summary>
[TestFixture]
public sealed class AshfallPrioritySlotTests : GameTest
{
    public override PoolSettings PoolSettings => new() { InLobby = true };

    // Pooled pairs are reused across tests and the pool system only clears itself on round
    // restart, so every test clears the player's slots before pinning into them.
    private static void ResetSlots(AshfallCharacterPoolSystem sys, NetUserId user)
    {
        var pool = sys.GetOrCreatePool(user);
        Array.Clear(pool.PrioritySlots);
        pool.ConfirmedPriorityIndex = -1;
    }

    [Test]
    public async Task PinSurvivesRerollAndConfirmUsesPinnedIdentity()
    {
        var pair = Pair;
        var server = pair.Server;
        var user = pair.Client.User!.Value;

        await server.WaitPost(() =>
            server.Resolve<IConfigurationManager>().SetCVar(Content.Shared.Ashfall.AshfallCCVars.CharacterPoolRefreshCooldown, 0f));

        var revision = -1;
        var firstId = Guid.Empty;
        var firstName = "";
        ProtoId<JobPrototype> job = default!;

        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<AshfallCharacterPoolSystem>();
            var pool = sys.GetOrCreatePool(user);
            var first = pool.Candidates[0];
            firstId = first.CandidateId;
            firstName = first.Profile.Name;
            Assert.That(first.CompatibleJobs, Is.Not.Empty);
            job = first.CompatibleJobs[0];
            revision = pool.Revision;
        });
        await pair.RunTicksSync(5);

        // Pin candidate 0 + one concrete compatible job into slot 0 (priority 1).
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<AshfallCharacterPoolSystem>();
            ResetSlots(sys, user);
            Assert.That(sys.TryPin(user, 0, firstId, job, revision), Is.True);

            // Guard rails: unknown candidate and stale revision are rejected.
            Assert.That(sys.TryPin(user, 1, Guid.NewGuid(), job, revision), Is.False);
            Assert.That(sys.TryPin(user, 1, firstId, job, revision + 100), Is.False);
            Assert.That(server.EntMan.System<AshfallCharacterPoolSystem>().GetOrCreatePool(user).PrioritySlots[1], Is.Null);

            // A Ready pin is locked: it cannot be moved out by re-pinning elsewhere, and
            // its own slot cannot be pinned over (reordering goes through MovePrioritySlot).
            Assert.That(sys.TryPin(user, 2, firstId, job, revision), Is.False);
            Assert.That(sys.GetOrCreatePool(user).PrioritySlots[0], Is.Not.Null);
            Assert.That(sys.GetOrCreatePool(user).PrioritySlots[2], Is.Null);
            Assert.That(sys.GetOrCreatePool(user).ConfirmedPriorityIndex, Is.EqualTo(0));
        });
        await pair.RunTicksSync(5);

        // Reroll via a real client refresh message: the regular pool is replaced, the pin survives.
        await pair.Client.WaitPost(() =>
            pair.Client.Resolve<INetManager>().ClientSendMessage(new MsgAshfallRequestPool { Refresh = true }));
        await PoolManager.WaitUntil(server, () =>
            server.EntMan.System<AshfallCharacterPoolSystem>().GetOrCreatePool(user).Revision > revision, maxTicks: 60);

        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<AshfallCharacterPoolSystem>();
            var pool = sys.GetOrCreatePool(user);

            Assert.That(pool.Candidates[0].CandidateId, Is.Not.EqualTo(firstId),
                "a rerolled pool mints fresh ids; the pinned person is not regenerated into it");

            var pinned = pool.PrioritySlots[0];
            Assert.That(pinned, Is.Not.Null, "pinned slot must survive a reroll");
            Assert.That(pinned!.Candidate.CandidateId, Is.EqualTo(firstId));
            Assert.That(pinned.Candidate.Profile.Name, Is.EqualTo(firstName));
            Assert.That(pinned.Job, Is.EqualTo(job));

            // Pinning is the confirmation: the first occupied slot resolves immediately and
            // keeps the pinned identity, not an index into the (now different) regular pool.
            Assert.That(pool.ConfirmedPriorityIndex, Is.EqualTo(0));
            var profile = sys.GetSelectedProfile(user);
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile!.Name, Is.EqualTo(firstName));
            Assert.That(profile.JobPriorities[job], Is.EqualTo(JobPriority.High));
            Assert.That(sys.HasCompleteSelection(user), Is.True);

            // A locked (Ready) slot rejects replacement and clearing: the pin is released
            // only by death or a played shift.
            var replacement = pool.Candidates[0];
            Assert.That(
                sys.TryPin(user, 0, replacement.CandidateId, replacement.CompatibleJobs[0], pool.Revision),
                Is.False);
            Assert.That(pool.PrioritySlots[0]!.Candidate.CandidateId, Is.EqualTo(firstId));
            Assert.That(sys.GetSelectedProfile(user)!.Name, Is.EqualTo(firstName),
                "the confirmed slot still resolves to the original pinned person");

            Assert.That(sys.ClearPrioritySlot(user, 0), Is.False);
            Assert.That(pool.PrioritySlots[0], Is.Not.Null);
            Assert.That(sys.HasCompleteSelection(user), Is.True);
        });
    }

    [Test]
    public async Task PoolOrdersHumansLeftNonHumansRight()
    {
        var pair = Pair;
        var server = pair.Server;
        var user = pair.Client.User!.Value;

        await server.WaitAssertion(() =>
        {
            var pool = server.EntMan.System<AshfallCharacterPoolSystem>().GetOrCreatePool(user);
            Assert.That(pool.Candidates, Has.Count.EqualTo(8));

            // Left column (indexes 0..3): humans only.
            for (var i = 0; i < 4; i++)
                Assert.That(pool.Candidates[i].Profile.Species, Is.EqualTo("Human"),
                    $"left column slot {i} must be a human");

            // Right column (indexes 4..7): non-humans, one candidate per species.
            var right = pool.Candidates.Skip(4).Select(c => c.Profile.Species).ToList();
            Assert.That(right, Is.All.Not.EqualTo("Human"), "right column must be non-humans");
            Assert.That(right, Is.Unique, "each non-human species appears exactly once");
        });
        await pair.RunTicksSync(5);
    }

    [Test]
    public async Task MovePrioritySlotSwapsAndReorders()
    {
        var pair = Pair;
        var server = pair.Server;
        var user = pair.Client.User!.Value;

        Guid firstId = default, secondId = default;
        ProtoId<JobPrototype> firstJob = default!, secondJob = default!;

        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<AshfallCharacterPoolSystem>();
            ResetSlots(sys, user);
            var pool = sys.GetOrCreatePool(user);
            firstId = pool.Candidates[0].CandidateId;
            secondId = pool.Candidates[1].CandidateId;
            firstJob = pool.Candidates[0].CompatibleJobs[0];
            secondJob = pool.Candidates[1].CompatibleJobs[0];

            Assert.That(sys.TryPin(user, 0, firstId, firstJob, pool.Revision), Is.True);
            Assert.That(sys.TryPin(user, 1, secondId, secondJob, pool.Revision), Is.True);
        });

        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<AshfallCharacterPoolSystem>();
            var pool = sys.GetOrCreatePool(user);

            // Occupied target: the two pins swap places, both survive.
            Assert.That(sys.MovePrioritySlot(user, 0, 1), Is.True);
            Assert.That(pool.PrioritySlots[0]!.Candidate.CandidateId, Is.EqualTo(secondId));
            Assert.That(pool.PrioritySlots[1]!.Candidate.CandidateId, Is.EqualTo(firstId));
            Assert.That(pool.ConfirmedPriorityIndex, Is.EqualTo(0));

            // Empty target: the pin moves, the source slot is freed.
            Assert.That(sys.MovePrioritySlot(user, 1, 4), Is.True);
            Assert.That(pool.PrioritySlots[1], Is.Null);
            Assert.That(pool.PrioritySlots[4]!.Candidate.CandidateId, Is.EqualTo(firstId));

            // Guard rails: no-op, moving an empty slot, out-of-range target.
            Assert.That(sys.MovePrioritySlot(user, 4, 4), Is.False);
            Assert.That(sys.MovePrioritySlot(user, 2, 3), Is.False);
            Assert.That(sys.MovePrioritySlot(user, 4, 9), Is.False);
            Assert.That(pool.PrioritySlots[4], Is.Not.Null);
        });
        await pair.RunTicksSync(5);
    }

    [Test]
    public async Task PrioritySlotCardMeasuresIdenticallyEmptyAndPinned()
    {
        var pair = Pair;
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var candidate = new AshfallCharacterCandidate
            {
                CandidateId = Guid.NewGuid(),
                Profile = HumanoidCharacterProfile.Random(),
            };
            var card = new Content.Client.Ashfall.CharacterGen.UI.AshfallPrioritySlotCard(0);

            card.Measure(new System.Numerics.Vector2(float.MaxValue, float.MaxValue));
            var emptyHeight = card.DesiredSize.Y;

            card.SetPin(new Content.Client.Ashfall.CharacterGen.AshfallClientPinnedSlot(
                0, candidate.CandidateId, candidate, new ProtoId<JobPrototype>("Passenger")));

            card.Measure(new System.Numerics.Vector2(float.MaxValue, float.MaxValue));
            var pinnedHeight = card.DesiredSize.Y;

            Assert.That(pinnedHeight, Is.EqualTo(emptyHeight),
                $"pinned slot card measures {pinnedHeight}px, empty one {emptyHeight}px: " +
                "pinned content must fit the height-locked card exactly like the empty state");
        });
        await pair.RunTicksSync(5);
    }

    [Test]
    public async Task LockedSlotsRejectTamperingUntilDeath()
    {
        var pair = Pair;
        var server = pair.Server;
        var user = pair.Client.User!.Value;

        Guid firstId = default;
        ProtoId<JobPrototype> job = default!;

        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<AshfallCharacterPoolSystem>();
            ResetSlots(sys, user);
            var pool = sys.GetOrCreatePool(user);
            firstId = pool.Candidates[0].CandidateId;
            job = pool.Candidates[0].CompatibleJobs[0];

            Assert.That(sys.TryPin(user, 0, firstId, job, pool.Revision), Is.True);
            var slot = pool.PrioritySlots[0]!;

            // An on-shift slot cannot be cleared, pinned over, or have its pin moved out.
            slot.Status = AshfallSlotStatus.OnShift;
            var second = pool.Candidates[1];
            Assert.That(sys.ClearPrioritySlot(user, 0), Is.False);
            Assert.That(sys.TryPin(user, 0, second.CandidateId, second.CompatibleJobs[0], pool.Revision), Is.False);
            Assert.That(sys.TryPin(user, 2, firstId, job, pool.Revision), Is.False);
            Assert.That(pool.PrioritySlots[0]!.Candidate.CandidateId, Is.EqualTo(firstId));
            Assert.That(pool.PrioritySlots[2], Is.Null);
            Assert.That(sys.HasCompleteSelection(user), Is.True);

            // Death of the spawned mob releases the slot and drops the confirmation so the
            // dead candidate can no longer be respawned through the selected profile.
            var mob = server.EntMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            var comp = server.EntMan.GetComponent<MobStateComponent>(mob);
            slot.SpawnedMob = mob;
            server.EntMan.EventBus.RaiseLocalEvent(mob,
                new MobStateChangedEvent(mob, comp, MobState.Alive, MobState.Dead), true);

            Assert.That(slot.Status, Is.EqualTo(AshfallSlotStatus.Dead));
            Assert.That(pool.ConfirmedPriorityIndex, Is.EqualTo(-1));
            Assert.That(sys.GetSelectedProfile(user), Is.Null);
            Assert.That(sys.HasCompleteSelection(user), Is.False);

            // A dead slot is unlocked again: clearing is allowed.
            Assert.That(sys.ClearPrioritySlot(user, 0), Is.True);
            Assert.That(pool.PrioritySlots[0], Is.Null);
        });
        await pair.RunTicksSync(5);
    }

    [Test]
    public async Task LifepathResubmitCannotReplaceReadySlot()
    {
        var pair = Pair;
        var server = pair.Server;
        var client = pair.Client;
        var user = client.User!.Value;

        // Real lifepath option ids so the server-side generation runs its normal path.
        ProtoId<AshfallLifepathOptionPrototype> origin = default!, vector = default!, flaw = default!, luggage = default!;
        await client.WaitAssertion(() =>
        {
            var protos = client.Resolve<IPrototypeManager>();
            origin = protos.EnumeratePrototypes<AshfallLifepathOptionPrototype>().First(o => o.Step == 1).ID;
            vector = protos.EnumeratePrototypes<AshfallLifepathOptionPrototype>().First(o => o.Step == 2).ID;
            flaw = protos.EnumeratePrototypes<AshfallLifepathOptionPrototype>().First(o => o.Step == 3).ID;
            luggage = protos.EnumeratePrototypes<AshfallLifepathOptionPrototype>().First(o => o.Step == 4).ID;
        });

        Guid firstId = default;
        ProtoId<JobPrototype> job = default!;
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<AshfallCharacterPoolSystem>();
            ResetSlots(sys, user);
            var pool = sys.GetOrCreatePool(user);
            firstId = pool.Candidates[0].CandidateId;
            job = pool.Candidates[0].CompatibleJobs[0];
            Assert.That(sys.TryPin(user, 0, firstId, job, pool.Revision), Is.True);
        });
        await pair.RunTicksSync(5);

        AshfallLifepathChoices MakeChoices(int slot) => new()
        {
            TargetSlotIndex = slot,
            Step1Origin = origin,
            Step2Vector = vector,
            Step3Flaw = flaw,
            Step4Luggage = luggage,
            SelectedSex = Sex.Male,
        };

        await client.WaitPost(() =>
            client.Resolve<INetManager>().ClientSendMessage(new MsgAshfallLifepathSubmit { Choices = MakeChoices(0) }));
        await client.WaitPost(() =>
            client.Resolve<INetManager>().ClientSendMessage(new MsgAshfallLifepathSubmit { Choices = MakeChoices(2) }));
        await PoolManager.WaitUntil(server, () =>
            server.EntMan.System<AshfallCharacterPoolSystem>().GetOrCreatePool(user).PrioritySlots[2] != null, maxTicks: 60);

        await server.WaitAssertion(() =>
        {
            var pool = server.EntMan.System<AshfallCharacterPoolSystem>().GetOrCreatePool(user);
            Assert.That(pool.PrioritySlots[0]!.Candidate.CandidateId, Is.EqualTo(firstId),
                "the Ready pin must survive a lifepath resubmission");
            Assert.That(pool.PrioritySlots[0]!.Status, Is.EqualTo(AshfallSlotStatus.Ready));
            Assert.That(pool.PrioritySlots[2], Is.Not.Null,
                "an empty slot must still accept a valid lifepath submission");
            Assert.That(pool.ConfirmedPriorityIndex, Is.EqualTo(2));
        });
    }
}
