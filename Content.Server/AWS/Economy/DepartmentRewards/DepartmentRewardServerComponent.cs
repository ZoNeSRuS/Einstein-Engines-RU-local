using System;
using System.Collections.Generic;
using Content.Shared.AWS.Economy.DepartmentRewards;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Server.AWS.Economy.DepartmentRewards;

/// <summary>
/// Stores persistent department reward state for a map or console entity.
/// </summary>
[RegisterComponent]
public sealed partial class DepartmentRewardServerComponent : Component
{
    public Dictionary<string, DepartmentRewardDepartmentState> Departments { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DepartmentRewardDepartmentState
{
    public bool StageTasksInitialized;
    public TimeSpan? NextTimerUpdate;
    public List<DepartmentRewardHistoryEntry> History { get; } = new();

    public Dictionary<DepartmentRewardStage, DepartmentRewardStageRuntime> StageTasks { get; } = new()
    {
        { DepartmentRewardStage.Start, new DepartmentRewardStageRuntime() },
        { DepartmentRewardStage.Mid, new DepartmentRewardStageRuntime() },
        { DepartmentRewardStage.Late, new DepartmentRewardStageRuntime() }
    };

    public DepartmentRewardStageRuntime GetRuntime(DepartmentRewardStage stage)
    {
        if (!StageTasks.TryGetValue(stage, out var runtime))
        {
            runtime = new DepartmentRewardStageRuntime();
            StageTasks[stage] = runtime;
        }

        return runtime;
    }
}
