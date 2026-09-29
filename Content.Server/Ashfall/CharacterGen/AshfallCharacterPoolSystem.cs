using Content.Shared.Ashfall.CharacterGen;
using Content.Shared.Ashfall.CharacterGen.Lifepath;
using Content.Shared.Preferences;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Log;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Mobs;
using Content.Shared.Ashfall;
using Content.Shared.Ashfall.CharacterGen.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Server.GameTicking.Events;
using Content.Server.GameTicking;
using Robust.Server.Player;
using Content.Shared.CCVar;
using System.Linq;
using System.Text.RegularExpressions;

namespace Content.Server.Ashfall.CharacterGen;

/// <summary>
///     Server-side manager maintaining candidate character pools per round for connected players.
/// </summary>
public sealed partial class AshfallCharacterPoolSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private MarkingManager _markingManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private NamingSystem _namingSystem = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    private ISawmill _sawmill = default!;
    private AshfallCharacterGenerator _generator = default!;
    private AshfallPersonGenerator _personGenerator = default!;

    private static readonly ProtoId<CharacterGenConstraintsPrototype> HumanConstraints = "HumanDefaultConstraints";

    // Same name rules as HumanoidCharacterProfile.EnsureValid: the lifepath custom name is
    // the only client-supplied string that reaches the spawn profile, so it gets the same
    // treatment instead of raw client text.
    private static readonly Regex RestrictedNameRegex = new(@"[^A-Za-zА-Яа-яЁё0-9 '\-]");
    private static readonly Regex ICNameCaseRegex = new(@"^(?<word>\w)|\b(?<word>\w)(?=\w*$)");

    private string SanitizeCustomName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var result = name.Trim();
        var maxNameLength = _cfg.GetCVar(CCVars.MaxNameLength);
        if (result.Length > maxNameLength)
            result = result[..maxNameLength];

        if (_cfg.GetCVar(CCVars.RestrictedNames))
            result = RestrictedNameRegex.Replace(result, string.Empty);

        if (_cfg.GetCVar(CCVars.ICNameCase))
            result = ICNameCaseRegex.Replace(result, m => m.Groups["word"].Value.ToUpper());

        return result.Trim();
    }

    private static bool IsSlotLocked(PrioritySlot? slot)
    {
        return slot is { } found && found.Status is AshfallSlotStatus.OnShift or AshfallSlotStatus.Ready;
    }

    private readonly Dictionary<NetUserId, PlayerCandidatePool> _playerPools = new();

    public const int PrioritySlotCount = 5;
    public const int CharacterSlotCount = 3;

    /// <summary>
    ///     One pinned Candidate + Job combination. The full candidate DTO is retained so the
    ///     person's data, appearance, history and eligible jobs survive pool rerolls unchanged;
    ///     the job is fixed at pin time and never falls back to another profession.
    /// </summary>
    internal sealed class PrioritySlot
    {
        public AshfallCharacterCandidate Candidate { get; set; } = default!;
        public ProtoId<JobPrototype> Job { get; set; }
        public AshfallSlotStatus Status { get; set; } = AshfallSlotStatus.Ready;
        public EntityUid? SpawnedMob { get; set; }
    }

    internal sealed class PlayerCandidatePool
    {
        public List<AshfallCharacterCandidate> Candidates { get; set; } = new();
        public int Revision { get; set; }
        public int SelectedIndex { get; set; } = -1;
        public ProtoId<JobPrototype>? SelectedJob { get; set; }
        public TimeSpan LastRefreshTime { get; set; } = TimeSpan.Zero;
        public int RefreshesUsed { get; set; } = 0;

        public PrioritySlot?[] PrioritySlots { get; set; } = new PrioritySlot?[PrioritySlotCount];

        /// <summary>
        ///     The wake-up selection resolved from the priority list at confirm time. Identity is
        ///     the slot itself, never an index into the current pool: pinned candidates survive
        ///     rerolls, so Candidates[slot.Index] may be a different person.
        /// </summary>
        public int ConfirmedPriorityIndex { get; set; } = -1;

        public bool TryGetConfirmedSlot(out PrioritySlot slot)
        {
            slot = null!;
            return ConfirmedPriorityIndex >= 0 &&
                   ConfirmedPriorityIndex < PrioritySlots.Length &&
                   PrioritySlots[ConfirmedPriorityIndex] is { } found && (slot = found) != null;
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("ashfall.pool");
        _generator = new AshfallCharacterGenerator(_protoManager, _markingManager, _namingSystem);
        _personGenerator = new AshfallPersonGenerator(_protoManager, _generator);

        _netManager.RegisterNetMessage<MsgAshfallRequestPool>(OnRequestPool);
        _netManager.RegisterNetMessage<MsgAshfallSelectCandidate>(OnSelectCandidate);
        _netManager.RegisterNetMessage<MsgAshfallPoolResponse>();
        _netManager.RegisterNetMessage<MsgAshfallPinCandidate>(OnPinCandidate);
        _netManager.RegisterNetMessage<MsgAshfallClearPrioritySlot>(OnClearPrioritySlot);
        _netManager.RegisterNetMessage<MsgAshfallMovePrioritySlot>(OnMovePrioritySlot);
        _netManager.RegisterNetMessage<MsgAshfallLifepathSubmit>(OnLifepathSubmit);
        _netManager.RegisterNetMessage<MsgAshfallSelectSlot>(OnSelectSlot);
        _netManager.RegisterNetMessage<MsgAshfallResetSlot>(OnResetSlot);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEnd);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _playerPools.Clear();
    }

    private void OnRequestPool(MsgAshfallRequestPool msg)
    {
        var player = msg.MsgChannel.UserId;
        var pool = GetOrCreatePool(player);

        if (msg.Refresh)
        {
            var cooldown = TimeSpan.FromSeconds(_cfg.GetCVar(AshfallCCVars.CharacterPoolRefreshCooldown));
            var maxRefreshes = _cfg.GetCVar(AshfallCCVars.CharacterPoolMaxRefreshes);

            if (_timing.CurTime - pool.LastRefreshTime >= cooldown &&
                (maxRefreshes < 0 || pool.RefreshesUsed < maxRefreshes))
            {
                pool.Candidates = GeneratePoolForPlayer();
                pool.Revision++;
                pool.SelectedIndex = -1;
                pool.SelectedJob = null;
                pool.LastRefreshTime = _timing.CurTime;
                pool.RefreshesUsed++;

                if (_playerManager.TryGetSessionById(player, out var session))
                    EntityManager.System<GameTicker>().ToggleReady(session, false);
            }
        }

        SendPoolResponse(msg.MsgChannel, pool);
    }

    private void OnSelectCandidate(MsgAshfallSelectCandidate msg)
    {
        var player = msg.MsgChannel.UserId;
        var pool = GetOrCreatePool(player);

        if (msg.PoolRevision == pool.Revision &&
            msg.SelectedIndex >= 0 &&
            msg.SelectedIndex < pool.Candidates.Count &&
            _protoManager.TryIndex<JobPrototype>(msg.SelectedJob, out var job) &&
            job.SetPreference &&
            pool.Candidates[msg.SelectedIndex].CompatibleJobs.Contains(job.ID) &&
            IsJobAllowed(player, job.ID, pool.Candidates[msg.SelectedIndex].Profile))
        {
            pool.SelectedIndex = msg.SelectedIndex;
            pool.SelectedJob = job.ID;
        }

        SendPoolResponse(msg.MsgChannel, pool);
    }

    private void OnPinCandidate(MsgAshfallPinCandidate msg)
    {
        var player = msg.MsgChannel.UserId;
        TryPin(player, msg.SlotIndex, msg.CandidateId, msg.JobId, msg.PoolRevision);
        SendPoolResponse(msg.MsgChannel, GetOrCreatePool(player));
    }

    private void OnClearPrioritySlot(MsgAshfallClearPrioritySlot msg)
    {
        var player = msg.MsgChannel.UserId;
        ClearPrioritySlot(player, msg.SlotIndex);
        SendPoolResponse(msg.MsgChannel, GetOrCreatePool(player));
    }

    private void OnMovePrioritySlot(MsgAshfallMovePrioritySlot msg)
    {
        var player = msg.MsgChannel.UserId;
        MovePrioritySlot(player, msg.SlotIndex, msg.TargetSlotIndex);
        SendPoolResponse(msg.MsgChannel, GetOrCreatePool(player));
    }

    private void OnLifepathSubmit(MsgAshfallLifepathSubmit msg)
    {
        var player = msg.MsgChannel.UserId;
        var pool = GetOrCreatePool(player);
        var slotIdx = msg.Choices.TargetSlotIndex;

        if (slotIdx < 0 || slotIdx >= CharacterSlotCount)
            return;

        // The submission is valid only against the pool view the questionnaire was filled
        // in. Any player-initiated slot mutation since (reset, clear, pin, reorder, another
        // submit) bumps the revision, so a delayed or repeated submission cannot repopulate
        // a released slot as if it were a fresh choice. Lifecycle transitions (death,
        // evacuation) push no response to the client and are guarded by the locked check
        // below instead, which runs against the slot state at processing time.
        if (msg.PoolRevision != pool.Revision)
        {
            SendPoolResponse(msg.MsgChannel, pool);
            return;
        }

        var existingSlot = pool.PrioritySlots[slotIdx];
        // Ready slots are pinned like OnShift ones: only death or a played shift releases
        // them, so resubmitting the questionnaire cannot silently reroll the candidate.
        if (IsSlotLocked(existingSlot))
        {
            _sawmill.Warning($"Player {player} attempted to modify pinned slot {slotIdx} (status: {existingSlot!.Status})!");
            SendPoolResponse(msg.MsgChannel, pool);
            return;
        }

        msg.Choices.CustomName = SanitizeCustomName(msg.Choices.CustomName);

        var (candidate, structure) = _personGenerator.GenerateFromLifepath(msg.Choices, _random);

        var selectedJobId = msg.Choices.SelectedJob.Id;
        var jobId = !string.IsNullOrEmpty(msg.Choices.SelectedJob.Id)
            ? msg.Choices.SelectedJob.Id
            : (candidate.CompatibleJobs.Count > 0 ? candidate.CompatibleJobs[0].Id : "Passenger");

        var eligibleJobs = AshfallJobScorer.ScoreEligibleJobs(structure, _protoManager);
        if (!_protoManager.TryIndex<JobPrototype>(jobId, out var job) ||
            !job.SetPreference ||
            !candidate.CompatibleJobs.Contains(job.ID) ||
            (!string.IsNullOrEmpty(selectedJobId) && !eligibleJobs.Contains(job.ID)) ||
            !IsJobAllowed(player, job.ID, candidate.Profile))
        {
            SendPoolResponse(msg.MsgChannel, pool);
            return;
        }

        if (!candidate.CompatibleJobs.Contains(jobId))
            candidate.CompatibleJobs.Insert(0, jobId);

        pool.PrioritySlots[slotIdx] = new PrioritySlot
        {
            Candidate = candidate,
            Job = jobId,
            Status = AshfallSlotStatus.Ready,
        };

        pool.ConfirmedPriorityIndex = slotIdx;
        pool.Revision++;
        SendPoolResponse(msg.MsgChannel, pool);
    }

    private void OnSelectSlot(MsgAshfallSelectSlot msg)
    {
        var player = msg.MsgChannel.UserId;
        var pool = GetOrCreatePool(player);
        if (msg.SlotIndex >= 0 && msg.SlotIndex < CharacterSlotCount &&
            pool.PrioritySlots[msg.SlotIndex] is { } slot &&
            slot.Status != AshfallSlotStatus.Dead)
        {
            pool.ConfirmedPriorityIndex = msg.SlotIndex;
            pool.Revision++;
        }
        SendPoolResponse(msg.MsgChannel, pool);
    }

    private void OnResetSlot(MsgAshfallResetSlot msg)
    {
        var player = msg.MsgChannel.UserId;
        var pool = GetOrCreatePool(player);
        if (msg.SlotIndex >= 0 && msg.SlotIndex < CharacterSlotCount)
        {
            var slot = pool.PrioritySlots[msg.SlotIndex];
            // A pinned candidate is only released by a played shift (evacuation) or death;
            // otherwise players would reroll endlessly hunting for high-value roles.
            if (IsSlotLocked(slot))
            {
                _sawmill.Warning($"Player {player} attempted to reset pinned slot {msg.SlotIndex} (status: {slot!.Status})!");
                SendPoolResponse(msg.MsgChannel, pool);
                return;
            }

            pool.PrioritySlots[msg.SlotIndex] = null;
            pool.Revision++;
            UpdateConfirmedIndex(pool);
        }
        SendPoolResponse(msg.MsgChannel, pool);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (ev.Player.UserId is { } userId && _playerPools.TryGetValue(userId, out var pool) &&
            pool.TryGetConfirmedSlot(out var slot) &&
            slot.Status == AshfallSlotStatus.Ready)
        {
            slot.Status = AshfallSlotStatus.OnShift;
            slot.SpawnedMob = ev.Mob;
        }
    }

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
            return;

        foreach (var pool in _playerPools.Values)
        {
            for (var i = 0; i < pool.PrioritySlots.Length; i++)
            {
                var slot = pool.PrioritySlots[i];
                if (slot != null && slot.SpawnedMob == ev.Target)
                {
                    slot.Status = AshfallSlotStatus.Dead;
                    slot.SpawnedMob = null;

                    // A dead candidate must never stay selected: resolve the confirmation to
                    // the next non-dead pinned candidate, so ready/late-join paths can neither
                    // respawn the dead person nor silently fall back to the legacy profile.
                    if (pool.ConfirmedPriorityIndex == i)
                        UpdateConfirmedIndex(pool);

                    break;
                }
            }
        }
    }

    private void OnRoundEnd(RoundEndMessageEvent ev)
    {
        foreach (var pool in _playerPools.Values)
        {
            for (var i = 0; i < pool.PrioritySlots.Length; i++)
            {
                var slot = pool.PrioritySlots[i];
                if (slot != null && slot.Status == AshfallSlotStatus.OnShift)
                {
                    slot.Status = AshfallSlotStatus.Evacuated;
                    slot.SpawnedMob = null;
                }
            }
        }
    }

    internal bool TryPin(NetUserId player, int slotIndex, Guid candidateId, string jobId, int poolRevision)
    {
        var pool = GetOrCreatePool(player);

        if (poolRevision != pool.Revision ||
            slotIndex < 0 ||
            slotIndex >= PrioritySlotCount ||
            FindCandidate(pool, candidateId) is not { } candidate ||
            !_protoManager.TryIndex<JobPrototype>(jobId, out var job) ||
            !job.SetPreference ||
            !candidate.CompatibleJobs.Contains(job.ID) ||
            !IsJobAllowed(player, job.ID, candidate.Profile))
        {
            return false;
        }

        // A pinned or on-shift slot keeps its combination (and its spawned-mob association)
        // until death or evacuation; reordering happens via MovePrioritySlot instead.
        if (IsSlotLocked(pool.PrioritySlots[slotIndex]))
        {
            _sawmill.Warning($"Player {player} attempted to pin over locked slot {slotIndex} (status: {pool.PrioritySlots[slotIndex]!.Status})!");
            return false;
        }

        // Re-pinning a candidate into another slot moves the combination there; a candidate
        // may never occupy two slots at once.
        var existing = FindPinnedSlotIndex(pool, candidateId);
        if (existing is { } oldSlot && oldSlot != slotIndex)
        {
            if (IsSlotLocked(pool.PrioritySlots[oldSlot]))
            {
                _sawmill.Warning($"Player {player} attempted to move a locked pin out of slot {oldSlot}!");
                return false;
            }

            pool.PrioritySlots[oldSlot] = null;
        }

        pool.PrioritySlots[slotIndex] = new PrioritySlot { Candidate = candidate, Job = job.ID };
        pool.Revision++;
        UpdateConfirmedIndex(pool);
        return true;
    }

    internal bool ClearPrioritySlot(NetUserId player, int slotIndex)
    {
        var pool = GetOrCreatePool(player);

        if (slotIndex < 0 || slotIndex >= PrioritySlotCount || pool.PrioritySlots[slotIndex] is not { } slot)
            return false;

        // Same lifecycle rule as reset and resubmission: Ready and OnShift slots are
        // released only by death or a played shift.
        if (IsSlotLocked(slot))
        {
            _sawmill.Warning($"Player {player} attempted to clear locked slot {slotIndex} (status: {slot.Status})!");
            return false;
        }

        pool.PrioritySlots[slotIndex] = null;
        pool.Revision++;
        UpdateConfirmedIndex(pool);
        return true;
    }

    internal bool MovePrioritySlot(NetUserId player, int from, int to)
    {
        var pool = GetOrCreatePool(player);

        // A pure reorder of already-validated pins: no pool or role checks, so candidates
        // that survived a reroll reorder exactly like fresh ones. An occupied target swaps.
        if (from == to ||
            from < 0 || from >= PrioritySlotCount ||
            to < 0 || to >= PrioritySlotCount ||
            pool.PrioritySlots[from] == null)
        {
            return false;
        }

        // Deliberately no locked-status check here: a swap preserves both slots' candidates
        // and any spawned-mob association, so it cannot discard a locked slot the way pin,
        // clear and reset would. Which candidate is confirmed stays freely selectable via
        // OnSelectSlot regardless, so reordering grants nothing the lifecycle locks protect.
        (pool.PrioritySlots[from], pool.PrioritySlots[to]) = (pool.PrioritySlots[to], pool.PrioritySlots[from]);
        pool.Revision++;
        UpdateConfirmedIndex(pool);
        return true;
    }

    // Pinning is the confirmation: the wake-up selection always resolves to the first occupied
    // priority slot; a later allocation pass can walk slots top-down from there. Dead slots
    // are skipped: a dead candidate is never a valid confirmation, so resolving to one would
    // leave no confirmed profile and drop the player back onto their legacy preferences.
    private static void UpdateConfirmedIndex(PlayerCandidatePool pool)
    {
        pool.ConfirmedPriorityIndex = -1;
        for (var i = 0; i < pool.PrioritySlots.Length; i++)
        {
            if (pool.PrioritySlots[i] is { } slot && slot.Status != AshfallSlotStatus.Dead)
            {
                pool.ConfirmedPriorityIndex = i;
                break;
            }
        }
    }

    private static AshfallCharacterCandidate? FindCandidate(PlayerCandidatePool pool, Guid candidateId)
    {
        foreach (var candidate in pool.Candidates)
        {
            if (candidate.CandidateId == candidateId)
                return candidate;
        }

        return null;
    }

    private static int? FindPinnedSlotIndex(PlayerCandidatePool pool, Guid candidateId)
    {
        for (var i = 0; i < pool.PrioritySlots.Length; i++)
        {
            if (pool.PrioritySlots[i]?.Candidate.CandidateId == candidateId)
                return i;
        }

        return null;
    }

    public HumanoidCharacterProfile? GetSelectedProfile(NetUserId userId)
    {
        if (_playerPools.TryGetValue(userId, out var pool) &&
            pool.TryGetConfirmedSlot(out var slot))
        {
            // The pinned candidate object is the identity: pool indexes are meaningless after
            // a reroll. The pinned job stays fixed; no fallback to another eligible profession.
            var candidate = slot.Candidate;
            var priorities = candidate.CompatibleJobs.ToDictionary(
                job => job,
                job => job == slot.Job ? JobPriority.High : JobPriority.Medium);

            return candidate.Profile
                .WithJobPriorities(priorities)
                .WithPreferenceUnavailable(PreferenceUnavailableMode.StayInLobby);
        }

        return null;
    }

    public bool TryGetSelectedProfile(NetUserId userId, out HumanoidCharacterProfile profile)
    {
        profile = GetSelectedProfile(userId)!;
        return profile != null;
    }

    public bool HasCompleteSelection(NetUserId userId)
    {
        return _playerPools.TryGetValue(userId, out var pool) &&
               pool.TryGetConfirmedSlot(out _);
    }

    public bool IsJobCompatible(NetUserId userId, ProtoId<JobPrototype> job)
    {
        return _playerPools.TryGetValue(userId, out var pool) &&
               pool.TryGetConfirmedSlot(out var slot) &&
               slot.Candidate.CompatibleJobs.Contains(job);
    }

    internal PlayerCandidatePool GetOrCreatePool(NetUserId userId)
    {
        if (!_playerPools.TryGetValue(userId, out var pool))
        {
            pool = new PlayerCandidatePool
            {
                Candidates = GeneratePoolForPlayer(),
                SelectedIndex = -1
            };
            _playerPools[userId] = pool;
        }

        return pool;
    }

    private static readonly ProtoId<CharacterGenConstraintsPrototype> ReptilianConstraints = "ReptilianDefaultConstraints";
    private static readonly ProtoId<CharacterGenConstraintsPrototype> MothConstraints = "MothDefaultConstraints";
    private static readonly ProtoId<CharacterGenConstraintsPrototype> ArachnidConstraints = "ArachnidDefaultConstraints";
    private static readonly ProtoId<CharacterGenConstraintsPrototype> VeiruConstraints = "VeiruDefaultConstraints";

    private List<AshfallCharacterCandidate> GeneratePoolForPlayer()
    {
        if (!_protoManager.TryIndex(HumanConstraints, out var humanConstraints))
            throw new InvalidOperationException($"Missing character generation constraints {HumanConstraints}.");

        var repConstraints = _protoManager.TryIndex(ReptilianConstraints, out var rC) ? rC : humanConstraints;
        var mothConstraints = _protoManager.TryIndex(MothConstraints, out var mC) ? mC : humanConstraints;
        var arachConstraints = _protoManager.TryIndex(ArachnidConstraints, out var aC) ? aC : humanConstraints;
        var veiruConstraints = _protoManager.TryIndex(VeiruConstraints, out var vC) ? vC : humanConstraints;

        // Spread the pool across professional domains so one player rarely sees eight candidates
        // from the same field. The domain is a generation bias, not a guarantee.
        var domains = _protoManager
            .EnumeratePrototypes<AshfallCareerRolePrototype>()
            .SelectMany(r => r.Domains)
            .Union(_protoManager.EnumeratePrototypes<AshfallEducationPrototype>().Select(e => e.Domain))
            .Distinct()
            .ToList();
        _random.Shuffle(domains);

        var candidates = new List<AshfallCharacterCandidate>(8);

        // Left Column (Slots 0..3): 4 Guaranteed Humans
        for (var i = 0; i < 4; i++)
        {
            var targetDomain = i < domains.Count ? domains[i] : null;
            candidates.Add(_personGenerator.GenerateCandidate(humanConstraints, _random, targetDomain).Candidate);
        }

        // Right Column (Slots 4..7): 4 non-human candidates, one per species.
        var nonHumanSpeciesList = new List<CharacterGenConstraintsPrototype>
        {
            repConstraints,
            mothConstraints,
            arachConstraints,
            veiruConstraints
        };
        _random.Shuffle(nonHumanSpeciesList);

        for (var i = 0; i < 4; i++)
        {
            var constraints = nonHumanSpeciesList[i];
            var targetDomain = i < domains.Count ? domains[i] : null;
            candidates.Add(_personGenerator.GenerateCandidate(constraints, _random, targetDomain).Candidate);
        }

        return candidates;
    }

    private void SendPoolResponse(INetChannel channel, PlayerCandidatePool pool)
    {
        var cooldownSec = _cfg.GetCVar(AshfallCCVars.CharacterPoolRefreshCooldown);
        var maxRefreshes = _cfg.GetCVar(AshfallCCVars.CharacterPoolMaxRefreshes);

        var timeSinceLast = _timing.CurTime - pool.LastRefreshTime;
        var remainingCooldown = (float)Math.Max(0, cooldownSec - timeSinceLast.TotalSeconds);
        var remainingRefreshes = maxRefreshes < 0 ? -1 : Math.Max(0, maxRefreshes - pool.RefreshesUsed);

        var response = new MsgAshfallPoolResponse
        {
            Candidates = pool.Candidates,
            PoolRevision = pool.Revision,
            SelectedIndex = pool.SelectedIndex,
            SelectedJob = pool.SelectedJob?.Id,
            CooldownSecondsRemaining = remainingCooldown,
            RemainingRefreshes = remainingRefreshes,
            ConfirmedSlotIndex = pool.ConfirmedPriorityIndex,
        };

        for (var i = 0; i < pool.PrioritySlots.Length; i++)
        {
            if (pool.PrioritySlots[i] is not { } slot)
                continue;

            response.PinnedSlots.Add(new AshfallPinnedSlot
            {
                SlotIndex = i,
                Candidate = slot.Candidate,
                JobId = slot.Job,
                Status = slot.Status,
            });
        }

        _netManager.ServerSendMessage(response, channel);
    }

    private bool IsJobAllowed(
        NetUserId userId,
        ProtoId<JobPrototype> job,
        HumanoidCharacterProfile profile)
    {
        if (!_playerManager.TryGetSessionById(userId, out var session))
            return false;

        var jobs = new List<ProtoId<JobPrototype>> { job };
        var ev = new IsRoleAllowedEvent(session, jobs, null, profile);
        RaiseLocalEvent(ref ev);
        return !ev.Cancelled;
    }

    public bool TryGetSelectedCandidate(NetUserId userId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AshfallCharacterCandidate? candidate)
    {
        if (_playerPools.TryGetValue(userId, out var pool))
        {
            if (pool.TryGetConfirmedSlot(out var slot))
            {
                candidate = slot.Candidate;
                return true;
            }

            if (pool.SelectedIndex >= 0 && pool.SelectedIndex < pool.Candidates.Count)
            {
                candidate = pool.Candidates[pool.SelectedIndex];
                return true;
            }
        }

        candidate = null;
        return false;
    }

    public bool TryGetMemoryTags(Guid candidateId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out HashSet<string>? tags)
    {
        foreach (var pool in _playerPools.Values)
        {
            foreach (var slot in pool.PrioritySlots)
            {
                if (slot?.Candidate.CandidateId == candidateId)
                {
                    tags = ExtractTagsFromCandidate(slot.Candidate);
                    return true;
                }
            }

            foreach (var candidate in pool.Candidates)
            {
                if (candidate.CandidateId == candidateId)
                {
                    tags = ExtractTagsFromCandidate(candidate);
                    return true;
                }
            }
        }

        tags = null;
        return false;
    }

    private static HashSet<string> ExtractTagsFromCandidate(AshfallCharacterCandidate candidate)
    {
        var tags = new HashSet<string>();
        if (!string.IsNullOrEmpty(candidate.PrimaryDomain))
        {
            tags.Add($"domain-{candidate.PrimaryDomain}");
            tags.Add($"family-{candidate.PrimaryDomain}");
        }

        if (candidate.Dossier.CultureId != null)
            tags.Add($"culture-{candidate.Dossier.CultureId}");

        return tags;
    }
}
