using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared.Ashfall.Barricade;

/// <summary>
/// Allows an entity to act as cover, blocking projectiles with distance-dependent chance.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BarricadeComponent : Component
{
    /// <summary>
    /// Interception chance if the shooter is within MinDistance (e.g. hugging the cover).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinHitChance = 0f;

    /// <summary>
    /// Maximum interception chance if shooter is at or beyond MaxDistance.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxHitChance = 0.75f;

    [DataField, AutoNetworkedField]
    public float MinDistance = 1.5f;

    [DataField, AutoNetworkedField]
    public float MaxDistance = 12f;

    /// <summary>
    /// Whitelisted entities that will always pass through the barricade.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;
}
