using System;
using Content.Shared.Access;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;

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
    /// Source account for rewards, defaults to CentCom account.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("rewardSourceAccountId")]
    public string RewardSourceAccountId = "NT-CentCom";

    /// <summary>
    /// Human-readable department name shown in the UI (localized).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("departmentNameLocId")]
    public LocId DepartmentNameLocId = "department-reward-console-placeholder-department";

    /// <summary>
    /// Placeholder task title used until gameplay logic is wired (localized).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("placeholderTaskTitleLocId")]
    public LocId PlaceholderTaskTitleLocId = "department-reward-console-placeholder-task-title";

    /// <summary>
    /// Placeholder task description used until gameplay logic is wired (localized).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("placeholderTaskDescriptionLocId")]
    public LocId PlaceholderTaskDescriptionLocId = "department-reward-console-placeholder-description";

    /// <summary>
    /// Placeholder reward text used until gameplay logic is wired (localized).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("placeholderRewardTextLocId")]
    public LocId PlaceholderRewardTextLocId = "department-reward-console-placeholder-reward";

    /// <summary>
    /// Access tags required to operate the console.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("authorizedAccessTags")]
    public HashSet<ProtoId<AccessLevelPrototype>> AuthorizedAccessTags { get; set; } = new()
    {
        "Captain",
        "NanotrasenRepresentative"
    };

    [ViewVariables(VVAccess.ReadWrite), DataField("failCooldown")]
    public TimeSpan FailCooldown = TimeSpan.FromSeconds(30);

    [DataField("midUnlockOffset")]
    public TimeSpan MidUnlockOffset = TimeSpan.FromSeconds(60);

    [DataField("lateUnlockOffset")]
    public TimeSpan LateUnlockOffset = TimeSpan.FromSeconds(90);

    [DataField("startUnlockOffset")]
    public TimeSpan StartUnlockOffset = TimeSpan.Zero;

    [DataField("cardSlot")]
    public ItemSlot CardSlot = new();

    public string GetDepartmentName() =>
        Loc.GetString(DepartmentNameLocId);

    public string GetPlaceholderTaskTitle() =>
        Loc.GetString(PlaceholderTaskTitleLocId);

    public string GetPlaceholderTaskDescription() =>
        Loc.GetString(PlaceholderTaskDescriptionLocId);

    public string GetPlaceholderReward() =>
        Loc.GetString(PlaceholderRewardTextLocId);
}
