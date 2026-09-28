using Content.Shared.Ashfall.CharacterGen;
using Content.Shared.Ashfall.CharacterGen.Lifepath;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Client.Ashfall.CharacterGen;

/// <summary>
///     One occupied priority slot as known to the client. The candidate DTO comes in full, so
///     pinned people keep their data, appearance and chosen job across pool rerolls.
/// </summary>
public sealed record AshfallClientPinnedSlot(int SlotIndex, Guid CandidateId, AshfallCharacterCandidate Candidate, ProtoId<JobPrototype> Job, AshfallSlotStatus Status = AshfallSlotStatus.Ready);

/// <summary>
///     Client-side system communicating with the server for candidate pool requests and selection.
/// </summary>
public sealed partial class AshfallCharacterGenSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;

    public List<AshfallCharacterCandidate> Candidates { get; private set; } = new();
    public int PoolRevision { get; private set; }
    public int SelectedIndex { get; private set; } = -1;
    public ProtoId<JobPrototype>? SelectedJob { get; private set; }
    public float CooldownSecondsRemaining { get; private set; }
    public int RemainingRefreshes { get; private set; } = -1;
    public List<AshfallClientPinnedSlot> PinnedSlots { get; private set; } = new();
    public int ConfirmedSlotIndex { get; private set; } = -1;

    /// <summary>
    ///     The first occupied priority slot (the confirmed candidate), or null when nothing is pinned.
    /// </summary>
    public AshfallClientPinnedSlot? ConfirmedPin =>
        ConfirmedSlotIndex >= 0
            ? GetPinnedSlot(ConfirmedSlotIndex)
            : PinnedSlots.OrderBy(s => s.SlotIndex).FirstOrDefault();

    public AshfallCharacterCandidate? SelectedCandidate
    {
        get
        {
            // Pinning IS the confirmation: the confirmed slot's candidate is the selected one.
            if (ConfirmedPin is { } pin)
                return pin.Candidate;

            return SelectedIndex >= 0 && SelectedIndex < Candidates.Count ? Candidates[SelectedIndex] : null;
        }
    }

    public Content.Shared.Preferences.HumanoidCharacterProfile? SelectedProfile => SelectedCandidate?.Profile;

    public event Action? PoolUpdated;

    public void HandlePoolResponse(MsgAshfallPoolResponse msg)
    {
        Candidates = msg.Candidates;
        PoolRevision = msg.PoolRevision;
        SelectedIndex = msg.SelectedIndex;
        SelectedJob = msg.SelectedJob;
        CooldownSecondsRemaining = msg.CooldownSecondsRemaining;
        RemainingRefreshes = msg.RemainingRefreshes;
        ConfirmedSlotIndex = msg.ConfirmedSlotIndex;
        PinnedSlots = msg.PinnedSlots
            .Select(s => new AshfallClientPinnedSlot(s.SlotIndex, s.Candidate.CandidateId, s.Candidate, new ProtoId<JobPrototype>(s.JobId), s.Status))
            .ToList();

        PoolUpdated?.Invoke();
    }

    /// <summary>
    ///     Requests the current candidate pool from the server, optionally requesting a reroll if allowed.
    /// </summary>
    public void RequestPool(bool refresh = false)
    {
        if (!_netManager.IsConnected)
            return;

        var msg = new MsgAshfallRequestPool { Refresh = refresh };
        _netManager.ClientSendMessage(msg);
    }

    /// <summary>
    ///     Selects a candidate by index.
    /// </summary>
    public void SelectCandidate(int index, ProtoId<JobPrototype> job)
    {
        if (index < 0 || index >= Candidates.Count)
            return;

        if (_netManager.IsConnected)
        {
            var msg = new MsgAshfallSelectCandidate
            {
                SelectedIndex = index,
                PoolRevision = PoolRevision,
                SelectedJob = job,
            };
            _netManager.ClientSendMessage(msg);
        }
    }

    /// <summary>
    ///     Pins the candidate + job pair into a priority slot. The server validates the pair;
    ///     the updated pool state arrives via MsgAshfallPoolResponse.
    /// </summary>
    public void PinCandidate(int slotIndex, Guid candidateId, ProtoId<JobPrototype> job)
    {
        if (slotIndex < 0 || slotIndex >= AshfallCharacterPoolConstants.PrioritySlotCount)
            return;

        if (!_netManager.IsConnected)
            return;

        _netManager.ClientSendMessage(new MsgAshfallPinCandidate
        {
            PoolRevision = PoolRevision,
            SlotIndex = slotIndex,
            CandidateId = candidateId,
            JobId = job,
        });
    }

    public void ClearPrioritySlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= AshfallCharacterPoolConstants.PrioritySlotCount)
            return;

        if (!_netManager.IsConnected)
            return;

        _netManager.ClientSendMessage(new MsgAshfallClearPrioritySlot { SlotIndex = slotIndex });
    }

    /// <summary>
    ///     Moves a pin to another slot; an occupied target swaps the two pins atomically.
    /// </summary>
    public void MovePrioritySlot(int slotIndex, int targetSlotIndex)
    {
        if (slotIndex < 0 || slotIndex >= AshfallCharacterPoolConstants.PrioritySlotCount ||
            targetSlotIndex < 0 || targetSlotIndex >= AshfallCharacterPoolConstants.PrioritySlotCount)
            return;

        if (!_netManager.IsConnected)
            return;

        _netManager.ClientSendMessage(new MsgAshfallMovePrioritySlot
        {
            SlotIndex = slotIndex,
            TargetSlotIndex = targetSlotIndex,
        });
    }

    public AshfallClientPinnedSlot? GetPinnedSlot(int slotIndex)
    {
        return PinnedSlots.FirstOrDefault(s => s.SlotIndex == slotIndex);
    }

    public AshfallClientPinnedSlot? GetPinByCandidate(Guid candidateId)
    {
        return PinnedSlots.FirstOrDefault(s => s.CandidateId == candidateId);
    }

    public void SubmitLifepath(AshfallLifepathChoices choices)
    {
        if (!_netManager.IsConnected)
            return;

        _netManager.ClientSendMessage(new MsgAshfallLifepathSubmit { Choices = choices });
    }

    public void SelectSlot(int slotIndex)
    {
        if (!_netManager.IsConnected)
            return;

        _netManager.ClientSendMessage(new MsgAshfallSelectSlot { SlotIndex = slotIndex });
    }

    public void ResetSlot(int slotIndex)
    {
        if (!_netManager.IsConnected)
            return;

        _netManager.ClientSendMessage(new MsgAshfallResetSlot { SlotIndex = slotIndex });
    }
}

/// <summary>
///     Slot layout constants shared between client UI messaging guards.
/// </summary>
public static class AshfallCharacterPoolConstants
{
    public const int PrioritySlotCount = 5;
    public const int CharacterSlotCount = 3;
}
