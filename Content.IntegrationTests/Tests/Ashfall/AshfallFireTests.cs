using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Ashfall.Fire.Systems;
using Content.Shared.Ashfall.Fire;
using Content.Shared.Ashfall.Fire.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Ashfall;

[TestFixture]
public sealed class AshfallFireTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> EthanolProto = "Ethanol";
    private static readonly ProtoId<ReagentPrototype> WaterProto = "Water";

    [Test]
    public async Task TestReagentFlammabilityAndCombustion()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var protoMan = server.ResolveDependency<IPrototypeManager>();

            Assert.Multiple(() =>
            {
                Assert.That(protoMan.TryIndex(EthanolProto, out var ethanol), Is.True);
                Assert.That(ethanol!.Flammability, Is.GreaterThan(0));

                Assert.That(protoMan.TryIndex(WaterProto, out var water), Is.True);
                Assert.That(water!.Flammability, Is.EqualTo(0));
            });

            var solution = new Solution();
            solution.AddReagent(EthanolProto, FixedPoint2.New(10));
            solution.AddReagent(WaterProto, FixedPoint2.New(10));

            var flammability = solution.GetSolutionFlammability(protoMan);
            Assert.That(flammability, Is.GreaterThan(0));

            solution.BurnFlammableReagents(0.25f, protoMan);
            Assert.That(solution.GetTotalPrototypeQuantity(EthanolProto), Is.EqualTo(FixedPoint2.New(2.5)));
            Assert.That(solution.GetTotalPrototypeQuantity(WaterProto), Is.EqualTo(FixedPoint2.New(10)));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestSolidFuelComponents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;

            var entity = entMan.SpawnEntity(null, Robust.Shared.Map.MapCoordinates.Nullspace);
            var comp = entMan.AddComponent<SolidFuelComponent>(entity);
            comp.IgnitionTime = 60f;
            comp.BurnTime = 45f;
            comp.Exposure = 0f;

            Assert.That(comp.Exposure, Is.EqualTo(0f));

            comp.Exposure = 30f;
            Assert.That(comp.Exposure / comp.IgnitionTime, Is.EqualTo(0.5f));

            entMan.DeleteEntity(entity);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestFlammableActiveFiresTracking()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var flammableSys = server.System<Content.Server.Atmos.EntitySystems.FlammableSystem>();

            var entity = entMan.SpawnEntity(null, Robust.Shared.Map.MapCoordinates.Nullspace);
            entMan.AddComponent<AppearanceComponent>(entity);
            var comp = entMan.AddComponent<Content.Shared.Atmos.Components.FlammableComponent>(entity);
            comp.FireStacks = 5f;

            Assert.That(flammableSys.ActiveFires.Contains(entity), Is.False);

            flammableSys.Ignite(entity, entity, comp);
            Assert.That(flammableSys.ActiveFires.Contains(entity), Is.True);
            Assert.That(comp.OnFire, Is.True);

            flammableSys.TryExtinguish((entity, comp));
            Assert.That(flammableSys.ActiveFires.Contains(entity), Is.False);
            Assert.That(comp.OnFire, Is.False);

            entMan.DeleteEntity(entity);
        });

        await pair.CleanReturnAsync();
    }
}
