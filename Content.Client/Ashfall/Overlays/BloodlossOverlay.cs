// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Ashfall;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Ashfall.Overlays;

public sealed partial class BloodlossOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "GreyscaleFullscreen";

    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IConfigurationManager _config = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => !_config.GetCVar(AshfallCCVars.AgonyOverlayEnabled) && Intensity > 0.005f;
    private readonly ShaderInstance _greyscaleShader;

    public float Intensity { get; set; }

    public BloodlossOverlay()
    {
        IoCManager.InjectDependencies(this);
        _greyscaleShader = _prototypeManager.Index(Shader).InstanceUnique();
        ZIndex = 10;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // Handled by AgonyOverlay when cinematic damage shaders are active
        if (_config.GetCVar(AshfallCCVars.AgonyOverlayEnabled) || Intensity <= 0.005f)
            return false;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || Intensity <= 0.005f)
            return;

        var handle = args.WorldHandle;
        _greyscaleShader.SetParameter("intensity", Intensity);
        _greyscaleShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        handle.UseShader(_greyscaleShader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
