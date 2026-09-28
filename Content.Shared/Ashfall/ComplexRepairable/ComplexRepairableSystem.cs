using Content.Shared.Administration.Logs;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tools.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Ashfall.ComplexRepairable;

public sealed partial class ComplexRepairableSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private SharedToolSystem _toolSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedStackSystem _stack = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ComplexRepairableComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ComplexRepairableComponent, ComplexRepairFinishedEvent>(OnRepairFinished);
        SubscribeLocalEvent<ComplexRepairableComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<ComplexRepairableComponent, ExaminedEvent>(OnExamined);
    }

    private void OnDamageChanged(Entity<ComplexRepairableComponent> ent, ref DamageChangedEvent args)
    {
        if (_damageableSystem.GetTotalDamage(ent.Owner) == 0)
        {
            if (ent.Comp.LeftToInsert != 0 || ent.Comp.AccumulatedDamage != FixedPoint2.Zero)
            {
                ent.Comp.LeftToInsert = 0;
                ent.Comp.AccumulatedDamage = FixedPoint2.Zero;
                Dirty(ent);
            }
            return;
        }

        var damageTaken = args.DamageDelta?.GetTotal() ?? FixedPoint2.Zero;

        if (damageTaken > 0 && ent.Comp.MaterialRepairTreshold > 0)
        {
            ent.Comp.AccumulatedDamage += damageTaken;
            var needed = (ent.Comp.AccumulatedDamage / ent.Comp.MaterialRepairTreshold).Int();
            if (needed > 0)
            {
                ent.Comp.LeftToInsert += needed;
                ent.Comp.AccumulatedDamage -= needed * ent.Comp.MaterialRepairTreshold;
                Dirty(ent);
            }
        }
    }

    private void OnRepairFinished(Entity<ComplexRepairableComponent> ent, ref ComplexRepairFinishedEvent args)
    {
        if (args.Cancelled)
            return;

        if (_damageableSystem.GetTotalDamage(ent.Owner) == 0)
            return;

        if (ent.Comp.LeftToInsert > 0)
            return;

        if (ent.Comp.Damage != null)
        {
            _damageableSystem.TryChangeDamage(ent.Owner, ent.Comp.Damage, true, false, origin: args.User);
            _adminLogger.Add(LogType.Healed, $"{ToPrettyString(args.User):user} repaired {ToPrettyString(ent.Owner):target} by {ent.Comp.Damage.GetTotal()}");
        }
        else
        {
            _damageableSystem.SetAllDamage(ent.Owner, 0);
            _adminLogger.Add(LogType.Healed, $"{ToPrettyString(args.User):user} repaired {ToPrettyString(ent.Owner):target} to full integrity");
        }

        if (_damageableSystem.GetTotalDamage(ent.Owner) == 0)
        {
            ent.Comp.LeftToInsert = 0;
            ent.Comp.AccumulatedDamage = FixedPoint2.Zero;
            Dirty(ent);
        }

        var msg = Loc.GetString("ashfall-complex-repairable-success", ("target", ent.Owner));
        _popup.PopupEntity(msg, ent.Owner, args.User);

        var ev = new ComplexRepairedEvent(ent, args.User);
        RaiseLocalEvent(ent.Owner, ref ev);
    }

    private void OnInteractUsing(Entity<ComplexRepairableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (_damageableSystem.GetTotalDamage(ent.Owner) == 0)
            return;

        // If structure needs reinforcement materials first
        if (ent.Comp.LeftToInsert > 0)
        {
            if (TryComp<StackComponent>(args.Used, out var stackComp) && stackComp.StackTypeId == ent.Comp.Material)
            {
                int toBeUsed = Math.Min(stackComp.Count, ent.Comp.LeftToInsert);
                if (toBeUsed > 0 && _stack.TryUse((args.Used, stackComp), toBeUsed))
                {
                    ent.Comp.LeftToInsert -= toBeUsed;
                    Dirty(ent);

                    var msg = Loc.GetString("ashfall-complex-repairable-material-success",
                        ("target", ent.Owner),
                        ("left", ent.Comp.LeftToInsert));
                    _popup.PopupEntity(msg, ent.Owner, args.User);

                    args.Handled = true;
                    return;
                }

                args.Handled = true;
                return;
            }

            // Only prompt about missing materials if the player is trying to repair with the required tool (e.g. welder).
            // Do not consume interactions for unrelated items like ID cards, crowbars, or wires.
            if (_toolSystem.HasQuality(args.Used, ent.Comp.QualityNeeded))
            {
                var materialName = _protoManager.TryIndex(ent.Comp.Material, out var stackProto)
                    ? Loc.GetString(stackProto.Name)
                    : ent.Comp.Material.Id;

                var needMsg = Loc.GetString("ashfall-complex-repairable-material-needed",
                    ("target", ent.Owner),
                    ("left", ent.Comp.LeftToInsert),
                    ("material", materialName));
                _popup.PopupEntity(needMsg, ent.Owner, args.User);
                args.Handled = true;
            }

            return;
        }

        float delay = ent.Comp.DoAfterModifier.Float() * Math.Max(1f, _damageableSystem.GetTotalDamage(ent.Owner).Float() / 15f);

        if (args.User == ent.Owner)
        {
            if (!ent.Comp.AllowSelfRepair)
                return;

            delay *= ent.Comp.SelfRepairPenalty;
        }

        args.Handled = _toolSystem.UseTool(
            args.Used,
            args.User,
            ent.Owner,
            delay,
            ent.Comp.QualityNeeded,
            new ComplexRepairFinishedEvent(),
            ent.Comp.FuelCost.Float());
    }

    private void OnExamined(Entity<ComplexRepairableComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.LeftToInsert <= 0)
            return;

        var materialName = _protoManager.TryIndex(ent.Comp.Material, out var stackProto)
            ? Loc.GetString(stackProto.Name)
            : ent.Comp.Material.Id;

        args.PushMarkup(Loc.GetString("ashfall-complex-repairable-examine",
            ("material", materialName),
            ("amount", ent.Comp.LeftToInsert)));
    }
}

[ByRefEvent]
public readonly record struct ComplexRepairedEvent(Entity<ComplexRepairableComponent> Ent, EntityUid User);

[Serializable, NetSerializable]
public sealed partial class ComplexRepairFinishedEvent : SimpleDoAfterEvent;
