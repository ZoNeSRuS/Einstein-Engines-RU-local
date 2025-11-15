using System;
using System.Collections.Generic;
using Content.Server.Popups;
using Content.Server.UserInterface;
using Content.Server.AWS.Economy.Bank;
using Content.Shared.AWS.Economy.DepartmentRewards;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.AWS.Economy.DepartmentRewards;

public sealed partial class DepartmentRewardConsoleSystem : SharedDepartmentRewardConsoleSystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EconomyBankAccountSystem _bank = default!;

    private const string RewardSourceAccountId = "NT-CentCom";

    private static readonly ProtoId<AccessLevelPrototype>[] AuthorizedAccess =
    {
        "Captain",
        "NanotrasenRepresentative"
    };

    private static readonly DepartmentRewardStage[] StageOrder =
    {
        DepartmentRewardStage.Start,
        DepartmentRewardStage.Mid,
        DepartmentRewardStage.Late
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DepartmentRewardConsoleComponent, BoundUIOpenedEvent>(OnConsoleOpened);
        SubscribeLocalEvent<DepartmentRewardConsoleComponent, DepartmentRewardConsoleConfirmMessage>(OnConfirm);
        SubscribeLocalEvent<DepartmentRewardConsoleComponent, DepartmentRewardConsoleFailMessage>(OnFail);
        SubscribeLocalEvent<DepartmentRewardConsoleComponent, MapInitEvent>(OnConsoleMapInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DepartmentRewardConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextTimerUpdate is not { } next || next > _timing.CurTime)
                continue;

            UpdateConsoleUi(uid, comp);
            ScheduleTimerUpdates(comp);
        }
    }

    protected override void OnAuthorizationSlotChanged(EntityUid uid, DepartmentRewardConsoleComponent component)
    {
        base.OnAuthorizationSlotChanged(uid, component);
        UpdateConsoleUi(uid, component);
    }

    private void OnConsoleMapInit(EntityUid uid, DepartmentRewardConsoleComponent component, MapInitEvent args)
    {
        InitializeStageTasks(component);
        ScheduleTimerUpdates(component);
        UpdateConsoleUi(uid, component);
    }

    private void OnConsoleOpened(EntityUid uid, DepartmentRewardConsoleComponent component, BoundUIOpenedEvent args)
    {
        EnsureStageTasksInitialized(component);
        UpdateConsoleUi(uid, component);
    }

    private void OnConfirm(EntityUid uid, DepartmentRewardConsoleComponent component, DepartmentRewardConsoleConfirmMessage args)
    {
        if (args.Actor is not { Valid: true } actor)
            return;

        EnsureStageTasksInitialized(component);

        if (component.CardSlot.Item is not { Valid: true } cardEntity || !HasAuthorizedAccess(cardEntity))
        {
            _popup.PopupEntity(Loc.GetString("department-reward-console-popup-need-card"), uid, actor);
            return;
        }

        if (!TryGetCurrentStageTask(component, out var stage, out var runtime))
        {
            _popup.PopupEntity(Loc.GetString("department-reward-console-popup-no-task"), uid, actor);
            return;
        }

        if (runtime.Reward <= 0)
        {
            _popup.PopupEntity(Loc.GetString("department-reward-console-popup-invalid-reward"), uid, actor);
            return;
        }

        var reason = Loc.GetString("department-reward-console-bank-reason",
            ("department", component.DepartmentName),
            ("task", runtime.Title ?? component.PlaceholderTaskTitle));

        if (!_bank.TrySendMoney(RewardSourceAccountId, component.DepartmentId, (ulong) runtime.Reward, reason, out var error))
        {
            _popup.PopupEntity(error ?? Loc.GetString("department-reward-console-popup-bank-failure"), uid, actor);
            return;
        }

        AddHistoryEntry(component, Loc.GetString("department-reward-console-history-task-complete",
            ("task", runtime.Title ?? component.PlaceholderTaskTitle)));

        runtime.Completed = true;

        UpdateConsoleUi(uid, component);
        ScheduleTimerUpdates(component);
    }

    private void OnFail(EntityUid uid, DepartmentRewardConsoleComponent component, DepartmentRewardConsoleFailMessage args)
    {
        if (args.Actor is not { Valid: true } actor)
            return;

        EnsureStageTasksInitialized(component);

        if (component.CardSlot.Item is not { Valid: true } cardEntity || !HasAuthorizedAccess(cardEntity))
        {
            _popup.PopupEntity(Loc.GetString("department-reward-console-popup-need-card"), uid, actor);
            return;
        }

        if (!TryGetCurrentStageTask(component, out var stage, out var runtime))
        {
            _popup.PopupEntity(Loc.GetString("department-reward-console-popup-no-task"), uid, actor);
            return;
        }

        var penalty = (int) MathF.Ceiling(runtime.Reward * runtime.PenaltyMultiplier);
        if (!_bank.TryForceDebit(component.DepartmentId, (ulong) penalty,
                Loc.GetString("department-reward-console-bank-penalty-reason",
                    ("department", component.DepartmentName),
                    ("task", runtime.Title ?? component.PlaceholderTaskTitle))))
        {
            _popup.PopupEntity(Loc.GetString("department-reward-console-popup-bank-failure"), uid, actor);
            return;
        }

        AddHistoryEntry(component, Loc.GetString("department-reward-console-history-task-failed",
            ("task", runtime.Title ?? component.PlaceholderTaskTitle),
            ("penalty", penalty)));

        AssignStageTask(component, stage, component.FailCooldown);
        UpdateConsoleUi(uid, component);
        ScheduleTimerUpdates(component);
    }

    private void UpdateConsoleUi(EntityUid uid, DepartmentRewardConsoleComponent component)
    {
        var state = BuildConsoleState(uid, component);
        _uiSystem.SetUiState(uid, DepartmentRewardConsoleUiKey.Key, state);
    }

    private DepartmentRewardConsoleState BuildConsoleState(EntityUid uid, DepartmentRewardConsoleComponent component)
    {
        EnsureStageTasksInitialized(component);

        var department = string.IsNullOrWhiteSpace(component.DepartmentName)
            ? Loc.GetString("department-reward-console-placeholder-department")
            : component.DepartmentName;

        var tasks = new List<DepartmentRewardTaskState>();
        var history = new List<DepartmentRewardHistoryEntry>(component.History);

        var cardName = string.Empty;
        var hasCard = false;
        var hasAuthorizedAccess = false;
        if (component.CardSlot.Item is { Valid: true } cardEntity)
        {
            hasCard = true;
            hasAuthorizedAccess = HasAuthorizedAccess(cardEntity);
            cardName = MetaData(cardEntity).EntityName ?? string.Empty;
        }

        var currentStageAvailable = TryGetCurrentStageTask(component, out _, out _);
        string statusText;
        if (_bank.TryGetAccount(component.DepartmentId, out var account))
        {
            var stateLoc = account.Value.Comp.Blocked
                ? "department-reward-console-status-account-blocked"
                : "department-reward-console-status-account-available";
            statusText = Loc.GetString("department-reward-console-status-account",
                ("balance", account.Value.Comp.Balance),
                ("state", Loc.GetString(stateLoc)));
        }
        else
        {
            statusText = Loc.GetString("department-reward-console-status-missing");
        }

        foreach (var stage in StageOrder)
        {
            var data = GetStageRuntime(component, stage);
            var title = data.Title ?? component.PlaceholderTaskTitle;
            var description = data.Description ?? component.PlaceholderTaskDescription;
            var rewardText = data.Reward > 0
                ? Loc.GetString("department-reward-console-reward-amount", ("amount", data.Reward))
                : component.PlaceholderRewardText;
            var stageLabel = GetStageLabel(stage);

            var isUnlocked = _timing.CurTime >= data.UnlockTime;
            var previousCompleted = ArePreviousStagesCompleted(component, stage);
            var available = isUnlocked && previousCompleted && !data.Completed;

            string stageStatus;
            if (data.Completed)
                stageStatus = Loc.GetString("department-reward-stage-completed");
            else if (!isUnlocked)
                stageStatus = Loc.GetString("department-reward-console-stage-cooldown", ("time", FormatCountdown(data.UnlockTime - _timing.CurTime)));
            else if (!previousCompleted)
                stageStatus = Loc.GetString("department-reward-stage-wait-previous");
            else
                stageStatus = Loc.GetString("department-reward-stage-available");

            tasks.Add(new DepartmentRewardTaskState(stage, stageLabel, title, description, rewardText, stageStatus, available, data.Completed));
        }

        return new DepartmentRewardConsoleState(
            department,
            tasks,
            statusText,
            cardName,
            hasCard,
            hasAuthorizedAccess && currentStageAvailable,
            hasAuthorizedAccess && currentStageAvailable,
            history);
    }

    private void AddHistoryEntry(DepartmentRewardConsoleComponent component, string description)
    {
        var time = _timing.CurTime;
        var entry = new DepartmentRewardHistoryEntry($"{time.Hours:00}:{time.Minutes:00}", description);
        component.History.Insert(0, entry);
        if (component.History.Count > 5)
            component.History.RemoveAt(component.History.Count - 1);
    }

    private void ScheduleTimerUpdates(DepartmentRewardConsoleComponent component)
    {
        var now = _timing.CurTime;
        foreach (var stage in StageOrder)
        {
            var data = GetStageRuntime(component, stage);
            if (data.Completed)
                continue;

            if (data.UnlockTime > now)
            {
                component.NextTimerUpdate = now + TimeSpan.FromSeconds(1);
                return;
            }
        }

        component.NextTimerUpdate = null;
    }

    private void InitializeStageTasks(DepartmentRewardConsoleComponent component)
    {
        if (component.StageTasksInitialized)
            return;

        component.StageTasksInitialized = true;
        AssignStageTask(component, DepartmentRewardStage.Start, component.StartUnlockOffset);
        AssignStageTask(component, DepartmentRewardStage.Mid, component.MidUnlockOffset);
        AssignStageTask(component, DepartmentRewardStage.Late, component.LateUnlockOffset);
        ScheduleTimerUpdates(component);
    }

    private void EnsureStageTasksInitialized(DepartmentRewardConsoleComponent component)
    {
        if (!component.StageTasksInitialized)
            InitializeStageTasks(component);
    }

    private void AssignStageTask(DepartmentRewardConsoleComponent component, DepartmentRewardStage stage, TimeSpan delay)
    {
        var runtime = GetStageRuntime(component, stage);
        var prototype = PickTaskPrototype(component.DepartmentId, stage);

        if (prototype != null)
        {
            runtime.TaskId = prototype.ID;
            runtime.Title = Loc.GetString(prototype.Title);
            runtime.Description = Loc.GetString(prototype.Description);
            runtime.Reward = prototype.RewardAmount;
            runtime.PenaltyMultiplier = prototype.PenaltyMultiplier;
        }
        else
        {
            runtime.TaskId = null;
            runtime.Title = component.PlaceholderTaskTitle;
            runtime.Description = component.PlaceholderTaskDescription;
            runtime.Reward = 0;
            runtime.PenaltyMultiplier = DepartmentRewardConsoleComponent.DefaultPenaltyMultiplier;
        }

        runtime.Completed = false;
        runtime.UnlockTime = _timing.CurTime + delay;
    }

    private DepartmentRewardStageRuntime GetStageRuntime(DepartmentRewardConsoleComponent component, DepartmentRewardStage stage)
    {
        if (!component.StageTasks.TryGetValue(stage, out var runtime))
        {
            runtime = new DepartmentRewardStageRuntime();
            component.StageTasks[stage] = runtime;
        }

        return runtime;
    }

    private bool TryGetCurrentStageTask(DepartmentRewardConsoleComponent component, out DepartmentRewardStage stage, out DepartmentRewardStageRuntime runtime)
    {
        var now = _timing.CurTime;
        foreach (var entry in StageOrder)
        {
            var data = GetStageRuntime(component, entry);
            if (data.Completed)
                continue;

            if (now < data.UnlockTime)
                continue;

            if (!ArePreviousStagesCompleted(component, entry))
                continue;

            stage = entry;
            runtime = data;
            return true;
        }

        stage = default;
        runtime = default!;
        return false;
    }

    private bool ArePreviousStagesCompleted(DepartmentRewardConsoleComponent component, DepartmentRewardStage stage)
    {
        foreach (var entry in StageOrder)
        {
            if (entry == stage)
                break;

            var runtime = GetStageRuntime(component, entry);
            if (!runtime.Completed)
                return false;
        }

        return true;
    }

    private string GetStageLabel(DepartmentRewardStage stage)
    {
        return stage switch
        {
            DepartmentRewardStage.Start => Loc.GetString("department-reward-stage-start"),
            DepartmentRewardStage.Mid => Loc.GetString("department-reward-stage-mid"),
            DepartmentRewardStage.Late => Loc.GetString("department-reward-stage-late"),
            _ => stage.ToString()
        };
    }

    private DepartmentRewardTaskPrototype? PickTaskPrototype(string departmentId, DepartmentRewardStage stage)
    {
        var pool = new List<DepartmentRewardTaskPrototype>();
        foreach (var proto in _proto.EnumeratePrototypes<DepartmentRewardTaskPrototype>())
        {
            if (!string.Equals(proto.DepartmentId, departmentId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (proto.Stage != stage)
                continue;

            pool.Add(proto);
        }

        if (pool.Count == 0)
            return null;

        return _random.Pick(pool);
    }

    private string FormatCountdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        var minutes = (int) span.TotalMinutes;
        if (minutes > 99)
            minutes = 99;
        var seconds = span.Seconds;
        return $"{minutes:00}:{seconds:00}";
    }

    private bool HasAuthorizedAccess(EntityUid card)
    {
        var tags = new HashSet<ProtoId<AccessLevelPrototype>>();

        if (TryComp<AccessComponent>(card, out var access) && access.Enabled)
            tags.UnionWith(access.Tags);

        var ev = new GetAccessTagsEvent(tags, _proto);
        RaiseLocalEvent(card, ref ev);

        foreach (var tag in tags)
        {
            foreach (var authorized in AuthorizedAccess)
            {
                if (tag == authorized)
                    return true;
            }
        }

        return false;
    }
}
