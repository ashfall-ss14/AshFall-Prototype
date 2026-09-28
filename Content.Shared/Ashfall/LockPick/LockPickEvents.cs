using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Ashfall.LockPick;

[Serializable, NetSerializable]
public sealed partial class LockPickDoAfterEvent : SimpleDoAfterEvent
{
}

[ByRefEvent]
public record struct LockPickSuccessEvent(EntityUid User, bool Success = false);
