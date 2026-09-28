// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Ashfall;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Ashfall.Combat.Concussion;

public sealed partial class ConcussionOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ConcussionShaderProto = "AshfallConcussion";

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => !_config.GetCVar(AshfallCCVars.AgonyOverlayEnabled) && ConcussionIntensity > 0.005f;

    private readonly ShaderInstance _shader;

    public float ConcussionIntensity;

    public ConcussionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index(ConcussionShaderProto).InstanceUnique();
        ZIndex = 24;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // Handled by AgonyOverlay when cinematic damage shaders are active
        if (_config.GetCVar(AshfallCCVars.AgonyOverlayEnabled) || ConcussionIntensity <= 0.005f)
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
        _shader.SetParameter("ConcussionIntensity", ConcussionIntensity);
        _shader.SetParameter("TIME", (float)_timing.RealTime.TotalSeconds);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
