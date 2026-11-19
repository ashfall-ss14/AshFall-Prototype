using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Ashfall.CharacterGen.UI;

/// <summary>
///     Animated CRT backdrop for the lifepath screens. A single fullscreen quad drawn with
///     a procedural shader (scanlines, grain, slow shimmer); nothing is captured from the
///     screen, so UI content above it is never distorted.
/// </summary>
public sealed class AshfallDossierBackground : PanelContainer
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "AshfallDossierBackground";

    private readonly ShaderInstance _shader;

    public AshfallDossierBackground()
    {
        MouseFilter = MouseFilterMode.Ignore;
        _shader = IoCManager.Resolve<IPrototypeManager>().Index(ShaderId).InstanceUnique();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = new UIBox2(0f, 0f, PixelSize.X, PixelSize.Y);
        handle.UseShader(_shader);
        handle.DrawRect(box, Color.White);
        handle.UseShader(null);
    }
}
