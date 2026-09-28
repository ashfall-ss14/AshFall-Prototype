using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared.Ashfall.LockPick.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class LockpickComponent : Component
{
    /// <summary>
    /// Multiplier applied to target lockpick time (lower = faster).
    /// </summary>
    [DataField]
    public float SpeedModifier = 1.0f;

    /// <summary>
    /// Sound played when lockpicking begins or clicks.
    /// </summary>
    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Machines/door_lock_off.ogg");
}
