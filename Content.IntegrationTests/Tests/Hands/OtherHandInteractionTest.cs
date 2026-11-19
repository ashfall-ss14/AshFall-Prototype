using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Input;
using Content.Shared.Paper;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Hands;

public sealed class OtherHandInteractionTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task KeyUsesHeldItemWithoutSwitchingHands()
    {
        var stamp = await PlaceInHands("RubberStampCaptain");
        var active = Hands!.ActiveHandId!;
        var other = HandSys.EnumerateHands((SPlayer, Hands)).Skip(1).Single();

        await PressKey(ContentKeyFunctions.InteractWithOtherHand);
        Assert.That(Hands.ActiveHandId, Is.EqualTo(active));

        EntityUid paper = default;
        await Server.WaitAssertion(() =>
        {
            paper = SSpawnAtPosition("Paper", SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(HandSys.TryPickup(SPlayer, paper, other), Is.True);
        });
        await RunTicks(3);

        await PressKey(ContentKeyFunctions.InteractWithOtherHand);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<PaperComponent>(paper).StampedBy, Has.Count.EqualTo(1));
            Assert.That(Hands.ActiveHandId, Is.EqualTo(active));
            Assert.That(HandSys.TryDrop(SPlayer, SEntMan.GetEntity(stamp)), Is.True);
        });
        await RunTicks(3);

        await PressKey(ContentKeyFunctions.InteractWithOtherHand);
        Assert.That(HandSys.GetActiveItem((SPlayer, Hands)), Is.EqualTo(paper));
        Assert.That(Hands.ActiveHandId, Is.EqualTo(active));
    }
}
