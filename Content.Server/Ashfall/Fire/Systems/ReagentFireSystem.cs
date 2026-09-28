using System.Numerics;
using Content.Server.Ashfall.Fire.Components;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Decals;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Ashfall.Fire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.IgnitionSource;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.StepTrigger.Components;
using Content.Shared.StepTrigger.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Ashfall.Fire.Systems;

/// <summary>
/// Handles ignition, burning, atmosphere heating, and spreading of flammable reagent puddles.
/// </summary>
public sealed partial class ReagentFireSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private DecalSystem _decalSystem = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedPopupSystem _popups = default!;
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private StepTriggerSystem _stepTrigger = default!;

    private readonly HashSet<EntityUid> _nearbySmoke = new();
    private static readonly EntProtoId FireSteamPrototype = "AshfallFireSteam";
    private static readonly ProtoId<DamageTypePrototype> StructuralDamage = "Structural";
    private static readonly ProtoId<DamageTypePrototype> HeatDamage = "Heat";
    private static readonly string[] BurntDecals = ["burnt1", "burnt2", "burnt3", "burnt4"];
    private static readonly Vector2i[] CardinalOffsets = [new(0, 1), new(0, -1), new(1, 0), new(-1, 0)];
    private static readonly AtmosDirection[] CardinalDirections = [AtmosDirection.North, AtmosDirection.South, AtmosDirection.East, AtmosDirection.West];

    private const float UpdateInterval = 0.5f;

    private readonly HashSet<EntityUid> _burningFires = [];
    private readonly List<EntityUid> _dueFires = [];
    private readonly List<Entity<ReagentPuddleFireComponent>> _exposedPuddles = [];
    private readonly List<Entity<ReagentPuddleFireComponent>> _spreadPuddles = [];
    private readonly HashSet<EntityUid> _standingEntities = [];
    private readonly HashSet<Entity<PuddleComponent>> _puddles = [];

    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<GridAtmosphereComponent> _gridAtmosQuery = default!;
    [Dependency] private EntityQuery<ReagentPuddleFireComponent> _fireQuery = default!;
    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;

    private float _puddleDamageMultiplier = 1.0f;
    private float _fireProtectionEffectiveness = 1.0f;
    private bool _volumeScalingEnabled = true;
    private float _volumeScalingReference = 20f;
    private float _volumeScalingCurve = 1.5f;
    private float _smallPuddleBurnThreshold = 5.0f;
    private float _smallPuddleBurnPercent = 0.5f;
    private float _updateAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(FlammableSystem));

        Subs.CVar(_cfg, AshfallFireCVars.PuddleFireDamageMultiplier, value => _puddleDamageMultiplier = value, true);
        Subs.CVar(_cfg, AshfallFireCVars.FireProtectionEffectiveness, value => _fireProtectionEffectiveness = value, true);
        Subs.CVar(_cfg, AshfallFireCVars.VolumeScalingEnabled, value => _volumeScalingEnabled = value, true);
        Subs.CVar(_cfg, AshfallFireCVars.VolumeScalingReference, value => _volumeScalingReference = value, true);
        Subs.CVar(_cfg, AshfallFireCVars.VolumeScalingCurve, value => _volumeScalingCurve = value, true);
        Subs.CVar(_cfg, AshfallFireCVars.SmallPuddleBurnThreshold, value => _smallPuddleBurnThreshold = value, true);
        Subs.CVar(_cfg, AshfallFireCVars.SmallPuddleBurnPercent, value => _smallPuddleBurnPercent = value, true);

        SubscribeLocalEvent<ReagentPuddleFireComponent, ComponentStartup>(OnFireStartup);
        SubscribeLocalEvent<ReagentPuddleFireComponent, ComponentShutdown>(OnFireShutdown);
        SubscribeLocalEvent<ReagentPuddleFireComponent, ExtinguishEvent>(OnPuddleExtinguish);
        SubscribeLocalEvent<ReagentPuddleFireComponent, StepTriggeredOnEvent>(OnStepTriggeredOn);
        SubscribeLocalEvent<ReagentPuddleFireComponent, StepTriggeredOffEvent>(OnStepTriggeredOff);
        SubscribeLocalEvent<PuddleComponent, ExtinguishEvent>(OnPuddleCompExtinguish);
        SubscribeLocalEvent<PuddleComponent, InteractUsingEvent>(OnPuddleInteractUsing);
        SubscribeLocalEvent<PuddleComponent, TileFireEvent>(OnPuddleTileFire);
    }

    private void OnPuddleExtinguish(Entity<ReagentPuddleFireComponent> ent, ref ExtinguishEvent args)
    {
        Extinguish(ent.Owner);
    }

    private void OnStepTriggeredOn(Entity<ReagentPuddleFireComponent> ent, ref StepTriggeredOnEvent args)
    {
        if (!ent.Comp.OnFire)
            return;

        BurnEntity(ent.Owner, args.Tripper, ent.Comp);
    }

    private void OnStepTriggeredOff(Entity<ReagentPuddleFireComponent> ent, ref StepTriggeredOffEvent args)
    {
        if (!ent.Comp.OnFire)
            return;

        BurnEntity(ent.Owner, args.Tripper, ent.Comp);
    }

    private void BurnEntity(EntityUid puddleUid, EntityUid victim, ReagentPuddleFireComponent fireComp)
    {
        if (TerminatingOrDeleted(victim))
            return;

        var effectiveFlammability = GetEffectiveFlammability(fireComp);

        if (_damageableQuery.HasComp(victim))
        {
            var damageAmount = FixedPoint2.New(4f * effectiveFlammability * _puddleDamageMultiplier);
            var damage = new DamageSpecifier();
            damage.DamageDict.Add(HeatDamage, damageAmount);

            var ignoreResistances = !_mobStateQuery.HasComp(victim);
            var appliedDamage = damage;
            if (!ignoreResistances)
            {
                var reduction = Math.Clamp(GetFireProtectionReduction(victim) * _fireProtectionEffectiveness, 0f, 1f);
                appliedDamage = damage * (1f - reduction);
            }

            _damageable.TryChangeDamage(victim, appliedDamage, ignoreResistances: ignoreResistances);
        }

        if (TryComp<FlammableComponent>(victim, out var flammable))
        {
            _flammable.AdjustFireStacks(victim, 1.5f * effectiveFlammability, flammable);
            _flammable.Ignite(victim, puddleUid, flammable);
        }

        var fireEvent = new TileFireEvent(Atmospherics.T0C + (100f * effectiveFlammability), 50f * effectiveFlammability);
        RaiseLocalEvent(victim, ref fireEvent);
    }

    private void OnPuddleCompExtinguish(Entity<PuddleComponent> ent, ref ExtinguishEvent args)
    {
        Extinguish(ent.Owner);
    }

    private void OnFireStartup(EntityUid uid, ReagentPuddleFireComponent component, ref ComponentStartup args)
    {
        if (component.OnFire)
            _burningFires.Add(uid);
    }

    private void OnFireShutdown(EntityUid uid, ReagentPuddleFireComponent component, ref ComponentShutdown args)
    {
        _burningFires.Remove(uid);

        if (component.PlayingStream != null)
        {
            _audio.Stop(component.PlayingStream);
            component.PlayingStream = null;
        }

        if (component.FireEffectEntity != null)
        {
            QueueDel(component.FireEffectEntity.Value);
            component.FireEffectEntity = null;
        }
    }

    private void OnPuddleInteractUsing(Entity<PuddleComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (_fireQuery.TryComp(ent, out var burningComp) && burningComp.OnFire)
        {
            if (TryComp<AbsorbentComponent>(args.Used, out _))
            {
                Extinguish(ent.Owner);
                _audio.PlayPvs("/Audio/Effects/sizzle.ogg", ent);
                _popups.PopupEntity(Loc.GetString("ashfall-fire-extinguished-mop"), ent, args.User);
                args.Handled = true;
                return;
            }

            if (TryComp<SolutionContainerManagerComponent>(args.Used, out _))
            {
                foreach (var (_, solRef) in _solutionContainerSystem.EnumerateSolutions(args.Used))
                {
                    var extinguishVol = 0f;
                    foreach (var (reagent, quantity) in solRef.Comp.Solution.Contents)
                    {
                        if (_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var p) &&
                            p.ReactiveEffects != null && p.ReactiveEffects.ContainsKey("Extinguish"))
                        {
                            extinguishVol += quantity.Float();
                        }
                    }

                    if (extinguishVol > 0)
                    {
                        Extinguish(ent.Owner);
                        _audio.PlayPvs("/Audio/Effects/sizzle.ogg", ent);
                        _popups.PopupEntity(Loc.GetString("ashfall-fire-extinguished-water"), ent, args.User);
                        args.Handled = true;
                        return;
                    }
                }
            }
        }

        if (TryComp<IgnitionSourceComponent>(args.Used, out var ignition) && ignition.Ignited)
        {
            if (!_fireQuery.TryComp(ent, out var fireComp))
            {
                UpdateFire(ent);
                _fireQuery.TryComp(ent, out fireComp);
            }

            if (fireComp is { OnFire: false } && fireComp.Flammability > 0)
            {
                Ignite(ent, fireComp);
                args.Handled = true;
            }
        }
    }

    private void OnPuddleTileFire(Entity<PuddleComponent> ent, ref TileFireEvent args)
    {
        if (!_fireQuery.TryComp(ent, out var fireComp))
        {
            UpdateFire(ent);
            _fireQuery.TryComp(ent, out fireComp);
        }

        if (fireComp is { OnFire: false }
            && args.Temperature >= GetIgnitionTemperature(fireComp))
        {
            Ignite(ent.Owner, fireComp);
        }
    }

    public void UpdateFire(Entity<PuddleComponent> ent)
    {
        if (ent.Comp.Solution == null)
            return;

        var solution = ent.Comp.Solution.Value.Comp.Solution;

        if (!_fireQuery.TryComp(ent, out var fireComp))
        {
            if (solution.GetSolutionFlammability(_prototypeManager) <= 0)
                return;

            fireComp = AddComp<ReagentPuddleFireComponent>(ent);
        }

        var oldFlammability = fireComp.Flammability;
        var oldFireState = fireComp.FireState;

        if (!RefreshFireState(fireComp, solution))
        {
            Extinguish(ent);
            return;
        }

        if (fireComp.OnFire)
        {
            if (fireComp.Flammability != oldFlammability || fireComp.FireState != oldFireState)
                UpdateFireVisuals(ent, fireComp);
        }
        else if (_xformQuery.TryComp(ent.Owner, out var xform))
        {
            TryAutoIgnite(ent.Owner, fireComp, xform);
        }
    }

    private float GetVolumeFactor(FixedPoint2 volume)
    {
        if (!_volumeScalingEnabled || _volumeScalingReference <= 0f)
            return 1f;

        var ratio = Math.Clamp(volume.Float() / _volumeScalingReference, 0f, 1f);
        return MathF.Pow(ratio, _volumeScalingCurve);
    }

    private static float GetEffectiveFlammability(ReagentPuddleFireComponent fireComp)
        => fireComp.Flammability * fireComp.VolumeFactor;

    private static float GetIgnitionTemperature(ReagentPuddleFireComponent fireComp)
        => 573.15f - (50f * GetEffectiveFlammability(fireComp));

    private bool RefreshFireState(ReagentPuddleFireComponent fireComp, Solution solution)
    {
        var flammability = solution.GetSolutionFlammability(_prototypeManager);
        if (flammability <= 0)
            return false;

        // If extinguishing reagents (water, foam) make up >= 20% of the puddle, quench it
        var extinguishVolume = 0f;
        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var proto) &&
                proto.ReactiveEffects != null && proto.ReactiveEffects.ContainsKey("Extinguish"))
            {
                extinguishVolume += quantity.Float();
            }
        }

        if (solution.Volume > 0 && (extinguishVolume / solution.Volume.Float()) >= 0.20f)
            return false;

        fireComp.Flammability = flammability;
        fireComp.SelfOxidizing = solution.IsSolutionSelfOxidizing(_prototypeManager);
        fireComp.VolumeFactor = GetVolumeFactor(solution.Volume);

        var effectiveFlammability = GetEffectiveFlammability(fireComp);
        fireComp.FireState = effectiveFlammability > 10 ? 3 : effectiveFlammability > 4 ? 2 : 1;

        return true;
    }

    private void UpdateFireVisuals(EntityUid uid, ReagentPuddleFireComponent fireComp)
    {
        var fireColor = GetFireColor(fireComp.Flammability);
        if (fireComp.FireEffectEntity is { } fireEffect)
        {
            _appearance.SetData(fireEffect, ReagentPuddleFireVisuals.FireState, fireComp.FireState);
            _appearance.SetData(fireEffect, ReagentPuddleFireVisuals.FireColor, fireColor);
        }

        if (TryComp<PointLightComponent>(uid, out var light))
        {
            _light.SetRadius(uid, MathF.Max(2f, fireComp.FireState + 1f), light);
            _light.SetColor(uid, fireColor, light);
        }
    }

    private static Color GetFireColor(int flammability)
        => flammability switch
        {
            <= 1 => Color.FromHex("#FF5500"),
            2 => Color.FromHex("#FF9000"),
            3 => Color.FromHex("#FFD000"),
            4 => Color.FromHex("#FFFFE0"),
            _ => Color.FromHex("#FFFFFF")
        };

    public void Ignite(EntityUid uid, ReagentPuddleFireComponent? fireComp = null)
    {
        if (!Resolve(uid, ref fireComp))
            return;

        if (fireComp.OnFire)
            return;

        fireComp.OnFire = true;
        fireComp.NeedsSpread = true;
        _burningFires.Add(uid);

        if (fireComp.PlayingStream == null)
        {
            var audio = _audio.PlayPvs(fireComp.LoopingSound, uid, AudioParams.Default.WithLoop(true).WithVolume(-5f));
            if (audio != null)
            {
                fireComp.PlayingStream = audio.Value.Entity;
            }
        }

        var fireColor = GetFireColor(fireComp.Flammability);

        var light = EnsureComp<PointLightComponent>(uid);
        _light.SetEnabled(uid, true, light);
        _light.SetRadius(uid, MathF.Max(2f, fireComp.FireState + 1f), light);
        _light.SetColor(uid, fireColor, light);
        _light.SetEnergy(uid, 2f, light);

        if (fireComp.FireEffectEntity == null)
        {
            var xform = Transform(uid);
            var fireEnt = Spawn("ReagentPuddleFireEffect", xform.Coordinates);
            _transform.SetParent(fireEnt, uid);
            fireComp.FireEffectEntity = fireEnt;
        }

        var stepTrigger = EnsureComp<StepTriggerComponent>(uid);
        _stepTrigger.SetRequiredTriggerSpeed(uid, 0f, stepTrigger);
        _stepTrigger.SetIntersectRatio(uid, 0.1f, stepTrigger);

        if (fireComp.FireEffectEntity is { } fireEffect)
        {
            _appearance.SetData(fireEffect, ReagentPuddleFireVisuals.FireState, fireComp.FireState);
            _appearance.SetData(fireEffect, ReagentPuddleFireVisuals.FireColor, fireColor);
        }
    }

    public void Extinguish(EntityUid uid)
    {
        if (!_fireQuery.TryComp(uid, out var fireComp))
            return;

        fireComp.OnFire = false;
        _burningFires.Remove(uid);

        if (fireComp.PlayingStream != null)
        {
            _audio.Stop(fireComp.PlayingStream);
            fireComp.PlayingStream = null;
        }

        RemComp<PointLightComponent>(uid);

        if (fireComp.FireEffectEntity != null)
        {
            QueueDel(fireComp.FireEffectEntity.Value);
            fireComp.FireEffectEntity = null;
        }

        RemComp<ReagentPuddleFireComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateAccumulator += frameTime;
        if (_updateAccumulator < UpdateInterval)
            return;

        _updateAccumulator -= UpdateInterval;

        CheckLyingIgnitionSources();

        if (_burningFires.Count == 0)
            return;

        _dueFires.Clear();
        _dueFires.AddRange(_burningFires);

        foreach (var uid in _dueFires)
        {
            if (!_fireQuery.TryComp(uid, out var fireComp) || !fireComp.OnFire)
            {
                _burningFires.Remove(uid);
                continue;
            }

            if (!TryComp<PuddleComponent>(uid, out var puddle)
                || !_xformQuery.TryComp(uid, out var xform))
            {
                Extinguish(uid);
                continue;
            }

            ProcessBurningPuddle(uid, fireComp, puddle, xform);
        }
    }

    private void CheckLyingIgnitionSources()
    {
        var ignQuery = EntityQueryEnumerator<IgnitionSourceComponent, TransformComponent>();
        while (ignQuery.MoveNext(out var uid, out var ignition, out var xform))
        {
            if (!ignition.Ignited || _containers.IsEntityOrParentInContainer(uid))
                continue;

            _puddles.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, 0.6f, _puddles);
            foreach (var puddle in _puddles)
            {
                if (!_fireQuery.TryComp(puddle, out var fireComp))
                {
                    UpdateFire(puddle);
                    _fireQuery.TryComp(puddle, out fireComp);
                }

                if (fireComp is { OnFire: false } && fireComp.Flammability > 0)
                {
                    Ignite(puddle.Owner, fireComp);
                }
            }
        }

        foreach (var uid in _flammable.ActiveFires)
        {
            if (!_xformQuery.TryComp(uid, out var xform) || _containers.IsEntityOrParentInContainer(uid))
                continue;

            _puddles.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, 0.6f, _puddles);
            foreach (var puddle in _puddles)
            {
                if (!_fireQuery.TryComp(puddle, out var fireComp))
                {
                    UpdateFire(puddle);
                    _fireQuery.TryComp(puddle, out fireComp);
                }

                if (fireComp is { OnFire: false } && fireComp.Flammability > 0)
                {
                    Ignite(puddle.Owner, fireComp);
                }
            }
        }
    }

    private void TryAutoIgnite(EntityUid uid, ReagentPuddleFireComponent fireComp, TransformComponent xform)
    {
        if (fireComp.Flammability <= 0 || xform.GridUid is not { } gridUid)
            return;

        var ambientPos = _transform.GetGridTilePositionOrDefault((uid, xform));

        if (_gridQuery.TryComp(gridUid, out var grid) && HasAdjacentBurningPuddle(gridUid, grid, ambientPos))
        {
            Ignite(uid, fireComp);
            return;
        }

        var ambientMix = _atmos.GetTileMixture(gridUid, null, ambientPos, excite: false);
        if (ambientMix == null)
            return;

        var autoIgnitionTemp = 773.15f - (50f * GetEffectiveFlammability(fireComp));
        if (ambientMix.Temperature < autoIgnitionTemp
            || (!fireComp.SelfOxidizing && ambientMix.GetMoles(Gas.Oxygen) <= 0.1f))
            return;

        Ignite(uid, fireComp);
        _atmos.GetTileMixture(gridUid, null, ambientPos, excite: true);
    }

    private void ProcessBurningPuddle(EntityUid uid, ReagentPuddleFireComponent fireComp, PuddleComponent puddle, TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid)
        {
            Extinguish(uid);
            return;
        }

        var tilePos = _transform.GetGridTilePositionOrDefault((uid, xform));
        var tileMix = _atmos.GetTileMixture(gridUid, null, tilePos, excite: true);

        var oxygenMoles = tileMix?.GetMoles(Gas.Oxygen) ?? 0f;
        if (!fireComp.SelfOxidizing && oxygenMoles <= 0.1f)
        {
            Extinguish(uid);
            return;
        }

        if (!_solutionContainerSystem.ResolveSolution(uid, puddle.SolutionName, ref puddle.Solution, out var solution))
        {
            Extinguish(uid);
            return;
        }

        var burnFraction = 0.05f / MathF.Pow(MathF.Max(1f, fireComp.Flammability), 3f);
        var currentVolume = solution.Volume.Float();
        if (currentVolume > 0f && currentVolume < _smallPuddleBurnThreshold)
        {
            var acceleratedFraction = _smallPuddleBurnPercent / MathF.Max(1f, fireComp.Flammability);
            burnFraction = MathF.Max(burnFraction, acceleratedFraction);
        }

        _solutionContainerSystem.BurnFlammableReagents(puddle.Solution.Value, burnFraction);

        if (!_fireQuery.TryComp(uid, out var refreshedFire) || !refreshedFire.OnFire)
            return;

        fireComp = refreshedFire;
        var effectiveFlammability = GetEffectiveFlammability(fireComp);

        if (tileMix != null)
        {
            var maxTemp = Atmospherics.T0C + (100f * MathF.Pow(effectiveFlammability, 1.5f));
            if (tileMix.Temperature < maxTemp)
            {
                var heatRate = 10f * effectiveFlammability;
                tileMix.Temperature = MathF.Min(tileMix.Temperature + heatRate, maxTemp);
            }

            if (!fireComp.SelfOxidizing)
            {
                var burnAmount = MathF.Min(0.2f * effectiveFlammability, oxygenMoles);
                tileMix.AdjustMoles(Gas.Oxygen, -burnAmount);
                tileMix.AdjustMoles(Gas.CarbonDioxide, burnAmount * 0.6f);
                tileMix.AdjustMoles(Gas.WaterVapor, burnAmount * 0.8f);
            }
            else
            {
                var burnAmount = 0.2f * effectiveFlammability;
                tileMix.AdjustMoles(Gas.CarbonDioxide, burnAmount * 0.6f);
                tileMix.AdjustMoles(Gas.WaterVapor, burnAmount * 0.8f);
            }
        }

        TryAddBurntDecal(gridUid, tilePos);
        RadiateHeatToAdjacentTiles(gridUid, tilePos, tileMix);

        if (fireComp.NeedsSpread)
        {
            fireComp.NeedsSpread = false;
            SpreadToAdjacentPuddles(gridUid, tilePos);
        }

        DamageStandingEntities(uid, gridUid, tilePos, tileMix, effectiveFlammability);
        TryEmitSmoke(uid, xform.Coordinates);
    }

    private void TryEmitSmoke(EntityUid uid, EntityCoordinates coords)
    {
        _nearbySmoke.Clear();
        _lookup.GetEntitiesInRange(coords, 0.8f, _nearbySmoke);
        foreach (var near in _nearbySmoke)
        {
            if (HasComp<SmokeComponent>(near))
                return;
        }

        if (!_random.Prob(0.25f))
            return;

        var smoke = Spawn(FireSteamPrototype, coords);
        if (TryComp<SmokeComponent>(smoke, out var smokeComp))
        {
            _smoke.StartSmoke(smoke, new Solution(), duration: 8f, spreadAmount: 1, smokeComp);
        }
    }

    private void TryAddBurntDecal(EntityUid gridUid, Vector2i tilePos)
    {
        if (!_random.Prob(0.25f))
            return;

        var tileBurntDecals = 0;
        foreach (var set in _decalSystem.GetDecalsInRange(gridUid, tilePos))
        {
            if (Array.IndexOf(BurntDecals, set.Decal.Id) == -1)
                continue;

            if (++tileBurntDecals >= 4)
                return;
        }

        _decalSystem.TryAddDecal(BurntDecals[_random.Next(BurntDecals.Length)],
            new EntityCoordinates(gridUid, tilePos),
            out _,
            cleanable: true);
    }

    private void RadiateHeatToAdjacentTiles(EntityUid gridUid, Vector2i tilePos, GasMixture? tileMix)
    {
        if (tileMix is not { Temperature: > Atmospherics.FireMinimumTemperatureToSpread })
            return;

        var radiatedTemp = tileMix.Temperature * Atmospherics.FireSpreadRadiosityScale;
        Entity<GridAtmosphereComponent?> gridAtmos = (gridUid, _gridAtmosQuery.CompOrNull(gridUid));
        if (gridAtmos.Comp == null)
            return;

        foreach (var offset in CardinalOffsets)
        {
            var adjacentPos = tilePos + offset;

            if (_atmos.GetTileMixture(gridUid, null, adjacentPos) is { } adjMix
                && adjMix.Temperature < radiatedTemp
                && !IsAnyAirBlocked(gridAtmos, adjacentPos))
            {
                adjMix.Temperature = radiatedTemp;
                _atmos.GetTileMixture(gridUid, null, adjacentPos, excite: true);
            }
        }
    }

    private bool IsAnyAirBlocked(Entity<GridAtmosphereComponent?> gridAtmos, Vector2i tile)
    {
        return _atmos.IsTileAirBlockedCached(gridAtmos, tile);
    }

    private void DamageStandingEntities(EntityUid uid, EntityUid gridUid, Vector2i tilePos, GasMixture? tileMix, float effectiveFlammability)
    {
        _standingEntities.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, tilePos, _standingEntities, 0f);
        _standingEntities.Remove(uid);

        if (_standingEntities.Count == 0)
            return;

        var damageAmount = FixedPoint2.New(2f * effectiveFlammability * _puddleDamageMultiplier);
        var totalDamage = new DamageSpecifier();
        totalDamage.DamageDict.Add(HeatDamage, damageAmount);

        var fireVolume = 50f * effectiveFlammability;
        var fireTemp = tileMix?.Temperature ?? (Atmospherics.T0C + (100f * effectiveFlammability));
        var fireEvent = new TileFireEvent(fireTemp, fireVolume);

        foreach (var ent in _standingEntities)
        {
            if (TerminatingOrDeleted(ent))
                continue;

            if (!_xformQuery.TryComp(ent, out var entXform))
                continue;

            if (_transform.GetGridTilePositionOrDefault((ent, entXform)) != tilePos)
                continue;

            if (_damageableQuery.HasComp(ent))
            {
                var ignoreResistances = !_mobStateQuery.HasComp(ent);
                var appliedDamage = totalDamage;

                if (!ignoreResistances)
                {
                    var reduction = Math.Clamp(GetFireProtectionReduction(ent) * _fireProtectionEffectiveness, 0f, 1f);
                    appliedDamage = totalDamage * (1f - reduction);
                }
                else
                {
                    appliedDamage = new DamageSpecifier(totalDamage);
                    appliedDamage.DamageDict.Add(StructuralDamage, damageAmount);
                }

                _damageable.TryChangeDamage(ent, appliedDamage, ignoreResistances: ignoreResistances);
            }

            if (TerminatingOrDeleted(ent))
                continue;

            if (TryComp<FlammableComponent>(ent, out var flammable))
            {
                _flammable.AdjustFireStacks(ent, 1f * effectiveFlammability, flammable);
                _flammable.Ignite(ent, uid, flammable);
            }

            RaiseLocalEvent(ent, ref fireEvent);
        }
    }

    private float GetFireProtectionReduction(EntityUid uid)
    {
        if (!TryComp<InventoryComponent>(uid, out var inv))
            return 0f;

        var survivalFactor = 1f;
        foreach (var slot in inv.Slots)
        {
            if (!_inventory.TryGetSlotEntity(uid, slot.Name, out var slotEnt, inv))
                continue;

            if (TryComp<FireProtectionComponent>(slotEnt, out var protection))
                survivalFactor *= 1f - Math.Clamp(protection.Reduction, 0f, 1f);
        }

        return 1f - survivalFactor;
    }

    private bool HasAdjacentBurningPuddle(EntityUid gridUid, MapGridComponent grid, Vector2i tilePos)
    {
        foreach (var offset in CardinalOffsets)
        {
            var anchored = _map.GetAnchoredEntities(gridUid, grid, tilePos + offset);
            while (anchored.MoveNext(out var ent))
            {
                if (_fireQuery.TryComp(ent, out var fire) && fire.OnFire)
                    return true;
            }
        }
        return false;
    }

    private void SpreadToAdjacentPuddles(EntityUid gridUid, Vector2i tilePos)
    {
        if (!_gridQuery.TryComp(gridUid, out var grid))
            return;

        _spreadPuddles.Clear();

        foreach (var offset in CardinalOffsets)
            CollectIgnitablePuddles(gridUid, grid, tilePos + offset, null, _spreadPuddles);

        foreach (var adjPuddle in _spreadPuddles)
            Ignite(adjPuddle, adjPuddle.Comp);
    }

    private void CollectIgnitablePuddles(EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        float? temperature,
        List<Entity<ReagentPuddleFireComponent>> puddles)
    {
        var anchored = _map.GetAnchoredEntities(gridUid, grid, tile);
        while (anchored.MoveNext(out var ent))
        {
            if (!_fireQuery.TryComp(ent, out var fireComp))
            {
                if (TryComp<PuddleComponent>(ent, out var puddle))
                {
                    UpdateFire((ent.Value, puddle));
                    _fireQuery.TryComp(ent, out fireComp);
                }
            }

            if (fireComp == null || fireComp.OnFire)
                continue;

            if (temperature is { } temp && temp < GetIgnitionTemperature(fireComp))
                continue;

            puddles.Add((ent.Value, fireComp));
        }
    }
}
