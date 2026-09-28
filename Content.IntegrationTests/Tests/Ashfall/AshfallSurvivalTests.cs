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

            // Deal 100 brute damage ignoring resistances (below 120 destruction threshold)
            var damageSpec = new DamageSpecifier(protoMan.Index(BluntDamage), FixedPoint2.New(100));
            server.System<DamageableSystem>().TryChangeDamage(entity, damageSpec, ignoreResistances: true);

            // 100 / 50 = 2 materials needed
            Assert.That(complex.LeftToInsert, Is.EqualTo(2));

            entMan.DeleteEntity(entity);
        });

        await pair.CleanReturnAsync();
    }
}
