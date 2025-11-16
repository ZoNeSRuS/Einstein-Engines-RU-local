using System;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Shared.AWS.Economy.DepartmentRewards;

public sealed class DepartmentRewardStageRuntime
{
    public ProtoId<DepartmentRewardTaskPrototype>? TaskId;
    public LocId? TitleLocId;
    public string? Title;
    public LocId? DescriptionLocId;
    public string? Description;
    public int Reward;
    public float PenaltyMultiplier = DepartmentRewardConsoleComponent.DefaultPenaltyMultiplier;
    public bool Completed;
    public TimeSpan UnlockTime;
}
