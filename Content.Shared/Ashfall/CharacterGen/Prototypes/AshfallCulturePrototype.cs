using Content.Shared.Dataset;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Ashfall.CharacterGen.Prototypes;

/// <summary>
///     A cultural / naming group of a species. Determines given names and surnames; the
///     birthplace is biased by both culture and origin but never determined by either.
///     Can optionally constrain the skin tone melanin range for human cultures.
/// </summary>
[Prototype]
public sealed partial class AshfallCulturePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<SpeciesPrototype> Species { get; private set; }

    [DataField]
    public float Weight { get; private set; } = 1f;

    /// <summary>
    ///     Player-facing cultural line shown in the dossier header.
    /// </summary>
    [DataField(required: true)]
    public LocId Label { get; private set; }

    [DataField(required: true)]
    public ProtoId<DatasetPrototype> FirstNamesMale { get; private set; }

    [DataField(required: true)]
    public ProtoId<DatasetPrototype> FirstNamesFemale { get; private set; }

    [DataField(required: true)]
    public ProtoId<DatasetPrototype> LastNames { get; private set; }

    /// <summary>
    ///     Applies Slavic female surname declension (Panslavic cultures).
    /// </summary>
    [DataField]
    public bool SurnameDeclension { get; private set; }

    /// <summary>
    ///     Birthplace datasets this culture biases towards (weight 3 in the birthplace pool).
    /// </summary>
    [DataField]
    public List<ProtoId<DatasetPrototype>> BirthplaceDatasets { get; private set; } = new();

    /// <summary>
    ///     Optional min/max range of melanin skin tone (0-100) for human cultures.
    /// </summary>
    [DataField]
    public Vector2? SkinToneRange { get; private set; }

    /// <summary>
    ///     Culture-specific hairstyles that this culture can roll in dossier generation.
    ///     Hairstyles registered in this list across all cultures are considered restricted
    ///     and will NOT be rolled by cultures that do not include them.
    /// </summary>
    [DataField]
    public List<ProtoId<MarkingPrototype>> CultureHairstyles { get; private set; } = new();

    /// <summary>
    ///     Lifepath affinities: soft weight multipliers that make some careers and traits more
    ///     likely for this culture's members. Nothing is locked, the environment only predisposes.
    /// </summary>
    [DataField]
    public List<AshfallCultureLifepathAffinity> LifepathAffinities { get; private set; } = new();
}

/// <summary>
///     A soft sampling weight for lifepath options whose tags match this affinity.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class AshfallCultureLifepathAffinity
{
    /// <summary>
    ///     Any-of tags matched against an option's tags and matching tags.
    /// </summary>
    [DataField(required: true)]
    public List<string> Tags { get; private set; } = new();

    /// <summary>
    ///     Weight multiplier applied to matching options; 1.0 is neutral.
    /// </summary>
    [DataField]
    public float Weight { get; private set; } = 1f;
}
