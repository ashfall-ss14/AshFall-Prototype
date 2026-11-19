using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Power.Components;
using Content.Shared.Botany.Events;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Botany;

public sealed class SpaceshroomTest : GameTest
{
    [Test]
    public async Task ExtractsSporesAndHarvestsMushroomsWithFoodSolution()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var gardener = SSpawnAtPosition("MobHuman", map.GridCoords);
            var wild = SSpawnAtPosition("FoodSpaceshroom", map.GridCoords);
            var extractor = SSpawnAtPosition("SeedExtractor", map.GridCoords);
            var tray = SSpawnAtPosition("hydroponicsTray", map.GridCoords);
            SEntMan.RemoveComponent<ApcPowerReceiverComponent>(extractor);

            var extract = new InteractUsingEvent(gardener, wild, extractor, map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(extractor, extract);
            Assert.That(extract.Handled, Is.True);

            var hands = SEntMan.System<SharedHandsSystem>();
            var spores = hands.EnumerateHeld(gardener).First();
            Assert.That(SComp<MetaDataComponent>(spores).EntityPrototype!.ID, Is.EqualTo("SpaceshroomSeeds"));
            var sow = new PlantingSeedAttemptEvent((spores, SComp<SeedComponent>(spores)), gardener);
            SEntMan.EventBus.RaiseLocalEvent(tray, ref sow);

            var trays = SEntMan.System<PlantTraySystem>();
            Assert.That(trays.TryGetPlant(tray, out var plant), Is.True);
            Assert.That(SComp<MetaDataComponent>(plant!.Value).EntityPrototype!.ID, Is.EqualTo("SpaceshroomPlants"));
            SEntMan.System<PlantHolderSystem>().AdjustsAge(plant.Value, 12);
            SEntMan.System<PlantSystem>().UpdatePlant(plant.Value, force: true);

            SEntMan.System<PlantHarvestSystem>().DoHarvest(plant.Value, gardener);
            var harvested = SEntMan.EntityQuery<MetaDataComponent>()
                .Where(meta => meta.Owner != wild && meta.EntityPrototype?.ID == "FoodSpaceshroom")
                .ToArray();
            Assert.That(harvested, Has.Length.EqualTo(3));
            Assert.That(trays.TryGetPlant(tray, out var remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(plant));

            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            foreach (var mushroom in harvested)
            {
                Assert.That(solutions.TryGetSolution(mushroom.Owner, "food", out _, out var solution), Is.True);
                Assert.That(solution!.GetTotalPrototypeQuantity("Nutriment"), Is.EqualTo(FixedPoint2.New(12)));
                Assert.That(solution.GetTotalPrototypeQuantity("Vitamin"), Is.EqualTo(FixedPoint2.New(8)));
                SEntMan.DeleteEntity(mushroom.Owner);
            }
        });
    }
}
