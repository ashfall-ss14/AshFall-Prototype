using System.IO;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Ashfall.CharacterGen;

/// <summary>
///     Client requests the current candidate pool or requests a pool refresh.
/// </summary>
public sealed class MsgAshfallRequestPool : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Core;

    public bool Refresh { get; set; }

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Refresh = buffer.ReadBoolean();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Refresh);
    }
}

[Serializable, NetSerializable]
public enum AshfallSlotStatus : byte
{
    Empty = 0,
    Ready = 1,
    OnShift = 2,
    Evacuated = 3,
    Dead = 4,
}

/// <summary>
///     One occupied priority slot. The candidate DTO is carried in full so pinned people and
///     their chosen job survive pool rerolls on the client without regeneration.
/// </summary>
[Serializable, NetSerializable]
public sealed class AshfallPinnedSlot
{
    public int SlotIndex { get; set; }
    public AshfallCharacterCandidate Candidate { get; set; } = new();
    public string JobId { get; set; } = string.Empty;
    public AshfallSlotStatus Status { get; set; } = AshfallSlotStatus.Ready;
}

/// <summary>
///     Server sends the candidate pool, current selection, and cooldown state to the client.
/// </summary>
public sealed class MsgAshfallPoolResponse : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Core;

    public List<AshfallCharacterCandidate> Candidates { get; set; } = new();
    public int PoolRevision { get; set; }
    public int SelectedIndex { get; set; } = -1;
    public string? SelectedJob { get; set; }
    public float CooldownSecondsRemaining { get; set; }
    public int RemainingRefreshes { get; set; } = -1; // -1 = unlimited
    public List<AshfallPinnedSlot> PinnedSlots { get; set; } = new();
    public int ConfirmedSlotIndex { get; set; } = -1;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var count = buffer.ReadInt32();
        Candidates = new List<AshfallCharacterCandidate>(count);
        for (var i = 0; i < count; i++)
        {
            var length = buffer.ReadVariableInt32();
            var bytes = buffer.ReadBytes(length);
            using var stream = new MemoryStream(bytes);
            serializer.DeserializeDirect(stream, out AshfallCharacterCandidate candidate);
            Candidates.Add(candidate);
        }

        PoolRevision = buffer.ReadInt32();
        SelectedIndex = buffer.ReadInt32();
        SelectedJob = buffer.ReadBoolean() ? buffer.ReadString() : null;
        CooldownSecondsRemaining = buffer.ReadFloat();
        RemainingRefreshes = buffer.ReadInt32();

        var pinnedCount = buffer.ReadInt32();
        PinnedSlots = new List<AshfallPinnedSlot>(pinnedCount);
        for (var i = 0; i < pinnedCount; i++)
        {
            var length = buffer.ReadVariableInt32();
            var bytes = buffer.ReadBytes(length);
            using var stream = new MemoryStream(bytes);
            serializer.DeserializeDirect(stream, out AshfallPinnedSlot slot);
            PinnedSlots.Add(slot);
        }

        ConfirmedSlotIndex = buffer.ReadInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Candidates.Count);
        foreach (var candidate in Candidates)
        {
            using var stream = new MemoryStream();
            serializer.SerializeDirect(stream, candidate);
            var bytes = stream.ToArray();
            buffer.WriteVariableInt32(bytes.Length);
            buffer.Write(bytes);
        }

        buffer.Write(PoolRevision);
        buffer.Write(SelectedIndex);
        buffer.Write(SelectedJob != null);
        if (SelectedJob != null)
            buffer.Write(SelectedJob);
        buffer.Write(CooldownSecondsRemaining);
        buffer.Write(RemainingRefreshes);

        buffer.Write(PinnedSlots.Count);
        foreach (var slot in PinnedSlots)
        {
            using var stream = new MemoryStream();
            serializer.SerializeDirect(stream, slot);
            var bytes = stream.ToArray();
            buffer.WriteVariableInt32(bytes.Length);
            buffer.Write(bytes);
        }

        buffer.Write(ConfirmedSlotIndex);
    }
}

/// <summary>
///     Client informs the server of their selected candidate index.
/// </summary>
public sealed class MsgAshfallSelectCandidate : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Core;

    public int SelectedIndex { get; set; }
    public int PoolRevision { get; set; }
    public string SelectedJob { get; set; } = string.Empty;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        PoolRevision = buffer.ReadInt32();
        SelectedIndex = buffer.ReadInt32();
        SelectedJob = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(PoolRevision);
        buffer.Write(SelectedIndex);
        buffer.Write(SelectedJob);
    }
}

/// <summary>
///     Client pins the candidate + job combination into a priority slot (0..4). The server
///     validates the pair against the current pool; the client draft is never trusted.
/// </summary>
public sealed class MsgAshfallPinCandidate : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Core;

    public int PoolRevision { get; set; }
    public int SlotIndex { get; set; }
    public Guid CandidateId { get; set; }
    public string JobId { get; set; } = string.Empty;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        PoolRevision = buffer.ReadInt32();
        SlotIndex = buffer.ReadInt32();
        CandidateId = buffer.ReadGuid();
        JobId = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(PoolRevision);
        buffer.Write(SlotIndex);
        buffer.Write(CandidateId);
        buffer.Write(JobId);
    }
}

/// <summary>
///     Client clears one priority slot; the other slots keep their positions.
/// </summary>
public sealed class MsgAshfallClearPrioritySlot : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Core;

    public int SlotIndex { get; set; }

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        SlotIndex = buffer.ReadInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(SlotIndex);
    }
}

/// <summary>
///     Client moves one pinned priority slot to another slot; an occupied target swaps.
///     A pure reorder of already-validated pins, so reroll survivors reorder fine too.
/// </summary>
public sealed class MsgAshfallMovePrioritySlot : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Core;

    public int SlotIndex { get; set; }
    public int TargetSlotIndex { get; set; }

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        SlotIndex = buffer.ReadInt32();
        TargetSlotIndex = buffer.ReadInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(SlotIndex);
        buffer.Write(TargetSlotIndex);
    }
}

/// <summary>
///     Client submits their Guided Lifepath choices to generate a candidate into a specific slot.
/// </summary>
public sealed class MsgAshfallLifepathSubmit : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Core;

    public Lifepath.AshfallLifepathChoices Choices { get; set; } = new();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var length = buffer.ReadVariableInt32();
        var bytes = buffer.ReadBytes(length);
        using var stream = new MemoryStream(bytes);
        serializer.DeserializeDirect(stream, out Lifepath.AshfallLifepathChoices choices);
        Choices = choices;
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        using var stream = new MemoryStream();
        serializer.SerializeDirect(stream, Choices);
        var bytes = stream.ToArray();
        buffer.WriteVariableInt32(bytes.Length);
        buffer.Write(bytes);
    }
}

/// <summary>
///     Client selects which of the slots is active to enter the shift with.
/// </summary>
public sealed class MsgAshfallSelectSlot : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Core;

    public int SlotIndex { get; set; }

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        SlotIndex = buffer.ReadInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(SlotIndex);
    }
}

/// <summary>
///     Client requests to purge/reset an occupied slot (subject to anti-reroll rules on server).
/// </summary>
public sealed class MsgAshfallResetSlot : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Core;

    public int SlotIndex { get; set; }

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        SlotIndex = buffer.ReadInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(SlotIndex);
    }
}
