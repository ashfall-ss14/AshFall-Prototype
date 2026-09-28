using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Armor;
using Content.Shared.Ashfall.Fire;
using Content.Shared.Ashfall.Fire.Components;
using Content.Shared.Ashfall.Fire.Events;
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
using Content.Shared.DoAfter;
using Content.Shared.EntityEffects.Effects.Atmos;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.IgnitionSource;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Smoking;
using Content.Shared.StepTrigger.Components;
using Content.Shared.StepTrigger.Systems;
using Content.Server.Ashfall.Fire.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Ashfall.Fire.Systems;

/// <summary>
/// Contact ignition and fuel consumption for combustible solid objects and floorings.
/// </summary>
public sealed partial class SolidFuelSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private TileSystem _tileSystem = default!;
    [Dependency] private ReagentFireSystem _reagentFire = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popups = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private EntityQuery<ReagentPuddleFireComponent> _puddleFireQuery = default!;

    public bool Enabled => _config.GetCVar(AshfallFireCVars.SolidFuelEnabled);

    private static readonly ProtoId<DamageTypePrototype> HeatDamage = "Heat";
    private static readonly EntProtoId FireSteamPrototype = "AshfallFireSteam";
    private readonly HashSet<EntityUid> _standingEntities = new();
    private readonly HashSet<EntityUid> _nearbySmoke = new();
    private readonly HashSet<Entity<SolidFuelComponent>> _nearby = new();
    private readonly HashSet<Entity<PuddleComponent>> _puddles = new();
    private readonly Dictionary<EntityUid, (EntityUid Source, float Rate, EntityUid? User)> _exposures = new();
    private float _elapsed;
    private readonly HashSet<Entity<SolidFuelComponent>> _floorCandidates = new();
    private readonly List<(EntityUid Uid, SolidFuelComponent Fuel, FlammableComponent Fire)> _fuelList = new();
    private readonly HashSet<EntityUid> _sources = new();
    private static readonly Vector2i[] Neighbors = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(FlammableSystem));
        SubscribeLocalEvent<SolidFuelComponent, InteractUsingEvent>(OnInteractUsing,
            before: new[] { typeof(FlammableSystem) });
        SubscribeLocalEvent<SolidFuelComponent, SolidFuelIgnitionDoAfterEvent>(OnIgnitionDoAfter);
        SubscribeLocalEvent<SolidFuelComponent, DoAfterAttemptEvent<SolidFuelIgnitionDoAfterEvent>>(OnIgnitionAttempt);
        SubscribeLocalEvent<SolidFuelComponent, ExtinguishedEvent>(OnExtinguished);
        SubscribeLocalEvent<SolidFuelComponent, ExtinguishEvent>(OnExtinguish);
        SubscribeLocalEvent<SolidFuelComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<IgnitionSourceComponent, AfterInteractEvent>(OnIgnitionAfterInteract);
        SubscribeLocalEvent<SolidFuelComponent, StepTriggeredOnEvent>(OnStepTriggeredOn);
        SubscribeLocalEvent<SolidFuelComponent, StepTriggeredOffEvent>(OnStepTriggeredOff);
    }

    private void OnExtinguished(Entity<SolidFuelComponent> ent, ref ExtinguishedEvent args)
    {
        ent.Comp.Exposure = 0;
        _exposures.Remove(ent);
    }

    private void OnShutdown(Entity<SolidFuelComponent> ent, ref ComponentShutdown args)
    {
        _exposures.Remove(ent);
    }

    private void OnExtinguish(Entity<SolidFuelComponent> ent, ref ExtinguishEvent args)
    {
        ent.Comp.Exposure = 0;
        _exposures.Remove(ent);
        if (args.FireStacksAdjustment < 0)
            ent.Comp.WetTime = Math.Clamp(ent.Comp.WetTime - args.FireStacksAdjustment, 0, 10);
    }

    public float GetIgnitionRate(EntityUid source)
    {
        if (TryComp<SmokableComponent>(source, out var smoke))
            return smoke.State == SmokableState.Lit ? MathF.Max(0, _config.GetCVar(AshfallFireCVars.SolidFuelCigaretteRate)) : 0f;

        if (TryComp<FlammableComponent>(source, out var fire) && fire.OnFire)
            return MathF.Max(0, _config.GetCVar(AshfallFireCVars.SolidFuelFireRate));

        if (TryComp<IgnitionSourceComponent>(source, out var ignition) && ignition.Ignited)
            return ignition.ContactIgnitionRate;

        return 0f;
    }

    public bool CanBurn(Entity<FlammableComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return false;

        return Enabled
               && ent.Comp.FireStacks >= 0
               && (!TryComp<SolidFuelComponent>(ent, out var fuel) || fuel.WetTime <= 0)
               && !IsWet(ent)
               && HasOxygen(ent);
    }

    public bool HasOxygen(EntityUid uid)
    {
        if (_atmos.GetContainingMixture(uid) is { } air && air.GetMoles(Gas.Oxygen) >= 1f)
            return true;

        if (!HasComp<AirtightComponent>(uid) || _turf.GetTileRef(Transform(uid).Coordinates) is not { } tile)
            return false;

        foreach (var offset in Neighbors)
        {
            if (_atmos.GetTileMixture(tile.GridUid, Transform(uid).MapUid, tile.GridIndices + offset) is { } adjacent
                && adjacent.GetMoles(Gas.Oxygen) >= 1f)
                return true;
        }

        return false;
    }

    private bool IsWet(EntityUid uid)
    {
        if (TryComp<AbsorbentComponent>(uid, out var absorbent) &&
            HasWater(uid, absorbent.SolutionName))
            return true;

        if (_containers.IsEntityOrParentInContainer(uid))
            return false;

        _puddles.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, 0.5f, _puddles);
        foreach (var puddle in _puddles)
        {
            if (HasWater(puddle.Owner, puddle.Comp.SolutionName))
                return true;
        }

        return false;
    }

    private bool HasWater(EntityUid uid, string solutionName)
    {
        if (!_solutions.TryGetSolution(uid, solutionName, out _, out var solution))
            return false;

        var extinguishAmount = FixedPoint2.Zero;
        var flammableAmount = FixedPoint2.Zero;

        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (quantity <= 0 || !_prototypes.TryIndex<ReagentPrototype>(reagent.Prototype, out var proto))
                continue;

            if (proto.ReactiveEffects != null && proto.ReactiveEffects.TryGetValue("Extinguish", out var reaction))
            {
                foreach (var effect in reaction.Effects)
                {
                    if (effect is Extinguish)
                    {
                        extinguishAmount += quantity;
                        break;
                    }
                }
            }

            if (proto.Flammability > 0)
                flammableAmount += quantity;
        }

        // Only count as wet/extinguishing if extinguishing liquid clearly exceeds flammable content
        return extinguishAmount > FixedPoint2.Zero && extinguishAmount >= flammableAmount;
    }

    private bool IsMopWetWithExtinguisher(EntityUid mop, AbsorbentComponent comp)
    {
        if (!_solutions.TryGetSolution(mop, comp.SolutionName, out _, out var solution))
            return false;

        var extinguishAmount = FixedPoint2.Zero;
        var flammableAmount = FixedPoint2.Zero;

        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (quantity <= 0 || !_prototypes.TryIndex<ReagentPrototype>(reagent.Prototype, out var proto))
                continue;

            if (proto.ReactiveEffects != null && proto.ReactiveEffects.ContainsKey("Extinguish"))
                extinguishAmount += quantity;

            if (proto.Flammability > 0)
                flammableAmount += quantity;
        }

        return extinguishAmount > FixedPoint2.Zero && extinguishAmount >= flammableAmount;
    }

    private void OnInteractUsing(Entity<SolidFuelComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp<FlammableComponent>(ent, out var fire) && fire.OnFire)
        {
            if (TryComp<AbsorbentComponent>(args.Used, out var absorbent))
            {
                var wet = IsMopWetWithExtinguisher(args.Used, absorbent);
                if (wet)
                {
                    _flammable.AdjustFireStacks(ent, -fire.FireStacks, fire);
                    _flammable.Extinguish(ent, fire);
                    ent.Comp.WetTime = MathF.Max(ent.Comp.WetTime, 8f);
                    ent.Comp.Exposure = 0;
                    _exposures.Remove(ent);
                    _audio.PlayPvs("/Audio/Effects/sizzle.ogg", ent);
                    _popups.PopupEntity(Loc.GetString("ashfall-fire-extinguished-mop"), ent, args.User);
                    args.Handled = true;
                    return;
                }
                else
                {
                    _flammable.AdjustFireStacks(ent, -1f, fire);
                    if (fire.FireStacks <= 0)
                    {
                        _flammable.Extinguish(ent, fire);
                        ent.Comp.Exposure = 0;
                        _exposures.Remove(ent);
                    }
                    _audio.PlayPvs("/Audio/Effects/thudswoosh.ogg", ent);
                    _popups.PopupEntity(Loc.GetString("ashfall-fire-extinguished-mop"), ent, args.User);
                    args.Handled = true;
                    return;
                }
            }

            if (TryComp<SolutionContainerManagerComponent>(args.Used, out _))
            {
                foreach (var (_, solRef) in _solutions.EnumerateSolutions(args.Used))
                {
                    var extinguishVol = 0f;
                    foreach (var (reagent, quantity) in solRef.Comp.Solution.Contents)
                    {
                        if (_prototypes.TryIndex<ReagentPrototype>(reagent.Prototype, out var p) &&
                            p.ReactiveEffects != null && p.ReactiveEffects.ContainsKey("Extinguish"))
                        {
                            extinguishVol += quantity.Float();
                        }
                    }

                    if (extinguishVol > 0)
                    {
                        _flammable.AdjustFireStacks(ent, -fire.FireStacks, fire);
                        _flammable.Extinguish(ent, fire);
                        ent.Comp.WetTime = MathF.Max(ent.Comp.WetTime, 10f);
                        ent.Comp.Exposure = 0;
                        _exposures.Remove(ent);
                        _audio.PlayPvs("/Audio/Effects/sizzle.ogg", ent);
                        _popups.PopupEntity(Loc.GetString("ashfall-fire-extinguished-water"), ent, args.User);
                        args.Handled = true;
                        return;
                    }
                }
            }
        }

        if (GetIgnitionRate(args.Used) <= 0 ||
            !TryComp<FlammableComponent>(ent, out var flammable) || flammable.OnFire || !Enabled)
            return;

        args.Handled = true;
        if (!CanBurn((ent, flammable)))
            return;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 1f,
            new SolidFuelIgnitionDoAfterEvent(), ent, target: ent, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnHandChange = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
        });
    }

    private void OnIgnitionAttempt(Entity<SolidFuelComponent> ent,
        ref DoAfterAttemptEvent<SolidFuelIgnitionDoAfterEvent> args)
    {
        if (args.DoAfter.Args.Used is not { } source || GetIgnitionRate(source) <= 0 ||
            !TryComp<FlammableComponent>(ent, out var fire) || fire.OnFire || !CanBurn((ent, fire)))
            args.Cancel();
    }

    private void OnIgnitionDoAfter(Entity<SolidFuelComponent> ent, ref SolidFuelIgnitionDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } source ||
            !TryComp<FlammableComponent>(ent, out var fire) || fire.OnFire || !CanBurn((ent, fire)))
            return;

        var rate = GetIgnitionRate(source);
        if (rate <= 0)
            return;

        ent.Comp.Exposure += rate * 2.5f * MathF.Max(0, _config.GetCVar(AshfallFireCVars.SolidFuelIgnitionMultiplier));
        if (ent.Comp.Exposure >= ent.Comp.IgnitionTime)
        {
            _flammable.AdjustFireStacks(ent, 2f, fire);
            _flammable.Ignite(ent, source, fire);
        }
        else
        {
            Expose(ent, source, rate, args.User);
        }

        args.Handled = true;
        args.Repeat = true;
    }

    private void Expose(EntityUid target, EntityUid source, float rate, EntityUid? user = null)
    {
        if (!_exposures.TryGetValue(target, out var old) || rate > old.Rate)
            _exposures[target] = (source, rate, user);
    }

    private void HeatNearby(EntityUid source)
    {
        var rate = GetIgnitionRate(source);
        if (rate <= 0 || _containers.IsEntityOrParentInContainer(source) || _containers.TryGetContainingContainer((source, null), out _))
            return;

        var burning = TryComp<FlammableComponent>(source, out var fire) && fire.OnFire;
        if (burning && HasComp<SolidFuelComponent>(source) && !CanBurn((source, fire!)))
            return;

        if (burning && !_config.GetCVar(AshfallFireCVars.SolidFuelSpread))
            return;

        var range = Math.Clamp(_config.GetCVar(burning ? AshfallFireCVars.SolidFuelSpreadRange : AshfallFireCVars.SolidFuelContactRange), 0f, 3f);
        if (range <= 0)
            return;

        HeatFloor(source, rate, range);

        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(source).Coordinates, range, _nearby, LookupFlags.Uncontained);
        foreach (var target in _nearby)
        {
            if (source != target.Owner &&
                _interaction.InRangeUnobstructed(source, target.Owner, range: range))
                Expose(target, source, rate);
        }
    }

    public EntityUid? TryGetOrSpawnSolidFuelFloor(EntityCoordinates coords)
    {
        var tile = _turf.GetTileRef(coords);
        if (tile is not { } floor ||
            ((ContentTileDefinition) _tiles[floor.Tile.TypeId]).SolidFuelEntity is not { } prototype)
            return null;

        var center = new EntityCoordinates(floor.GridUid,
            new Vector2(floor.GridIndices.X + 0.5f, floor.GridIndices.Y + 0.5f));

        _floorCandidates.Clear();
        _lookup.GetEntitiesInRange(center, 0.1f, _floorCandidates, LookupFlags.Uncontained);
        foreach (var candidate in _floorCandidates)
        {
            if (candidate.Comp.TileType != null && !TerminatingOrDeleted(candidate) &&
                !EntityManager.IsQueuedForDeletion(candidate))
            {
                return candidate.Owner;
            }
        }

        var fuel = Spawn(prototype, center);
        Comp<SolidFuelComponent>(fuel).TileType = ((ContentTileDefinition) _tiles[floor.Tile.TypeId]).ID;
        return fuel;
    }

    public bool TryIgniteFloor(EntityUid gridUid, Vector2i indices, EntityUid? cause = null)
    {
        var center = new EntityCoordinates(gridUid, new Vector2(indices.X + 0.5f, indices.Y + 0.5f));
        if (TryGetOrSpawnSolidFuelFloor(center) is not { } fuel)
            return false;

        if (TryComp<FlammableComponent>(fuel, out var fire) && !fire.OnFire && CanBurn((fuel, fire)))
        {
            _flammable.AdjustFireStacks(fuel, 2f, fire);
            _flammable.Ignite(fuel, cause ?? fuel, fire);
            return true;
        }

        return false;
    }

    private void OnIgnitionAfterInteract(Entity<IgnitionSourceComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !ent.Comp.Ignited || !args.CanReach || !Enabled)
            return;

        var coords = args.ClickLocation;

        // 1. Try igniting flammable puddles at click location
        _puddles.Clear();
        _lookup.GetEntitiesInRange(coords, 0.75f, _puddles);
        foreach (var puddle in _puddles)
        {
            if (!_puddleFireQuery.TryComp(puddle, out var puddleFire))
            {
                _reagentFire.UpdateFire((puddle.Owner, puddle.Comp));
                _puddleFireQuery.TryComp(puddle, out puddleFire);
            }

            if (puddleFire is { OnFire: false } && puddleFire.Flammability > 0)
            {
                _reagentFire.Ignite(puddle.Owner, puddleFire);
                args.Handled = true;
                return;
            }
        }

        // 2. Try igniting combustible floor at click location
        var target = TryGetOrSpawnSolidFuelFloor(coords);
        if (target == null || !TryComp<SolidFuelComponent>(target.Value, out var fuel) ||
            !TryComp<FlammableComponent>(target.Value, out var fire) || fire.OnFire || !CanBurn((target.Value, fire)))
            return;

        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 1f,
            new SolidFuelIgnitionDoAfterEvent(), target.Value, target: target.Value, used: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnHandChange = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
        });
    }

    private void HeatFloor(EntityUid source, float rate, float range)
    {
        var coords = Transform(source).Coordinates;
        var radius = (int) MathF.Ceiling(range);
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (x * x + y * y > range * range)
                    continue;

                var offsetCoords = coords.Offset(new Vector2(x, y));
                if (TryGetOrSpawnSolidFuelFloor(offsetCoords) is not { } fuel)
                    continue;

                if (fuel != source && _interaction.InRangeUnobstructed(source, fuel, range: range + 0.71f))
                    Expose(fuel, source, rate);
            }
        }
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 1f)
            return;

        var elapsed = _elapsed;
        _elapsed = 0;

        if (!Enabled)
        {
            _fuelList.Clear();
            var disabled = EntityQueryEnumerator<SolidFuelComponent, FlammableComponent>();
            while (disabled.MoveNext(out var uid, out var fuel, out var fire))
                _fuelList.Add((uid, fuel, fire));

            foreach (var (uid, fuel, fire) in _fuelList)
            {
                if (TerminatingOrDeleted(uid))
                    continue;

                fuel.Exposure = 0;
                _flammable.Extinguish(uid, fire);
                if (fuel.TileType != null)
                    QueueDel(uid);
            }
            _exposures.Clear();
            return;
        }

        _sources.Clear();
        var sources = EntityQueryEnumerator<IgnitionSourceComponent>();
        while (sources.MoveNext(out var uid, out var ignition))
        {
            if (ignition.Ignited)
                _sources.Add(uid);
        }

        var cigarettes = EntityQueryEnumerator<SmokableComponent>();
        while (cigarettes.MoveNext(out var uid, out var smoke))
        {
            if (smoke.State == SmokableState.Lit)
                _sources.Add(uid);
        }

        foreach (var uid in _flammable.ActiveFires)
            _sources.Add(uid);

        foreach (var source in _sources)
            HeatNearby(source);

        _fuelList.Clear();
        var fuels = EntityQueryEnumerator<SolidFuelComponent, FlammableComponent>();
        while (fuels.MoveNext(out var uid, out var fuel, out var fire))
            _fuelList.Add((uid, fuel, fire));

        foreach (var (uid, fuel, fire) in _fuelList)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            fuel.WetTime = MathF.Max(0, fuel.WetTime - elapsed);

            if (fuel.TileType is { } tileType &&
                (_turf.GetTileRef(Transform(uid).Coordinates) is not { } tile ||
                 ((ContentTileDefinition) _tiles[tile.Tile.TypeId]).ID != tileType.Id))
            {
                QueueDel(uid);
                continue;
            }

            if (!fire.OnFire && fuel.Exposure <= 0 && !_exposures.ContainsKey(uid))
            {
                if (fuel.TileType != null && fuel.BurnedTime <= 0 && fuel.WetTime <= 0)
                    QueueDel(uid);
                continue;
            }

            if (!CanBurn((uid, fire)))
            {
                fuel.Exposure = 0;
                _flammable.Extinguish(uid, fire);
                if (fuel.TileType != null && fuel.BurnedTime <= 0 && fuel.WetTime <= 0)
                    QueueDel(uid);
                continue;
            }

            if (fire.OnFire)
            {
                DamageStandingEntities(uid);
                HeatAtmosphere(uid);
                TryEmitSmoke(uid);

                fuel.Exposure = 0;
                fuel.BurnedTime += elapsed * MathF.Max(0, _config.GetCVar(AshfallFireCVars.SolidFuelBurnMultiplier));
                if (fuel.BurnedTime >= fuel.BurnTime)
                {
                    if (fuel.TileType != null && _turf.GetTileRef(Transform(uid).Coordinates) is { } floor)
                        _tileSystem.DeconstructTile(floor, spawnItem: false);

                    Spawn(fuel.AshPrototype, Transform(uid).Coordinates);
                    QueueDel(uid);
                }
                continue;
            }

            if (_exposures.TryGetValue(uid, out var exposure) && !Deleted(exposure.Source) &&
                GetIgnitionRate(exposure.Source) > 0)
            {
                fuel.Exposure += exposure.Rate * elapsed * MathF.Max(0, _config.GetCVar(AshfallFireCVars.SolidFuelIgnitionMultiplier));
                if (fuel.Exposure >= fuel.IgnitionTime)
                {
                    _flammable.AdjustFireStacks(uid, 2f, fire);
                    _flammable.Ignite(uid, exposure.Source, fire);
                }
            }
            else
            {
                fuel.Exposure = MathF.Max(0, fuel.Exposure - (fuel.CoolingRate * elapsed));
            }
        }

        _exposures.Clear();
    }

    private void OnStepTriggeredOn(Entity<SolidFuelComponent> ent, ref StepTriggeredOnEvent args)
    {
        if (!TryComp<FlammableComponent>(ent, out var fire) || !fire.OnFire)
            return;

        BurnEntity(ent.Owner, args.Tripper);
    }

    private void OnStepTriggeredOff(Entity<SolidFuelComponent> ent, ref StepTriggeredOffEvent args)
    {
        if (!TryComp<FlammableComponent>(ent, out var fire) || !fire.OnFire)
            return;

        BurnEntity(ent.Owner, args.Tripper);
    }

    private void BurnEntity(EntityUid floorUid, EntityUid victim)
    {
        if (TerminatingOrDeleted(victim))
            return;

        if (TryComp<DamageableComponent>(victim, out _))
        {
            var damageAmount = FixedPoint2.New(5);
            var damage = new DamageSpecifier();
            damage.DamageDict.Add(HeatDamage, damageAmount);

            var ignoreResistances = !HasComp<MobStateComponent>(victim);
            var appliedDamage = damage;
            if (!ignoreResistances)
            {
                var reduction = Math.Clamp(GetFireProtectionReduction(victim), 0f, 0.8f);
                appliedDamage = damage * (1f - reduction);
            }

            _damageable.TryChangeDamage(victim, appliedDamage, ignoreResistances: ignoreResistances);
        }

        if (TryComp<FlammableComponent>(victim, out var flammable))
        {
            _flammable.AdjustFireStacks(victim, 1.5f, flammable);
            _flammable.Ignite(victim, floorUid, flammable);
        }

        var fireEvent = new TileFireEvent(Atmospherics.T0C + 250f, 60f);
        RaiseLocalEvent(victim, ref fireEvent);
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

    private void DamageStandingEntities(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid)
            return;

        var tilePos = _transform.GetGridTilePositionOrDefault((uid, xform));
        _standingEntities.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, tilePos, _standingEntities, 0f);
        _standingEntities.Remove(uid);

        if (_standingEntities.Count == 0)
            return;

        var damageAmount = FixedPoint2.New(4);
        var totalDamage = new DamageSpecifier();
        totalDamage.DamageDict.Add(HeatDamage, damageAmount);

        var fireEvent = new TileFireEvent(Atmospherics.T0C + 250f, 60f);

        foreach (var ent in _standingEntities)
        {
            if (TerminatingOrDeleted(ent))
                continue;

            if (_transform.GetGridTilePositionOrDefault(ent) != tilePos)
                continue;

            if (TryComp<DamageableComponent>(ent, out _))
            {
                var ignoreResistances = !HasComp<MobStateComponent>(ent);
                var appliedDamage = totalDamage;

                if (!ignoreResistances)
                {
                    var reduction = Math.Clamp(GetFireProtectionReduction(ent), 0f, 0.8f);
                    appliedDamage = totalDamage * (1f - reduction);
                }

                _damageable.TryChangeDamage(ent, appliedDamage, ignoreResistances: ignoreResistances);
            }

            if (TryComp<FlammableComponent>(ent, out var flammable))
            {
                _flammable.AdjustFireStacks(ent, 1f, flammable);
                _flammable.Ignite(ent, uid, flammable);
            }

            RaiseLocalEvent(ent, ref fireEvent);
        }
    }

    private void HeatAtmosphere(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid)
            return;

        var tilePos = _transform.GetGridTilePositionOrDefault((uid, xform));
        var tileMix = _atmos.GetTileMixture(gridUid, null, tilePos, excite: true);
        if (tileMix == null)
            return;

        var oxygenMoles = tileMix.GetMoles(Gas.Oxygen);
        if (oxygenMoles > 0.05f)
        {
            var consumedO2 = MathF.Min(0.2f, oxygenMoles);
            tileMix.AdjustMoles(Gas.Oxygen, -consumedO2);
            tileMix.AdjustMoles(Gas.CarbonDioxide, consumedO2 * 0.7f);
        }

        var maxTemp = Atmospherics.T0C + 350f;
        if (tileMix.Temperature < maxTemp)
        {
            tileMix.Temperature = MathF.Min(tileMix.Temperature + 25f, maxTemp);
        }
    }

    private void TryEmitSmoke(EntityUid uid)
    {
        var coords = Transform(uid).Coordinates;
        _nearbySmoke.Clear();
        _lookup.GetEntitiesInRange(coords, 0.8f, _nearbySmoke);
        foreach (var near in _nearbySmoke)
        {
            if (HasComp<SmokeComponent>(near))
                return;
        }

        if (!_random.Prob(0.30f))
            return;

        var smoke = Spawn(FireSteamPrototype, coords);
        if (TryComp<SmokeComponent>(smoke, out var smokeComp))
        {
            _smoke.StartSmoke(smoke, new Solution(), duration: 8f, spreadAmount: 1, smokeComp);
        }
    }
}
