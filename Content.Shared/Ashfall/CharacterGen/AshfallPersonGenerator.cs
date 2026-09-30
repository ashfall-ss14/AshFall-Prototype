using Content.Shared.Ashfall.CharacterGen.Prototypes;
using Content.Shared.Ashfall.CharacterGen.Lifepath;
using Content.Shared.Dataset;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Enums;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Shared.Ashfall.CharacterGen;

/// <summary>
///     Generates a structured person first (culture → origin → education → career roles →
///     competencies with provenance), then derives dossier text and job eligibility from that
///     structure. No visible field is rolled independently.
/// </summary>
public sealed class AshfallPersonGenerator
{
    private const int MaxGenerationAttempts = 3;
    private const int LadderRetryBudget = 4;

    // Accent color of the dossier section headers, reused for the education type tags.
    private const string EducationTagColor = "#C8782E";

    /// <summary>
    ///     Families without entry-level assignments: a mid-life retraining into them would leave
    ///     the person without a job in the new field until a leadership post.
    /// </summary>
    private static readonly HashSet<string> RetrainingExcludedFamilies = new() { "Command", "Media" };
    private const int AgeEducationStart = 17;

    private static readonly ProtoId<CharacterGenConstraintsPrototype> DefaultHumanConstraints = "HumanDefaultConstraints";
    private static readonly ProtoId<SpeciesPrototype> DefaultSpeciesHuman = "Human";
    private static readonly ProtoId<AshfallCulturePrototype> DefaultCultureMashriqi = "Mashriqi";

    // Career event generation is reserved for the next phase of the rework; the pipeline layer
    // already exists and content can be enabled by flipping this flag once events are authored.
    private static readonly bool EnableCareerEvents = false;

    private readonly IPrototypeManager _prototypes;
    private readonly AshfallCharacterGenerator _characterGenerator;
    private readonly ISawmill _sawmill;

    public AshfallPersonGenerator(
        IPrototypeManager prototypes,
        AshfallCharacterGenerator characterGenerator)
    {
        _prototypes = prototypes;
        _characterGenerator = characterGenerator;
        _sawmill = Logger.GetSawmill("ashfall.chargen");
    }

    public (AshfallCharacterCandidate Candidate, AshfallPersonStructure Structure) GenerateCandidate(
        CharacterGenConstraintsPrototype constraints,
        IRobustRandom random,
        string? targetDomain = null)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxGenerationAttempts; attempt++)
        {
            try
            {
                return GenerateValidatedCandidate(constraints, random, targetDomain);
            }
            catch (AshfallValidationException e)
            {
                lastError = e;
                _sawmill.Warning($"Ashfall candidate validation failed (attempt {attempt}): {e.Message}");
            }
        }

        _sawmill.Error(
            $"Ashfall candidate generation failed {MaxGenerationAttempts} times, falling back to a conservative structure. Last reason: {lastError?.Message}");
        return GenerateConservativeCandidate(constraints, random, targetDomain);
    }

    /// <summary>
    ///     Generates a coherent candidate strictly constrained by player's Guided Lifepath choices.
    /// </summary>
    public (AshfallCharacterCandidate Candidate, AshfallPersonStructure Structure) GenerateFromLifepath(
        AshfallLifepathChoices choices,
        IRobustRandom random)
    {
        _prototypes.TryIndex(choices.Step1Origin, out var step1);
        ProtoId<SpeciesPrototype> speciesId = step1?.Species ?? DefaultSpeciesHuman;
        var species = _prototypes.Index(speciesId);

        ProtoId<AshfallCulturePrototype> cultureId = step1?.Culture ?? DefaultCultureMashriqi;
        var culture = _prototypes.Index(cultureId);

        CharacterGenConstraintsPrototype constraints;
        if (_prototypes.TryIndex<CharacterGenConstraintsPrototype>($"{species.ID}DefaultConstraints", out var foundConstraints))
        {
            constraints = foundConstraints;
        }
        else
        {
            constraints = _prototypes.EnumeratePrototypes<CharacterGenConstraintsPrototype>()
                .FirstOrDefault(c => c.Species == species.ID) ?? _prototypes.Index(DefaultHumanConstraints);
        }

        HumanoidCharacterProfile profile;
        Sex sex;
        Gender gender;
        int age;
        string morphology = string.Empty;

        // Identity is rebuilt server-side from the origin constraints; the client only
        // contributes the preview's sex roll (kept when the species allows it) and,
        // after an explicit reroll, the custom name.
        age = choices.ExperienceTier switch
        {
            0 => random.Next(19, 25),
            2 => random.Next(44, 60),
            _ => random.Next(27, 40),
        };

        if (species.Sexes.Contains(choices.SelectedSex))
        {
            sex = choices.SelectedSex;
        }
        else
        {
            sex = random.Pick(species.Sexes);
        }

        gender = sex switch
        {
            Sex.Female => Gender.Female,
            Sex.Male => Gender.Male,
            _ => Gender.Epicene
        };

        var name = !string.IsNullOrWhiteSpace(choices.CustomName)
            ? choices.CustomName
            : GenerateName(culture, gender, random);

        var (appearance, morph) = _characterGenerator.GenerateAppearance(constraints, species, sex, age, random, culture);
        morphology = morph;
        profile = _characterGenerator.BuildProfile(name, species, sex, gender, age, appearance, random);

        var person = new AshfallPersonStructure
        {
            SpeciesId = species.ID,
            Age = age,
            Culture = culture.ID,
        };
        person.StructureTags.Add($"species-{species.ID.ToLowerInvariant()}");

        var origin = PickOrigin(person.StructureTags, random);
        person.Origin = origin.ID;
        person.StructureTags.UnionWith(origin.ProvidedTags);
        person.Birthplace = GenerateBirthplace(culture, origin, random);

        _prototypes.TryIndex(choices.Step2Vector, out var step2);
        var domain = step2?.Domain ?? "Engineering";

        var education = PickEducation(age, person.StructureTags, domain, random);
        person.Education = education.ID;
        var educationYears = random.Next(education.YearsMin, education.YearsMax + 1);
        person.AvailableCareerYears = Math.Max(0, age - (AgeEducationStart + educationYears));
        person.StructureTags.Add($"domain-{education.Domain}");
        person.StructureTags.UnionWith(education.ProvidedTags);

        foreach (var (competency, experience) in education.BaseExperience)
        {
            person.AddExperience(competency, experience, AshfallProvenanceSource.Education, education.ID);
        }

        GenerateCareer(person, education, domain, random);

        if (EnableCareerEvents)
            GenerateCareerEvents(person, random);

        PickCertifications(person, random);

        person.LeadershipHistory = person.Career.Any(s => s.Leadership);
        if (person.LeadershipHistory)
            person.StructureTags.Add("leadership");

        if (person.Competencies.Values.Any(c => c.Level >= AshfallCareerLevel.Senior))
            person.StructureTags.Add("senior-career");

        ComputeCurrentIdentity(person, education);
        person.PrimaryDomain = domain;

        if (person.Age < 28)
            person.StructureTags.Add("age-young");
        else if (person.Age >= 50)
            person.StructureTags.Add("age-elder");

        _prototypes.TryIndex(choices.Step3Flaw, out var step3);
        if (step3?.Psychotype is { } ptId && _prototypes.TryIndex(ptId, out var indexedPt))
        {
            person.Psychotype = indexedPt.ID;
            var ptTag = string.IsNullOrEmpty(indexedPt.Tag)
                ? $"psychotype-{indexedPt.ID.ToLowerInvariant()}"
                : indexedPt.Tag;
            person.StructureTags.Add(ptTag);
        }
        else
        {
            var psychotype = PickPsychotype(person.StructureTags, random);
            if (psychotype != null)
            {
                person.Psychotype = psychotype.ID;
                var ptTag = string.IsNullOrEmpty(psychotype.Tag)
                    ? $"psychotype-{psychotype.ID.ToLowerInvariant()}"
                    : psychotype.Tag;
                person.StructureTags.Add(ptTag);
            }
        }

        var eligible = AshfallJobScorer.ScoreEligibleJobs(person, _prototypes);
        // A requested job is a preference only. It never adds qualification evidence.
        if (!string.IsNullOrEmpty(choices.SelectedJob.Id) &&
            _prototypes.TryIndex<AshfallJobCareerPrototype>(choices.SelectedJob.Id, out var selectedCareer) &&
            selectedCareer.Domain == domain &&
            eligible.Contains(choices.SelectedJob))
        {
            eligible.Remove(choices.SelectedJob);
            eligible.Insert(0, choices.SelectedJob);
        }

        var candidate = AssembleCandidate(profile, person, sex, morphology, random, eligible);

        _prototypes.TryIndex(choices.Step4Luggage, out var step4);
        if (step4?.HookFragment is { } hookId && _prototypes.TryIndex(hookId, out var hookFrag))
        {
            var hookSection = candidate.Dossier.Sections.FirstOrDefault(s => s.Kind == "hook");
            if (hookSection != null)
            {
                hookSection.Lines.Clear();
                hookSection.Lines.Add(Loc.GetString(hookFrag.Text, ("sex", sex == Sex.Female ? "female" : "male")));
            }
        }

        return (candidate, person);
    }


    private (AshfallCharacterCandidate, AshfallPersonStructure) GenerateValidatedCandidate(
        CharacterGenConstraintsPrototype constraints,
        IRobustRandom random,
        string? targetDomain)
    {
        var (profile, structure, sex, morphology, eligible) =
            GenerateStructure(constraints, random, targetDomain, withCareer: true);

        // Validation runs on the structured person BEFORE any dossier text is assembled:
        // an impossible profile is regenerated without wasting loc template work.
        var failure = Validate(structure, eligible);
        if (failure != null)
            throw new AshfallValidationException(failure);

        var candidate = AssembleCandidate(profile, structure, sex, morphology, random, eligible);
        return (candidate, structure);
    }

    /// <summary>
    ///     Known-valid minimal structure: education only, no career history. Fallback-only jobs
    ///     (Passenger and similar) remain eligible, so the pool never hangs on broken content.
    /// </summary>
    private (AshfallCharacterCandidate, AshfallPersonStructure) GenerateConservativeCandidate(
        CharacterGenConstraintsPrototype constraints,
        IRobustRandom random,
        string? targetDomain)
    {
        try
        {
            var (profile, structure, sex, morphology, eligible) =
                GenerateStructure(constraints, random, targetDomain, withCareer: false);
            var candidate = AssembleCandidate(profile, structure, sex, morphology, random, eligible);
            return (candidate, structure);
        }
        catch (Exception e)
        {
            // Last-resort candidate: the pool must never hang on broken content.
            _sawmill.Error($"Ashfall conservative generation failed, emitting an empty candidate: {e.Message}");
            var profile = GenerateIdentityOnly(constraints, random);
            var candidate = new AshfallCharacterCandidate
            {
                Profile = profile,
                PrimaryDomain = "Service",
                CompatibleJobs = { new ProtoId<JobPrototype>("Passenger") },
            };
            return (candidate, new AshfallPersonStructure { PrimaryDomain = "Service" });
        }
    }

    private HumanoidCharacterProfile GenerateIdentityOnly(
        CharacterGenConstraintsPrototype constraints,
        IRobustRandom random)
    {
        var species = _prototypes.Index(constraints.Species);
        var (sex, gender, age) = _characterGenerator.GenerateIdentity(constraints, random);
        var (appearance, _) = _characterGenerator.GenerateAppearance(constraints, species, sex, age, random);
        return _characterGenerator.BuildProfile("...", species, sex, gender, age, appearance, random);
    }

    private (HumanoidCharacterProfile Profile, AshfallPersonStructure Structure, Sex Sex, string Morphology, List<ProtoId<JobPrototype>> EligibleJobs) GenerateStructure(
        CharacterGenConstraintsPrototype constraints,
        IRobustRandom random,
        string? targetDomain,
        bool withCareer)
    {
        var species = _prototypes.Index(constraints.Species);

        // 1. Culture: chosen before appearance; naming group only, skin tone is not an input.
        var culture = PickWeighted(
            _prototypes.EnumeratePrototypes<AshfallCulturePrototype>()
                .Where(c => c.Species == species.ID)
                .Select(c => (c, c.Weight))
                .ToList(),
            random);

        // 2. Identity (sex, gender, age) and name.
        var (sex, gender, age) = _characterGenerator.GenerateIdentity(constraints, random);
        var name = GenerateName(culture, gender, random);

        // 3. Appearance, decoupled from culture.
        var (appearance, morphology) = _characterGenerator.GenerateAppearance(constraints, species, sex, age, random, culture);
        var profile = _characterGenerator.BuildProfile(name, species, sex, gender, age, appearance, random);

        var person = new AshfallPersonStructure
        {
            SpeciesId = species.ID,
            Age = age,
            Culture = culture.ID,
        };
        person.StructureTags.Add($"species-{species.ID.ToLowerInvariant()}");

        // 4. Origin: biases what follows, never gates it.
        var origin = PickOrigin(person.StructureTags, random);
        person.Origin = origin.ID;
        person.StructureTags.UnionWith(origin.ProvidedTags);

        // 5. Birthplace: culture and origin bias the pool, neither determines it.
        person.Birthplace = GenerateBirthplace(culture, origin, random);

        // 6. Education.
        var education = PickEducation(age, person.StructureTags, targetDomain, random);
        person.Education = education.ID;
        var educationYears = random.Next(education.YearsMin, education.YearsMax + 1);
        person.AvailableCareerYears = Math.Max(0, age - (AgeEducationStart + educationYears));
        person.StructureTags.Add($"domain-{education.Domain}");
        person.StructureTags.UnionWith(education.ProvidedTags);

        foreach (var (competency, experience) in education.BaseExperience)
        {
            person.AddExperience(competency, experience, AshfallProvenanceSource.Education, education.ID);
        }

        // 7. Career history: a trajectory inside the professional family, bounded by the
        // chronology and deliberately leaving gaps.
        if (withCareer)
            GenerateCareer(person, education, targetDomain, random);

        // 8. Career events (reserved layer, disabled until content exists).
        if (EnableCareerEvents)
            GenerateCareerEvents(person, random);

        // 8b. Additional education lines: a specialization for established specialists, a course
        // for older employees. Both stay inside the career families.
        PickCertifications(person, random);

        person.LeadershipHistory = person.Career.Any(s => s.Leadership);
        if (person.LeadershipHistory)
            person.StructureTags.Add("leadership");

        if (person.Competencies.Values.Any(c => c.Level >= AshfallCareerLevel.Senior))
            person.StructureTags.Add("senior-career");

        // 9. Current professional identity: the last career stage defines the track a person
        // keeps; older tracks remain only as historical provenance.
        ComputeCurrentIdentity(person, education);
        person.PrimaryDomain = ComputePrimaryDomain(person, education);

        if (person.Age < 28)
            person.StructureTags.Add("age-young");
        else if (person.Age >= 50)
            person.StructureTags.Add("age-elder");

        // 10. Psychotype selection: biases demeanor, stress responses and quirks.
        var psychotype = PickPsychotype(person.StructureTags, random);
        if (psychotype != null)
        {
            person.Psychotype = psychotype.ID;
            var ptTag = string.IsNullOrEmpty(psychotype.Tag)
                ? $"psychotype-{psychotype.ID.ToLowerInvariant()}"
                : psychotype.Tag;
            person.StructureTags.Add(ptTag);
        }

        var eligible = AshfallJobScorer.ScoreEligibleJobs(person, _prototypes);

        return (profile, person, sex, morphology, eligible);
    }

    // Dossier assembly: runs only after validation, deriving visible text from the structure.

    private AshfallCharacterCandidate AssembleCandidate(
        HumanoidCharacterProfile profile,
        AshfallPersonStructure person,
        Sex sex,
        string morphology,
        IRobustRandom random,
        List<ProtoId<JobPrototype>> eligible)
    {
        return new AshfallCharacterCandidate
        {
            Profile = profile,
            Dossier = AssembleDossier(person, sex, morphology, random),
            PrimaryDomain = person.PrimaryDomain,
            CompatibleJobs = eligible,
        };
    }

    // 7. Career history: the person's final professional identity is chosen first, and the
    // career ladder is built backward from it — education grows into the entry role, which
    // develops into the current profession. Stays inside the career family by construction.

    private void GenerateCareer(
        AshfallPersonStructure person,
        AshfallEducationPrototype education,
        string? targetDomain,
        IRobustRandom random)
    {
        person.CareerFamilies.Add(education.Domain);

        // Adjacent specialization: domains bridged by an education competency the person actually
        // mastered (Qualified+). E.g. medical chemistry (Chemistry) may step into science work.
        if (random.Prob(0.18f))
        {
            foreach (var (competencyId, experience) in education.BaseExperience)
            {
                if (experience < AshfallCareerLevels.QualifiedThreshold)
                    continue;

                var competency = _prototypes.Index(competencyId);
                person.CareerFamilies.UnionWith(competency.Domains);
            }
        }

        // A major career change is a rare, deliberate event, never ordinary stint selection.
        var retrainingDone = false;
        string? retrainingFamily = null;
        if (person.Age >= 26 && random.Prob(0.04f))
        {
            var newFamilies = _prototypes
                .EnumeratePrototypes<AshfallCareerRolePrototype>()
                .Select(r => r.Domain)
                .Where(d => !person.CareerFamilies.Contains(d))
                .Distinct()
                .ToList();

            foreach (var family in newFamilies)
            {
                // The retrained identity must be an entry role of the new field, reachable
                // without the old career's tags. If the family has none, no retraining happens.
                var entryExists = _prototypes
                    .EnumeratePrototypes<AshfallCareerRolePrototype>()
                    .Any(r => r.Domain == family &&
                              r.MinAge <= person.Age &&
                              r.RequiredTags.IsSubsetOf(person.StructureTags));

                if (!entryExists)
                    continue;

                retrainingFamily = family;
                person.CareerFamilies.Add(family);
                person.RetrainingCount++;
                person.StructureTags.Add("retrained");
                retrainingDone = true;
                break;
            }
        }

        if (person.AvailableCareerYears <= 0)
            return;

        // Not every year of adult life is employment: job searching, relocations, leave and
        // short unlisted contracts leave gaps between the shown stints.
        var targetYears = Math.Max(1, (int)(person.AvailableCareerYears * random.NextFloat(0.6f, 1f)));
        if (targetYears > person.AvailableCareerYears)
            targetYears = person.AvailableCareerYears;

        // A dossier only shows the stages that matter; the hidden chronology stays richer.
        var stintCap = person.Age < 30 ? 2 : person.Age < 45 ? 3 : 4;

        // --- Final professional identity: who this person has become ---
        var finalCandidates = new List<(AshfallCareerRolePrototype Role, float Weight)>();
        foreach (var role in _prototypes.EnumeratePrototypes<AshfallCareerRolePrototype>())
        {
            if (retrainingDone)
            {
                // A retrained person's current identity is the new profession, at its entry level:
                // a fresh start carries no prior-family work history tags.
                if (role.Domain != retrainingFamily)
                    continue;
                if (role.MinAge > person.Age)
                    continue;
                if (!role.RequiredTags.IsSubsetOf(person.StructureTags))
                    continue;
            }
            else
            {
                if (!person.CareerFamilies.Contains(role.Domain))
                    continue;
                if (role.MinAge > person.Age)
                    continue;
                if (!role.RequiredTags.IsSubsetOf(person.StructureTags) && !ChainCanProvide(role, person, random))
                    continue;
            }

            // Entry roles can only be the first career stint: as a final identity they are only
            // valid for a fresh career, never for an established professional (retrainings
            // legitimately start at an entry role of the new field).
            if (role.EntryRole && !retrainingDone && targetYears >= 3)
                continue;

            // Seniority-appropriate identities: long careers aim at senior and supervisory
            // professions, short ones at entry and mid work.
            // A role whose requirements need predecessors needs career room for them.
            var needsLadder = !role.RequiredTags.IsSubsetOf(person.StructureTags);
            if (needsLadder && targetYears < 3)
                continue;

            var weight = GetWeight(role.Weight, role.WeightModifiers, person.StructureTags);
            var senior = role.Leadership || role.MinAge >= 28;
            if (senior)
                weight *= targetYears >= 8 ? 3f : 0.4f;
            if (targetDomain != null && role.Domain == targetDomain)
                weight *= 4f;

            if (weight > 0f)
                finalCandidates.Add((role, weight));
        }

        if (finalCandidates.Count == 0)
        {
            _sawmill.Warning($"Ashfall career: no final identity for age={person.Age} families=[{string.Join(",", person.CareerFamilies)}]");
            return;
        }

        // --- Backward ladder: predecessors that grow into the final role. If a pick gets
        // stuck without its required history, it is retried with a different final role. ---
        List<AshfallCareerRolePrototype> ladder;
        HashSet<string> missingTags;
        AshfallCareerRolePrototype finalRole;
        var ladderComplete = false;
        var ladderRetries = LadderRetryBudget;
        var held = new HashSet<string>();
        do
        {
            finalRole = PickWeighted(finalCandidates, random);

            ladder = new List<AshfallCareerRolePrototype> { finalRole };
            held.Clear();
            held.Add(finalRole.ID);
            missingTags = new HashSet<string>(finalRole.RequiredTags);
            missingTags.ExceptWith(person.StructureTags);

            while (ladder.Count < stintCap && missingTags.Count > 0)
            {
                AshfallCareerRolePrototype? provider = null;
                var providerWeight = 0f;

                foreach (var role in _prototypes.EnumeratePrototypes<AshfallCareerRolePrototype>())
                {
                    if (!person.CareerFamilies.Contains(role.Domain))
                        continue;
                    if (held.Contains(role.ID))
                        continue;
                    if (role.ProvidedTags.Count == 0 || !role.ProvidedTags.Overlaps(missingTags))
                        continue;
                    if (role.MinAge > finalRole.MinAge)
                        continue;
                    // Providers are prepended, so an entry role may only join as the last insert:
                    // the earliest stint, which is exactly where entry roles belong.
                    if (role.EntryRole && !missingTags.IsSubsetOf(role.ProvidedTags))
                        continue;

                    var weight = GetWeight(role.Weight, role.WeightModifiers, person.StructureTags);
                    if (weight > providerWeight)
                    {
                        provider = role;
                        providerWeight = weight;
                    }
                }

                if (provider == null)
                    break;

                ladder.Insert(0, provider);
                held.Add(provider.ID);
                missingTags.ExceptWith(provider.ProvidedTags);
                missingTags.UnionWith(provider.RequiredTags);
                missingTags.ExceptWith(person.StructureTags);
            }

            ladderComplete = missingTags.Count == 0;
            if (!ladderComplete)
                finalCandidates.RemoveAll(entry => entry.Role.ID == finalRole.ID);
        }
        while (!ladderComplete && --ladderRetries > 0 && finalCandidates.Count > 0);

        if (!ladderComplete)
        {
            // Retry budget exhausted: fall back to a role whose requirements the person already
            // meets, so the career never ends in a stage without its prerequisites.
            var covered = finalCandidates
                .Where(entry => entry.Role.RequiredTags.IsSubsetOf(person.StructureTags))
                .ToList();
            if (covered.Count > 0)
            {
                finalRole = PickWeighted(covered, random);
                ladder = new List<AshfallCareerRolePrototype> { finalRole };
                ladderComplete = true;
            }
        }

        if (!ladderComplete)
        {
            _sawmill.Warning($"Ashfall career: incomplete ladder for {finalRole.ID}, age={person.Age}, missing=[{string.Join(",", missingTags)}]");
        }

        // A long career shows progression even when the tags are already covered: pad the
        // ladder with earlier same-family roles that share competencies with the history.
        while (ladder.Count < stintCap && random.Prob(0.65f))
        {
            var earliest = ladder[0];
            // An entry role opens the career: nothing may be prepended before it.
            if (earliest.EntryRole)
                break;

            AshfallCareerRolePrototype? earlier = null;
            var earlierWeight = 0f;

            foreach (var role in _prototypes.EnumeratePrototypes<AshfallCareerRolePrototype>())
            {
                if (!person.CareerFamilies.Contains(role.Domain))
                    continue;
                if (held.Contains(role.ID))
                    continue;
                if (role.MinAge > earliest.MinAge)
                    continue;
                if (role.YearsMin > person.AvailableCareerYears / 2)
                    continue;
                // The earliest stage still grows out of the education field.
                if (!role.ExperiencePerYear.Keys.Any(education.BaseExperience.ContainsKey))
                    continue;

                var weight = GetWeight(role.Weight, role.WeightModifiers, person.StructureTags);
                if (weight > earlierWeight)
                {
                    earlier = role;
                    earlierWeight = weight;
                }
            }

            if (earlier == null)
                break;

            ladder.Insert(0, earlier);
            held.Add(earlier.ID);
        }

        // --- Years: assigned backward from the current age, so the final profession is the
        // latest stage and earlier stages sit where their age gates allow. Gaps between stints
        // are the unfilled parts of the timeline. ---
        var chronological = ladder.ToList();
        var planned = new List<(AshfallCareerRolePrototype Role, int Years)>();
        var remaining = targetYears;
        var cursorAge = person.Age;

        for (var i = chronological.Count - 1; i >= 0; i--)
        {
            var role = chronological[i];
            var isFinal = i == chronological.Count - 1;

            // The role must have been held old enough: its duration may reach back to the year
            // the age gate is reached (a 20-year-old's first barkeep year starts at 19).
            var maxByAge = cursorAge - role.MinAge + 1;
            if (maxByAge < 1)
                break;

            // Reserve timeline room for the predecessor stages of an incomplete ladder.
            var reserve = !isFinal || ladderComplete ? 0 : 1;
            var maxYears = Math.Max(1, Math.Min(Math.Min(role.YearsMax, remaining - reserve), maxByAge));
            var minYears = Math.Min(Math.Max(role.YearsMin, 1), maxYears);
            var years = isFinal
                ? Math.Max(minYears, (int)(Math.Min(remaining, maxByAge) * random.NextFloat(0.5f, 0.8f)))
                : random.Next(minYears, maxYears + 1);
            years = Math.Clamp(years, 1, maxYears);

            planned.Insert(0, (role, years));
            remaining -= years;
            if (remaining < 1)
                break;

            // Small gaps between contracts: transitions, relocations, job searching.
            cursorAge = cursorAge - years - random.Next(0, 2);
        }

        foreach (var (role, years) in planned)
        {
            var employer = PickEmployer(role, random);

            person.Career.Add(new AshfallCareerStint
            {
                Role = role.ID,
                Employer = employer?.ID,
                Years = years,
                Leadership = role.Leadership,
            });

            foreach (var (competency, perYear) in role.ExperiencePerYear)
            {
                person.AddExperience(competency, perYear * years, AshfallProvenanceSource.Career, role.ID);
            }

            person.StructureTags.UnionWith(role.ProvidedTags);
            person.CareerFamilies.Add(role.Domain);
        }


        person.UsedCareerYears = Math.Min(person.Career.Sum(s => s.Years), person.AvailableCareerYears);
    }

    /// <summary>
    ///     Whether the required tags of a role can be provided by other roles of the person's
    ///     career families (i.e. a backward ladder exists for it at all).
    /// </summary>
    private bool ChainCanProvide(
        AshfallCareerRolePrototype role,
        AshfallPersonStructure person,
        IRobustRandom random)
    {
        var missing = new HashSet<string>(role.RequiredTags);
        missing.ExceptWith(person.StructureTags);
        if (missing.Count == 0)
            return true;

        foreach (var provider in _prototypes.EnumeratePrototypes<AshfallCareerRolePrototype>())
        {
            if (provider.ID == role.ID)
                continue;
            if (!person.CareerFamilies.Contains(provider.Domain))
                continue;

            missing.ExceptWith(provider.ProvidedTags);
        }

        return missing.Count == 0;
    }

    private AshfallEmployerPrototype? PickEmployer(AshfallCareerRolePrototype role, IRobustRandom random)
    {
        var compatible = _prototypes
            .EnumeratePrototypes<AshfallEmployerPrototype>()
            .Where(e => e.Industries.Overlaps(role.EmployerIndustries))
            .Select(e => (e, e.Weight))
            .ToList();

        return compatible.Count == 0 ? null : PickWeighted(compatible, random);
    }

    // 8. Career events (reserved).

    private void GenerateCareerEvents(AshfallPersonStructure person, IRobustRandom random)
    {
        // A person receives at most one significant event, uncommonly.
        if (!random.Prob(0.25f) || person.Career.Count == 0)
            return;

        var eligible = _prototypes
            .EnumeratePrototypes<AshfallCareerEventPrototype>()
            .Where(e => e.MinAge <= person.Age)
            .Where(e => e.RequiredTags.IsSubsetOf(person.StructureTags))
            .Select(e => (e, GetWeight(e.Weight, e.WeightModifiers, person.StructureTags)))
            .Where(entry => entry.Item2 > 0f)
            .ToList();

        if (eligible.Count == 0)
            return;

        var @event = PickWeighted(eligible, random);
        person.Events.Add(@event.ID);

        foreach (var (competency, experience) in @event.BaseExperience)
        {
            person.AddExperience(competency, experience, AshfallProvenanceSource.Event, @event.ID);
        }

        person.StructureTags.UnionWith(@event.ProvidedTags);
    }

    // Selection helpers.

    private AshfallOriginPrototype PickOrigin(HashSet<string> tags, IRobustRandom random)
    {
        var eligible = new List<(AshfallOriginPrototype Origin, float Weight)>();
        foreach (var origin in _prototypes.EnumeratePrototypes<AshfallOriginPrototype>())
        {
            if (origin.RequiredTags.Count > 0 && !origin.RequiredTags.IsSubsetOf(tags))
                continue;
            if (origin.ExcludedTags.Count > 0 && origin.ExcludedTags.Overlaps(tags))
                continue;

            var weight = GetWeight(origin.Weight, origin.WeightModifiers, tags);
            if (weight > 0f)
                eligible.Add((origin, weight));
        }

        if (eligible.Count == 0)
            throw new AshfallValidationException("No origin prototype is eligible for the candidate's traits.");

        return PickWeighted(eligible, random);
    }

    private AshfallEducationPrototype PickEducation(int age, HashSet<string> tags, string? targetDomain, IRobustRandom random)
    {
        var eligible = new List<(AshfallEducationPrototype Education, float Weight)>();
        foreach (var education in _prototypes.EnumeratePrototypes<AshfallEducationPrototype>())
        {
            if (education.MinAge > age)
                continue;

            var weight = GetWeight(education.Weight, education.WeightModifiers, tags);
            if (targetDomain != null && education.Domain == targetDomain)
                weight *= 5f;

            if (weight > 0f)
                eligible.Add((education, weight));
        }

        if (eligible.Count == 0)
            throw new AshfallValidationException($"Age={age}: no education prototype is plausible at this age.");

        return PickWeighted(eligible, random);
    }

    private string GenerateName(AshfallCulturePrototype culture, Gender gender, IRobustRandom random)
    {
        var firstDataset = _prototypes.Index<DatasetPrototype>(
            gender == Gender.Female ? culture.FirstNamesFemale : culture.FirstNamesMale);
        var lastDataset = _prototypes.Index<DatasetPrototype>(culture.LastNames);

        var first = random.Pick(firstDataset.Values);
        var last = random.Pick(lastDataset.Values);

        if (culture.SurnameDeclension && gender == Gender.Female)
            last = ApplyGenderToSurname(last);

        return $"{first} {last}";
    }

    private string GenerateBirthplace(AshfallCulturePrototype culture, AshfallOriginPrototype origin, IRobustRandom random)
    {
        // Culture biases strongest, origin adds its own bias; a culture's birthplace is never fixed.
        var pool = new List<(DatasetPrototype Dataset, float Weight)>();
        foreach (var datasetId in culture.BirthplaceDatasets)
        {
            if (_prototypes.TryIndex(datasetId, out var cultureDataset))
                pool.Add((cultureDataset, 3f));
        }
        foreach (var datasetId in origin.BirthplaceDatasets)
        {
            if (_prototypes.TryIndex(datasetId, out var originDataset) && !pool.Any(p => p.Dataset.ID == originDataset.ID))
                pool.Add((originDataset, 2f));
        }

        if (pool.Count == 0)
            return string.Empty;

        var dataset = PickWeighted(pool, random);
        return dataset.Values.Count > 0 ? random.Pick(dataset.Values) : string.Empty;
    }

    private static string ApplyGenderToSurname(string surname)
    {
        if (string.IsNullOrWhiteSpace(surname))
            return surname;

        if (surname.EndsWith("ский", StringComparison.OrdinalIgnoreCase))
            return surname[..^4] + "ская";
        if (surname.EndsWith("цкий", StringComparison.OrdinalIgnoreCase))
            return surname[..^4] + "цкая";
        if (surname.EndsWith("ой", StringComparison.OrdinalIgnoreCase))
            return surname[..^2] + "ая";
        if (surname.EndsWith("ов", StringComparison.OrdinalIgnoreCase) ||
            surname.EndsWith("ев", StringComparison.OrdinalIgnoreCase) ||
            surname.EndsWith("ин", StringComparison.OrdinalIgnoreCase) ||
            surname.EndsWith("ын", StringComparison.OrdinalIgnoreCase))
        {
            return surname + "а";
        }

        return surname;
    }

    // 9. Current professional identity.

    private void ComputeCurrentIdentity(AshfallPersonStructure person, AshfallEducationPrototype education)
    {
        // The current track is the last career stage; without a career it is the education itself.
        var currentDomains = new HashSet<string>();
        if (person.Career.Count > 0)
        {
            currentDomains.Add(_prototypes.Index(person.Career[^1].Role).Domain);
        }
        else
        {
            currentDomains.Add(education.Domain);
        }

        person.CurrentDomains.UnionWith(currentDomains);

        // A competency stays active when it is exercised by the current track: current-field
        // education, or any stint whose work overlaps the current domains. An abandoned track
        // keeps its provenance but no longer grants assignments.
        if (currentDomains.Contains(education.Domain))
        {
            person.ActiveCompetencies.UnionWith(education.BaseExperience.Keys);
        }

        foreach (var stint in person.Career)
        {
            if (!_prototypes.TryIndex(stint.Role, out var role))
                continue;

            if (currentDomains.Contains(role.Domain) ||
                (role.Domains.Count > 0 && role.Domains.Overlaps(currentDomains)))
            {
                person.ActiveCompetencies.UnionWith(role.ExperiencePerYear.Keys);
            }
        }

        person.HasActiveRestriction = true;
    }

    // Additional education lines.

    private void PickCertifications(AshfallPersonStructure person, IRobustRandom random)
    {
        var hasEstablished = person.Competencies.Values.Any(c => c.Level >= AshfallCareerLevel.Qualified);

        // Specialists add a specialization; older employees sometimes add a course.
        if (!(hasEstablished && random.Prob(0.4f)) && !(person.Age >= 32 && random.Prob(0.25f)))
            return;

        var candidates = _prototypes
            .EnumeratePrototypes<AshfallCertificationPrototype>()
            .Where(c => person.CareerFamilies.Contains(c.Domain))
            .Select(c => (c, c.Weight))
            .ToList();

        if (candidates.Count == 0)
            return;

        var cert = PickWeighted(candidates, random);
        person.Certifications.Add(cert.ID);

        foreach (var (competency, experience) in cert.Experience)
        {
            person.AddExperience(competency, experience, AshfallProvenanceSource.Education, cert.ID);
        }
    }

    private string ComputePrimaryDomain(AshfallPersonStructure person, AshfallEducationPrototype education)
    {
        // The current profession is the identity; without a career it is the education field.
        if (person.Career.Count > 0)
            return _prototypes.Index(person.Career[^1].Role).Domain;

        string? bestDomain = null;
        var bestLevel = AshfallCareerLevel.Trainee;
        var bestExperience = 0f;

        foreach (var (competencyId, state) in person.Competencies.OrderBy(kvp => kvp.Key.Id, StringComparer.Ordinal))
        {
            var competency = _prototypes.TryIndex(competencyId, out var proto) ? proto : null;
            if (competency == null)
                continue;

            foreach (var domain in competency.Domains)
            {
                if (bestDomain == null ||
                    state.Level > bestLevel ||
                    (state.Level == bestLevel && state.Experience > bestExperience))
                {
                    bestDomain = domain;
                    bestLevel = state.Level;
                    bestExperience = state.Experience;
                }
            }
        }

        return bestDomain ?? "Service";
    }

    // Dossier assembly: runs only after validation, deriving visible text from the structure.

    private AshfallCharacterDossier AssembleDossier(AshfallPersonStructure person, Sex sex, string morphology, IRobustRandom random)
    {
        var sexKey = sex switch
        {
            Sex.Female => "female",
            Sex.Male => "male",
            _ => "other"
        };

        var sections = new List<AshfallDossierSection>();

        var origin = _prototypes.Index(person.Origin);
        sections.Add(new AshfallDossierSection
        {
            Kind = "origin",
            Title = Loc.GetString("ashfall-lore-title-origin"),
            Lines = { Loc.GetString(origin.Text, ("sex", sexKey)) },
        });

        var education = _prototypes.Index(person.Education);

        // Primary education diploma followed by bulleted certifications and retraining.
        var educationLine = Loc.GetString(education.Text, ("sex", sexKey));
        var educationLines = new List<string> { DomainTag(education.Domain) + educationLine };

        foreach (var certId in person.Certifications)
        {
            if (_prototypes.TryIndex(certId, out var cert))
                educationLines.Add("• " + CertTag(cert) + Loc.GetString(cert.Text, ("sex", sexKey)));
        }

        if (person.RetrainingCount > 0)
            educationLines.Add(DomainTag(person.PrimaryDomain) + Loc.GetString("ashfall-education-retrained", ("sex", sexKey)));

        sections.Add(new AshfallDossierSection
        {
            Kind = "education",
            Title = Loc.GetString("ashfall-lore-title-education"),
            Lines = educationLines,
        });

        // Established competencies only: trainee skills are not dossier-worthy qualifications.
        var qualifications = person.Competencies
            .Where(kvp => kvp.Value.Level >= AshfallCareerLevel.Qualified)
            .OrderByDescending(kvp => kvp.Value.Level)
            .ThenByDescending(kvp => kvp.Value.Experience)
            .Take(3);

        foreach (var (competencyId, _) in qualifications)
        {
            var competency = _prototypes.Index(competencyId);
            sections.Add(new AshfallDossierSection
            {
                Kind = "qualification",
                Title = Loc.GetString(competency.QualificationTitle),
                Lines = { Loc.GetString(competency.QualificationText, ("sex", sexKey)) },
            });
        }

        if (person.Career.Count > 0)
        {
            var careerLines = new List<string>();
            foreach (var stint in person.Career)
            {
                var role = _prototypes.Index(stint.Role);
                var employer = stint.Employer is { } employerId
                    ? Loc.GetString(_prototypes.Index(employerId).Name)
                    : Loc.GetString("ashfall-dossier-career-no-employer");

                careerLines.Add(Loc.GetString("ashfall-dossier-career-line",
                    ("role", Loc.GetString(role.Name)),
                    ("employer", employer),
                    ("years", stint.Years)));
            }

            sections.Add(new AshfallDossierSection
            {
                Kind = "career",
                Title = Loc.GetString("ashfall-lore-title-career"),
                Lines = careerLines,
            });
        }

        AddPersonalitySection(sections, person, random, sexKey);
        AddFragmentSection(sections, person, random, AshfallCharacterLoreCategory.Evaluation, "evaluation", "ashfall-lore-title-evaluation", sexKey);
        AddFragmentSection(sections, person, random, AshfallCharacterLoreCategory.PersonalHook, "hook", "ashfall-lore-title-personalhook", sexKey);
        AddFragmentSection(sections, person, random, AshfallCharacterLoreCategory.PreCryo, "precryo", "ashfall-lore-title-precryo", sexKey);

        var culture = _prototypes.Index(person.Culture);
        return new AshfallCharacterDossier
        {
            CulturalOrigin = Loc.GetString(culture.Label),
            CultureId = person.Culture,
            Birthplace = person.Birthplace,
            Morphology = morphology,
            Sections = sections,
        };
    }

    private void AddPersonalitySection(
        List<AshfallDossierSection> sections,
        AshfallPersonStructure person,
        IRobustRandom random,
        string sexKey)
    {
        AshfallPsychotypePrototype? psychotype = null;
        if (person.Psychotype is { } ptId && _prototypes.TryIndex(ptId, out var indexedPt))
        {
            psychotype = indexedPt;
        }
        else
        {
            psychotype = PickPsychotype(person.StructureTags, random);
            if (psychotype != null)
            {
                person.Psychotype = psychotype.ID;
                var ptTag = string.IsNullOrEmpty(psychotype.Tag)
                    ? $"psychotype-{psychotype.ID.ToLowerInvariant()}"
                    : psychotype.Tag;
                person.StructureTags.Add(ptTag);
            }
        }

        var lines = new List<string>();
        if (psychotype != null)
        {
            lines.Add(Loc.GetString("ashfall-lore-psychotype-line", ("type", Loc.GetString(psychotype.Name))));
        }

        // Slot 1: Demeanor (Category: Personality)
        var demeanor = PickFragment(AshfallCharacterLoreCategory.Personality, person.Age, person.StructureTags, sexKey, random);
        if (demeanor != null)
        {
            lines.Add(Loc.GetString("ashfall-lore-trait-demeanor-line",
                ("text", Loc.GetString(demeanor.Text, ("sex", sexKey)))));
        }

        // Slot 2: Under stress (Category: PersonalityStress)
        var stress = PickFragment(AshfallCharacterLoreCategory.PersonalityStress, person.Age, person.StructureTags, sexKey, random);
        if (stress != null)
        {
            lines.Add(Loc.GetString("ashfall-lore-trait-stress-line",
                ("text", Loc.GetString(stress.Text, ("sex", sexKey)))));
        }

        // Slot 3: Quirk / Habit (Category: PersonalityQuirk)
        var quirk = PickFragment(AshfallCharacterLoreCategory.PersonalityQuirk, person.Age, person.StructureTags, sexKey, random);
        if (quirk != null)
        {
            lines.Add(Loc.GetString("ashfall-lore-trait-quirk-line",
                ("text", Loc.GetString(quirk.Text, ("sex", sexKey)))));
        }

        if (lines.Count > 0)
        {
            sections.Add(new AshfallDossierSection
            {
                Kind = "personality",
                Title = Loc.GetString("ashfall-lore-title-personality"),
                Lines = lines,
            });
        }
    }

    private AshfallPsychotypePrototype? PickPsychotype(
        HashSet<string> tags,
        IRobustRandom random)
    {
        var eligible = _prototypes
            .EnumeratePrototypes<AshfallPsychotypePrototype>()
            .Where(proto =>
                proto.RequiredTags.IsSubsetOf(tags) &&
                !proto.ExcludedTags.Overlaps(tags))
            .Select(proto => (Proto: proto, Weight: GetWeight(proto.Weight, proto.WeightModifiers, tags)))
            .Where(entry => entry.Weight > 0f)
            .ToList();

        if (eligible.Count == 0)
            return _prototypes.EnumeratePrototypes<AshfallPsychotypePrototype>().FirstOrDefault();

        return PickWeighted(eligible, random);
    }

    private static string DomainTag(string domain)
    {
        return TypeTag(Loc.GetString($"ashfall-domain-{domain.ToLowerInvariant()}"));
    }

    private static string CertTag(AshfallCertificationPrototype cert)
    {
        var label = Loc.GetString(cert.Kind == AshfallCertificationKind.Specialization
            ? "ashfall-cert-kind-specialization"
            : "ashfall-cert-kind-course");
        return TypeTag(label);
    }

    private static string TypeTag(string label)
    {
        // No square brackets here: Robust rich text would swallow them as an unknown tag.
        return $"[color={EducationTagColor}]{label}[/color] — ";
    }

    private void AddFragmentSection(
        List<AshfallDossierSection> sections,
        AshfallPersonStructure person,
        IRobustRandom random,
        AshfallCharacterLoreCategory category,
        string kind,
        string titleKey,
        string sexKey)
    {
        var fragment = PickFragment(category, person.Age, person.StructureTags, sexKey, random);
        if (fragment == null)
            return;

        sections.Add(new AshfallDossierSection
        {
            Kind = kind,
            Title = Loc.GetString(titleKey),
            Lines = { Loc.GetString(fragment.Text, ("sex", sexKey)) },
        });
    }

    // Fragment picking (personality, evaluations, hooks, cryo circumstances). The hidden
    // structure tags gate and weight what fits this person's life.

    private AshfallCharacterLoreFragmentPrototype? PickFragment(
        AshfallCharacterLoreCategory category,
        int age,
        HashSet<string> tags,
        string sexKey,
        IRobustRandom random)
    {
        var eligible = _prototypes
            .EnumeratePrototypes<AshfallCharacterLoreFragmentPrototype>()
            .Where(fragment =>
                fragment.Category == category &&
                age >= fragment.MinAge &&
                age <= fragment.MaxAge &&
                fragment.RequiredTags.IsSubsetOf(tags) &&
                !fragment.ExcludedTags.Overlaps(tags))
            .Select(fragment => (Fragment: fragment, Weight: GetWeight(fragment.Weight, fragment.WeightModifiers, tags)))
            .Where(entry => entry.Weight > 0f)
            .ToList();

        if (eligible.Count == 0)
            return null;

        return PickWeighted(eligible, random);
    }

    private static float GetWeight(float baseWeight, List<AshfallTagWeightModifier> modifiers, HashSet<string> tags)
    {
        var weight = baseWeight;
        foreach (var modifier in modifiers)
        {
            if (tags.Contains(modifier.Tag))
                weight *= modifier.Multiplier;
        }

        return weight;
    }

    // Lore fragments predate the tag-weight modifier type and keep their own identical schema.
    private static float GetWeight(float baseWeight, List<AshfallLoreWeightModifier> modifiers, HashSet<string> tags)
    {
        var weight = baseWeight;
        foreach (var modifier in modifiers)
        {
            if (tags.Contains(modifier.Tag))
                weight *= modifier.Multiplier;
        }

        return weight;
    }

    private static T PickWeighted<T>(List<(T Item, float Weight)> entries, IRobustRandom random)
    {
        if (entries.Count == 0)
            throw new AshfallValidationException("Cannot pick from an empty weighted pool.");

        var totalWeight = 0f;
        foreach (var entry in entries)
            totalWeight += entry.Weight;

        var roll = random.NextFloat(0f, totalWeight);
        var cursor = 0f;

        foreach (var entry in entries)
        {
            cursor += entry.Weight;
            if (roll <= cursor)
                return entry.Item;
        }

        return entries[^1].Item;
    }

    // Validation: the structured person must be internally believable.

    private static string? Validate(AshfallPersonStructure person, List<ProtoId<JobPrototype>> eligibleJobs)
    {
        if (eligibleJobs.Count == 0)
            return $"Age={person.Age} Education={person.Education}: no eligible jobs.";

        // Breadth control: a life trajectory stays inside its professional family (main field,
        // adjacent specialization, one retraining). A sprawling multi-domain assignment list is
        // a generation failure even when every individual entry is technically justified.
        if (person.RetrainingCount > 1)
            return $"Age={person.Age}: {person.RetrainingCount} career retrainings.";

        var nonFallback = eligibleJobs.ToList();
        if (nonFallback.Count > AshfallJobScorer.MaxAssignments)
            return $"Age={person.Age} Education={person.Education}: assignment list is too broad ({nonFallback.Count} jobs, families: {string.Join(",", person.CareerFamilies)}).";

        if (person.UsedCareerYears > person.AvailableCareerYears)
            return $"Age={person.Age} Education={person.Education}: career chronology exceeds available years " +
                   $"({person.UsedCareerYears} > {person.AvailableCareerYears}).";

        foreach (var (competencyId, state) in person.Competencies)
        {
            if (state.Experience > 0f && state.Provenance.Count == 0)
                return $"Age={person.Age}: competency {competencyId} has experience but no provenance.";
        }

        return null;
    }
}

/// <summary>
///     Thrown when a generated person structure fails validation; carries a human-readable
///     reason intended for server logs.
/// </summary>
public sealed class AshfallValidationException : Exception
{
    public AshfallValidationException(string message) : base(message)
    {
    }
}
