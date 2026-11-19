using Content.IntegrationTests.Fixtures;
using Content.Shared.Movement.Components;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Movement;

public sealed class KnockdownTest : GameTest
{
    [Test]
    public async Task OnlyServerStandsAutomatically()
    {
        await Server.WaitAssertion(() =>
        {
            var entity = SSpawn(null);
            MakeMovableAndKnockedDown(SEntMan, entity);

            SEntMan.System<SharedStunSystem>().Update(0);
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(entity), Is.False);
            SEntMan.DeleteEntity(entity);
        });

        await Client.WaitAssertion(() =>
        {
            var entity = CSpawn(null);
            MakeMovableAndKnockedDown(CEntMan, entity);

            var stun = CEntMan.System<SharedStunSystem>();
            stun.Update(0);
            Assert.That(CEntMan.HasComponent<KnockedDownComponent>(entity), Is.True);
            Assert.That(stun.TryStanding(entity), Is.True);
            Assert.That(CEntMan.HasComponent<KnockedDownComponent>(entity), Is.False);
            CEntMan.DeleteEntity(entity);
        });
    }

    private static void MakeMovableAndKnockedDown(IEntityManager entities, EntityUid entity)
    {
        var mover = entities.AddComponent<InputMoverComponent>(entity);
        entities.System<MetaDataSystem>().SetEntityPaused(entity, false);
        entities.AddComponent<KnockedDownComponent>(entity);
        mover.CanMove = true;
    }
}
