using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared.Ashfall.CharacterGen;
using Content.Shared.Ashfall.CharacterGen.Lifepath;
using Content.Shared.Ashfall.CharacterGen.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests.Ashfall;

[TestFixture]
public sealed class AshfallCharacterGenTests : GameTest
{
    private static readonly ProtoId<CharacterGenConstraintsPrototype> HumanConstraints = "HumanDefaultConstraints";

    /// <summary>
    ///     Silicon jobs are not personnel-file candidates; TramDriver is deliberately removed from
    ///     generation (niche job, not on rotation); T* prototypes and LoadoutTester are test
    ///     fixtures from Content.IntegrationTests, not real playable jobs.
    /// </summary>
    private static readonly HashSet<string> JobCareerExclusions = new()
    {
        "StationAi",
        "Borg",
        "TramDriver",
        "TChaplain",
        "TMime",
        "TAssistant",
        "TClown",
        "TCaptain",
        "TestInternalsDummy",
        "LoadoutTester",
    };

    [SidedDependency(Side.Server)] private IPrototypeManager _protoMan = default!;
    [SidedDependency(Side.Server)] private MarkingManager _markingMan = default!;
    [SidedDependency(Side.Server)] private NamingSystem _namingSys = default!;
    [SidedDependency(Side.Server)] private IRobustRandom _random = default!;
    [SidedDependency(Side.Server)] private IRobustSerializer _serializer = default!;

    private AshfallPersonGenerator _personGenerator = default!;

    private AshfallPersonGenerator PersonGenerator =>
        _personGenerator ??= new AshfallPersonGenerator(
            _protoMan,
            new AshfallCharacterGenerator(_protoMan, _markingMan, _namingSys));

    [Test]
    [RunOnSide(Side.Server)]
    public void FuzzHumanCharacterGenerationTest()
    {
        Assert.That(_protoMan.TryIndex(HumanConstraints, out var constraints), Is.True);
        Assert.That(constraints, Is.Not.Null);

        var species = _protoMan.Index<SpeciesPrototype>(constraints!.Species);
        Assert.That(_protoMan.TryIndex(species.SkinColoration, out var colorationProto), Is.True);
        var strategy = colorationProto!.Strategy;

        var youngCount = 0;
        var youngGrayCount = 0;
        var oldCount = 0;
        var oldGrayCount = 0;

        for (var i = 0; i < 2000; i++)
        {
            var (candidate, _) = PersonGenerator.GenerateCandidate(constraints!, _random);
            var profile = candidate.Profile;

            // 1. Species is Human
            Assert.That(profile.Species.Id, Is.EqualTo("Human"));

            // 2. Age is within valid range
            Assert.That(profile.Age, Is.GreaterThanOrEqualTo(18));
            Assert.That(profile.Age, Is.LessThanOrEqualTo(75));

            // 3. Name is generated
            Assert.That(string.IsNullOrWhiteSpace(profile.Name), Is.False);

            // 4. Skin tone satisfies natural human melanin curve
            Assert.That(strategy.VerifySkinColor(profile.Appearance.SkinColor, out var reason), Is.True,
                $"Skin color {profile.Appearance.SkinColor} failed verification: {reason}");

            // 5. Eye color is not pure black or zero alpha
            Assert.That(profile.Appearance.EyeColor.A, Is.EqualTo(1.0f));

            // 6. Hair marking exists
            Assert.That(profile.Appearance.Markings.ContainsKey("Head"), Is.True);
            var headMarkings = profile.Appearance.Markings["Head"];
            Assert.That(headMarkings.ContainsKey(HumanoidVisualLayers.Hair), Is.True);
            Assert.That(headMarkings[HumanoidVisualLayers.Hair].Count, Is.GreaterThan(0));

            // 7. Track age vs gray hair distribution
            var hairColor = headMarkings[HumanoidVisualLayers.Hair][0].MarkingColors[0];
            var hsv = Color.ToHsv(hairColor);
            var isGray = hsv.Y < 0.15f && hsv.Z > 0.35f;

            if (profile.Age < 30)
            {
                youngCount++;
                if (isGray) youngGrayCount++;
            }
            else if (profile.Age >= 60)
            {
                oldCount++;
                if (isGray) oldGrayCount++;
            }

            // 8. Facial hair check for males
            if (profile.Sex == Sex.Male && headMarkings.TryGetValue(HumanoidVisualLayers.FacialHair, out var facialList) && facialList.Count > 0)
            {
                var facialColor = facialList[0].MarkingColors[0];
                // Facial hair color should be close to hair color
                Assert.That(Math.Abs(facialColor.R - hairColor.R), Is.LessThan(0.15f));
            }
        }

        // Statistical assertions on age graying curve
        if (youngCount > 0 && oldCount > 0)
        {
            var youngGrayRate = (float)youngGrayCount / youngCount;
            var oldGrayRate = (float)oldGrayCount / oldCount;
            Assert.That(oldGrayRate, Is.GreaterThan(youngGrayRate),
                $"Elder gray rate ({oldGrayRate:P1}) should be significantly higher than young gray rate ({youngGrayRate:P1})");
        }
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void CandidateCanBeNetworkSerializedTest()
    {
        Assert.That(_protoMan.TryIndex(HumanConstraints, out var constraints), Is.True);
        var (expected, _) = PersonGenerator.GenerateCandidate(constraints!, _random);

        using var stream = new MemoryStream();
        _serializer.SerializeDirect(stream, expected);
        stream.Position = 0;
        _serializer.DeserializeDirect(stream, out AshfallCharacterCandidate actual);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Profile.Name, Is.EqualTo(expected.Profile.Name));
            Assert.That(actual.CompatibleJobs, Is.EqualTo(expected.CompatibleJobs));
            Assert.That(actual.PrimaryDomain, Is.EqualTo(expected.PrimaryDomain));
            Assert.That(actual.Dossier.Sections.Count, Is.EqualTo(expected.Dossier.Sections.Count));
            Assert.That(actual.Dossier.CulturalOrigin, Is.EqualTo(expected.Dossier.CulturalOrigin));
            for (var i = 0; i < expected.Dossier.Sections.Count; i++)
            {
                Assert.That(actual.Dossier.Sections[i].Kind, Is.EqualTo(expected.Dossier.Sections[i].Kind));
                Assert.That(actual.Dossier.Sections[i].Title, Is.EqualTo(expected.Dossier.Sections[i].Title));
                Assert.That(actual.Dossier.Sections[i].Lines, Is.EqualTo(expected.Dossier.Sections[i].Lines));
            }
        });
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void GeneratorDataHasRequiredCoverageTest()
    {
        var competencies = _protoMan.EnumeratePrototypes<AshfallCompetencyPrototype>().ToArray();
        var cultures = _protoMan.EnumeratePrototypes<AshfallCulturePrototype>().ToArray();
        var origins = _protoMan.EnumeratePrototypes<AshfallOriginPrototype>().ToArray();
        var educations = _protoMan.EnumeratePrototypes<AshfallEducationPrototype>().ToArray();
        var roles = _protoMan.EnumeratePrototypes<AshfallCareerRolePrototype>().ToArray();
        var employers = _protoMan.EnumeratePrototypes<AshfallEmployerPrototype>().ToArray();
        var jobCareers = _protoMan.EnumeratePrototypes<AshfallJobCareerPrototype>().ToArray();
        var fragments = _protoMan.EnumeratePrototypes<AshfallCharacterLoreFragmentPrototype>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(competencies.Length, Is.GreaterThanOrEqualTo(20), "competencies");
            Assert.That(cultures.Count(c => c.Species == "Human"), Is.GreaterThanOrEqualTo(18), "human cultures");
            Assert.That(cultures.Count(c => c.Species != "Human"), Is.GreaterThanOrEqualTo(8), "species cultures");
            Assert.That(origins.Length, Is.GreaterThanOrEqualTo(15), "origins");
            Assert.That(educations.Length, Is.GreaterThanOrEqualTo(12), "education paths");
            Assert.That(roles.Length, Is.GreaterThanOrEqualTo(30), "career roles");
            Assert.That(employers.Length, Is.GreaterThanOrEqualTo(20), "employers");
            Assert.That(fragments.Count(x => x.Category == AshfallCharacterLoreCategory.Personality), Is.GreaterThanOrEqualTo(32), "personality fragments");
            Assert.That(fragments.Count(x => x.Category == AshfallCharacterLoreCategory.PersonalityStress), Is.GreaterThanOrEqualTo(32), "personality stress fragments");
            Assert.That(fragments.Count(x => x.Category == AshfallCharacterLoreCategory.PersonalityQuirk), Is.GreaterThanOrEqualTo(32), "personality quirk fragments");
            Assert.That(fragments.Count(x => x.Category == AshfallCharacterLoreCategory.Evaluation), Is.GreaterThanOrEqualTo(32), "evaluation fragments");
            Assert.That(fragments.Count(x => x.Category == AshfallCharacterLoreCategory.PersonalHook), Is.GreaterThanOrEqualTo(44), "hook fragments");
            Assert.That(fragments.Count(x => x.Category == AshfallCharacterLoreCategory.PreCryo), Is.GreaterThanOrEqualTo(16), "precryo fragments");
            Assert.That(_protoMan.EnumeratePrototypes<AshfallPsychotypePrototype>().Count(), Is.GreaterThanOrEqualTo(8), "psychotypes");
        });

        // Every personnel-file-selectable job must have a career requirement definition.
        var selectableJobs = _protoMan
            .EnumeratePrototypes<JobPrototype>()
            .Where(job => job.SetPreference && !JobCareerExclusions.Contains(job.ID))
            .ToArray();

        var missingJobs = selectableJobs
            .Where(job => !_protoMan.HasIndex<AshfallJobCareerPrototype>(job.ID))
            .Select(job => job.ID)
            .ToArray();

        Assert.That(missingJobs, Is.Empty,
            $"Missing ashfallJobCareer for: {string.Join(", ", missingJobs)}");

        // Every career role must have at least one compatible employer industry-wise.
        foreach (var role in roles)
        {
            var compatible = employers.Any(e => e.Industries.Overlaps(role.EmployerIndustries));
            Assert.That(compatible, Is.True, $"{role.ID}: no employer with industries [{string.Join(", ", role.EmployerIndustries)}]");
        }

        // Fallback jobs must exist so a person can never end up with an empty assignment list.
        Assert.That(jobCareers.Any(j => j.FallbackOnly), Is.True, "no fallbackOnly job career defined");
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void GenerationIsDeterministicAndCompleteTest()
    {
        Assert.That(_protoMan.TryIndex(HumanConstraints, out var constraints), Is.True);

        // Determinism: identical seeds produce identical candidates.
        for (var seed = 0; seed < 25; seed++)
        {
            var firstRandom = new RobustRandom();
            var secondRandom = new RobustRandom();
            firstRandom.SetSeed(seed);
            secondRandom.SetSeed(seed);

            var (first, firstStructure) = PersonGenerator.GenerateCandidate(constraints!, firstRandom);
            var (second, secondStructure) = PersonGenerator.GenerateCandidate(constraints!, secondRandom);

            Assert.That(second.Profile.Name, Is.EqualTo(first.Profile.Name), $"seed {seed}");
            Assert.That(second.CompatibleJobs, Is.EqualTo(first.CompatibleJobs), $"seed {seed}");
            Assert.That(second.Dossier.Sections.Count, Is.EqualTo(first.Dossier.Sections.Count), $"seed {seed}");
            Assert.That(secondStructure.UsedCareerYears, Is.EqualTo(firstStructure.UsedCareerYears), $"seed {seed}");
        }

        // Generative invariants across many seeds.
        var generatedCertificationCount = 0;
        for (var seed = 100; seed < 600; seed++)
        {
            var random = new RobustRandom();
            random.SetSeed(seed);

            var (candidate, structure) = PersonGenerator.GenerateCandidate(constraints!, random);

            Assert.That(candidate.CompatibleJobs, Is.Not.Empty, $"seed {seed}");
            Assert.That(structure.UsedCareerYears, Is.LessThanOrEqualTo(structure.AvailableCareerYears),
                $"seed {seed}: chronology exceeded available years");

            foreach (var (competencyId, state) in structure.Competencies)
            {
                Assert.That(state.Provenance, Is.Not.Empty,
                    $"seed {seed}: competency {competencyId} has experience but no provenance");
            }

            foreach (var jobId in candidate.CompatibleJobs)
            {
                Assert.That(_protoMan.TryIndex(jobId, out var job), Is.True, $"seed {seed}: {jobId}");
                Assert.That(job!.SetPreference, Is.True, $"seed {seed}: {jobId}");
            }

            // Core structure-derived sections always exist; the career section legitimately may
            // be absent for young candidates with no work history yet.
            Assert.That(candidate.Dossier.Sections.Select(s => s.Kind),
                Does.Contain("origin").And.Contain("education").And.Contain("personality"),
                $"seed {seed}");
            if (structure.AvailableCareerYears > 1)
            {
                Assert.That(candidate.Dossier.Sections.Select(s => s.Kind), Does.Contain("career"),
                    $"seed {seed}: candidate with adult years should have work history");
            }
            Assert.That(candidate.Dossier.Sections, Has.All.Matches<AshfallDossierSection>(s =>
                !string.IsNullOrWhiteSpace(s.Title) && s.Lines.Count > 0 && s.Lines.All(l => !string.IsNullOrWhiteSpace(l))),
                $"seed {seed}");

            var educationLines = candidate.Dossier.Sections.Single(s => s.Kind == "education").Lines;
            var certificationLines = educationLines.Where(line => line.StartsWith("• ")).ToArray();
            Assert.That(certificationLines, Has.Length.EqualTo(structure.Certifications.Count), $"seed {seed}");
            Assert.That(certificationLines, Has.All.Contains("[color="), $"seed {seed}");

            for (var i = 0; i < certificationLines.Length; i++)
            {
                var certification = _protoMan.Index(structure.Certifications[i]);
                var labelKey = certification.Kind == AshfallCertificationKind.Specialization
                    ? "ashfall-cert-kind-specialization"
                    : "ashfall-cert-kind-course";
                Assert.That(certificationLines[i], Does.Contain(Loc.GetString(labelKey)), $"seed {seed}");
                generatedCertificationCount++;
            }
        }

        Assert.That(generatedCertificationCount, Is.GreaterThan(0), "no generated candidates had certifications");
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void JobEligibilityFixturesTest()
    {
        // Scoring fixtures use explicitly constructed structures; no randomness involved.
        // Education choice per fixture avoids domain-bonus interference with expectations.

        AshfallPersonStructure MakePerson(string educationId, params (string Competency, float Experience)[] experience)
        {
            var person = new AshfallPersonStructure
            {
                SpeciesId = "Human",
                Age = 40,
                Education = educationId,
                AvailableCareerYears = 20,
                UsedCareerYears = 10,
            };
            foreach (var (competency, exp) in experience)
            {
                person.AddExperience(competency, exp, AshfallProvenanceSource.Career, "FixtureRole");
            }
            return person;
        }

        bool Valid(AshfallPersonStructure person, string jobId)
        {
            var jobCareer = _protoMan.Index<AshfallJobCareerPrototype>(jobId);
            return AshfallJobScorer.Score(person, jobCareer, _protoMan) >= 0;
        }

        // Established medical chemist: doctor and chemist both plausible, intern and CMO are not.
        var chemist = MakePerson("AshfallEducationScience",
            ("Medical", 4.5f), ("ClinicalMedicine", 4.0f), ("Chemistry", 8.0f), ("Pharmacology", 6.0f));
        Assert.Multiple(() =>
        {
            Assert.That(Valid(chemist, "Chemist"), Is.True, "Chemist");
            Assert.That(Valid(chemist, "MedicalDoctor"), Is.True, "MedicalDoctor");
            Assert.That(Valid(chemist, "MedicalIntern"), Is.False, "MedicalIntern must reject established specialists");
            Assert.That(Valid(chemist, "ChiefMedicalOfficer"), Is.False, "CMO requires command");
        });

        // Junior medical retrainee (e.g. experienced engineer who switched fields): intern remains open.
        var retrainee = MakePerson("AshfallEducationMedicalCollege",
            ("Engineering", 15f), ("Electrical", 8f), ("Medical", 2f), ("ClinicalMedicine", 1.5f));
        Assert.Multiple(() =>
        {
            Assert.That(Valid(retrainee, "MedicalIntern"), Is.True, "MedicalIntern for a genuine medical junior");
            Assert.That(Valid(retrainee, "StationEngineer"), Is.True, "StationEngineer for the senior engineer");
            Assert.That(Valid(retrainee, "ChiefEngineer"), Is.False, "ChiefEngineer requires command");
        });

        // Young cadet: officer-level work only.
        var cadet = MakePerson("AshfallEducationSecurity", ("Security", 2f));
        Assert.Multiple(() =>
        {
            Assert.That(Valid(cadet, "SecurityCadet"), Is.True, "SecurityCadet");
            Assert.That(Valid(cadet, "SecurityOfficer"), Is.True, "SecurityOfficer");
            Assert.That(Valid(cadet, "Warden"), Is.False, "Warden requires experience");
            Assert.That(Valid(cadet, "HeadOfSecurity"), Is.False, "HoS requires senior security and command");
        });

        // Experienced custody officer: warden is reachable, cadet is not (overqualified).
        var veteran = MakePerson("AshfallEducationSecurity", ("Security", 9f), ("Custody", 6f));
        Assert.Multiple(() =>
        {
            Assert.That(Valid(veteran, "Warden"), Is.True, "Warden");
            Assert.That(Valid(veteran, "SecurityOfficer"), Is.True, "SecurityOfficer");
            Assert.That(Valid(veteran, "SecurityCadet"), Is.False, "Cadet rejects overqualified officers");
            Assert.That(Valid(veteran, "HeadOfSecurity"), Is.False, "HoS requires command");
        });

        // Command career with leadership history and personnel background.
        var commander = MakePerson("AshfallEducationCommand",
            ("Security", 9f), ("Command", 6f), ("Administration", 5f), ("PersonnelManagement", 2f));
        commander.LeadershipHistory = true;
        Assert.Multiple(() =>
        {
            Assert.That(Valid(commander, "HeadOfSecurity"), Is.True, "HeadOfSecurity");
            Assert.That(Valid(commander, "HeadOfPersonnel"), Is.True, "HeadOfPersonnel");
            Assert.That(Valid(commander, "Captain"), Is.False, "Captain requires experienced command");
        });

        // Senior administration with long command experience: Captain.
        var director = MakePerson("AshfallEducationCommand",
            ("Command", 9f), ("Administration", 6f), ("PersonnelManagement", 5f));
        director.LeadershipHistory = true;
        Assert.Multiple(() =>
        {
            Assert.That(Valid(director, "Captain"), Is.True, "Captain");
            Assert.That(Valid(director, "HeadOfPersonnel"), Is.True, "HeadOfPersonnel");
        });

        // Lifelong botanist: a deliberately narrow assignment list, and no fallback dilution.
        var botanist = MakePerson("AshfallEducationBiology", ("Botany", 9f), ("Agriculture", 6f));
        var botanistJobs = AshfallJobScorer.ScoreEligibleJobs(botanist, _protoMan);
        Assert.Multiple(() =>
        {
            Assert.That(botanistJobs, Does.Contain(new ProtoId<JobPrototype>("Botanist")), "Botanist");
            Assert.That(botanistJobs[0].Id, Is.EqualTo("Botanist"), "Botanist should be the top assignment");
            Assert.That(botanistJobs, Does.Not.Contain(new ProtoId<JobPrototype>("Passenger")), "Passenger must not dilute a specialist list");
        });

        // A person with no competencies only qualifies for fallback work.
        var unskilled = MakePerson("AshfallEducationGeneral");
        var unskilledJobs = AshfallJobScorer.ScoreEligibleJobs(unskilled, _protoMan);
        Assert.That(unskilledJobs, Is.Not.Empty, "fallback assignments must always exist");
        Assert.That(unskilledJobs.All(jobId => _protoMan.Index<AshfallJobCareerPrototype>(jobId).FallbackOnly),
            Is.True, "unskilled person should only receive fallback jobs");
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void LifepathIdentityIsRebuiltServerSideTest()
    {
        var origin = _protoMan.EnumeratePrototypes<AshfallLifepathOptionPrototype>().First(o => o.Step == 1);
        var vector = _protoMan.EnumeratePrototypes<AshfallLifepathOptionPrototype>().First(o => o.Step == 2);
        var flaw = _protoMan.EnumeratePrototypes<AshfallLifepathOptionPrototype>().First(o => o.Step == 3);
        var luggage = _protoMan.EnumeratePrototypes<AshfallLifepathOptionPrototype>().First(o => o.Step == 4);
        var expectedSpecies = origin.Species?.Id ?? "Human";
        var species = _protoMan.Index<SpeciesPrototype>(expectedSpecies);

        for (var seed = 0; seed < 50; seed++)
        {
            var random = new RobustRandom();
            random.SetSeed(seed);

            var choices = new AshfallLifepathChoices
            {
                Step1Origin = origin.ID,
                Step2Vector = vector.ID,
                Step3Flaw = flaw.ID,
                Step4Luggage = luggage.ID,
                ExperienceTier = 2,
                SelectedSex = Sex.Female,
            };

            var (candidate, _) = PersonGenerator.GenerateFromLifepath(choices, random);
            var profile = candidate.Profile;

            Assert.That(profile.Species.Id, Is.EqualTo(expectedSpecies), $"seed {seed}");
            Assert.That(profile.Age, Is.InRange(44, 60), $"seed {seed}: veteran tier age range");
            Assert.That(species.Sexes, Does.Contain(profile.Sex), $"seed {seed}");
            Assert.That(profile.Name, Is.Not.Empty, $"seed {seed}");
        }

        // A rerolled custom name is the only client-authored field that survives submission.
        var custom = new AshfallLifepathChoices
        {
            Step1Origin = origin.ID,
            Step2Vector = vector.ID,
            Step3Flaw = flaw.ID,
            Step4Luggage = luggage.ID,
            CustomName = "Тест Кандидат",
            SelectedSex = Sex.Female,
        };
        var named = PersonGenerator.GenerateFromLifepath(custom, _random).Candidate;
        Assert.That(named.Profile.Name, Is.EqualTo("Тест Кандидат"));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void CultureNamingIsCoherentTest()
    {
        Assert.That(_protoMan.TryIndex(HumanConstraints, out var constraints), Is.True);

        // Names must always come from the culture's own pools: a candidate's name components
        // cannot be validated directly against the datasets, but species pools must never mix.
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new RobustRandom();
            random.SetSeed(seed);
            var (candidate, structure) = PersonGenerator.GenerateCandidate(constraints!, random);

            Assert.That(candidate.Dossier.CulturalOrigin, Is.Not.Empty, $"seed {seed}");
            Assert.That(candidate.Dossier.Birthplace, Is.Not.Empty, $"seed {seed}");
            Assert.That(candidate.Profile.Name.Split(' ').Length, Is.EqualTo(2), $"seed {seed}: two-part name expected");

            var culture = _protoMan.Index(structure.Culture);
            Assert.That(culture.Species, Is.EqualTo("Human"), $"seed {seed}");
        }
    }
}
