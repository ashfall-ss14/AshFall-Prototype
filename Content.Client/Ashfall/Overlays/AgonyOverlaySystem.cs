using Content.Client.Camera;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DamageOverlay;
using Content.Shared.Mobs;
using Content.Shared.Ashfall;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Client.Ashfall.Overlays;

public sealed partial class AgonyOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private CameraRecoilSystem _recoil = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private AppearanceSystem _appearance = default!;

    private AgonyOverlay _overlay = default!;
    private float _lastPainLevel;
    private bool _initializedPainBaseline;
    private float _heartbeatTimer;
    private float _shockBlur;
    private float _acuteConcussion;

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
        _initializedPainBaseline = false;
        _heartbeatTimer = 0f;
        _shockBlur = 0f;
        _acuteConcussion = 0f;
        if (!_overlayManager.HasOverlay<AgonyOverlay>())
            _overlayManager.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        _lastPainLevel = 0f;
        _initializedPainBaseline = false;
        _heartbeatTimer = 0f;
        _shockBlur = 0f;
        _acuteConcussion = 0f;
        _overlay.PainIntensity = 0f;
        _overlay.ShockIntensity = 0f;
        _overlay.BlurIntensity = 0f;
        _overlay.AberrationIntensity = 0f;
        _overlay.ConcussionIntensity = 0f;
        _overlay.BloodlossIntensity = 0f;
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
            _initializedPainBaseline = false;
            _lastPainLevel = 0f;
            DecayOverlay(frameTime);
            return;
        }

        if (!_initializedPainBaseline)
        {
            _lastPainLevel = damageOverlay.PainLevel;
            _initializedPainBaseline = true;
        }
        else if (_config.GetCVar(AshfallCCVars.AgonyOverlayEnabled))
        {
            // Acute damage detection (gunshot, melee hit, blast spike)
            if (damageOverlay.PainLevel > _lastPainLevel + 0.003f)
            {
                var painDelta = damageOverlay.PainLevel - _lastPainLevel;
                _overlay.ShockIntensity = MathF.Min(1.0f, _overlay.ShockIntensity + MathF.Max(0.50f, painDelta * 6f));
                _shockBlur = MathF.Min(1.0f, _shockBlur + MathF.Max(0.55f, painDelta * 6.5f));

                if (painDelta > 0.015f)
                {
                    var kickMagnitude = Math.Clamp(painDelta * 3.5f, 0.15f, 0.85f);
                    var kick = _random.NextAngle().ToVec() * kickMagnitude;
                    _recoil.KickCamera(local, kick);
                }

                if (painDelta > 0.06f)
                {
                    _acuteConcussion = MathF.Min(1.0f, _acuteConcussion + painDelta * 2.5f);
                }
            }
        }
        _lastPainLevel = damageOverlay.PainLevel;

        if (!_config.GetCVar(AshfallCCVars.AgonyOverlayEnabled))
        {
            _overlay.ShockIntensity = 0f;
            _overlay.BlurIntensity = 0f;
            _overlay.AberrationIntensity = 0f;
            _overlay.ConcussionIntensity = 0f;
            _overlay.BloodlossIntensity = 0f;
            _overlay.CritIntensity = 0f;
            _overlay.OxygenIntensity = 0f;
            _overlay.FireIntensity = 0f;
            _overlay.PainIntensity = 0f;
            _shockBlur = 0f;
            _acuteConcussion = 0f;
            return;
        }

        if (_overlay.ShockIntensity > 0f)
        {
            _overlay.ShockIntensity = MathF.Max(0f, _overlay.ShockIntensity - frameTime * 2.8f);
        }

        if (_shockBlur > 0f)
        {
            _shockBlur = MathF.Max(0f, _shockBlur - frameTime * 2.4f);
        }

        if (_acuteConcussion > 0f)
        {
            _acuteConcussion = MathF.Max(0f, _acuteConcussion - frameTime * 1.5f);
        }

        // Heartbeat pulse wave for accommodation loss (blur) at high pain
        _heartbeatTimer += frameTime * (1.15f + 0.70f * damageOverlay.PainLevel);
        var heartCycle = _heartbeatTimer % 1.0f;
        var pulseWaveBlur = 0f;
        if (damageOverlay.PainLevel > 0.45f && damageOverlay.CurrentState == MobState.Alive)
        {
            // Systole pulse window (0.0 to 0.22 of cycle)
            if (heartCycle < 0.22f)
            {
                var beatProgress = MathF.Sin(heartCycle / 0.22f * MathF.PI);
                var painFactor = (damageOverlay.PainLevel - 0.45f) / 0.55f;
                pulseWaveBlur = beatProgress * painFactor * 0.70f;
            }
        }

        var targetBlur = Math.Clamp(_shockBlur + pulseWaveBlur, 0f, 1f);

        // Chromatic aberration intensity (scales with pain + hit shock)
        var targetAberration = 0f;
        if (damageOverlay.PainLevel > 0.02f && damageOverlay.CurrentState == MobState.Alive)
        {
            targetAberration = Math.Clamp(MathF.Pow(damageOverlay.PainLevel, 0.70f) * 0.028f + _overlay.ShockIntensity * 0.035f, 0f, 0.07f);
        }
        else if (_overlay.ShockIntensity > 0f)
        {
            targetAberration = _overlay.ShockIntensity * 0.035f;
        }

        // Non-linear organic pain curve: early damage (10-30%) is tangibly felt,
        // while high damage (50-80%) builds up intense trauma feedback.
        float targetPain = 0f;
        if (damageOverlay.PainLevel > 0.02f && damageOverlay.CurrentState == MobState.Alive)
        {
            targetPain = Math.Clamp(MathF.Pow(damageOverlay.PainLevel, 0.75f), 0f, 1f);
        }

        // Hypovolemia / Blood loss desaturation
        float targetBloodloss = 0f;
        if (TryComp<BloodstreamComponent>(local, out var bloodstream))
        {
            var bloodLevel = _bloodstream.GetBloodLevel((local, bloodstream));
            if (bloodLevel < 0.85f)
            {
                targetBloodloss = Math.Clamp((0.85f - bloodLevel) / 0.55f, 0f, 1f);
            }
        }

        // Concussion / Stamina stun / Diplopia
        float targetConcussion = _acuteConcussion;
        if (TryComp<StaminaComponent>(local, out var stamina))
        {
            if (stamina.StaminaDamage > 25f)
            {
                targetConcussion = MathF.Max(targetConcussion, Math.Clamp((stamina.StaminaDamage - 25f) / 65f, 0f, 1f));
            }
        }

        // Oxygen starvation / asphyxiation tunnel vision
        float targetOxygen = 0f;
        if (damageOverlay.OxygenLevel > 0.04f && damageOverlay.CurrentState == MobState.Alive)
        {
            targetOxygen = Math.Clamp(MathF.Pow(damageOverlay.OxygenLevel, 0.80f), 0f, 1f);
        }

        // Flammable on fire status
        float targetFire = 0f;
        if (damageOverlay.CurrentState != MobState.Dead)
        {
            bool onFire = false;
            float fireStacks = 0f;
            float maxStacks = 10f;

            if (TryComp<FlammableComponent>(local, out var flammable))
            {
                onFire = flammable.OnFire;
                fireStacks = flammable.FireStacks;
                maxStacks = MathF.Max(1f, flammable.MaximumFireStacks);
            }
            else if (_appearance.TryGetData<bool>(local, FireVisuals.OnFire, out var appOnFire) && appOnFire)
            {
                onFire = true;
                if (_appearance.TryGetData<float>(local, FireVisuals.FireStacks, out var appStacks))
                    fireStacks = appStacks;
            }

            if (onFire)
            {
                targetFire = Math.Clamp(0.5f + (fireStacks / maxStacks) * 0.5f, 0.4f, 1.0f);
            }
        }

        float targetCrit = 0f;
        if (damageOverlay.CurrentState == MobState.Critical)
        {
            targetCrit = Math.Clamp(0.65f + damageOverlay.CritLevel * 0.35f, 0f, 1f);
        }

        var lerpSpeed = MathF.Min(1f, 5.0f * frameTime);
        _overlay.PainIntensity = MathHelper.Lerp(_overlay.PainIntensity, targetPain, lerpSpeed);
        _overlay.BlurIntensity = MathHelper.Lerp(_overlay.BlurIntensity, targetBlur, MathF.Min(1f, 8.0f * frameTime));
        _overlay.AberrationIntensity = MathHelper.Lerp(_overlay.AberrationIntensity, targetAberration, lerpSpeed);
        _overlay.ConcussionIntensity = MathHelper.Lerp(_overlay.ConcussionIntensity, targetConcussion, lerpSpeed);
        _overlay.BloodlossIntensity = MathHelper.Lerp(_overlay.BloodlossIntensity, targetBloodloss, lerpSpeed);
        _overlay.OxygenIntensity = MathHelper.Lerp(_overlay.OxygenIntensity, targetOxygen, lerpSpeed);
        _overlay.FireIntensity = MathHelper.Lerp(_overlay.FireIntensity, targetFire, lerpSpeed * 1.5f);
        _overlay.CritIntensity = MathHelper.Lerp(_overlay.CritIntensity, targetCrit, lerpSpeed);
    }

    private void DecayOverlay(float deltaSeconds)
    {
        if (_overlay.ShockIntensity <= 0.001f && _overlay.PainIntensity <= 0.001f &&
            _overlay.BlurIntensity <= 0.001f && _overlay.AberrationIntensity <= 0.001f &&
            _overlay.ConcussionIntensity <= 0.001f && _overlay.BloodlossIntensity <= 0.001f &&
            _overlay.CritIntensity <= 0.001f && _overlay.OxygenIntensity <= 0.001f && _overlay.FireIntensity <= 0.001f)
        {
            _overlay.ShockIntensity = 0f;
            _overlay.PainIntensity = 0f;
            _overlay.BlurIntensity = 0f;
            _overlay.AberrationIntensity = 0f;
            _overlay.ConcussionIntensity = 0f;
            _overlay.BloodlossIntensity = 0f;
            _overlay.CritIntensity = 0f;
            _overlay.OxygenIntensity = 0f;
            _overlay.FireIntensity = 0f;
            return;
        }

        var decay = deltaSeconds * 3f;
        _overlay.ShockIntensity = MathF.Max(0f, _overlay.ShockIntensity - decay);
        _overlay.PainIntensity = MathF.Max(0f, _overlay.PainIntensity - decay);
        _overlay.BlurIntensity = MathF.Max(0f, _overlay.BlurIntensity - decay * 1.5f);
        _overlay.AberrationIntensity = MathF.Max(0f, _overlay.AberrationIntensity - decay * 0.1f);
        _overlay.ConcussionIntensity = MathF.Max(0f, _overlay.ConcussionIntensity - decay);
        _overlay.BloodlossIntensity = MathF.Max(0f, _overlay.BloodlossIntensity - decay);
        _overlay.CritIntensity = MathF.Max(0f, _overlay.CritIntensity - decay);
        _overlay.OxygenIntensity = MathF.Max(0f, _overlay.OxygenIntensity - decay);
        _overlay.FireIntensity = MathF.Max(0f, _overlay.FireIntensity - decay);
    }
}
