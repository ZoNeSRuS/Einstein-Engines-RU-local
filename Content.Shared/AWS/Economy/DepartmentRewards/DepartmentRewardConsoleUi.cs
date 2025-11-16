using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared.AWS.Economy.DepartmentRewards;

[Serializable, NetSerializable]
public sealed class DepartmentRewardConsoleState : BoundUserInterfaceState
{
    public string DepartmentName;
    public List<DepartmentRewardTaskState> Tasks;
    public string StatusText;
    public string AuthorizationText;
    public bool CanEject;
    public bool ConfirmEnabled;
    public bool FailEnabled;
    public List<DepartmentRewardHistoryEntry> History;

    public DepartmentRewardConsoleState(
        string departmentName,
        List<DepartmentRewardTaskState> tasks,
        string statusText,
        string authorizationText,
        bool canEject,
        bool confirmEnabled,
        bool failEnabled,
        List<DepartmentRewardHistoryEntry>? history = null)
    {
        DepartmentName = departmentName;
        Tasks = tasks;
        StatusText = statusText;
        AuthorizationText = authorizationText;
        CanEject = canEject;
        ConfirmEnabled = confirmEnabled;
        FailEnabled = failEnabled;
        History = history ?? new List<DepartmentRewardHistoryEntry>();
    }
}

[Serializable, NetSerializable]
public sealed class DepartmentRewardMasterConsoleState : BoundUserInterfaceState
{
    public List<DepartmentRewardMasterTaskState> Tasks;
    public List<DepartmentRewardMasterHistoryEntry> History;

    public DepartmentRewardMasterConsoleState(
        List<DepartmentRewardMasterTaskState> tasks,
        List<DepartmentRewardMasterHistoryEntry> history)
    {
        Tasks = tasks;
        History = history;
    }
}

[Serializable, NetSerializable]
public sealed class DepartmentRewardMasterTaskState
{
    public string DepartmentId;
    public string DepartmentName;
    public DepartmentRewardTaskState Task;

    public DepartmentRewardMasterTaskState(string departmentId, string departmentName, DepartmentRewardTaskState task)
    {
        DepartmentId = departmentId;
        DepartmentName = departmentName;
        Task = task;
    }
}

[Serializable, NetSerializable]
public sealed class DepartmentRewardMasterHistoryEntry
{
    public string DepartmentId;
    public string DepartmentName;
    public DepartmentRewardHistoryEntry Entry;

    public DepartmentRewardMasterHistoryEntry(string departmentId, string departmentName, DepartmentRewardHistoryEntry entry)
    {
        DepartmentId = departmentId;
        DepartmentName = departmentName;
        Entry = entry;
    }
}

[Serializable, NetSerializable]
public sealed class DepartmentRewardTaskState
{
    public DepartmentRewardStage Stage;
    public bool Available;
    public bool Completed;
    public bool Visible;
    public string? TitleLocId;
    public string? TitleFallback;
    public string? DescriptionLocId;
    public string? DescriptionFallback;
    public int RewardAmount;
    public string? RewardFallback;
    public DepartmentRewardTaskStatus Status;
    public string? UnlockStationTimeText;

    public DepartmentRewardTaskState(
        DepartmentRewardStage stage,
        bool available,
        bool completed,
        bool visible,
        string? titleLocId,
        string? titleFallback,
        string? descriptionLocId,
        string? descriptionFallback,
        int rewardAmount,
        string? rewardFallback,
        DepartmentRewardTaskStatus status,
        string? unlockStationTimeText)
    {
        Stage = stage;
        Available = available;
        Completed = completed;
        Visible = visible;
        TitleLocId = titleLocId;
        TitleFallback = titleFallback;
        DescriptionLocId = descriptionLocId;
        DescriptionFallback = descriptionFallback;
        RewardAmount = rewardAmount;
        RewardFallback = rewardFallback;
        Status = status;
        UnlockStationTimeText = unlockStationTimeText;
    }
}

[Serializable, NetSerializable]
public enum DepartmentRewardTaskStatus
{
    Available = 0,
    Cooldown = 1,
    WaitingPrevious = 2,
    Completed = 3
}

[Serializable, NetSerializable]
public sealed class DepartmentRewardHistoryEntry
{
    public string TimeText;
    public DepartmentRewardHistoryEntryType Type;
    public string? TaskTitleLocId;
    public string? TaskTitleFallback;
    public int? Penalty;
    public string? DescriptionFallback;
    public float StationTimeSeconds;

    public DepartmentRewardHistoryEntry(
        string timeText,
        DepartmentRewardHistoryEntryType type,
        string? taskTitleLocId,
        string? taskTitleFallback,
        int? penalty,
        string? descriptionFallback,
        float stationTimeSeconds)
    {
        TimeText = timeText;
        Type = type;
        TaskTitleLocId = taskTitleLocId;
        TaskTitleFallback = taskTitleFallback;
        Penalty = penalty;
        DescriptionFallback = descriptionFallback;
        StationTimeSeconds = stationTimeSeconds;
    }
}

[Serializable, NetSerializable]
public enum DepartmentRewardHistoryEntryType : byte
{
    Completed = 0,
    Failed = 1
}

[Serializable, NetSerializable]
public sealed class DepartmentRewardConsoleConfirmMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class DepartmentRewardConsoleFailMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public enum DepartmentRewardConsoleUiKey
{
    Key = 0
}

[Serializable, NetSerializable]
public enum DepartmentRewardMasterConsoleUiKey
{
    Key = 0
}
