using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Rules;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Prototypes;
using Ashfall.Server.Degradation.Components;
using Content.Server.Ashfall.Restoration;
using Ashfall.Shared.Degradation;

namespace Ashfall.Server.Degradation;

/// <summary>
/// Selects degradation passes when the rule is added and records their manifest on each initialized station.
/// </summary>
public sealed partial class DegradationRuleSystem : GameRuleSystem<DegradationRuleComponent>
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private RestorationSystem _restoration = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit,
            before: new[] { typeof(RoundstartStationVariationRuleSystem) });
    }

    protected override void Added(
        EntityUid uid,
        DegradationRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleAddedEvent args)
    {
        var profile = _prototype.Index(component.Profile);
        var seed = component.Seed ?? RobustRandom.Next();
        var scenario = DegradationScenarioSelector.Select(profile, GameTicker.ReadyPlayerCount(), seed);
        component.Scenario = scenario;

        if (scenario.MissingRequiredTags.Count > 0)
        {
            Log.Error(
                $"Degradation profile {profile.ID} cannot satisfy required tags: " +
                string.Join(", ", scenario.MissingRequiredTags));
            GameTicker.EndGameRule((uid, gameRule));
            return;
        }

        foreach (var fault in scenario.Faults)
        {
            var faultRule = GameTicker.AddGameRule(fault.Rule);
            if (faultRule == null)
            {
                Log.Warning($"Degradation fault {fault.Id} ({fault.Rule}) was ignored and will not be applied.");
                continue;
            }

            if (fault.TargetZone is { } zone)
                EnsureComp<DegradationZoneTargetComponent>(faultRule.Value).Zone = zone;

            component.ActivatedFaults.Add(fault.Id);
            component.ActivatedRules.Add(fault.Rule);
        }

        Log.Info(
            $"Built degradation scenario {profile.ID}, seed {scenario.Seed}, " +
            $"players {scenario.ReadyPlayers}, budget {scenario.Budget}: " +
            string.Join(", ", component.ActivatedFaults));
    }

    private void OnStationPostInit(ref StationPostInitEvent ev)
    {
        var query = EntityQueryEnumerator<DegradationRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var rule, out _))
        {
            if (!HasComp<GameRuleComponent>(uid) || rule.Scenario == null)
                continue;

            foreach (var grid in ev.Station.Comp.Grids)
                _restoration.CapturePristineLayout(grid);

            var state = EnsureComp<DegradationStateComponent>(ev.Station);
            state.Profile = rule.Profile.Id;
            state.Seed = rule.Scenario.Seed;
            state.Budget = rule.Scenario.Budget;
            state.ReadyPlayers = rule.Scenario.ReadyPlayers;
            state.Faults = new List<string>(rule.ActivatedFaults);
            state.Rules = new List<EntProtoId>(rule.ActivatedRules);
            return;
        }
    }
}
