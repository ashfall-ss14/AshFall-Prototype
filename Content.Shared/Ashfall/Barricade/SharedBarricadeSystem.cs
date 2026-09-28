using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;
using Robust.Shared.Physics.Events;

namespace Content.Shared.Ashfall.Barricade;

public abstract partial class SharedBarricadeSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _entityWhitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BarricadeComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<PassBarricadeComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<PassBarricadeComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<PassBarricadeComponent, EndCollideEvent>(OnEndCollide);
    }

    private void OnPreventCollide(Entity<BarricadeComponent> entity, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (_entityWhitelist.IsWhitelistPass(entity.Comp.Whitelist, args.OtherEntity))
        {
            args.Cancelled = true;
            return;
        }

        if (TryComp<ProjectileComponent>(args.OtherEntity, out var projectile) &&
            ProjectileTryPassBarricade(entity, (args.OtherEntity, projectile)))
        {
            args.Cancelled = true;
        }
    }

    private void OnLand(Entity<PassBarricadeComponent> entity, ref LandEvent args)
    {
        entity.Comp.CollideBarricades.Clear();
    }

    private void OnProjectileHit(Entity<PassBarricadeComponent> entity, ref ProjectileHitEvent args)
    {
        entity.Comp.CollideBarricades.Clear();
    }

    private void OnEndCollide(Entity<PassBarricadeComponent> entity, ref EndCollideEvent args)
    {
        if (HasComp<BarricadeComponent>(args.OtherEntity))
            entity.Comp.CollideBarricades.Remove(args.OtherEntity);
    }

    protected virtual bool ProjectileTryPassBarricade(Entity<BarricadeComponent> entity, Entity<ProjectileComponent> projEnt)
    {
        if (TryComp<PassBarricadeComponent>(projEnt.Owner, out var passBarricade) &&
            passBarricade.CollideBarricades.TryGetValue(entity.Owner, out var isPass))
        {
            return isPass;
        }

        return false;
    }
}
