using Robust.Shared.GameStates;

namespace Content.Shared.Ashfall.LockPick.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class TargetLockPickComponent : Component
{
    /// <summary>
    /// Base duration in seconds required to pick this lock.
    /// </summary>
    [DataField]
    public float Time = 8.0f;

    /// <summary>
    /// Probability of successful picking (0.0 to 1.0).
    /// </summary>
    [DataField]
    public float Chance = 0.65f;
}
