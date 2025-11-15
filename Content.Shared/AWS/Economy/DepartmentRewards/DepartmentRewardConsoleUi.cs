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
public sealed class DepartmentRewardTaskState
{
    public DepartmentRewardStage Stage;
    public string StageLabel;
    public string Title;
    public string Description;
    public string RewardText;
    public string StatusText;
    public bool Available;
    public bool Completed;
    public bool Visible;

    public DepartmentRewardTaskState(
        DepartmentRewardStage stage,
        string stageLabel,
        string title,
        string description,
        string rewardText,
        string statusText,
        bool available,
        bool completed,
        bool visible)
    {
        Stage = stage;
        StageLabel = stageLabel;
        Title = title;
        Description = description;
        RewardText = rewardText;
        StatusText = statusText;
        Available = available;
        Completed = completed;
        Visible = visible;
    }
}

[Serializable, NetSerializable]
public sealed class DepartmentRewardHistoryEntry
{
    public string TimeText;
    public string Description;

    public DepartmentRewardHistoryEntry(string timeText, string description)
    {
        TimeText = timeText;
        Description = description;
    }
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
