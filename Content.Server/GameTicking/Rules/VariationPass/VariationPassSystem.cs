using Ashfall.Server.Degradation.Components;
using Content.Server._Starlight.Zones;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking.Rules;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.GameTicking.Rules.VariationPass;

/// <summary>
///     Base class for procedural variation rule passes, which apply some kind of variation to a station,
///     so we simply reduce the boilerplate for the event handling a bit with this.
/// </summary>
public abstract partial class VariationPassSystem<T> : GameRuleSystem<T>
    where T: IComponent
{
    [Dependency] protected ServerStationSystem Stations = default!;
    [Dependency] protected IRobustRandom Random = default!;
    [Dependency] protected ZoneSystem Zones = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<T, StationVariationPassEvent>(ApplyVariation);
    }

    protected bool IsMemberOfStation(Entity<TransformComponent> ent, ref StationVariationPassEvent args)
    {
        return Stations.GetOwningStation(ent, ent.Comp) == args.Station.Owner;
    }

    protected bool IsTarget(EntityUid rule, Entity<TransformComponent> ent, ref StationVariationPassEvent args)
    {
        if (!IsMemberOfStation(ent, ref args))
            return false;
        if (!TryComp<DegradationZoneTargetComponent>(rule, out var target))
            return true;
        return Zones.TryGetZone((ent.Owner, (TransformComponent?) ent.Comp), out var zone) && zone.ID == target.Zone.Id;
    }

    protected bool TryFindRandomTargetTile(EntityUid rule,
        Entity<Content.Shared.Station.Components.StationDataComponent> station,
        out EntityCoordinates coordinates)
    {
        for (var i = 0; i < 64; i++)
        {
            if (!Stations.TryFindRandomTileOnStation(station, out _, out _, out coordinates))
                continue;
            if (!TryComp<DegradationZoneTargetComponent>(rule, out var target) ||
                Zones.TryGetZone(coordinates, out var zone) && zone.ID == target.Zone.Id)
                return true;
        }

        coordinates = default;
        return false;
    }

    protected abstract void ApplyVariation(Entity<T> ent, ref StationVariationPassEvent args);
}
