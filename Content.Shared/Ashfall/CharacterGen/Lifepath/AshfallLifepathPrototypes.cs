using Content.Shared.Ashfall.CharacterGen.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Roles;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Ashfall.CharacterGen.Lifepath;

/// <summary>
///     Data-driven narrative choice for the Guided Lifepath questionnaire.
/// </summary>
[Prototype]
public sealed partial class AshfallLifepathOptionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Which step this option belongs to (1 = Origin, 2 = Career Vector, 3 = Inner Flaw, 4 = Luggage).
    /// </summary>
    [DataField(required: true)]
    public int Step { get; private set; }

    /// <summary>
    ///     Localization string for the option's heading.
    /// </summary>
    [DataField(required: true)]
    public LocId Title { get; private set; } = default!;

    /// <summary>
    ///     Narrative description in Disco Elysium style.
    /// </summary>
    [DataField(required: true)]
    public LocId Description { get; private set; } = default!;

    /// <summary>
    ///     Department/theme indicator bar color on the left edge.
    /// </summary>
    [DataField]
    public Color IndicatorColor { get; private set; } = Color.FromHex("#D48944");

    /// <summary>
    ///     Tags describing this origin or option (e.g. origin-mining, origin-station, etc.).
    /// </summary>
    [DataField]
    public List<string> Tags { get; private set; } = new();

    /// <summary>
    ///     Tags this option matches against from prior steps for dynamic causal filtering.
    /// </summary>
    [DataField]
    public List<string> MatchingTags { get; private set; } = new();

    // --- Step 1: Biological Origin & Species ---

    [DataField]
    public ProtoId<SpeciesPrototype>? Species { get; private set; }

    [DataField]
    public ProtoId<AshfallCulturePrototype>? Culture { get; private set; }

    // --- Step 2: Youth & Career Vector ---

    [DataField]
    public string? Domain { get; private set; }

    /// <summary>
    ///     Base uniform job used to clothe the preview dummy at Step 2.
    /// </summary>
    [DataField]
    public ProtoId<JobPrototype>? PreviewJob { get; private set; }

    // --- Step 3: Inner Flaw & Psychotype ---

    [DataField]
    public ProtoId<AshfallPsychotypePrototype>? Psychotype { get; private set; }

    [DataField]
    public LocId? StressWeaknessLoc { get; private set; }

    // --- Step 4: Personal Luggage & Hook ---

    [DataField]
    public ProtoId<AshfallCharacterLoreFragmentPrototype>? HookFragment { get; private set; }

    [DataField]
    public LocId? QuirkDescription { get; private set; }

    /// <summary>
    ///     Cosmetic accessory or item identifier applied as the finishing touch.
    /// </summary>
    [DataField]
    public string? AccessoryId { get; private set; }

    /// <summary>
    ///     Experience tier (0 = young, 1 = mid, 2 = elder) set by the age-step options only.
    /// </summary>
    [DataField]
    public int ExperienceTier { get; private set; } = -1;

    /// <summary>
    ///     Context text variants. The first variant whose conditions match the choices made in
    ///     previous steps replaces the base description (and optionally the weakness or quirk).
    /// </summary>
    [DataField]
    public List<AshfallLifepathTextVariant> Variants { get; private set; } = new();
}

/// <summary>
///     An alternative text for a lifepath option, shown when earlier answers match its conditions.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class AshfallLifepathTextVariant
{
    /// <summary>
    ///     Any-of match against the tags accumulated from previous steps.
    /// </summary>
    [DataField]
    public List<string>? RequiresTags { get; private set; }

    /// <summary>
    ///     Exact match against the craft domain chosen at the previous step.
    /// </summary>
    [DataField]
    public string? RequiresDomain { get; private set; }

    [DataField]
    public ProtoId<AshfallPsychotypePrototype>? RequiresPsychotype { get; private set; }

    [DataField(required: true)]
    public LocId Description { get; private set; } = default!;

    [DataField]
    public LocId? StressWeaknessLoc { get; private set; }

    [DataField]
    public LocId? QuirkDescription { get; private set; }
}

/// <summary>
///     Encapsulates player's choices across all 4 steps of the Guided Lifepath.
/// </summary>
[Serializable, NetSerializable]
public sealed class AshfallLifepathChoices
{
    public int TargetSlotIndex { get; set; } = 0;

    public ProtoId<AshfallLifepathOptionPrototype> Step1Origin { get; set; } = default!;
    public ProtoId<AshfallLifepathOptionPrototype> Step2Vector { get; set; } = default!;
    public ProtoId<AshfallLifepathOptionPrototype> Step3Flaw { get; set; } = default!;
    public ProtoId<AshfallLifepathOptionPrototype> Step4Luggage { get; set; } = default!;

    /// <summary>
    ///     Chosen profession from the filtered list of same-domain jobs.
    /// </summary>
    public ProtoId<JobPrototype> SelectedJob { get; set; } = default!;

    /// <summary>
    ///     Experience category: 0 = Junior/Graduate, 1 = Experienced Specialist, 2 = Veteran.
    /// </summary>
    public int ExperienceTier { get; set; } = 1;

    /// <summary>
    ///     Custom name if player chose to overwrite procedural cultural name.
    /// </summary>
    public string? CustomName { get; set; }

    /// <summary>
    ///     Sex rolled for the client-side preview. The server keeps it only when the
    ///     chosen origin's species actually allows it; the rest of the identity is
    ///     rebuilt server-side from the origin constraints.
    /// </summary>
    public Sex SelectedSex { get; set; } = Sex.Male;
}
