using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.DoAfter;
using Content.Shared.Nutrition.EntitySystems;

namespace Content.IntegrationTests.Tests.Nutrition;

public sealed class IngestionTest : GameTest
{
    [Test]
    public async Task StartingAnotherMealCancelsTheFirst()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var eater = SSpawnAtPosition("MobHuman", map.GridCoords);
            var apple = SSpawnAtPosition("FoodApple", map.GridCoords);
            var banana = SSpawnAtPosition("FoodBanana", map.GridCoords);
            var ingestion = SEntMan.System<IngestionSystem>();

            Assert.That(ingestion.TryIngest(eater, apple), Is.True);
            var first = SComp<DoAfterComponent>(eater).DoAfters.Values.Single();

            Assert.That(ingestion.TryIngest(eater, banana), Is.False);
            Assert.That(first.Cancelled, Is.True);
            Assert.That(ingestion.TryIngest(eater, banana), Is.True);
        });
    }
}
