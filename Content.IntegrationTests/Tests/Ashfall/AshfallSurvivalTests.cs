using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Ashfall.Barricade;
using Content.Shared.Ashfall.ComplexRepairable;
using Content.Shared.Ashfall.LockPick;
using Content.Shared.Ashfall.LockPick.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Lock;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Ashfall;

[TestFixture]
public sealed class AshfallSurvivalTests : GameTest
{
    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    [Test]
    public async Task TestLockpickUnlocksTarget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var lockSys = server.System<LockSystem>();

            var target = entMan.SpawnEntity(null, Robust.Shared.Map.MapCoordinates.Nullspace);
            var lockComp = entMan.AddComponent<LockComponent>(target);
            lockSys.Lock(target, null, lockComp);

            var targetLockPick = entMan.AddComponent<TargetLockPickComponent>(target);
            targetLockPick.Chance = 1.0f;
            targetLockPick.Time = 0.0f;

            Assert.That(lockComp.Locked, Is.True);

            // Raise success event
            var ev = new LockPickSuccessEvent(target);
            entMan.EventBus.RaiseLocalEvent(target, ref ev);

            Assert.That(lockComp.Locked, Is.False);
            Assert.That(ev.Success, Is.True);

            entMan.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestBarricadeComponentProperties()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;

            var entity = entMan.SpawnEntity(null, Robust.Shared.Map.MapCoordinates.Nullspace);
            var barricade = entMan.AddComponent<BarricadeComponent>(entity);
            barricade.MinHitChance = 0.0f;
            barricade.MaxHitChance = 0.75f;
            barricade.MinDistance = 1.5f;
            barricade.MaxDistance = 12.0f;

            Assert.Multiple(() =>
            {
                Assert.That(barricade.MinHitChance, Is.EqualTo(0.0f));
                Assert.That(barricade.MaxHitChance, Is.EqualTo(0.75f));
                Assert.That(barricade.MinDistance, Is.EqualTo(1.5f));
                Assert.That(barricade.MaxDistance, Is.EqualTo(12.0f));
            });

            entMan.DeleteEntity(entity);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestComplexRepairableMaterialThreshold()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var protoMan = server.ResolveDependency<IPrototypeManager>();

            var entity = entMan.SpawnEntity("AshfallBarricadeWooden", Robust.Shared.Map.MapCoordinates.Nullspace);
            var complex = entMan.GetComponent<ComplexRepairableComponent>(entity);
            complex.MaterialRepairTreshold = 50;
            complex.LeftToInsert = 0;

            Assert.That(complex.LeftToInsert, Is.EqualTo(0));

            // Deal small hits to verify accumulation (30 damage + 30 damage)
            var smallHit = new DamageSpecifier(protoMan.Index(BluntDamage), FixedPoint2.New(30));
            server.System<DamageableSystem>().TryChangeDamage(entity, smallHit, ignoreResistances: true);
            Assert.That(complex.LeftToInsert, Is.EqualTo(0));
            Assert.That(complex.AccumulatedDamage, Is.EqualTo(FixedPoint2.New(30)));

            server.System<DamageableSystem>().TryChangeDamage(entity, smallHit, ignoreResistances: true);
            Assert.That(complex.LeftToInsert, Is.EqualTo(1));
            Assert.That(complex.AccumulatedDamage, Is.EqualTo(FixedPoint2.New(10)));

            // Deal another 40 damage: 10 + 40 = 50 -> 1 more needed, 0 remainder
            var secondHit = new DamageSpecifier(protoMan.Index(BluntDamage), FixedPoint2.New(40));
            server.System<DamageableSystem>().TryChangeDamage(entity, secondHit, ignoreResistances: true);
            Assert.That(complex.LeftToInsert, Is.EqualTo(2));
            Assert.That(complex.AccumulatedDamage, Is.EqualTo(FixedPoint2.Zero));

            entMan.DeleteEntity(entity);
        });

        await pair.CleanReturnAsync();
    }
}
