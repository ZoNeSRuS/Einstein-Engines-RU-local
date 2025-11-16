using Content.Shared.AWS.Economy.DepartmentRewards;
using Content.Shared.Access.Components;
using Content.Shared.PDA;
using Robust.Shared.Localization;

namespace Content.Server.AWS.Economy.DepartmentRewards;

/// <summary>
/// Provides PDA instruction overrides for department reward tasks.
/// </summary>
public sealed class DepartmentRewardPdaSystem : EntitySystem
{
    [Dependency] private readonly DepartmentRewardConsoleSystem _departmentRewardConsole = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DepartmentRewardPdaComponent, PdaCollectInstructionEvent>(OnCollectInstructions);
    }

    private void OnCollectInstructions(EntityUid uid, DepartmentRewardPdaComponent component, PdaCollectInstructionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<PdaComponent>(uid, out var pda))
            return;

        var idCard = CompOrNull<IdCardComponent>(pda.ContainedId);
        if (idCard == null || idCard.JobDepartments.Count == 0)
            return;

        foreach (var department in idCard.JobDepartments)
        {
            var rewardDepartmentId = $"NT-{department.Id}";
            if (!_departmentRewardConsole.TryGetActiveDepartmentTask(uid, rewardDepartmentId, out var task))
                continue;

            var title = ResolveTaskText(task.TitleLocId, task.TitleFallback);
            var description = ResolveTaskText(task.DescriptionLocId, task.DescriptionFallback);
            if (string.IsNullOrWhiteSpace(description))
            {
                var alertLevelKey = pda.StationAlertLevel != null ? $"alert-level-{pda.StationAlertLevel}" : "alert-level-unknown";
                description = Loc.GetString($"{alertLevelKey}-instructions");
            }

            args.DisplayText = Loc.GetString("department-reward-pda-instruction-display",
                ("title", title),
                ("instruction", description));
            args.CopyText = Loc.GetString("department-reward-pda-instruction-copy",
                ("title", title),
                ("instruction", description));
            args.Handled = true;
            return;
        }
    }

    private string ResolveTaskText(string? locId, string? fallback)
    {
        if (!string.IsNullOrEmpty(locId))
            return Loc.GetString(locId);

        return fallback ?? string.Empty;
    }
}
