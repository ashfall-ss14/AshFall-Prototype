using System.Numerics;
using Content.Client.Lobby.UI.ProfileEditorControls;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.Ashfall.CharacterGen.UI;

/// <summary>
///     Profile preview framed as a bust "personnel file photograph": the sprite is zoomed so that
///     only the top <see cref="PortraitFraction"/> of the body (head, neck, shoulders) fills the
///     control. The aspect ratio is preserved, everything below is simply clipped.
///     The zoom is computed from the actual sprite bounds, so it works consistently across species.
/// </summary>
public sealed class ProfilePortraitSpriteView : ProfilePreviewSpriteView
{
    /// <summary>
    ///     How much of the sprite height (from the top) the control should frame.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float PortraitFraction { get; set; } = 0.5f;

    /// <summary>
    ///     Extra zoom multiplier on top of the height framing; below 1.0 draws the bust smaller
    ///     than the control instead of filling it.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float PortraitScale { get; set; } = 1f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float VerticalOffset { get; set; }

    [ViewVariables(VVAccess.ReadWrite)]
    public float HorizontalOffset { get; set; }

    [ViewVariables(VVAccess.ReadWrite)]
    public ShaderInstance? MorphShader { get; set; }

    [ViewVariables(VVAccess.ReadWrite)]
    public float MorphProgress { get; set; } = 1.0f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float MorphTime { get; set; }

    [ViewVariables(VVAccess.ReadWrite)]
    public bool IsSilhouette { get; set; }

    private EntityUid? _boundsDummy;
    private Box2 _lastBounds;

    protected override void Draw(IRenderHandle renderHandle)
    {
        if (!EntMan.TryGetComponent(PreviewDummy, out SpriteComponent? sprite) ||
            !EntMan.TryGetComponent(PreviewDummy, out TransformComponent? xform))
            return;

        SpriteSystem ??= EntMan.System<SpriteSystem>();
        var transformSystem = EntMan.System<SharedTransformSystem>();

        // Ensure the sprite is animated despite possibly not being visible in any viewport.
        SpriteSystem.ForceUpdate(PreviewDummy);

        var bounds = sprite.CalculateRotatedBoundingBox(default, Angle.Zero, EyeRotation).CalcBoundingBox();
        if (bounds.Height <= 0f)
            return;

        // Layers (organ visuals, markings, job clothes) keep settling for a frame or two after
        // LoadPreview respawns the dummy; hold the previous crop until the bounds stabilize.
        if (_boundsDummy != PreviewDummy)
        {
            _boundsDummy = PreviewDummy;
        }
        else if (!_lastBounds.EqualsApprox(bounds, 0.5f))
        {
            bounds = _lastBounds;
        }
        _lastBounds = bounds;

        var ppm = EyeManager.PixelsPerMeter;

        // Keep the portrait crop fixed by height so every dossier uses the same close framing.
        var zoom = PortraitScale * PixelSize.Y / (ppm * UIScale * bounds.Height * PortraitFraction);
        var scale = new Vector2(zoom, zoom);

        // Center the sprite's actual bounding box horizontally (its origin can be off-center for
        // some species) and pin its top to the control's top: everything below the framed area is
        // clipped by the control. Anchoring via the box top (not via height/2) keeps the head in
        // place even when gear shifts the bounding box.
        var position = new Vector2(
            PixelSize.X / 2f - bounds.Center.X * ppm * zoom * UIScale + HorizontalOffset * UIScale,
            VerticalOffset * UIScale + bounds.Top * ppm * zoom * UIScale);

        var world = renderHandle.DrawingHandleWorld;
        var oldModulate = world.Modulate;
        // The screen handle was pre-multiplied by the manager with the accumulated tree
        // modulate (panel fades etc.); the world handle does not inherit it, so entities
        // drawn here must take their tint from the screen handle instead of own values.
        var tint = renderHandle.DrawingHandleScreen.Modulate;
        // Dim-fade: darken RGB by the fade alpha and keep the sprite opaque, so every layer
        // (body, clothes, hair) dims as one silhouette instead of revealing layers behind it.
        // The dim bottoms out at the panel tone, not pure black, to blend into the dossier.
        const float FloorR = 0x14 / 255f, FloorG = 0x15 / 255f, FloorB = 0x16 / 255f;
        var fade = tint.A;
        tint.R = tint.R * fade + FloorR * (1f - fade);
        tint.G = tint.G * fade + FloorG * (1f - fade);
        tint.B = tint.B * fade + FloorB * (1f - fade);
        tint.A = 1;
        world.Modulate *= tint;

        if (MorphShader != null)
        {
            MorphShader.SetParameter("progress", MorphProgress);
            MorphShader.SetParameter("time", MorphTime);
            MorphShader.SetParameter("isSilhouette", IsSilhouette ? 1.0f : 0.0f);
            world.UseShader(MorphShader);
        }

        renderHandle.DrawEntity(PreviewDummy, position, scale, null, EyeRotation, OverrideDirection, sprite, xform, transformSystem);

        if (MorphShader != null)
        {
            world.UseShader(null);
        }
        world.Modulate = oldModulate;
    }
}
