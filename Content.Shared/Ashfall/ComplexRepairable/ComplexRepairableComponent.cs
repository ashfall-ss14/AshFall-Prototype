using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Content.Shared.Tools;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Ashfall.ComplexRepairable;

/// <summary>
/// Marks an entity as requiring multi-stage repairs with material insertion and welding fuel when heavily damaged.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComplexRepairableComponent : Component
{
    /// <summary>
    /// Specific damage amounts to heal. If null, heals all damage on the entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier? Damage;

    /// <summary>
    /// Welding fuel cost to perform the final repair step.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 FuelCost = 5;

    /// <summary>
    /// Prototype ID of the material stack needed to patch breaches (e.g. Steel, Plasteel, WoodPlank).
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<StackPrototype> Material = "Steel";

    /// <summary>
    /// Number of material sheets left to insert before welding can begin.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int LeftToInsert;

    /// <summary>
    /// For every this much damage taken, one piece of material must be inserted.
    /// </summary>
    [DataField("materialRepairTreshold"), AutoNetworkedField]
    public FixedPoint2 MaterialRepairTreshold = 50;

    /// <summary>
    /// Tool quality needed for the final repair step (default Welding).
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ToolQualityPrototype> QualityNeeded = "Welding";

    /// <summary>
    /// Base modifier applied to repair DoAfter time.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 DoAfterModifier = 1;

    /// <summary>
    /// Time multiplier penalty if entity repairs itself.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SelfRepairPenalty = 3f;

    /// <summary>
    /// Whether self-repair is allowed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool AllowSelfRepair = true;
}
