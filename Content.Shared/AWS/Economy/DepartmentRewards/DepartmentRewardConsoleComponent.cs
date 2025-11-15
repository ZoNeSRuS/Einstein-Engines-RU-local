using System;
using System.Collections.Generic;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.AWS.Economy.DepartmentRewards;

/// <summary>
/// Marker component for the new departmental reward consoles.
/// For now it only stores presentation data so that the UI can display a mock state.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DepartmentRewardConsoleComponent : Component
{
    public const string AuthorizationSlotId = "DepartmentRewardConsole-card";
    public const float DefaultPenaltyMultiplier = 1.5f;

    /// <summary>
    /// Linked department account id (e.g. NT-Cargo).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("departmentId")]
    public string DepartmentId = "NT-Cargo";

    /// <summary>
    /// Human-readable department name shown in the UI.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("departmentName")]
    public string DepartmentName = "Cargo";

    /// <summary>
    /// Placeholder task title used until gameplay logic is wired.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("placeholderTaskTitle")]
    public string PlaceholderTaskTitle = string.Empty;

    /// <summary>
    /// Placeholder task description used until gameplay logic is wired.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("placeholderTaskDescription")]
    public string PlaceholderTaskDescription = string.Empty;

    /// <summary>
    /// Placeholder reward text used until gameplay logic is wired.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("placeholderRewardText")]
    public string PlaceholderRewardText = string.Empty;

    [ViewVariables(VVAccess.ReadWrite), DataField("failCooldown")]
    public TimeSpan FailCooldown = TimeSpan.FromSeconds(30);

    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan? NextTimerUpdate;

    [DataField("midUnlockOffset")]
    public TimeSpan MidUnlockOffset = TimeSpan.FromSeconds(60);

    [DataField("lateUnlockOffset")]
    public TimeSpan LateUnlockOffset = TimeSpan.FromSeconds(90);

    [DataField("startUnlockOffset")]
    public TimeSpan StartUnlockOffset = TimeSpan.Zero;

    [DataField("cardSlot")]
    public ItemSlot CardSlot = new();

    [DataField]
    public List<DepartmentRewardHistoryEntry> History = new();

    [DataField]
    public bool StageTasksInitialized;

    [ViewVariables]
    public Dictionary<DepartmentRewardStage, DepartmentRewardStageRuntime> StageTasks { get; } = new()
    {
        { DepartmentRewardStage.Start, new DepartmentRewardStageRuntime() },
        { DepartmentRewardStage.Mid, new DepartmentRewardStageRuntime() },
        { DepartmentRewardStage.Late, new DepartmentRewardStageRuntime() }
    };
}

public sealed class DepartmentRewardStageRuntime
{
    public ProtoId<DepartmentRewardTaskPrototype>? TaskId;
    public string? Title;
    public string? Description;
    public int Reward;
    public float PenaltyMultiplier = DepartmentRewardConsoleComponent.DefaultPenaltyMultiplier;
    public bool Completed;
    public TimeSpan UnlockTime;
}
