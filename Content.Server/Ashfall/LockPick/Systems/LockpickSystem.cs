using Content.Server.Doors.Systems;
using Content.Server.Popups;
using Content.Shared.Ashfall.LockPick;
using Content.Shared.Ashfall.LockPick.Components;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Ashfall.LockPick.Systems;

public sealed partial class LockpickSystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private DoorSystem _door = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LockpickComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<LockpickComponent, GetVerbsEvent<UtilityVerb>>(OnGetUtilityVerbs);
        SubscribeLocalEvent<LockpickComponent, LockPickDoAfterEvent>(OnLockPickDoAfter);
        SubscribeLocalEvent<TargetLockPickComponent, LockPickSuccessEvent>(OnLockPickSuccess);
    }

    private void OnAfterInteract(Entity<LockpickComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target)
            return;

        if (!TryComp<TargetLockPickComponent>(target, out var targetLockPick))
            return;

        if (TryStartLockpick(ent, args.User, target, targetLockPick))
            args.Handled = true;
    }

    private void OnGetUtilityVerbs(Entity<LockpickComponent> ent, ref GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var target = args.Target;
        if (!TryComp<TargetLockPickComponent>(target, out var targetLockPick))
            return;

        if (!CanLockpick(target))
            return;

        var user = args.User;
        var verb = new UtilityVerb
        {
            Act = () => TryStartLockpick(ent, user, target, targetLockPick),
            Text = Loc.GetString("ashfall-lockpick-verb"),
            Icon = new SpriteSpecifier.Rsi(new ResPath("Ashfall/Objects/Tools/lockpick.rsi"), "lockpick"),
        };
        args.Verbs.Add(verb);
    }

    public bool CanLockpick(EntityUid target)
    {
        if (TryComp<DoorComponent>(target, out var door))
        {
            if (door.State == DoorState.Welded || door.State != DoorState.Closed)
                return false;
        }

        if (TryComp<LockComponent>(target, out var lockComp) && !lockComp.Locked && !HasComp<DoorComponent>(target))
            return false;

        return true;
    }

    public bool TryStartLockpick(Entity<LockpickComponent> ent, EntityUid user, EntityUid target, TargetLockPickComponent targetLockPick)
    {
        if (!CanLockpick(target))
            return false;

        // AfterInteract can fire for targets the user only sees; require real reachability.
        if (!_interaction.InRangeUnobstructed(user, target))
            return false;

        var duration = targetLockPick.Time * ent.Comp.SpeedModifier;

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            duration,
            new LockPickDoAfterEvent(),
            ent.Owner,
            target)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
            DuplicateCondition = DuplicateConditions.SameTool,
        });
    }

    private void OnLockPickDoAfter(Entity<LockpickComponent> ent, ref LockPickDoAfterEvent args)
    {
        if (args.Cancelled || args.Target is not { } target)
            return;

        if (!TryComp<TargetLockPickComponent>(target, out var targetLockPick))
            return;

        // Recheck eligibility: the door could have been welded, opened or unlocked while the do-after ran.
        if (!CanLockpick(target))
            return;

        _audio.PlayPvs(ent.Comp.Sound, target);

        if (!_random.Prob(targetLockPick.Chance))
        {
            _popup.PopupEntity(Loc.GetString("ashfall-lockpick-failed"), args.User, args.User, PopupType.SmallCaution);
            return;
        }

        var ev = new LockPickSuccessEvent(args.User);
        RaiseLocalEvent(target, ref ev);

        if (ev.Success)
        {
            _popup.PopupEntity(Loc.GetString("ashfall-lockpick-success"), args.User, args.User, PopupType.Medium);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("ashfall-lockpick-failed"), args.User, args.User, PopupType.SmallCaution);
        }
    }

    private void OnLockPickSuccess(Entity<TargetLockPickComponent> target, ref LockPickSuccessEvent args)
    {
        var uid = target.Owner;

        // Unlock standard locks (closets, safes, secure storage)
        if (TryComp<LockComponent>(uid, out var lockComp) && lockComp.Locked)
        {
            _lock.Unlock(uid, args.User, lockComp);
            args.Success = true;
        }

        // Handle door bolts and opening
        if (TryComp<DoorBoltComponent>(uid, out var boltComp) && boltComp.BoltsDown)
        {
            if (_door.TrySetBoltDown((uid, boltComp), false, args.User))
                args.Success = true;
        }

        if (TryComp<DoorComponent>(uid, out var doorComp))
        {
            // Bypass access checks when opening picked door
            if (_door.TryOpen(uid, doorComp, user: null))
            {
                args.Success = true;
            }
        }
    }
}
