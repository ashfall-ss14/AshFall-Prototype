using Robust.Shared.Serialization;

namespace Content.Shared.Botany.Events;

/// <summary>
/// Event of plant growing ticking.
/// </summary>
[ByRefEvent]
[Serializable, NetSerializable]
public readonly record struct PlantGrowEvent(NetEntity Tray);

/// <summary>
/// Event raised when a harvest is attempted.
/// </summary>
[ByRefEvent]
public record struct PlantHarvestAttemptEvent(EntityUid User, EntityUid Target, EntityUid? Used = null, bool Cancelled = false);

/// <summary>
/// Event raised after a plant has been harvested.
/// </summary>
[ByRefEvent]
public readonly record struct PlantHarvestedEvent(EntityUid User, EntityUid Target);
