using System.Numerics;
using Content.Client.Shuttles.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Shuttle;

public sealed class RadarConsoleTest : GameTest
{
    [TestCase("HandHeldMassScanner", true)]
    [TestCase("ComputerRadar", false)]
    public async Task SetsCameraRotationOnlyForHandheldScanner(string prototype, bool rotateWithEye)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var radar = SSpawnAtPosition(prototype, map.GridCoords);
            var ui = SEntMan.System<SharedUserInterfaceSystem>();
            Assert.That(ui.TryGetUiState<NavBoundUserInterfaceState>(radar, RadarConsoleUiKey.Key, out var state), Is.True);
            Assert.That(state!.State.RotateWithEye, Is.EqualTo(rotateWithEye));
            Assert.That(state.State.RotateWithEntity, Is.EqualTo(!rotateWithEye));
        });
    }

    [Test]
    [RunOnSide(Side.Client)]
    public void CameraRotationChangesHandheldRadarCoordinates()
    {
        var origin = new EntityCoordinates(CSpawn(null), Vector2.Zero);
        using var radar = new ShuttleNavControl();
        var state = new NavInterfaceState(256, CEntMan.GetNetCoordinates(origin), Angle.Zero, new())
        {
            RotateWithEntity = false,
            RotateWithEye = true,
        };
        radar.UpdateState(state);

        var eye = Client.ResolveDependency<IEyeManager>().CurrentEye;
        var originalRotation = eye.Rotation;
        var point = new ScreenCoordinates(radar.SetSize / 2f + new Vector2(32, 0), WindowId.Invalid);
        try
        {
            eye.Rotation = Angle.Zero;
            var east = radar.GetMouseCoordinates(point).Position;
            eye.Rotation = Angle.FromDegrees(90);
            var south = radar.GetMouseCoordinates(point).Position;

            Assert.That(east.X, Is.GreaterThan(0));
            Assert.That(south.X, Is.EqualTo(0).Within(0.001f));
            Assert.That(south.Y, Is.EqualTo(-east.X).Within(0.001f));
        }
        finally
        {
            eye.Rotation = originalRotation;
        }
    }
}
