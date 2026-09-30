using System.Numerics;
using Content.Shared.Ashfall.Barricade;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
using Robust.Shared.Random;

namespace Content.Server.Ashfall.Barricade;

public sealed partial class BarricadeSystem : SharedBarricadeSystem
{
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private IRobustRandom _random = default!;

    protected override bool ProjectileTryPassBarricade(Entity<BarricadeComponent> entity, Entity<ProjectileComponent> projEnt)
    {
        var (uid, comp) = entity;
        var (projUid, projComp) = projEnt;

        var passBarricade = EnsureComp<PassBarricadeComponent>(projUid);
        if (passBarricade.CollideBarricades.TryGetValue(uid, out var isPass))
            return isPass;

        var hitChance = CalculateHitChance(entity, projComp.Shooter);
        var isHit = _random.Prob(hitChance);

        passBarricade.CollideBarricades[uid] = !isHit;
        Dirty(projUid, passBarricade);

        return !isHit;
    }

    private float CalculateHitChance(Entity<BarricadeComponent> entity, EntityUid? shooter)
    {
        var (uid, comp) = entity;
        var distance = comp.MaxDistance;

        if (shooter is { Valid: true } && Exists(shooter.Value))
        {
            var barricadePos = _transform.GetMapCoordinates(uid);
            var shooterPos = _transform.GetMapCoordinates(shooter.Value);
            if (barricadePos.MapId == shooterPos.MapId)
            {
                distance = Vector2.Distance(barricadePos.Position, shooterPos.Position);
            }
        }

        var distanceDiff = comp.MaxDistance - comp.MinDistance;
        if (distanceDiff <= 0f)
            return comp.MaxHitChance;

        var chanceDiff = comp.MaxHitChance - comp.MinHitChance;
        var clampedDist = Math.Clamp(distance - comp.MinDistance, 0f, distanceDiff);
        var hitChance = comp.MinHitChance + (clampedDist / distanceDiff) * chanceDiff;

        return Math.Clamp(hitChance, comp.MinHitChance, comp.MaxHitChance);
    }
}
