using Content.Server.Doors.Systems;
using Content.Server.Popups;
using Content.Shared.Ashfall.LockPick;
using Content.Shared.Ashfall.LockPick.Components;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server.Ashfall.LockPick.Systems;

public sealed partial class LockpickSystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private DoorSystem _door = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LockpickComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<LockpickComponent, LockPickDoAfterEvent>(OnLockPickDoAfter);
        SubscribeLocalEvent<TargetLockPickComponent, LockPickSuccessEvent>(OnLockPickSuccess);
    }

    private void OnAfterInteract(Entity<LockpickComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target)
            return;

        if (!TryComp<TargetLockPickComponent>(target, out var targetLockPick))
            return;

        if (TryComp<DoorComponent>(target, out var door) && door.State == DoorState.Welded)
            return;

        args.Handled = true;

        var duration = targetLockPick.Time * ent.Comp.SpeedModifier;

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            args.User,
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
            _door.SetBoltsDown((uid, boltComp), false, args.User);
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
