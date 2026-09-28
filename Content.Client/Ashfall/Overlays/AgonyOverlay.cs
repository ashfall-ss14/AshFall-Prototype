using Content.Shared.Ashfall;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Ashfall.Overlays;

public sealed partial class AgonyOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> AgonyShaderProto = "AshfallAgony";

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IConfigurationManager _config = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture =>
        _config.GetCVar(AshfallCCVars.AgonyOverlayEnabled) &&
        (PainIntensity > 0.005f || ShockIntensity > 0.005f || BlurIntensity > 0.005f ||
         AberrationIntensity > 0.001f || ConcussionIntensity > 0.005f || BloodlossIntensity > 0.005f ||
         CritIntensity > 0.005f || OxygenIntensity > 0.005f || FireIntensity > 0.005f);

    private readonly ShaderInstance _shader;

    public float PainIntensity;
    public float ShockIntensity;
    public float BlurIntensity;
    public float AberrationIntensity;
    public float ConcussionIntensity;
    public float BloodlossIntensity;
    public float CritIntensity;
    public float OxygenIntensity;
    public float FireIntensity;

    public AgonyOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index(AgonyShaderProto).InstanceUnique();
        ZIndex = 25;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_config.GetCVar(AshfallCCVars.AgonyOverlayEnabled))
            return false;

        if (PainIntensity <= 0.005f && ShockIntensity <= 0.005f && BlurIntensity <= 0.005f &&
            AberrationIntensity <= 0.001f && ConcussionIntensity <= 0.005f && BloodlossIntensity <= 0.005f &&
            CritIntensity <= 0.005f && OxygenIntensity <= 0.005f && FireIntensity <= 0.005f)
            return false;

        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return false;

        return args.Viewport.Eye == eyeComp.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("PainIntensity", PainIntensity);
        _shader.SetParameter("ShockIntensity", ShockIntensity);
        _shader.SetParameter("BlurIntensity", BlurIntensity);
        _shader.SetParameter("AberrationIntensity", AberrationIntensity);
        _shader.SetParameter("ConcussionIntensity", ConcussionIntensity);
        _shader.SetParameter("BloodlossIntensity", BloodlossIntensity);
        _shader.SetParameter("CritIntensity", CritIntensity);
        _shader.SetParameter("OxygenIntensity", OxygenIntensity);
        _shader.SetParameter("FireIntensity", FireIntensity);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
