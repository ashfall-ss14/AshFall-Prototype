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

    /// <summary>
    /// Probability that a completed attempt shocks the user (0.0 to 1.0).
    /// Insulated gloves negate the shock entirely.
    /// </summary>
    [DataField]
    public float ShockChance = 0.30f;

    /// <summary>
    /// Shock damage dealt to an uninsulated user.
    /// </summary>
    [DataField]
    public int ShockDamage = 15;

    /// <summary>
    /// Shock stun duration in seconds for an uninsulated user.
    /// </summary>
    [DataField]
    public float ShockTime = 3.0f;
}
