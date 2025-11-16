using System;
using System.Collections.Generic;
using Content.Server.AWS.Economy.Bank;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server.UserInterface;
using Content.Shared.AWS.Economy.DepartmentRewards;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Map;
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
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

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

        var pending = new HashSet<DepartmentStateKey>();
        var query = EntityQueryEnumerator<DepartmentRewardConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var state = GetDepartmentState(uid, comp, out var serverUid);
            if (state.NextTimerUpdate is not { } next || next > _timing.CurTime)
                continue;

            pending.Add(new DepartmentStateKey(serverUid, comp.DepartmentId));
            ScheduleTimerUpdates(state);
        }

        foreach (var entry in pending)
        {
            UpdateConsolesForDepartment(entry.ServerUid, entry.DepartmentId);
        }
    }

    protected override void OnAuthorizationSlotChanged(EntityUid uid, DepartmentRewardConsoleComponent component)
    {
        base.OnAuthorizationSlotChanged(uid, component);
        UpdateConsoleUi(uid, component);
    }

    private void OnConsoleMapInit(EntityUid uid, DepartmentRewardConsoleComponent component, MapInitEvent args)
    {
        var state = GetDepartmentState(uid, component, out _);
        InitializeStageTasks(component, state);
        ScheduleTimerUpdates(state);
        UpdateConsoleUi(uid, component, state);
    }

    private void OnConsoleOpened(EntityUid uid, DepartmentRewardConsoleComponent component, BoundUIOpenedEvent args)
    {
        var state = GetDepartmentState(uid, component, out _);
        EnsureStageTasksInitialized(component, state);
        UpdateConsoleUi(uid, component, state);
    }

    private void OnConfirm(EntityUid uid, DepartmentRewardConsoleComponent component, DepartmentRewardConsoleConfirmMessage args)
    {
        if (args.Actor is not { Valid: true } actor)
            return;

        var state = GetDepartmentState(uid, component, out var serverUid);
        EnsureStageTasksInitialized(component, state);

        if (component.CardSlot.Item is not { Valid: true } cardEntity || !HasAuthorizedAccess(cardEntity, component))
        {
            _popup.PopupEntity(Loc.GetString("department-reward-console-popup-need-card"), uid, actor);
            return;
        }

        if (!TryGetCurrentStageTask(state, out var stage, out var runtime))
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
            ("department", component.GetDepartmentName()),
            ("task", runtime.Title ?? component.GetPlaceholderTaskTitle()));

        if (!_bank.TrySendMoney(component.RewardSourceAccountId, component.DepartmentId, (ulong) runtime.Reward, reason, out var error))
        {
            _popup.PopupEntity(error ?? Loc.GetString("department-reward-console-popup-bank-failure"), uid, actor);
            return;
        }

        AddHistoryEntry(component,
            state,
            DepartmentRewardHistoryEntryType.Completed,
            runtime.TitleLocId?.Id,
            runtime.Title ?? component.GetPlaceholderTaskTitle());

        runtime.Completed = true;

        ScheduleTimerUpdates(state);
        UpdateConsolesForDepartment(serverUid, component.DepartmentId);
    }

    private void OnFail(EntityUid uid, DepartmentRewardConsoleComponent component, DepartmentRewardConsoleFailMessage args)
    {
        if (args.Actor is not { Valid: true } actor)
            return;

        var state = GetDepartmentState(uid, component, out var serverUid);
        EnsureStageTasksInitialized(component, state);

        if (component.CardSlot.Item is not { Valid: true } cardEntity || !HasAuthorizedAccess(cardEntity, component))
        {
            _popup.PopupEntity(Loc.GetString("department-reward-console-popup-need-card"), uid, actor);
            return;
        }

        if (!TryGetCurrentStageTask(state, out var stage, out var runtime))
        {
            _popup.PopupEntity(Loc.GetString("department-reward-console-popup-no-task"), uid, actor);
            return;
        }

        var penaltyAmount = Math.Ceiling(runtime.Reward * runtime.PenaltyMultiplier);
        var penaltyValue = (long) Math.Clamp(penaltyAmount, 0, long.MaxValue);
        var debitAmount = (ulong) penaltyValue;
        if (!_bank.TryForceDebit(component.DepartmentId, debitAmount,
                Loc.GetString("department-reward-console-bank-penalty-reason",
                    ("department", component.GetDepartmentName()),
                    ("task", runtime.Title ?? component.GetPlaceholderTaskTitle()))))
        {
            _popup.PopupEntity(Loc.GetString("department-reward-console-popup-bank-failure"), uid, actor);
            return;
        }

        var historyPenalty = (int) Math.Clamp(penaltyValue, 0, int.MaxValue);
        AddHistoryEntry(component,
            state,
            DepartmentRewardHistoryEntryType.Failed,
            runtime.TitleLocId?.Id,
            runtime.Title ?? component.GetPlaceholderTaskTitle(),
            historyPenalty);

        AssignStageTask(component, state, stage, component.FailCooldown);
        ScheduleTimerUpdates(state);
        UpdateConsolesForDepartment(serverUid, component.DepartmentId);
    }

    private void UpdateConsoleUi(EntityUid uid, DepartmentRewardConsoleComponent component, DepartmentRewardDepartmentState? state = null)
    {
        state ??= GetDepartmentState(uid, component, out _);
        var uiState = BuildConsoleState(uid, component, state);
        _uiSystem.SetUiState(uid, DepartmentRewardConsoleUiKey.Key, uiState);
    }

    private DepartmentRewardConsoleState BuildConsoleState(EntityUid uid, DepartmentRewardConsoleComponent component, DepartmentRewardDepartmentState state)
    {
        EnsureStageTasksInitialized(component, state);

        var department = component.GetDepartmentName();

        var tasks = new List<DepartmentRewardTaskState>();
        var history = new List<DepartmentRewardHistoryEntry>(state.History);

        var cardName = Loc.GetString("department-reward-console-auth-missing");
        var hasCard = false;
        var hasAuthorizedAccess = false;
        if (component.CardSlot.Item is { Valid: true } cardEntity)
        {
            hasCard = true;
            hasAuthorizedAccess = HasAuthorizedAccess(cardEntity);
            cardName = MetaData(cardEntity).EntityName ?? string.Empty;
        }

        var currentStageAvailable = TryGetCurrentStageTask(state, out _, out _);
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
            var data = state.GetRuntime(stage);
            var titleFallback = data.Title ?? component.GetPlaceholderTaskTitle();
            var descriptionFallback = data.Description ?? component.GetPlaceholderTaskDescription();
            var titleLocId = data.TitleLocId?.Id;
            var descriptionLocId = data.DescriptionLocId?.Id;
            var rewardAmount = data.Reward;
            var rewardFallback = component.GetPlaceholderReward();

            var isUnlocked = _timing.CurTime >= data.UnlockTime;
            var previousCompleted = ArePreviousStagesCompleted(state, stage);
            var available = isUnlocked && previousCompleted && !data.Completed;

            var visible = isUnlocked && previousCompleted;
            var status = DepartmentRewardTaskStatus.Available;
            string? unlockStationTimeText = null;
            if (data.Completed)
            {
                status = DepartmentRewardTaskStatus.Completed;
            }
            else if (!isUnlocked)
            {
                status = DepartmentRewardTaskStatus.Cooldown;
                unlockStationTimeText = FormatStationTime(GetStationTime(data.UnlockTime));
            }
            else if (!previousCompleted)
            {
                status = DepartmentRewardTaskStatus.WaitingPrevious;
            }

            tasks.Add(new DepartmentRewardTaskState(
                stage,
                available,
                data.Completed,
                visible,
                titleLocId,
                titleFallback,
                descriptionLocId,
                descriptionFallback,
                rewardAmount,
                rewardFallback,
                status,
                unlockStationTimeText));
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

    private void AddHistoryEntry(DepartmentRewardConsoleComponent component,
        DepartmentRewardDepartmentState state,
        DepartmentRewardHistoryEntryType type,
        string? taskTitleLocId,
        string taskTitleFallback,
        int? penalty = null)
    {
        var stationTime = GetStationTime(_timing.CurTime);
        var entry = new DepartmentRewardHistoryEntry(
            FormatStationTime(stationTime),
            type,
            taskTitleLocId,
            taskTitleFallback,
            penalty,
            BuildHistoryFallback(type, taskTitleFallback, penalty));
        state.History.Insert(0, entry);
        if (state.History.Count > 5)
            state.History.RemoveAt(state.History.Count - 1);
    }

    private string BuildHistoryFallback(DepartmentRewardHistoryEntryType type, string taskTitle, int? penalty)
    {
        return type switch
        {
            DepartmentRewardHistoryEntryType.Completed => Loc.GetString(
                "department-reward-console-history-task-complete",
                ("task", taskTitle)),
            DepartmentRewardHistoryEntryType.Failed => Loc.GetString(
                "department-reward-console-history-task-failed",
                ("task", taskTitle),
                ("penalty", penalty ?? 0)),
            _ => taskTitle
        };
    }

    private void ScheduleTimerUpdates(DepartmentRewardDepartmentState state)
    {
        var now = _timing.CurTime;
        foreach (var stage in StageOrder)
        {
            var data = state.GetRuntime(stage);
            if (data.Completed)
                continue;

            if (data.UnlockTime > now)
            {
                state.NextTimerUpdate = now + TimeSpan.FromSeconds(1);
                return;
            }
        }

        state.NextTimerUpdate = null;
    }

    private void InitializeStageTasks(DepartmentRewardConsoleComponent component, DepartmentRewardDepartmentState state)
    {
        if (state.StageTasksInitialized)
            return;

        state.StageTasksInitialized = true;
        AssignStageTask(component, state, DepartmentRewardStage.Start, component.StartUnlockOffset, relativeToCurrent: false);
        AssignStageTask(component, state, DepartmentRewardStage.Mid, component.MidUnlockOffset, relativeToCurrent: false);
        AssignStageTask(component, state, DepartmentRewardStage.Late, component.LateUnlockOffset, relativeToCurrent: false);
        ScheduleTimerUpdates(state);
    }

    private void EnsureStageTasksInitialized(DepartmentRewardConsoleComponent component, DepartmentRewardDepartmentState state)
    {
        if (!state.StageTasksInitialized)
            InitializeStageTasks(component, state);
    }

    private void AssignStageTask(DepartmentRewardConsoleComponent component, DepartmentRewardDepartmentState state, DepartmentRewardStage stage, TimeSpan delay, bool relativeToCurrent = true)
    {
        var runtime = state.GetRuntime(stage);
        var prototype = PickTaskPrototype(component.DepartmentId, stage);

        if (prototype != null)
        {
            runtime.TaskId = prototype.ID;
            runtime.TitleLocId = prototype.Title;
            runtime.Title = Loc.GetString(prototype.Title);
            runtime.DescriptionLocId = prototype.Description;
            runtime.Description = Loc.GetString(prototype.Description);
            runtime.Reward = prototype.RewardAmount;
            runtime.PenaltyMultiplier = prototype.PenaltyMultiplier;
        }
        else
        {
            runtime.TaskId = null;
            runtime.TitleLocId = null;
            runtime.Title = component.GetPlaceholderTaskTitle();
            runtime.DescriptionLocId = null;
            runtime.Description = component.GetPlaceholderTaskDescription();
            runtime.Reward = 0;
            runtime.PenaltyMultiplier = DepartmentRewardConsoleComponent.DefaultPenaltyMultiplier;
        }

        runtime.Completed = false;
        var target = relativeToCurrent
            ? _timing.CurTime + delay
            : _gameTicker.RoundStartTimeSpan + delay;

        if (!relativeToCurrent && target < _timing.CurTime)
            target = _timing.CurTime;

        runtime.UnlockTime = target;
    }

    private bool TryGetCurrentStageTask(DepartmentRewardDepartmentState state, out DepartmentRewardStage stage, out DepartmentRewardStageRuntime runtime)
    {
        var now = _timing.CurTime;
        foreach (var entry in StageOrder)
        {
            var data = state.GetRuntime(entry);
            if (data.Completed)
                continue;

            if (now < data.UnlockTime)
                continue;

            if (!ArePreviousStagesCompleted(state, entry))
                continue;

            stage = entry;
            runtime = data;
            return true;
        }

        stage = default;
        runtime = default!;
        return false;
    }

    private bool ArePreviousStagesCompleted(DepartmentRewardDepartmentState state, DepartmentRewardStage stage)
    {
        foreach (var entry in StageOrder)
        {
            if (entry == stage)
                break;

            var runtime = state.GetRuntime(entry);
            if (!runtime.Completed)
                return false;
        }

        return true;
    }

    private DepartmentRewardTaskPrototype? PickTaskPrototype(string departmentId, DepartmentRewardStage stage)
    {
        var pool = new List<(DepartmentRewardTaskPrototype Prototype, float Weight)>();
        foreach (var proto in _proto.EnumeratePrototypes<DepartmentRewardTaskPrototype>())
        {
            if (!string.Equals(proto.DepartmentId, departmentId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (proto.Stage != stage)
                continue;

            pool.Add((proto, Math.Max(0.01f, proto.Weight)));
        }

        if (pool.Count == 0)
            return null;

        var total = 0f;
        foreach (var entry in pool)
        {
            total += entry.Weight;
        }

        var target = _random.NextFloat(0f, total);
        var running = 0f;
        foreach (var entry in pool)
        {
            running += entry.Weight;
            if (target <= running)
                return entry.Prototype;
        }

        return pool[^1].Prototype;
    }

    private string FormatStationTime(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        var minutes = Math.Floor(span.TotalMinutes);
        var rounded = TimeSpan.FromMinutes(minutes);
        return rounded.ToString("hh\\:mm");
    }

    private TimeSpan GetStationTime(TimeSpan absolute)
    {
        var start = _gameTicker.RoundStartTimeSpan;
        if (absolute <= start)
            return TimeSpan.Zero;

        return absolute - start;
    }

    private DepartmentRewardServerComponent EnsureServerComponent(EntityUid consoleUid, out EntityUid serverUid)
    {
        if (TryComp<DepartmentRewardServerComponent>(consoleUid, out var server))
        {
            serverUid = consoleUid;
            return server;
        }

        var xform = Transform(consoleUid);
        if (xform.MapID != MapId.Nullspace && _mapManager.MapExists(xform.MapID))
        {
            serverUid = _mapManager.GetMapEntityId(xform.MapID);
        }
        else
        {
            serverUid = consoleUid;
        }

        return EnsureComp<DepartmentRewardServerComponent>(serverUid);
    }

    private DepartmentRewardDepartmentState GetDepartmentState(EntityUid consoleUid, DepartmentRewardConsoleComponent component, out EntityUid serverUid)
    {
        var server = EnsureServerComponent(consoleUid, out serverUid);
        if (!server.Departments.TryGetValue(component.DepartmentId, out var state))
        {
            state = new DepartmentRewardDepartmentState();
            server.Departments[component.DepartmentId] = state;
        }

        return state;
    }

    private void UpdateConsolesForDepartment(EntityUid serverUid, string departmentId)
    {
        var query = EntityQueryEnumerator<DepartmentRewardConsoleComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            var server = EnsureServerComponent(uid, out var consoleServerUid);
            if (consoleServerUid != serverUid)
                continue;

            if (!string.Equals(component.DepartmentId, departmentId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!server.Departments.TryGetValue(departmentId, out var state))
                continue;

            UpdateConsoleUi(uid, component, state);
        }
    }

    private readonly struct DepartmentStateKey : IEquatable<DepartmentStateKey>
    {
        public readonly EntityUid ServerUid;
        public readonly string DepartmentId;

        public DepartmentStateKey(EntityUid serverUid, string departmentId)
        {
            ServerUid = serverUid;
            DepartmentId = departmentId;
        }

        public bool Equals(DepartmentStateKey other) =>
            ServerUid == other.ServerUid && string.Equals(DepartmentId, other.DepartmentId, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) =>
            obj is DepartmentStateKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(ServerUid, StringComparer.OrdinalIgnoreCase.GetHashCode(DepartmentId));
    }

    private bool HasAuthorizedAccess(EntityUid card, DepartmentRewardConsoleComponent component)
    {
        var tags = new HashSet<ProtoId<AccessLevelPrototype>>();

        if (TryComp<AccessComponent>(card, out var access) && access.Enabled)
            tags.UnionWith(access.Tags);

        var ev = new GetAccessTagsEvent(tags, _proto);
        RaiseLocalEvent(card, ref ev);

        if (component.AuthorizedAccessTags.Count == 0)
            return true;

        foreach (var tag in tags)
        {
            foreach (var authorized in component.AuthorizedAccessTags)
            {
                if (tag == authorized)
                    return true;
            }
        }

        return false;
    }
}
