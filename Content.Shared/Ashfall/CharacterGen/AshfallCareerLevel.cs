namespace Content.Shared.Ashfall.CharacterGen;

/// <summary>
///     Professional seniority within a single competency. Computed exclusively from accumulated
///     experience (never granted directly by any content prototype).
/// </summary>
public enum AshfallCareerLevel : byte
{
    Trainee = 0,
    Junior = 1,
    Qualified = 2,
    Experienced = 3,
    Senior = 4,
}

public static class AshfallCareerLevels
{
    // Experience thresholds for each level. A competency with less experience than the Junior
    // threshold is a Trainee. Values are tuned so ~1 year of core work at rate 1.0 per year
    // progresses: junior after ~1.5 years, qualified after ~4, experienced after ~8, senior after ~12.
    public const float JuniorThreshold = 1.5f;
    public const float QualifiedThreshold = 4f;
    public const float ExperiencedThreshold = 8f;
    public const float SeniorThreshold = 12f;

    public static AshfallCareerLevel FromExperience(float experience)
    {
        if (experience >= SeniorThreshold)
            return AshfallCareerLevel.Senior;
        if (experience >= ExperiencedThreshold)
            return AshfallCareerLevel.Experienced;
        if (experience >= QualifiedThreshold)
            return AshfallCareerLevel.Qualified;
        if (experience >= JuniorThreshold)
            return AshfallCareerLevel.Junior;
        return AshfallCareerLevel.Trainee;
    }

    public static float ThresholdFor(AshfallCareerLevel level) => level switch
    {
        AshfallCareerLevel.Senior => SeniorThreshold,
        AshfallCareerLevel.Experienced => ExperiencedThreshold,
        AshfallCareerLevel.Qualified => QualifiedThreshold,
        AshfallCareerLevel.Junior => JuniorThreshold,
        _ => 0f,
    };

    /// <summary>
    ///     Number of full level steps between two levels, negative when <paramref name="actual"/> is below <paramref name="required"/>.
    /// </summary>
    public static int StepsAbove(this AshfallCareerLevel actual, AshfallCareerLevel required)
    {
        return (int)actual - (int)required;
    }
}
