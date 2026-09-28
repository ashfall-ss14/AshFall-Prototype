using Content.Shared.Ashfall.Particles;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client.Ashfall.Particles.Visuals;

/// <summary>
/// Handles emitting fire particles, embers, and smoke on burning entities.
/// Inspired by Goob-Station PR #6510 and Starlight particle systems.
/// </summary>
public sealed partial class FlammableParticleSystem : EntitySystem
{
    [Dependency] private ParticleSystem _particles = default!;
    [Dependency] private AppearanceSystem _appearance = default!;

    private static readonly ProtoId<ParticleEffectPrototype> FireEffect = "AshfallFireContinuous";
    private static readonly ProtoId<ParticleEffectPrototype> SmokeEffect = "AshfallFireSmoke";
    private static readonly ProtoId<ParticleEffectPrototype> EmbersEffect = "AshfallFireEmbers";

    private const float MaxStacks = 10f;

    private sealed class FireState
    {
        public ActiveEmitter? FireEmitter;
        public ActiveEmitter? SmokeEmitter;
        public ActiveEmitter? EmbersEmitter;
        public bool OnFire;
    }

    private readonly Dictionary<EntityUid, FireState> _active = new();

    public IReadOnlyCollection<EntityUid> ActiveBurningEntities => _active.Keys;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FlammableComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<FlammableComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnAppearanceChange(Entity<FlammableComponent> ent, ref AppearanceChangeEvent args)
    {
        if (!_appearance.TryGetData(ent, FireVisuals.OnFire, out bool onFire))
            onFire = false;

        if (!_appearance.TryGetData(ent, FireVisuals.FireStacks, out float stacks))
            stacks = 0f;

        // Lazy allocation: do not track entities that are not and have not been on fire
        if (!onFire && !_active.ContainsKey(ent))
            return;

        if (!_active.TryGetValue(ent, out var state))
        {
            state = new FireState();
            _active[ent] = state;
        }

        if (onFire && !state.OnFire)
        {
            state.SmokeEmitter = _particles.CreateParticle(SmokeEffect, ent.Owner, attach: true);
            state.FireEmitter = _particles.CreateParticle(FireEffect, ent.Owner, attach: true);
            state.EmbersEmitter = _particles.CreateParticle(EmbersEffect, ent.Owner, attach: true);

            if (state.SmokeEmitter != null) state.SmokeEmitter.Intensity = 1f;
            if (state.FireEmitter != null) state.FireEmitter.Intensity = 1f;
            if (state.EmbersEmitter != null) state.EmbersEmitter.Intensity = 1f;

            state.OnFire = true;
        }
        else if (!onFire && state.OnFire)
        {
            StopState(state);
            state.OnFire = false;
            _active.Remove(ent);
        }

        if (state.OnFire && state.FireEmitter != null)
        {
            var intensity = Math.Clamp(stacks / MaxStacks * 2f, 0.8f, 2.5f);
            if (state.FireEmitter != null)
                state.FireEmitter.Intensity = intensity;
            if (state.SmokeEmitter != null)
                state.SmokeEmitter.Intensity = intensity;
            if (state.EmbersEmitter != null)
                state.EmbersEmitter.Intensity = intensity;
        }
    }

    private void OnShutdown(Entity<FlammableComponent> ent, ref ComponentShutdown args)
    {
        if (_active.Remove(ent, out var state))
            StopState(state);
    }

    private void StopState(FireState state)
    {
        if (state.FireEmitter != null)
        {
            _particles.RemoveParticle(state.FireEmitter);
            state.FireEmitter = null;
        }

        if (state.SmokeEmitter != null)
        {
            _particles.RemoveParticle(state.SmokeEmitter);
            state.SmokeEmitter = null;
        }

        if (state.EmbersEmitter != null)
        {
            _particles.RemoveParticle(state.EmbersEmitter);
            state.EmbersEmitter = null;
        }
    }
}
