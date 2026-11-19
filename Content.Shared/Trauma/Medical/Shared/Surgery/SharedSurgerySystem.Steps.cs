// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Medical.Common.Body;
using Content.Medical.Common.Surgery;
using Content.Medical.Common.Surgery.Tools;
using Content.Medical.Common.Traumas;
using Content.Medical.Shared.Body;
using Content.Medical.Shared.Surgery.Conditions;
using Content.Medical.Shared.Surgery.Effects.Step;
using Content.Medical.Shared.Surgery.Steps;
using Content.Medical.Shared.Surgery.Steps.Parts;
using Content.Medical.Shared.Surgery.Tools;
using Content.Medical.Shared.Traumas;
using Content.Medical.Shared.Wounds;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body;
using Content.Shared.Buckle.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Shared.Traits.Assorted;
using Content.Trauma.Common.Body.Part;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Medical.Shared.Surgery;

public abstract partial class SharedSurgerySystem
{
    [Dependency] protected BodyPartSystem _part = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private EntityQuery<BodyPartComponent> _partQuery = default!;
    [Dependency] private EntityQuery<BleedInflicterComponent> _bleedQuery = default!;
    [Dependency] private EntityQuery<OrganComponent> _organQuery = default!;
    [Dependency] private EntityQuery<SurgeryComponent> _query = default!;
    [Dependency] private EntityQuery<SurgeryIgnoreClothingComponent> _ignoreQuery = default!;
    [Dependency] private EntityQuery<SurgeryStepComponent> _stepQuery = default!;
    [Dependency] private EntityQuery<SurgeryToolComponent> _toolQuery = default!;

    public static readonly ProtoId<DamageGroupPrototype> Brute = "Brute";
    public static readonly ProtoId<DamageTypePrototype> Poison = "Poison";
    private static readonly FixedPoint2 SepsisDamageLimit = FixedPoint2.New(90);

    private readonly List<EntityUid> _nextStepList = new();

    private void InitializeSteps()
    {
        /*  Abandon all hope ye who enter here. Now I am become shitcoder, the bloater of files.
            On a serious note, I really hate how much bloat this pattern of subscribing to a StepEvent and a CheckEvent
            creates in terms of readability. And while Check DOES only run on the server side, it's still annoying to parse through.*/

        SubSurgery<SurgeryTendWoundsEffectComponent>(OnTendWoundsStep, OnTendWoundsCheck);
        SubSurgery<SurgeryStepCavityEffectComponent>(OnCavityStep, OnCavityCheck);
        SubSurgery<SurgeryAddPartStepComponent>(OnAddPartStep, OnAddPartCheck);
        SubSurgery<SurgeryAffixPartStepComponent>(OnAffixPartStep, OnAffixPartCheck);
        SubSurgery<SurgeryRemovePartStepComponent>(OnRemovePartStep, OnRemovePartCheck);
        SubSurgery<SurgeryAddOrganStepComponent>(OnAddOrganStep, OnAddOrganCheck);
        SubSurgery<SurgeryRemoveOrganStepComponent>(OnRemoveOrganStep, OnRemoveOrganCheck);
        SubSurgery<SurgeryAffixOrganStepComponent>(OnAffixOrganStep, OnAffixOrganCheck);
        SubSurgery<SurgeryAddOrganSlotStepComponent>(OnAddOrganSlotStep, OnAddOrganSlotCheck);
        SubSurgery<SurgeryTraumaTreatmentStepComponent>(OnTraumaTreatmentStep, OnTraumaTreatmentCheck);
        SubSurgery<SurgeryBleedsTreatmentStepComponent>(OnBleedsTreatmentStep, OnBleedsTreatmentCheck);
        SubscribeLocalEvent<SurgeryAddPartStepComponent, SurgeryCanPerformStepEvent>(OnAddPartCanPerform);
        SubscribeLocalEvent<SurgeryAddOrganStepComponent, SurgeryCanPerformStepEvent>(OnAddOrganCanPerform);
        Subs.BuiEvents<SurgeryTargetComponent>(SurgeryUIKey.Key, subs =>
        {
            subs.Event<SurgeryStepChosenBuiMsg>(OnSurgeryTargetStepChosen);
        });
    }

    private void SubSurgery<TComp>(EntityEventRefHandler<TComp, SurgeryStepEvent> onStep,
        EntityEventRefHandler<TComp, SurgeryStepCompleteCheckEvent> onComplete) where TComp : IComponent
    {
        SubscribeLocalEvent(onStep);
        SubscribeLocalEvent(onComplete);
    }

    #region Event Methods
    [SubscribeLocalEvent]
    private void OnToolStep(Entity<SurgeryStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryToolAudio(ent, args))
           return;

        ApplyComponentChanges(args, ent.Comp);
        HandleOrganModifications(args, ent.Comp);

        HandleSanitization(args);

        OnStepApplied(ent, ref args);
    }

    protected virtual void OnStepApplied(Entity<SurgeryStepComponent> ent, ref SurgeryStepEvent args)
    {
    }

    private void ApplyComponentChanges(SurgeryStepEvent args, SurgeryStepComponent comp)
    {
        AddOrRemoveComponentsToEntity(args.Part, comp.Add);
        AddOrRemoveComponentsToEntity(args.Part, comp.Remove, true);
        AddOrRemoveComponentsToEntity(args.Body, comp.BodyAdd);
        AddOrRemoveComponentsToEntity(args.Body, comp.BodyRemove, true);
    }

    private void HandleOrganModifications(SurgeryStepEvent args, SurgeryStepComponent comp)
    {
        HandleOrganModification(args.Part, args.Body, comp.AddOrganOnAdd);
        HandleOrganModification(args.Part, args.Body, comp.RemoveOrganOnAdd, true); // oh my goida code
    }

    [SubscribeLocalEvent]
    private void OnToolCheck(Entity<SurgeryStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (CheckComponentChanges(ent.Comp, args) || CheckOrganChanges(ent.Comp, args))
            args.Cancelled = true;
    }

    private bool CheckComponentChanges(SurgeryStepComponent comp, SurgeryStepCompleteCheckEvent args)
    {
        return TryToolCheck(comp.Add, args.Part) ||
               TryToolCheck(comp.Remove, args.Part, checkMissing: false) ||
               TryToolCheck(comp.BodyAdd, args.Body) ||
               TryToolCheck(comp.BodyRemove, args.Body, checkMissing: false);
    }

    private bool CheckOrganChanges(SurgeryStepComponent comp, SurgeryStepCompleteCheckEvent args)
    {
        return TryToolOrganCheck(comp.AddOrganOnAdd, args.Part) ||
               TryToolOrganCheck(comp.RemoveOrganOnAdd, args.Part, checkMissing: false);
    }

    [SubscribeLocalEvent]
    private void OnToolCanPerform(Entity<SurgeryStepComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        if (args.IsInvalid)
            return;

        if (args.TargetSlots != SlotFlags.NONE
            && !_ignoreQuery.HasComp(args.User)
            && !_ignoreQuery.HasComp(args.Tool)
            && _inventory.TryGetContainerSlotEnumerator(args.Body, out var containerSlotEnumerator, args.TargetSlots))
        {
            while (containerSlotEnumerator.MoveNext(out var containerSlot))
            {
                if (!containerSlot.ContainedEntity.HasValue)
                    continue;

                if (_ignoreQuery.HasComp(containerSlot.ContainedEntity.Value))
                    continue;

                args.Invalid = StepInvalidReason.Armor;
                args.Popup = Loc.GetString("surgery-ui-window-steps-error-armor");
                return;
            }
        }

        if (ent.Comp.Tool == null)
            return;

        foreach (var reg in ent.Comp.Tool.Values)
        {
            if (GetSurgeryComp(args.Tool, reg.Component) is {} data)
            {
                args.ValidTool = data;
                return; // multiple required tools isn't supported so just return
            }

            args.Invalid = StepInvalidReason.MissingTool;

            if (reg.Component is CauteryComponent && TryComp<WelderComponent>(args.Tool, out var welder) && !welder.Enabled)
                args.Popup = Loc.GetString("surgery-cautery-unlit");
            else if (reg.Component is BaseSurgeryToolComponent required)
                args.Popup = Loc.GetString("surgery-ui-window-tool-required", ("tool", required.ToolName));
            else
                Log.Error($"Surgery step {ToPrettyString(ent)} wants bad component {reg.Component} which isn't a ISurgeryTool");

            return;
        }
    }

    [SubscribeLocalEvent]
    private void OnTableCanPerform(Entity<SurgeryOperatingTableConditionComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        if (args.IsInvalid)
            return;

        // mobs that can't be buckled can never be operated because of this check
        if (!TryComp(args.Body, out BuckleComponent? buckle) ||
            !HasComp<OperatingTableComponent>(buckle.BuckledTo))
        {
            args.Invalid = StepInvalidReason.NeedsOperatingTable;
        }
    }

    private void OnTendWoundsStep(Entity<SurgeryTendWoundsEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (_wounds.GetWoundableSeverityPoint(
                args.Part,
                damageGroup: ent.Comp.MainGroup,
                healable: true) <= 0)
            return;

        // Right now the bonus is based off the body's total damage, maybe we could make it based off each part in the future.
        var bonus = ent.Comp.HealMultiplier * _wounds.GetWoundableSeverityPoint(args.Part, damageGroup: ent.Comp.MainGroup);

        if (_mobState.IsDead(args.Body))
            bonus *= 0.2;

        var adjustedDamage = new DamageSpecifier(ent.Comp.Damage);

        if (ProtoMan.TryIndex<DamageGroupPrototype>(ent.Comp.MainGroup, out var group))
        {
            foreach (var type in group.DamageTypes)
                adjustedDamage.DamageDict[type] -= bonus;
        }

        var ev = new SurgeryStepDamageEvent(args.User, args.Body, args.Part, args.Surgery, adjustedDamage);
        RaiseLocalEvent(args.Body, ref ev);
    }

    private void OnTendWoundsCheck(Entity<SurgeryTendWoundsEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (_wounds.HasDamageOfGroup(args.Part, ent.Comp.MainGroup))
            args.Cancelled = true;
    }

    private void OnCavityStep(Entity<SurgeryStepCavityEffectComponent> ent, ref SurgeryStepEvent args)
    {
        // <Trauma> - rewritten to use event
        var ev = new GetBodyPartCavityEvent();
        RaiseLocalEvent(args.Part, ref ev);
        if (ev.Container is not {} container)
            return;

        var activeHandEntity = _hands.EnumerateHeld(args.User).FirstOrDefault();
        if (activeHandEntity != default && ent.Comp.Action == "Insert")
            _container.Insert(activeHandEntity, container);
        else if (ent.Comp.Action == "Remove" && container.ContainedEntity is {} contained)
            _hands.TryPickupAnyHand(args.User, contained);
        // </Trauma>
    }

    private void OnCavityCheck(Entity<SurgeryStepCavityEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        // <Trauma> - rewritten to use event
        var ev = new GetBodyPartCavityEvent();
        RaiseLocalEvent(args.Part, ref ev);
        if (ev.Container is not {} container
            || (ent.Comp.Action == "Insert" && container.Count == 0)
            || (ent.Comp.Action == "Remove" && container.Count != 0))
            args.Cancelled = true;
        // </Trauma>
    }

    private void OnAddPartStep(Entity<SurgeryAddPartStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryComp(args.Surgery, out SurgeryPartRemovedConditionComponent? removedComp)
            || !_organQuery.TryComp(args.Tool, out var organ)
            || organ.Category != removedComp.Category)
            return;

        if (HasComp<RottingComponent>(args.Tool))
            return;

        if (_body.InsertOrgan(args.Body, (args.Tool, organ)))
        {
            EnsureComp<OrganReattachedComponent>(args.Tool);

            if (_container.TryGetContainer(args.Tool, "body_part_organs", out var partContainer))
            {
                foreach (var contained in partContainer.ContainedEntities.ToArray())
                {
                    _container.Remove(contained, partContainer);
                    _body.InsertOrgan(args.Body, contained);
                }
            }
        }
    }

    private void OnAddPartCanPerform(Entity<SurgeryAddPartStepComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        if (args.IsInvalid)
            return;

        if (HasComp<RottingComponent>(args.Tool))
        {
            args.Invalid = StepInvalidReason.ToolInvalid;
            args.Popup = Loc.GetString("surgery-error-rotting");
        }
    }

    private void OnAddOrganSlotStep(Entity<SurgeryAddOrganSlotStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryComp(args.Surgery, out SurgeryOrganSlotConditionComponent? condition))
            return;

        _part.TryAddSlot(args.Part, condition.OrganSlot);
    }

    private void OnAddOrganSlotCheck(Entity<SurgeryAddOrganSlotStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryComp(args.Surgery, out SurgeryOrganSlotConditionComponent? condition))
            return;

        args.Cancelled |= !_part.HasOrganSlot(args.Part, condition.OrganSlot);
    }

    private void OnAffixPartStep(Entity<SurgeryAffixPartStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryComp(args.Surgery, out SurgeryPartRemovedConditionComponent? removedComp))
            return;

        EntityUid? targetPart = _body.GetOrgan(args.Body, removedComp.Category);
        if (targetPart == null && removedComp.Part != BodyPartType.Other)
            targetPart = _part.FindBodyPart(args.Body, removedComp.Part, removedComp.Symmetry)?.Owner;

        if (targetPart == null)
            return;

        // We reward players for properly affixing the parts by healing a little bit of damage, and enabling the part temporarily.
        _wounds.TryHealWounds(targetPart.Value, 12f, out _, damageGroup: Brute);
        RemComp<OrganReattachedComponent>(targetPart.Value);
    }

    private void OnAffixPartCheck(Entity<SurgeryAffixPartStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryComp(args.Surgery, out SurgeryPartRemovedConditionComponent? removedComp))
            return;

        if (_body.GetOrgan(args.Body, removedComp.Category) is {} organ && HasComp<OrganReattachedComponent>(organ))
            args.Cancelled = true;
    }

    private void OnAddPartCheck(Entity<SurgeryAddPartStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryComp(args.Surgery, out SurgeryPartRemovedConditionComponent? removedComp)
            || _body.GetOrgan(args.Body, removedComp.Category) == null)
            args.Cancelled = true;
    }

    private void OnRemovePartStep(Entity<SurgeryRemovePartStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (!_organQuery.TryComp(args.Part, out var organ) || organ.Body != args.Body)
            return;

        if (_part.GetParentPart(args.Part) is { } parentPart &&
            _wounds.AmputateWoundable(parentPart, args.Part, args.User))
        {
            return;
        }

        if (_body.RemoveOrgan(args.Body, args.Part))
            _hands.TryPickupAnyHand(args.User, args.Part);
        else
            _popup.PopupEntity(Loc.GetString("surgery-popup-step-SurgeryStepRemovePart-failed"), args.User, args.User);
    }

    private void OnRemovePartCheck(Entity<SurgeryRemovePartStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        args.Cancelled |= !_partQuery.HasComp(args.Part) || _body.GetBody(args.Part) == args.Body;
    }

    private void OnAddOrganStep(Entity<SurgeryAddOrganStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryComp(args.Surgery, out SurgeryOrganConditionComponent? organComp))
            return;

        if (HasComp<RottingComponent>(args.Tool))
            return;

        if (!HasComp<InternalChildOrganComponent>(args.Tool) ||
            _body.GetCategory(args.Tool) is not {} category ||
            category != organComp.Organ ||
            !_part.InsertOrgan(args.Part, args.Tool))
            return;

        EnsureComp<OrganReattachedComponent>(args.Tool);

        var ev = new SurgeryStepDamageChangeEvent(args.User, args.Body, args.Part, ent);
            RaiseLocalEvent(ent, ref ev);
            args.Complete = true;
    }

    private void OnAddOrganCanPerform(Entity<SurgeryAddOrganStepComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        if (args.IsInvalid)
            return;

        if (HasComp<RottingComponent>(args.Tool))
        {
            args.Invalid = StepInvalidReason.ToolInvalid;
            args.Popup = Loc.GetString("surgery-error-rotting");
        }
    }

    private void OnAddOrganCheck(Entity<SurgeryAddOrganStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryComp<SurgeryOrganConditionComponent>(args.Surgery, out var organComp))
            return;

        // For now we naively assume that every entity will only have one of each organ type.
        // that we do surgery on, but in the future we'll need to reference their prototype somehow
        // to know if they need 2 hearts, 2 lungs, etc.
        // The step is completed if the part has the target organ.
        args.Cancelled |= !_part.HasOrgan(args.Part, organComp.Organ);
    }

    private void OnAffixOrganStep(Entity<SurgeryAffixOrganStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryComp(args.Surgery, out SurgeryOrganConditionComponent? organComp)
            || !organComp.Reattaching)
            return;

        if (_part.GetOrgan(args.Part, organComp.Organ) is {} organ)
            RemComp<OrganReattachedComponent>(organ);
    }

    private void OnAffixOrganCheck(Entity<SurgeryAffixOrganStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryComp(args.Surgery, out SurgeryOrganConditionComponent? organComp)
            || !organComp.Reattaching)
            return;

        var category = organComp.Organ;
        args.Cancelled |= _part.GetOrgan(args.Part, category) is {} organ && HasComp<OrganReattachedComponent>(organ);
    }

    private void OnRemoveOrganStep(Entity<SurgeryRemoveOrganStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryComp<SurgeryOrganConditionComponent>(args.Surgery, out var organComp) ||
            _part.GetOrgan(args.Part, organComp.Organ) is not {} organ)
            return;

        if (_part.RemoveOrgan(args.Part, organ))
            _hands.TryPickupAnyHand(args.User, organ);
        else
            _popup.PopupEntity(Loc.GetString("surgery-popup-step-SurgeryStepRemoveOrgan-failed"), args.User, args.User);
    }

    private void OnRemoveOrganCheck(Entity<SurgeryRemoveOrganStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryComp<SurgeryOrganConditionComponent>(args.Surgery, out var organComp))
            return;

        args.Cancelled |= _part.HasOrgan(args.Part, organComp.Organ);
    }

    private void OnTraumaTreatmentStep(Entity<SurgeryTraumaTreatmentStepComponent> ent, ref SurgeryStepEvent args)
    {
        var healAmount = ent.Comp.Amount;
        switch (ent.Comp.TraumaType)
        {
            case TraumaType.OrganDamage:
                foreach (var organ in _part.GetPartOrgans(args.Part).Values)
                {
                    if (!TryComp<InternalChildOrganComponent>(organ, out var internalOrgan))
                        continue;

                    foreach (var modifier in internalOrgan.IntegrityModifiers.ToArray())
                    {
                        if (modifier.Value <= 0)
                            continue;

                        if (healAmount >= modifier.Value)
                        {
                            if (!_trauma.TryRemoveOrganDamageModifier(
                                (organ.Owner, internalOrgan),
                                modifier.Key.Item2,
                                modifier.Key.Item1))
                                continue;

                            healAmount -= modifier.Value;
                            if (healAmount == 0)
                                return;
                        }
                        else
                        {
                            if (_trauma.TryChangeOrganDamageModifier(
                                (organ.Owner, internalOrgan),
                                -healAmount,
                                modifier.Key.Item2,
                                modifier.Key.Item1))
                                return;
                        }
                    }
                }

                break;

            case TraumaType.BoneDamage:
                _trauma.DamageBone(args.Part, -healAmount);
                break;

            case TraumaType.Dismemberment:
                _trauma.RemoveTraumas(args.Part, TraumaType.Dismemberment);

                break;
        }
    }

    private void OnTraumaTreatmentCheck(Entity<SurgeryTraumaTreatmentStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (_trauma.HasWoundableTrauma(args.Part, ent.Comp.TraumaType))
            args.Cancelled = true;
    }

    private void OnBleedsTreatmentStep(Entity<SurgeryBleedsTreatmentStepComponent> ent, ref SurgeryStepEvent args)
    {
        var remainingClamps = (int) ent.Comp.Amount.Float();
        foreach (var wound in _wounds.GetWoundableWounds(args.Part))
        {
            if (remainingClamps <= 0)
                break;

            if (!_bleedQuery.TryComp(wound, out var bleeds) || !bleeds.IsBleeding)
                continue;

            bleeds.BleedingAmountRaw = 0;
            bleeds.Scaling = 0;
            bleeds.IsBleeding = false; // Won't bleed as long as it's not reopened

            DirtyFields(wound, bleeds, null,
                nameof(BleedInflicterComponent.BleedingAmountRaw),
                nameof(BleedInflicterComponent.Scaling),
                nameof(BleedInflicterComponent.IsBleeding));

            remainingClamps--;
        }
    }

    private void OnBleedsTreatmentCheck(Entity<SurgeryBleedsTreatmentStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        foreach (var wound in _wounds.GetWoundableWounds(args.Part))
        {
            if (!_bleedQuery.TryComp(wound, out var bleeds) || !bleeds.IsBleeding)
                continue;

            args.Cancelled = true;
            break;
        }
    }

    private void OnSurgeryTargetStepChosen(Entity<SurgeryTargetComponent> ent, ref SurgeryStepChosenBuiMsg args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var user = args.Actor;
        if (GetEntity(args.Entity) is {} body &&
            GetEntity(args.Part) is {} targetPart)
        {
            TryDoSurgeryStep(body, targetPart, user, args.Surgery, args.Step);
        }
    }
    #endregion

    #region Helper Methods
    private void HandleSanitization(SurgeryStepEvent args)
    {
        if (_inventory.TryGetSlotEntity(args.User, "gloves", out var _)
            && _inventory.TryGetSlotEntity(args.User, "mask", out var _))
            return;

        var sepsisEv = new SurgerySanitizationEvent();
        RaiseLocalEvent(args.User, ref sepsisEv);

        if (sepsisEv.Handled)
            return;

        if (TryComp<SurgeryTargetComponent>(args.Body, out var surgeryTargetComponent) &&
            surgeryTargetComponent.SepsisImmune)
            return;

        if (_damageable.GetDamageAmount(args.Part, Poison) >= SepsisDamageLimit)
            return;

        if (ProtoMan.TryIndex(Poison, out var poisonProto))
        {
            var sepsis = new DamageSpecifier(poisonProto, 5);
            var ev = new SurgeryStepDamageEvent(args.User, args.Body, args.Part, args.Surgery, sepsis);
            RaiseLocalEvent(args.Body, ref ev);
        }
    }

    private bool TryToolAudio(Entity<SurgeryStepComponent> ent, SurgeryStepEvent args)
    {
        if (ent.Comp.Tool == null)
            return true;

        foreach (var reg in ent.Comp.Tool.Values)
        {
            if (!HasSurgeryComp(args.Tool, reg.Component))
                return false;

            if (_toolQuery.CompOrNull(args.Tool)?.EndSound is not { } sound)
                continue;
            _audio.PlayPredicted(sound, args.Tool, args.User);
            break; // no overlaying sounds
        }

        return true;
    }
    private void HandleOrganModification(EntityUid organTarget,
        EntityUid bodyTarget,
        Dictionary<ProtoId<OrganCategoryPrototype>, ComponentRegistry>? modifications,
        bool remove = false)
    {
        if (modifications == null)
            return;

        var organs = _part.GetPartOrgans(organTarget);
        foreach (var (category, components) in modifications)
        {
            if (!organs.TryGetValue(category, out var organ))
                continue;

            var comp = EnsureComp<OrganComponentsComponent>(organ);
            if (remove)
            {
                foreach (var key in components.Keys)
                {
                    comp.OnAdd?.Remove(key);
                    comp.AddedKeys.Remove(key);
                }
            }
            else
            {
                comp.OnAdd ??= new ComponentRegistry();

                foreach (var (key, compToAdd) in components)
                {
                    comp.OnAdd[key] = compToAdd;
                    comp.AddedKeys.Add(key);
                }
                EntityManager.AddComponents(bodyTarget, components);
            }

            Dirty(organ, comp);
        }
    }

    private void AddOrRemoveComponentsToEntity(EntityUid ent, ComponentRegistry? componentRegistry, bool remove = false)
    {
        if (componentRegistry is not {} comps)
            return;

        if (remove)
            EntityManager.RemoveComponents(ent, comps);
        else
            EntityManager.AddComponents(ent, comps);
    }

    private bool TryToolCheck(ComponentRegistry? components, EntityUid target, bool checkMissing = true)
    {
        if (components == null)
            return false;

        foreach (var (_, entry) in components)
        {
            var hasComponent = HasComp(target, entry.Component.GetType());
            if (checkMissing != hasComponent)
                return true; // Early exit if condition fails
        }

        return false;
    }

    private bool TryToolOrganCheck(IReadOnlyDictionary<ProtoId<OrganCategoryPrototype>, ComponentRegistry>? organChanges, EntityUid part, bool checkMissing = true)
    {
        if (organChanges == null)
            return false;

        var organs = _part.GetPartOrgans(part);
        foreach (var (category, compsToAdd) in organChanges)
        {
            if (!organs.TryGetValue(category, out var organ) ||
                !TryComp<OrganComponentsComponent>(organ, out var organComps))
                continue;
            if (checkMissing)
            {
                if (compsToAdd.Keys.Any(key => !organComps.AddedKeys.Contains(key)))
                    return true;
            }
            else
            {
                if (compsToAdd.Keys.Any(key => organComps.AddedKeys.Contains(key)))
                    return true;
            }
        }

        return false;
    }

    private bool TryDoSurgeryStep(EntityUid body, EntityUid targetPart, EntityUid user, EntProtoId surgeryId, EntProtoId stepId)
        => TryDoSurgeryStep(body, targetPart, user, surgeryId, stepId, out _);

    /// <summary>
    /// Do a surgery step on a part, if it can be done.
    /// Returns true if it succeeded.
    /// </summary>
    public bool TryDoSurgeryStep(EntityUid body, EntityUid targetPart, EntityUid user, EntProtoId surgeryId, EntProtoId stepId, out StepInvalidReason error)
    {
        error = StepInvalidReason.None;
        if (!IsSurgeryValid(body, targetPart, surgeryId, stepId, user, out var surgery, out var part, out var step))
        {
            error = StepInvalidReason.SurgeryInvalid;
            return false;
        }

        if (!PreviousStepsComplete(body, part, surgery, stepId, user))
        {
            error = StepInvalidReason.MissingPreviousSteps;
            return false;
        }

        if (IsStepComplete(body, part, stepId, surgery))
        {
            error = StepInvalidReason.StepCompleted;
            return false;
        }

        var tool = _hands.GetActiveItemOrSelf(user);
        if (!CanPerformStep(user, body, part, step, tool, true, out _, out error, out var data))
            return false;

        var toolComp = _toolQuery.CompOrNull(tool);
        var usedEv = new SurgeryToolUsedEvent(user, body);
        usedEv.IgnoreToggle = toolComp?.IgnoreToggle ?? false;
        RaiseLocalEvent(tool, ref usedEv);
        if (usedEv.Cancelled)
        {
            error = StepInvalidReason.ToolInvalid;
            return false;
        }

        if (toolComp?.StartSound is {} sound)
            _audio.PlayPredicted(sound, tool, user);

        _rotateToFace.TryFaceCoordinates(user, _transform.GetMapCoordinates(body).Position);

        // We need to check for nullability because of surgeries that dont require a tool, like Cavity Implants
        var speed = data?.Speed ?? 1f;
        var toolUsed = data?.Used ?? false; // if no tool is being used you can't consume it
        var ev = new SurgeryDoAfterEvent(surgeryId, stepId, toolUsed);
        var duration = GetSurgeryDuration(step, user, body, speed);

        if (!_interaction.InRangeUnobstructed(user, body))
        {
            error = StepInvalidReason.SurgeryInvalid;
            return false;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(duration), ev, body, part)
        {
            BreakOnMove = true,
            DistanceThreshold = null,
            CancelDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
            NeedHand = true,
            BreakOnHandChange = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
            ExtraCheck = () => _interaction.InRangeUnobstructed(user, body),
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            error = StepInvalidReason.DoAfterFailed;
            return false;
        }

        var userName = Identity.Entity(user, EntityManager);
        object targetName = body == user
            ? Loc.GetString("surgery-popup-self")
            : Identity.Entity(body, EntityManager);

        var locName = $"surgery-popup-procedure-{surgeryId}-step-{stepId}";
        if (!Loc.TryGetString(locName, out var locResult, ("user", userName), ("target", targetName), ("part", part), ("tool", tool)))
            locResult = Loc.GetString($"surgery-popup-step-{stepId}", ("user", userName), ("target", targetName), ("part", part), ("tool", tool));

        if (_net.IsServer)
        {
            _popup.PopupEntity(locResult, user, user);

            if (IsConsciousAndAwake(body))
            {
                _jittering.DoJitter(body, TimeSpan.FromSeconds(duration + 0.5f), true, 18f, 7f);
                Timer.Spawn(TimeSpan.FromSeconds(0.6), () =>
                {
                    if (Deleted(user) || Deleted(body))
                        return;

                    _popup.PopupEntity(Loc.GetString("surgery-pain-thrash", ("target", targetName)), user, user, PopupType.MediumCaution);
                    if (body != user)
                        _popup.PopupEntity(Loc.GetString("surgery-pain-thrash-patient"), body, body, PopupType.LargeCaution);
                });
            }
        }

        return true;
    }

    public bool IsConsciousAndAwake(EntityUid target)
    {
        if (!TryComp<MobStateComponent>(target, out var mobState) || mobState.CurrentState != MobState.Alive)
            return false;

        if (Status.HasEffectComp<ForcedSleepingStatusEffectComponent>(target))
            return false;

        if (Status.TryEffectsWithComp<PainNumbnessStatusEffectComponent>(target, out _))
            return false;

        if (HasComp<SleepingComponent>(target))
            return false;

        return true;
    }

    private float GetSurgeryDuration(EntityUid surgeryStep, EntityUid user, EntityUid target, float toolSpeed)
    {
        if (!_stepQuery.TryComp(surgeryStep, out var stepComp))
            return 2f; // Shouldnt really happen but just a failsafe.

        var speed = toolSpeed;
        if(TryComp<BuckleComponent>(target, out var buckleComp)) // Get buckle component from target.
            if(TryComp<OperatingTableComponent>(buckleComp.BuckledTo, out var operatingTableComponent))  // If they are buckled to entity with operating table component
                speed *= operatingTableComponent.SpeedModifier; // apply surgery speed modifier
        if (TryComp(user, out SurgerySpeedModifierComponent? surgerySpeedMod))
            speed *= surgerySpeedMod.SpeedModifier;

        if (IsConsciousAndAwake(target))
            speed *= 0.55f;

        var ev = new BeforeSurgeryStepDurationEvent(user, surgeryStep, target, speed);
        RaiseLocalEvent(user, ref ev);
        speed = ev.Speed;

        return stepComp.Duration / Math.Max(0.01f, speed);
    }

    private (Entity<SurgeryComponent> Surgery, int Step)? GetNextStep(EntityUid body, EntityUid part, Entity<SurgeryComponent?> surgery, List<EntityUid> requirements, EntityUid user)
    {
        if (!_query.Resolve(surgery, ref surgery.Comp))
            return null;

        if (requirements.Contains(surgery))
        {
            Log.Error($"Surgery {surgery} has a requirement loop: {string.Join(", ", requirements)}");
            return null;
        }

        var ev = new SurgeryIgnorePreviousStepsEvent();
        RaiseLocalEvent(user, ref ev);
        if (ev.Handled)
        {
            for (var i = surgery.Comp.Steps.Count - 1; i >= 0; i--)
            {
                var surgeryStep = surgery.Comp.Steps[i];
                if (!IsStepComplete(body, part, surgeryStep, surgery))
                    return ((surgery, surgery.Comp), -i - 1);
            }

            return null;
        }

        requirements.Add(surgery);

        if (surgery.Comp.Requirement is { } requirementId)
        {
            if (GetSingleton(requirementId) is not { } requirement)
            {
                Log.Warning($"Surgery {surgery} requirement prototype {requirementId} could not be found.");
                return null;
            }

            if (GetNextStep(body, part, requirement, requirements, user) is { } requiredNext)
                return requiredNext;
        }

        for (var i = 0; i < surgery.Comp.Steps.Count; i++)
        {
            var surgeryStep = surgery.Comp.Steps[i];
            if (!IsStepComplete(body, part, surgeryStep, surgery))
                return ((surgery, surgery.Comp), i);
        }

        return null;
    }

    public (Entity<SurgeryComponent> Surgery, int Step)? GetNextStep(EntityUid body, EntityUid part, EntityUid surgery, EntityUid user)
    {
        _nextStepList.Clear();
        return GetNextStep(body, part, surgery, _nextStepList, user);
    }

    private bool AllStepsComplete(EntityUid body, EntityUid part, Entity<SurgeryComponent> surgery, EntityUid user)
    {
        var ev = new SurgeryIgnorePreviousStepsEvent();
        RaiseLocalEvent(user, ref ev);
        if (ev.Handled)
            return true;

        if (surgery.Comp.Requirement is { } requirement)
        {
            if (GetSingleton(requirement) is not { } requiredEnt ||
                !TryComp(requiredEnt, out SurgeryComponent? requiredComp) ||
                !AllStepsComplete(body, part, (requiredEnt, requiredComp), user))
            {
                return false;
            }
        }

        return surgery.Comp.Steps.All(surgeryStep => IsStepComplete(body, part, surgeryStep, surgery));
    }

    private bool PreviousStepsComplete(EntityUid body, EntityUid part, Entity<SurgeryComponent> surgery, EntProtoId step, EntityUid user)
    {
        var ev = new SurgeryIgnorePreviousStepsEvent();
        RaiseLocalEvent(user, ref ev);
        if (ev.Handled)
            return true;

        // TODO RMC14 use index instead of the prototype id
        if (surgery.Comp.Requirement is { } requirement)
        {
            if (GetSingleton(requirement) is not { } requiredEnt ||
                !TryComp(requiredEnt, out SurgeryComponent? requiredComp) ||
                !AllStepsComplete(body, part, (requiredEnt, requiredComp), user))
            {
                return false;
            }
        }

        return surgery.Comp.Steps.TakeWhile(surgeryStep => surgeryStep != step).All(surgeryStep => IsStepComplete(body, part, surgeryStep, surgery));
    }

    private bool CanPerformStep(EntityUid user,
        EntityUid body,
        EntityUid part,
        EntityUid step,
        EntityUid tool,
        bool doPopup,
        out string? popup,
        out StepInvalidReason reason,
        out BaseSurgeryToolComponent? data)
    {
        data = null;

        var type = _partQuery.CompOrNull(part)?.PartType ?? BodyPartType.Other;

        var slot = type switch
        {
            BodyPartType.Head => SlotFlags.HEAD,
            BodyPartType.Torso => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
            BodyPartType.Arm => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
            BodyPartType.Hand => SlotFlags.GLOVES,
            BodyPartType.Leg => SlotFlags.OUTERCLOTHING | SlotFlags.LEGS,
            BodyPartType.Foot => SlotFlags.FEET,
            BodyPartType.Tail => SlotFlags.NONE,
            BodyPartType.Other => SlotFlags.NONE,
            _ => SlotFlags.NONE,
        };

        var check = new SurgeryCanPerformStepEvent(user, body, tool, slot);
        RaiseLocalEvent(step, ref check);
        if (check.IsValid) // if the step doesn't stop it check the body after
            RaiseLocalEvent(body, ref check);

        popup = check.Popup;
        reason = check.Invalid;
        data = check.ValidTool;

        if (check.IsValid)
            return true;

        if (doPopup && check.Popup != null)
            _popup.PopupEntity(check.Popup, user, user, PopupType.SmallCaution);

        return false;
    }

    private bool CanPerformStep(EntityUid user, EntityUid body, EntityUid part, EntityUid step, EntityUid tool, bool doPopup)
    {
        return CanPerformStep(user, body, part, step, tool, doPopup, out _, out _, out _);
    }

    public bool CanPerformStepWithHeld(EntityUid user, EntityUid body, EntityUid part, EntityUid step, bool doPopup, out string? popup)
    {
        var tool = _hands.GetActiveItemOrSelf(user);
        return CanPerformStep(user, body, part, step, tool, doPopup, out popup, out _, out _);
    }

    private bool IsStepComplete(EntityUid body, EntityUid part, EntProtoId step, EntityUid surgery)
    {
        if (GetSingleton(step) is not { } stepEnt)
            return false;

        var ev = new SurgeryStepCompleteCheckEvent(body, part, surgery);
        RaiseLocalEvent(stepEnt, ref ev);
        return !ev.Cancelled;
    }

    private BaseSurgeryToolComponent? GetSurgeryComp(EntityUid tool, IComponent component)
    {
        if (TryComp(tool, component.GetType(), out var found) && found is BaseSurgeryToolComponent data)
        {
            if (data is CauteryComponent && TryComp<WelderComponent>(tool, out var welder) && !welder.Enabled)
                return null;

            return data;
        }

        return null;
    }

    private bool HasSurgeryComp(EntityUid tool, IComponent component) => GetSurgeryComp(tool, component) != null;
    #endregion
}
