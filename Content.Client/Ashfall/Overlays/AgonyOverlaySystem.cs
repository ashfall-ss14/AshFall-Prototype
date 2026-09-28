using Content.Client.Camera;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DamageOverlay;
using Content.Shared.Mobs;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Client.Ashfall.Overlays;

public sealed partial class AgonyOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly CameraRecoilSystem _recoil = default!;

    private AgonyOverlay _overlay = default!;
    private float _lastPainLevel;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new AgonyOverlay();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);

        if (_player.LocalEntity != null)
        {
            _overlayManager.AddOverlay(_overlay);
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay(_overlay);
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        _lastPainLevel = 0f;
        if (!_overlayManager.HasOverlay<AgonyOverlay>())
            _overlayManager.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        _lastPainLevel = 0f;
        _overlay.PainIntensity = 0f;
        _overlay.ShockIntensity = 0f;
        _overlay.CritIntensity = 0f;
        _overlay.OxygenIntensity = 0f;
        _overlay.FireIntensity = 0f;
        _overlayManager.RemoveOverlay(_overlay);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } local || !TryComp<DamageOverlayComponent>(local, out var damageOverlay))
        {
            _lastPainLevel = 0f;
            DecayOverlay(frameTime);
            return;
        }

        // Acute damage detection (gunshot, melee hit, blast spike)
        if (damageOverlay.PainLevel > _lastPainLevel + 0.003f)
        {
            var painDelta = damageOverlay.PainLevel - _lastPainLevel;
            _overlay.ShockIntensity = MathF.Min(1.0f, _overlay.ShockIntensity + MathF.Max(0.4f, painDelta * 5f));

            if (painDelta > 0.015f)
            {
                var kickMagnitude = Math.Clamp(painDelta * 3.5f, 0.15f, 0.85f);
                var kick = _random.NextAngle().ToVec() * kickMagnitude;
                _recoil.KickCamera(local, kick);
            }
        }
        _lastPainLevel = damageOverlay.PainLevel;

        if (_overlay.ShockIntensity > 0f)
        {
            _overlay.ShockIntensity = MathF.Max(0f, _overlay.ShockIntensity - frameTime * 3.2f);
        }

        // Non-linear organic pain curve: early damage (10-30%) is tangibly felt,
        // while high damage (50-80%) builds up intense trauma feedback.
        float targetPain = 0f;
        if (damageOverlay.PainLevel > 0.02f && damageOverlay.CurrentState == MobState.Alive)
        {
            targetPain = Math.Clamp(MathF.Pow(damageOverlay.PainLevel, 0.75f), 0f, 1f);
        }

        // Oxygen starvation / asphyxiation tunnel vision
        float targetOxygen = 0f;
        if (damageOverlay.OxygenLevel > 0.04f && damageOverlay.CurrentState == MobState.Alive)
        {
            targetOxygen = Math.Clamp(MathF.Pow(damageOverlay.OxygenLevel, 0.80f), 0f, 1f);
        }

        // Flammable on fire status
        float targetFire = 0f;
        if (TryComp<FlammableComponent>(local, out var flammable) && flammable.OnFire && damageOverlay.CurrentState != MobState.Dead)
        {
            targetFire = Math.Clamp(0.5f + (flammable.FireStacks / MathF.Max(1f, flammable.MaximumFireStacks)) * 0.5f, 0.4f, 1.0f);
        }

        float targetCrit = 0f;
        if (damageOverlay.CurrentState == MobState.Critical)
        {
            targetCrit = Math.Clamp(0.65f + damageOverlay.CritLevel * 0.35f, 0f, 1f);
        }

        var lerpSpeed = MathF.Min(1f, 5.0f * frameTime);
        _overlay.PainIntensity = MathHelper.Lerp(_overlay.PainIntensity, targetPain, lerpSpeed);
        _overlay.OxygenIntensity = MathHelper.Lerp(_overlay.OxygenIntensity, targetOxygen, lerpSpeed);
        _overlay.FireIntensity = MathHelper.Lerp(_overlay.FireIntensity, targetFire, lerpSpeed * 1.5f);
        _overlay.CritIntensity = MathHelper.Lerp(_overlay.CritIntensity, targetCrit, lerpSpeed);
    }

    private void DecayOverlay(float deltaSeconds)
    {
        if (_overlay.ShockIntensity <= 0.001f && _overlay.PainIntensity <= 0.001f &&
            _overlay.CritIntensity <= 0.001f && _overlay.OxygenIntensity <= 0.001f && _overlay.FireIntensity <= 0.001f)
        {
            _overlay.ShockIntensity = 0f;
            _overlay.PainIntensity = 0f;
            _overlay.CritIntensity = 0f;
            _overlay.OxygenIntensity = 0f;
            _overlay.FireIntensity = 0f;
            return;
        }

        var decay = deltaSeconds * 3f;
        _overlay.ShockIntensity = MathF.Max(0f, _overlay.ShockIntensity - decay);
        _overlay.PainIntensity = MathF.Max(0f, _overlay.PainIntensity - decay);
        _overlay.CritIntensity = MathF.Max(0f, _overlay.CritIntensity - decay);
        _overlay.OxygenIntensity = MathF.Max(0f, _overlay.OxygenIntensity - decay);
        _overlay.FireIntensity = MathF.Max(0f, _overlay.FireIntensity - decay);
    }
}
