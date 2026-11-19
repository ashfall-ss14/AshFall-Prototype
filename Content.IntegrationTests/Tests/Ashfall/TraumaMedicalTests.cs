using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Medical.Common.Body;
using Content.Medical.Common.Targeting;
using Content.Medical.Common.Traumas;
using Content.Medical.Server.Surgery;
using Content.Medical.Shared.Body;
using Content.Medical.Shared.DelayedDeath;
using Content.Medical.Shared.Surgery;
using Content.Medical.Shared.Surgery.Tools;
using Content.Medical.Shared.Traumas;
using Content.Medical.Shared.Wounds;
using Content.Server.Ashfall.Interaction.OfferItem;
using Content.Server.Medical.CPR;
using Content.Shared.Ashfall.Interaction.OfferItem;
using Content.Shared.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Emp;
using Content.Shared.FixedPoint;
using Content.Shared.Gibbing;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Medical.CPR;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusEffectNew;
using Content.Shared.Trauma.Cybernetics;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Content.Trauma.Shared.Knowledge.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Ashfall;

public sealed class TraumaMedicalTests : GameTest
{
    private static readonly ProtoId<OrganCategoryPrototype> Head = "Head";
    private static readonly ProtoId<OrganCategoryPrototype> Heart = "Heart";
    private static readonly ProtoId<OrganCategoryPrototype> Lungs = "Lungs";
    private static readonly ProtoId<OrganCategoryPrototype> LeftArm = "ArmLeft";

    [TestCase(17, 0)]
    [TestCase(25, 8)]
    [RunOnSide(Side.Server)]
    public void OrganSurgeryTreatsOnlySelectedPart(int eyeDamage, int remainingEyeDamage)
    {
        var body = SEntMan.System<BodySystem>();
        var trauma = SEntMan.System<TraumaSystem>();
        var patient = SSpawn("MobHuman");
        var head = body.GetOrgan(patient, Head)!.Value;
        var eyes = body.GetOrgan(patient, EyesSystem.EyesCategory)!.Value;
        var heart = body.GetOrgan(patient, Heart)!.Value;
        var tool = SSpawn("Hemostat");
        var step = SSpawn("SurgeryStepHealOrgans");

        Assert.That(trauma.TryCreateOrganDamageModifier(eyes, eyeDamage, tool, "test"), Is.True);
        Assert.That(trauma.TryCreateOrganDamageModifier(heart, 10, tool, "test"), Is.True);

        var ev = new SurgeryStepEvent(patient, patient, head, tool, step, step, true);
        SEntMan.EventBus.RaiseLocalEvent(step, ref ev);

        var eyeOrgan = SComp<InternalChildOrganComponent>(eyes);
        var heartOrgan = SComp<InternalChildOrganComponent>(heart);
        Assert.Multiple(() =>
        {
            Assert.That(eyeOrgan.OrganIntegrity, Is.EqualTo(eyeOrgan.IntegrityCap - remainingEyeDamage));
            Assert.That(eyeOrgan.IntegrityModifiers.ContainsKey(("test", tool)), Is.EqualTo(remainingEyeDamage > 0));
            Assert.That(heartOrgan.OrganIntegrity, Is.EqualTo(heartOrgan.IntegrityCap - 10));
        });
    }

    [TestCase("Scalpel")]
    [TestCase("Hemostat")]
    [TestCase("Retractor")]
    [TestCase("Cautery")]
    [TestCase("Saw")]
    [RunOnSide(Side.Server)]
    public void SurgicalToolsHaveAudio(string prototype)
    {
        var tool = SSpawn(prototype);
        var component = SComp<SurgeryToolComponent>(tool);
        Assert.That(component.StartSound, Is.Not.Null);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void CyberneticOrganDisablesUntilEmpEnds()
    {
        var bodySystem = SEntMan.System<BodySystem>();
        var human = SSpawn("MobHuman");
        var heart = bodySystem.GetOrgan(human, Heart)!.Value;
        var cybernetics = SEntMan.AddComponent<CyberneticsComponent>(heart);
        cybernetics.EmpDamage = new DamageSpecifier();
        Assert.That(SEntMan.HasComponent<EnabledOrganComponent>(heart), Is.True);

        var pulse = new EmpPulseEvent(0, false, false, TimeSpan.FromSeconds(5), null);
        SEntMan.EventBus.RaiseLocalEvent(heart, ref pulse);
        Assert.That(pulse.Disabled, Is.True);
        Assert.That(bodySystem.EnableOrgan(heart), Is.False);

        var ended = new EmpDisabledRemovedEvent();
        SEntMan.EventBus.RaiseLocalEvent(heart, ref ended);
        Assert.That(cybernetics.Disabled, Is.False);
        Assert.That(SEntMan.HasComponent<EnabledOrganComponent>(heart), Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void TargetedWoundCanBeHealed()
    {
        var human = SSpawn("MobHuman");
        var body = SEntMan.System<BodySystem>();
        var wounds = SEntMan.System<WoundSystem>();
        var damage = SEntMan.System<DamageableSystem>();
        var head = body.GetOrgan(human, Head)!.Value;
        var amount = new DamageSpecifier { DamageDict = new() { { "Heat", FixedPoint2.New(20) } } };
        damage.ChangeDamage(human, amount, targetPart: TargetBodyPart.Head, canMiss: false);
        Assert.That(wounds.GetWoundableWounds(head), Has.Count.EqualTo(1));
        Assert.That(damage.GetTotalDamage(human), Is.EqualTo(FixedPoint2.New(20)));
        damage.ChangeDamage(human, -amount, targetPart: TargetBodyPart.Head, canMiss: false);
        Assert.That(wounds.GetWoundableWounds(head), Is.Empty);
        Assert.That(damage.GetTotalDamage(human), Is.EqualTo(FixedPoint2.Zero));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void SurgeryAndStatusMessagesResolve()
    {
        var human = SSpawn("MobHuman");
        var tool = SSpawn("Scalpel");
        var head = SEntMan.System<BodySystem>().GetOrgan(human, Head)!.Value;
        var message = Loc.GetString("surgery-popup-step-SurgeryStepOpenIncisionScalpel",
            ("user", human), ("target", Loc.GetString("surgery-popup-self")), ("part", head), ("tool", tool));
        Assert.That(message, Does.Not.Contain("{$"));
        Assert.That(message, Does.Contain(SEntMan.GetComponent<MetaDataComponent>(tool).EntityName));
        Assert.That(Loc.GetString("inspect-part-status-title"), Does.Not.Contain("inspect-part-status-title"));
        Assert.That(Loc.GetString("inspect-part-status-line", ("part", "голова"), ("status", "без повреждений"), ("possessive", "")), Does.Contain("голова"));
        Assert.That(Loc.GetString("examine-border-line"), Does.Not.Contain("examine-border-line"));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void JobKnowledgeUsesFloorsWithoutBreakingMasteryGains()
    {
        var human = SSpawn("MobHuman");
        var knowledge = SEntMan.System<SharedKnowledgeSystem>();
        var container = knowledge.EnsureKnowledgeContainer(human);
        EntProtoId skill = "StrengthKnowledge";

        knowledge.RaiseMastery(container, skill, 1, popup: false);
        knowledge.RaiseMastery(container, skill, 1, popup: false);
        Assert.That(knowledge.GetMastery(knowledge.GetKnowledge(container, skill)!.Value.Owner), Is.EqualTo(2));

        knowledge.ApplyJobFloors(human, new Dictionary<EntProtoId, int> { [skill] = 1 });
        Assert.That(knowledge.GetMastery(knowledge.GetKnowledge(container, skill)!.Value.Owner), Is.EqualTo(2));

        knowledge.ApplyJobFloors(human, new Dictionary<EntProtoId, int> { [skill] = 3 });
        Assert.That(knowledge.GetMastery(knowledge.GetKnowledge(container, skill)!.Value.Owner), Is.EqualTo(3));
    }

    [Test]
    public async Task CprLocksTargetAndCleansUpOnCancel()
    {
        EntityUid patient = default;
        await Server.WaitAssertion(() =>
        {
            var performer = SSpawn("MobHuman");
            var secondPerformer = SSpawn("MobHuman");
            patient = SSpawn("MobHuman");
            var mobState = SEntMan.System<MobStateSystem>();
            var verbs = SEntMan.System<SharedVerbSystem>();
            var cpr = SEntMan.System<CPRSystem>();
            var doAfter = SEntMan.System<SharedDoAfterSystem>();

            mobState.ChangeMobState(patient, MobState.Dead);
            verbs.GetLocalVerbs(patient, performer, typeof(InnateVerb), force: true)
                .Single(x => x.Text == Loc.GetString("cpr-verb"))
                .Act!();

            Assert.That(cpr.IsCPRActive(patient), Is.True);
            var firstDoAfter = SComp<DoAfterComponent>(performer);
            Assert.That(firstDoAfter.DoAfters, Has.Count.EqualTo(1));

            verbs.GetLocalVerbs(patient, secondPerformer, typeof(InnateVerb), force: true)
                .Single(x => x.Text == Loc.GetString("cpr-verb"))
                .Act!();
            Assert.That(SEntMan.TryGetComponent<DoAfterComponent>(secondPerformer, out var secondDoAfter) &&
                        secondDoAfter.DoAfters.Count > 0,
                Is.False);

            doAfter.Cancel(performer, firstDoAfter.DoAfters.Keys.Single());
        });

        await Server.WaitRunTicks(20);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<CPRSystem>().IsCPRActive(patient), Is.False));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void CprCannotStartWithoutLungs()
    {
        var performer = SSpawn("MobHuman");
        var patient = SSpawn("MobHuman");
        var body = SEntMan.System<BodySystem>();
        var mobState = SEntMan.System<MobStateSystem>();
        var verbs = SEntMan.System<SharedVerbSystem>();
        var cpr = SEntMan.System<CPRSystem>();

        mobState.ChangeMobState(patient, MobState.Dead);
        var lungs = body.GetOrgan(patient, Lungs)!.Value;
        Assert.That(body.RemoveOrgan(patient, lungs), Is.True);

        var verb = verbs.GetLocalVerbs(patient, performer, typeof(InnateVerb), force: true)
            .Single(x => x.Text == Loc.GetString("cpr-verb"));
        verb.Act!();

        Assert.That(cpr.IsCPRActive(patient), Is.False);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void CprCycleHealsAndExtendsDelayedDeathOnce()
    {
        var performer = SSpawn("MobHuman");
        var patient = SSpawn("MobHuman");
        var damage = SEntMan.System<DamageableSystem>();
        var mobState = SEntMan.System<MobStateSystem>();
        var verbs = SEntMan.System<SharedVerbSystem>();
        var delayedDeath = SEntMan.AddComponent<DelayedDeathComponent>(patient);
        var deathBefore = TimeSpan.FromMinutes(1);
        delayedDeath.NextDeath = deathBefore;
        var asphyxiation = new DamageSpecifier { DamageDict = new() { { "Asphyxiation", FixedPoint2.New(12) } } };
        damage.TryChangeDamage(patient, asphyxiation, ignoreResistances: true);
        var damageBefore = damage.GetTotalDamage(patient);
        mobState.ChangeMobState(patient, MobState.Critical);

        verbs.GetLocalVerbs(patient, performer, typeof(InnateVerb), force: true)
            .Single(x => x.Text == Loc.GetString("cpr-verb"))
            .Act!();
        var doAfter = SComp<DoAfterComponent>(performer).DoAfters.Values.Single();
        SEntMan.EventBus.RaiseLocalEvent(patient, (object) doAfter.Args.Event);

        Assert.That(damageBefore - damage.GetTotalDamage(patient), Is.EqualTo(FixedPoint2.New(6)));
        Assert.That(delayedDeath.NextDeath, Is.EqualTo(deathBefore + TimeSpan.FromSeconds(4)));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void CprCannotReviveWithoutBrain()
    {
        var performer = SSpawn("MobHuman");
        var patient = SSpawn("MobHuman");
        var body = SEntMan.System<BodySystem>();
        var mobState = SEntMan.System<MobStateSystem>();
        var verbs = SEntMan.System<SharedVerbSystem>();
        var cpr = SEntMan.System<SharedCPRSystem>();
        cpr.SetResuscitationChance(performer, 1f);
        mobState.ChangeMobState(patient, MobState.Dead);
        var brain = body.GetOrgan(patient, SharedCPRSystem.BrainCategory)!.Value;
        Assert.That(body.RemoveOrgan(patient, brain), Is.True);

        verbs.GetLocalVerbs(patient, performer, typeof(InnateVerb), force: true)
            .Single(x => x.Text == Loc.GetString("cpr-verb"))
            .Act!();
        var doAfter = SComp<DoAfterComponent>(performer).DoAfters.Values.Single();
        SEntMan.EventBus.RaiseLocalEvent(patient, (object) doAfter.Args.Event);

        Assert.That(mobState.IsDead(patient), Is.True);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void VitalDamageIgnoresLimbs()
    {
        var human = SSpawn("MobHuman");
        var damage = SEntMan.System<DamageableSystem>();
        var thresholds = SEntMan.System<MobThresholdSystem>();
        var amount = new DamageSpecifier { DamageDict = new() { { "Blunt", FixedPoint2.New(20) } } };

        damage.ChangeDamage(human, amount, targetPart: TargetBodyPart.Head, canMiss: false);
        damage.ChangeDamage(human, amount, targetPart: TargetBodyPart.LeftArm, canMiss: false);

        Assert.That(thresholds.CheckVitalDamage((human, SComp<DamageableComponent>(human))),
            Is.EqualTo(FixedPoint2.New(20)));
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void PainNumbnessSuppressesSurgeryPain()
    {
        var human = SSpawn("MobHuman");
        var surgery = SEntMan.System<SurgerySystem>();
        var status = SEntMan.System<StatusEffectsSystem>();

        Assert.That(surgery.IsConsciousAndAwake(human), Is.True);
        Assert.That(status.TrySetStatusEffectDuration(human, "StatusEffectPainNumbness", TimeSpan.FromMinutes(1)), Is.True);
        Assert.That(surgery.IsConsciousAndAwake(human), Is.False);
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void ArmorOnlyReducesCoveredPartDamage()
    {
        var human = SSpawn("MobHuman");
        var armor = SSpawn("ClothingOuterArmorBasic");
        var inventory = SEntMan.System<InventorySystem>();
        var body = SEntMan.System<BodySystem>();
        var damage = SEntMan.System<DamageableSystem>();
        var amount = new DamageSpecifier { DamageDict = new() { { "Piercing", FixedPoint2.New(10) } } };

        Assert.That(inventory.TryEquip(human, armor, "outerClothing"), Is.True);
        damage.ChangeDamage(human, amount, targetPart: TargetBodyPart.Chest, canMiss: false);
        damage.ChangeDamage(human, amount, targetPart: TargetBodyPart.LeftArm, canMiss: false);

        var chest = body.GetOrgan(human, "Torso")!.Value;
        var arm = body.GetOrgan(human, LeftArm)!.Value;
        Assert.That(damage.GetTotalDamage(chest), Is.LessThan(damage.GetTotalDamage(arm)));
        Assert.That(damage.GetTotalDamage(arm), Is.EqualTo(FixedPoint2.New(10)));
    }

    [Test]
    public async Task OfferTransfersOnlyStoredActiveHandItem()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var giver = SSpawnAtPosition("MobHuman", map.GridCoords);
            var receiver = SSpawnAtPosition("MobHuman", map.GridCoords);
            var item = SSpawnAtPosition("Crowbar", map.GridCoords);
            var hands = SEntMan.System<SharedHandsSystem>();
            var offerSystem = SEntMan.System<OfferItemSystem>();
            var giverHands = SComp<HandsComponent>(giver);
            var receiverHands = SComp<HandsComponent>(receiver);

            Assert.That(hands.TryPickupAnyHand(giver, item, handsComp: giverHands), Is.True);
            var offer = SComp<OfferItemComponent>(giver);
            var receive = SComp<OfferItemComponent>(receiver);
            Assert.That(offerSystem.TryStartOffer(giver), Is.True);

            var interact = new InteractUsingEvent(giver, item, receiver, SComp<TransformComponent>(receiver).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(receiver, interact);
            Assert.That(interact.Handled, Is.True);

            offerSystem.Receive((receiver, receive));

            Assert.That(hands.IsHolding((receiver, receiverHands), item), Is.True);
            Assert.That(offer.Item, Is.Null);
            Assert.That(receive.Target, Is.Null);
        });
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void OfferCancelsWhenActiveHandChanges()
    {
        var giver = SSpawn("MobHuman");
        var item = SSpawn("Crowbar");
        var hands = SEntMan.System<SharedHandsSystem>();
        var offerSystem = SEntMan.System<OfferItemSystem>();
        var handsComponent = SComp<HandsComponent>(giver);

        Assert.That(hands.TryPickupAnyHand(giver, item, handsComp: handsComponent), Is.True);
        Assert.That(offerSystem.TryStartOffer(giver), Is.True);

        var offer = SComp<OfferItemComponent>(giver);
        var otherHand = handsComponent.SortedHands.Single(x => x != offer.Hand);
        Assert.That(hands.TrySetActiveHand((giver, handsComponent), otherHand), Is.True);

        Assert.That(offer.IsInOfferMode, Is.False);
        Assert.That(offer.Hand, Is.Null);
        Assert.That(offer.Item, Is.Null);
        Assert.That(offer.Target, Is.Null);
    }

    [Test]
    [Category("FieldCautery")]
    public async Task FieldCauteryRequiresActivationAndTreatsOneWound()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var userCoords = map.GridCoords.Offset(new System.Numerics.Vector2(0.5f, 0.5f));
            var patientCoords = map.GridCoords.Offset(new System.Numerics.Vector2(1.5f, 0.5f));
            var user = SSpawnAtPosition("MobHuman", userCoords);
            var patient = SSpawnAtPosition("MobHuman", patientCoords);
            var lighter = SSpawnAtPosition("Lighter", userCoords);
            var transform = SEntMan.System<SharedTransformSystem>();
            transform.AnchorEntity(user, SComp<TransformComponent>(user));
            transform.AnchorEntity(patient, SComp<TransformComponent>(patient));
            var hands = SEntMan.System<SharedHandsSystem>();
            var toggles = SEntMan.System<ItemToggleSystem>();
            var body = SEntMan.System<BodySystem>();
            var wounds = SEntMan.System<WoundSystem>();
            var damage = SEntMan.System<DamageableSystem>();
            var amount = new DamageSpecifier { DamageDict = new() { { "Slash", FixedPoint2.New(20) } } };
            var handsComponent = SComp<HandsComponent>(user);

            Assert.That(hands.TryPickupAnyHand(user, lighter, handsComp: handsComponent), Is.True);
            damage.ChangeDamage(patient, amount, targetPart: TargetBodyPart.Chest, canMiss: false);

            var chest = body.GetOrgan(patient, "Torso")!.Value;
            EntProtoId puncture = "Puncture";
            Assert.That(wounds.TryCreateWound(
                (chest, SComp<WoundableComponent>(chest)),
                puncture,
                FixedPoint2.New(10),
                out _,
                null), Is.True);
            var bleedingWounds = wounds.GetWoundableWounds(chest)
                .Where(wound => SEntMan.TryGetComponent<BleedInflicterComponent>(wound, out var bleed) && bleed.IsBleeding)
                .ToList();
            Assert.That(bleedingWounds, Has.Count.EqualTo(2));
            var worstWound = bleedingWounds.MaxBy(wound => SComp<BleedInflicterComponent>(wound).BleedingAmountRaw);

            var inactiveInteraction = new InteractUsingEvent(user, lighter, patient, SComp<TransformComponent>(patient).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(patient, inactiveInteraction);
            Assert.That(inactiveInteraction.Handled, Is.False);
            Assert.That(SEntMan.TryGetComponent<DoAfterComponent>(user, out var inactiveDoAfter) &&
                        inactiveDoAfter.DoAfters.Count > 0,
                Is.False);

            Assert.That(toggles.TryActivate((lighter, SComp<ItemToggleComponent>(lighter)), user), Is.True);
            var damageBefore = damage.GetTotalDamage(chest);
            var interaction = SEntMan.System<SharedInteractionSystem>();
            var actionBlocker = SEntMan.System<Content.Shared.ActionBlocker.ActionBlockerSystem>();
            Assert.That(interaction.InRangeAndAccessible(user, patient), Is.True);
            Assert.That(interaction.InRangeUnobstructed(user, lighter), Is.True);
            Assert.That(actionBlocker.CanInteract(user, patient), Is.True);
            var activeInteraction = new InteractUsingEvent(user, lighter, patient, SComp<TransformComponent>(patient).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(patient, activeInteraction);
            Assert.That(activeInteraction.Handled, Is.True);

            var completion = new FieldCauterizeDoAfterEvent(SEntMan.GetNetEntity(chest));
            var completionArgs = new DoAfterArgs(SEntMan, user, 3f, completion, patient, patient, lighter);
            completion.DoAfter = new Content.Shared.DoAfter.DoAfter(0, completionArgs, TimeSpan.Zero);
            SEntMan.EventBus.RaiseLocalEvent(patient, completion);

            var bleedingAfter = wounds.GetWoundableWounds(chest)
                .Count(wound => SEntMan.TryGetComponent<BleedInflicterComponent>(wound, out var bleed) && bleed.IsBleeding);
            Assert.That(bleedingAfter, Is.EqualTo(bleedingWounds.Count - 1));
            Assert.That(SComp<BleedInflicterComponent>(worstWound).IsBleeding, Is.False);
            Assert.That(damage.GetTotalDamage(chest) - damageBefore, Is.EqualTo(FixedPoint2.New(15)));
        });
    }

    [Test]
    [RunOnSide(Side.Server)]
    public void GunBashPrototypeDealsBluntAndStaminaDamage()
    {
        var gun = SSpawn("WeaponPistolViper");
        var melee = SComp<MeleeWeaponComponent>(gun);

        Assert.That(SEntMan.HasComponent<AltFireMeleeComponent>(gun), Is.True);
        Assert.That(melee.Damage.DamageDict["Blunt"], Is.GreaterThan(FixedPoint2.Zero));
        Assert.That(melee.BluntStaminaDamageFactor, Is.GreaterThan(FixedPoint2.Zero));
    }

    [Test]
    public async Task TorsoGibbingDropsAllLimbs()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid human = default;
        EntityUid torso = default;
        EntityUid head = default;
        EntityUid leftArm = default;
        EntityUid rightArm = default;
        EntityUid leftLeg = default;
        EntityUid rightLeg = default;
        EntityUid leftHand = default;
        EntityUid rightHand = default;
        EntityUid leftFoot = default;
        EntityUid rightFoot = default;

        await server.WaitAssertion(() =>
        {
            human = server.EntMan.Spawn("MobHuman", map.MapCoords);
            var body = server.System<BodySystem>();
            torso = body.GetOrgan(human, "Torso")!.Value;
            head = body.GetOrgan(human, "Head")!.Value;
            leftArm = body.GetOrgan(human, "ArmLeft")!.Value;
            rightArm = body.GetOrgan(human, "ArmRight")!.Value;
            leftLeg = body.GetOrgan(human, "LegLeft")!.Value;
            rightLeg = body.GetOrgan(human, "LegRight")!.Value;
            leftHand = body.GetOrgan(human, "HandLeft")!.Value;
            rightHand = body.GetOrgan(human, "HandRight")!.Value;
            leftFoot = body.GetOrgan(human, "FootLeft")!.Value;
            rightFoot = body.GetOrgan(human, "FootRight")!.Value;

            server.System<GibbingSystem>().Gib(torso);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(torso), Is.True);
            Assert.That(server.EntMan.Deleted(human), Is.True);
            Assert.That(server.EntMan.Deleted(head), Is.False, "Head was deleted!");
            Assert.That(server.EntMan.Deleted(leftArm), Is.False, "Left arm was deleted!");
            Assert.That(server.EntMan.Deleted(rightArm), Is.False, "Right arm was deleted!");
            Assert.That(server.EntMan.Deleted(leftLeg), Is.False, "Left leg was deleted!");
            Assert.That(server.EntMan.Deleted(rightLeg), Is.False, "Right leg was deleted!");
            Assert.That(server.EntMan.Deleted(leftHand), Is.False, "Left hand was deleted!");
            Assert.That(server.EntMan.Deleted(rightHand), Is.False, "Right hand was deleted!");
            Assert.That(server.EntMan.Deleted(leftFoot), Is.False, "Left foot was deleted!");
            Assert.That(server.EntMan.Deleted(rightFoot), Is.False, "Right foot was deleted!");
        });
    }
}
