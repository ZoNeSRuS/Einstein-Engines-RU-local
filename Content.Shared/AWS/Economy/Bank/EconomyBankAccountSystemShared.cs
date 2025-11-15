using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Robust.Shared.Containers;
using Content.Shared.Popups;
using JetBrains.Annotations;
using Robust.Shared.Serialization;
using System.Linq;
using System.Collections.Generic;
using Content.Shared.Access.Systems;
using Content.Shared.Access.Components;
using System;
using Robust.Shared.Prototypes;
using Content.Shared.Roles;
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Mind;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Mind.Components;
using Robust.Shared.Analyzers;

namespace Content.Shared.AWS.Economy.Bank
{
    [Virtual]
    public class EconomyBankAccountSystemShared : EntitySystem
    {
        [Dependency] protected readonly EntityManager _entManager = default!;
        [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
        [Dependency] private readonly ItemSlotsSystem _itemSlotsSystem = default!;
        [Dependency] private readonly SharedUserInterfaceSystem _userInterfaceSystem = default!;
        [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

        private const int MaxAtmHistoryEntries = 10;

        private EntityQuery<ContainerManagerComponent> _containerQuery;
        private bool _containerQueryInitialized;

        public override void Initialize()
        {
            base.Initialize();

            _containerQuery = GetEntityQuery<ContainerManagerComponent>();
            _containerQueryInitialized = true;

            SubscribeLocalEvent<EconomyBankTerminalComponent, ExaminedEvent>(OnBankTerminalExamine);
            SubscribeLocalEvent<EconomyBankTerminalComponent, EconomyTerminalMessage>(OnTerminalMessage);

            SubscribeLocalEvent<EconomyAccountHolderComponent, ExaminedEvent>(OnBankAccountExamine);
            SubscribeLocalEvent<EconomyMoneyHolderComponent, ExaminedEvent>(OnMoneyHolderExamine);

            SubscribeLocalEvent<EconomyBankATMComponent, ComponentInit>(OnATMComponentInit);
            SubscribeLocalEvent<EconomyBankATMComponent, ComponentRemove>(OnATMComponentRemove);
            SubscribeLocalEvent<EconomyBankATMComponent, EntInsertedIntoContainerMessage>(OnATMItemSlotChanged);
            SubscribeLocalEvent<EconomyBankATMComponent, EntRemovedFromContainerMessage>(OnATMItemSlotChanged);

            SubscribeLocalEvent<EconomyManagementConsoleComponent, ComponentInit>(OnManagementConsoleInit);
            SubscribeLocalEvent<EconomyManagementConsoleComponent, ComponentRemove>(OnManagementConsoleRemove);
            SubscribeLocalEvent<EconomyManagementConsoleComponent, EntInsertedIntoContainerMessage>(OnManagementConsoleSlotChanged);
            SubscribeLocalEvent<EconomyManagementConsoleComponent, EntRemovedFromContainerMessage>(OnManagementConsoleEntRemoved);
        }

        /// <summary>
        /// Checks if the account exists (valid).
        /// </summary>
        /// <returns>True if the account exists, false otherwise.</returns>
        [PublicAPI]
        public bool IsValidAccount(string accountID)
        {
            var accounts = GetAccounts(EconomyBankAccountMask.All);
            return accounts.ContainsKey(accountID);
        }

        /// <summary>
        /// Tries to fetch the account with the given ID.
        /// </summary>
        /// <returns>True if the fetching was successful, false otherwise.</returns>
        [PublicAPI]
        public bool TryGetAccount(string accountID, [NotNullWhen(true)] out Entity<EconomyBankAccountComponent>? account)
        {
            var accounts = GetAccounts(EconomyBankAccountMask.All);
            if (accounts.TryGetValue(accountID, out var foundAccount))
            {
                account = foundAccount;
                return true;
            }

            account = null;
            return false;
        }

        /// <summary>
        /// Returns all currently existing accounts.
        /// </summary>
        /// <param name="flag">Filter mask to fetch accounts.</param>
        [PublicAPI]
        public IReadOnlyDictionary<string, Entity<EconomyBankAccountComponent>> GetAccounts(EconomyBankAccountMask flag = EconomyBankAccountMask.NotBlocked, List<BankAccountTag>? accountTags = null)
        {
            var accountsEnum = _entManager.EntityQueryEnumerator<EconomyBankAccountComponent>();
            var result = new Dictionary<string, Entity<EconomyBankAccountComponent>>();

            while (accountsEnum.MoveNext(out var ent, out var comp))
            {
                var shouldAdd = flag switch
                {
                    EconomyBankAccountMask.All => true,
                    EconomyBankAccountMask.NotBlocked => !comp.Blocked,
                    EconomyBankAccountMask.Blocked => comp.Blocked,
                    EconomyBankAccountMask.ByTags => accountTags != null && comp.AccountTags.Any(accountTags.Contains),
                    _ => false
                };

                if (shouldAdd)
                    result.Add(comp.AccountID, (ent, comp));
            }

            return result;
        }

        /// <summary>
        /// Returns JobEntry from salaries prototype.
        /// </summary>
        [PublicAPI]
        public bool TryGetSalaryJobEntry(ProtoId<JobPrototype> jobName, ProtoId<EconomySallariesPrototype> salaries, [NotNullWhen(true)] out EconomySallariesJobEntry? jobEntry)
        {
            jobEntry = null;
            if (!_prototypeManager.TryIndex(salaries, out var salariesPrototype))
                return false;

            if (!salariesPrototype.Jobs.TryGetValue(jobName, out var job))
                return false;

            jobEntry = job;
            return true;
        }

        private void OnBankAccountExamine(Entity<EconomyAccountHolderComponent> entity, ref ExaminedEvent args)
        {
            if (!TryGetAccount(entity.Comp.AccountID, out var accountEntity))
                return;

            var account = accountEntity.Value.Comp;
            args.PushMarkup(Loc.GetString("bankaccount-component-on-examine-detailed-message",
                ("id", account.AccountID)));
            args.PushMarkup(Loc.GetString("moneyholder-component-on-examine-detailed-message",
                ("moneyName", account.AllowedCurrency),
                ("balance", account.Balance)));
        }

        private void OnTerminalMessage(EntityUid uid, EconomyBankTerminalComponent comp, EconomyTerminalMessage args)
        {
            UpdateTerminal((uid, comp), args.Amount, args.Reason);
        }

        private void OnBankTerminalExamine(Entity<EconomyBankTerminalComponent> entity, ref ExaminedEvent args)
        {
            var comp = entity.Comp;
            args.PushMarkup(Loc.GetString("economyBankTerminal-component-on-examine-connected-to",
                ("accountId", comp.LinkedAccount)));

            if (comp.Amount > 0)
            {
                args.PushMarkup(Loc.GetString("economyBankTerminal-component-on-examine-pay-for-ifmorethanzero",
                ("amount", comp.Amount),
                ("currencyName", comp.AllowCurrency)));
            }
            else args.PushMarkup(Loc.GetString("economyBankTerminal-component-on-examine-pay-for-iflessthanzero"));

            if (comp.Reason != string.Empty)
                args.PushMarkup(Loc.GetString("economyBankTerminal-component-on-examine-reason", ("reason", comp.Reason)));
        }
        private void OnMoneyHolderExamine(Entity<EconomyMoneyHolderComponent> entity, ref ExaminedEvent args)
        {
            args.PushMarkup(Loc.GetString("moneyholder-component-on-examine-detailed-message",
                ("moneyName", entity.Comp.AllowCurrency),
                ("balance", entity.Comp.Balance)));
        }
        private void OnATMComponentInit(EntityUid uid, EconomyBankATMComponent atm, ComponentInit args)
        {
            _itemSlotsSystem.AddItemSlot(uid, EconomyBankATMComponent.ATMCardId, atm.CardSlot);

            UpdateATMUserInterface((uid, atm));
        }
        private void OnATMComponentRemove(EntityUid uid, EconomyBankATMComponent atm, ComponentRemove args)
        {
            _itemSlotsSystem.RemoveItemSlot(uid, atm.CardSlot);
        }

        private void OnATMItemSlotChanged(EntityUid uid, EconomyBankATMComponent atm, ContainerModifiedMessage args)
        {
            if (args.Container.ID != atm.CardSlot.ID)
                return;

            UpdateATMUserInterface((uid, atm));
        }

        [PublicAPI]
        public void UpdateATMUserInterface(Entity<EconomyBankATMComponent> entity, string? error = null)
        {
            EconomyBankATMAccountInfo? uiAccount = null;
            var finalError = error;

            if (TryGetATMInsertedAccount(entity, out var accountHolder))
            {
                if (TryBuildAccountInfo(accountHolder.Value.Comp.AccountID, out var info))
                    uiAccount = info;
            }
            else
            {
                finalError = null;
            }

            _userInterfaceSystem.SetUiState(entity.Owner, EconomyBankATMUiKey.Key, new EconomyBankATMUserInterfaceState
            {
                BankAccount = uiAccount,
                Error = finalError,
            });
        }

        private List<EconomyBankAccountLogField> BuildAtmLogSnapshot(List<EconomyBankAccountLogField> source)
        {
            if (source.Count == 0)
                return new();

            var startIndex = Math.Max(0, source.Count - MaxAtmHistoryEntries);
            var result = new List<EconomyBankAccountLogField>(source.Count - startIndex);

            for (var i = source.Count - 1; i >= startIndex; i--)
            {
                result.Add(source[i]);
            }

            return result;
        }

        [PublicAPI]
        public bool TryBuildAccountInfo(EconomyAccountHolderComponent holder, out EconomyBankATMAccountInfo info)
        {
            return TryBuildAccountInfo(holder.AccountID, out info);
        }

        [PublicAPI]
        public bool TryBuildAccountInfo(string accountId, out EconomyBankATMAccountInfo info)
        {
            info = default!;

            if (!TryGetAccount(accountId, out var account))
                return false;

            info = BuildAtmAccountInfo(account.Value.Comp);
            return true;
        }

        private EconomyBankATMAccountInfo BuildAtmAccountInfo(EconomyBankAccountComponent account)
        {
            return new EconomyBankATMAccountInfo
            {
                Balance = account.Balance,
                AccountId = account.AccountID,
                AccountName = account.AccountName,
                Blocked = account.Blocked,
                Logs = BuildAtmLogSnapshot(account.Logs),
            };
        }

        [PublicAPI]
        public bool TryGetATMInsertedAccount(EconomyBankATMComponent atm, [NotNullWhen(true)] out Entity<EconomyAccountHolderComponent>? ent)
        {
            ent = null;

            if (TryComp(atm.CardSlot.Item, out EconomyAccountHolderComponent? bankAccount))
            {
                ent = (atm.CardSlot.Item.Value, bankAccount);
                return true;
            }

            return false;
        }

        [PublicAPI]
        public void UpdateTerminal(Entity<EconomyBankTerminalComponent> entity, ulong amount, string? reason)
        {
            if (amount != 0)
                _popupSystem.PopupPredicted(Loc.GetString("economybanksystem-vending-insertcard"), entity, null);

            entity.Comp.Amount = amount;
            entity.Comp.Reason = reason is null ? string.Empty : reason;

            _entManager.Dirty(entity);
        }

        private void OnManagementConsoleInit(Entity<EconomyManagementConsoleComponent> ent, ref ComponentInit args)
        {
            _itemSlotsSystem.AddItemSlot(ent, EconomyManagementConsoleComponent.ConsoleCardID, ent.Comp.CardSlot);
            _itemSlotsSystem.AddItemSlot(ent, EconomyManagementConsoleComponent.TargetCardID, ent.Comp.TargetCardSlot);

            UpdateManagementConsoleUserInterface(ent, null);
        }

        private void OnManagementConsoleRemove(Entity<EconomyManagementConsoleComponent> ent, ref ComponentRemove args)
        {
            _itemSlotsSystem.RemoveItemSlot(ent, ent.Comp.CardSlot);
            _itemSlotsSystem.RemoveItemSlot(ent, ent.Comp.TargetCardSlot);
        }

        private void OnManagementConsoleSlotChanged(Entity<EconomyManagementConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
        {
            EconomyBankAccountComponent? account = null;
            if (TryComp<EconomyAccountHolderComponent>(ent.Comp.TargetCardSlot.Item, out var holder))
            {
                if (TryGetAccount(holder.AccountID, out var accountEnt))
                    account = accountEnt.Value.Comp;
            }

            UpdateManagementConsoleUserInterface(ent, account);
        }


        private void OnManagementConsoleEntRemoved(Entity<EconomyManagementConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
        {
            // Keep the inserted account info if we took out the ID card.
            EconomyBankAccountComponent? account = null;
            if (args.Container.ID == ent.Comp.CardSlot.ID && TryComp<EconomyAccountHolderComponent>(ent.Comp.TargetCardSlot.Item, out var holder))
            {
                if (TryGetAccount(holder.AccountID, out var accountEnt))
                    account = accountEnt.Value.Comp;
            }

            UpdateManagementConsoleUserInterface(ent, account);
        }

        [PublicAPI]
        public (bool, string?, Entity<EconomyAccountHolderComponent>?) GetManagementConsoleInsertedCardsStateInfo(Entity<EconomyManagementConsoleComponent> ent)
        {
            if (!TryComp<AccessReaderComponent>(ent, out var accessReader))
                return (false, null, null);

            Entity<EconomyAccountHolderComponent>? accountHolder = null;
            if (ent.Comp.TargetCardSlot.Item is { } targetCard && TryComp<EconomyAccountHolderComponent>(targetCard, out var holderComp))
                accountHolder = (targetCard, holderComp);

            var priveleged = false;
            string? idCardName = null;
            if (ent.Comp.CardSlot.Item is { } idCard)
            {
                priveleged = _accessReaderSystem.IsAllowed(idCard, ent, accessReader);
                if (TryComp<IdCardComponent>(idCard, out var idCardComp))
                    idCardName = idCardComp.FullName;
            }

            return (priveleged, idCardName, accountHolder);
        }

        [PublicAPI]
        public void UpdateManagementConsoleUserInterface(Entity<EconomyManagementConsoleComponent> ent, EconomyBankAccountComponent? bankAccount)
        {
            var stateInfo = GetManagementConsoleInsertedCardsStateInfo(ent);
            var netHolder = GetNetEntity(stateInfo.Item3);
            var uiState = new EconomyManagementConsoleUserInterfaceState()
            {
                Priveleged = stateInfo.Item1,
                IDCardName = stateInfo.Item2,
                AccountHolder = netHolder,
                HolderID = stateInfo.Item3?.Comp.AccountID,
                AccountID = bankAccount?.AccountID,
                AccountName = bankAccount?.AccountName,
                Balance = bankAccount?.Balance,
                Penalty = bankAccount?.Penalty,
                Blocked = bankAccount?.Blocked,
                CanReachPayDay = bankAccount?.CanReachPayDay,
                JobName = bankAccount?.JobName,
                Salary = bankAccount?.Salary
            };
            _userInterfaceSystem.SetUiState(ent.Owner, EconomyManagementConsoleUiKey.Key, uiState);
        }

        //[PublicAPI]
        //public List<IEconomyMoneyHolder> GetEntityMoneyHolders(EntityUid entityUid, HolderProccessType type)
        //{
        //    var moneyHolders = new List<IEconomyMoneyHolder>();

        //    if (type.HasFlag(HolderProccessType.AccountHolder))
        //    {

        //    }

        //    if (type.HasFlag(HolderProccessType.MoneyHolder))
        //    {
        //        var proccessEmagged = type.HasFlag(HolderProccessType.Emagged);
        //        var proccessNotEmagged = type.HasFlag(HolderProccessType.NotEmagged);

        //        if (!proccessEmagged && !proccessNotEmagged)
        //            goto skip;
        //    }

        //skip:

        //    return moneyHolders;
        //}

        [PublicAPI]
        public ulong CountHoldMoney(EntityUid entityUid)
        {
            EnsureContainerQuery();

            ulong totalMoney = GetEntityMoney(entityUid);

            if (!_containerQuery.TryGetComponent(entityUid, out var containerManager))
                return totalMoney;

            var containersToProcess = new Stack<ContainerManagerComponent>();
            containersToProcess.Push(containerManager);

            ProcessPulledEntity(entityUid, ref totalMoney, containersToProcess);
            ProcessContainedEntities(containersToProcess, ref totalMoney);

            return totalMoney;
        }

        private void EnsureContainerQuery()
        {
            if (_containerQueryInitialized)
                return;

            _containerQuery = GetEntityQuery<ContainerManagerComponent>();
            _containerQueryInitialized = true;
        }

        private void ProcessPulledEntity(EntityUid entityUid, ref ulong totalMoney, Stack<ContainerManagerComponent> containersToProcess)
        {
            if (!TryComp<PullerComponent>(entityUid, out var puller) || puller.Pulling is not { } pulledEntity)
                return;

            totalMoney += GetEntityMoney(pulledEntity);

            if (!HasComp<MindContainerComponent>(pulledEntity)
                && _containerQuery.TryGetComponent(pulledEntity, out var pulledContainerManager))
            {
                containersToProcess.Push(pulledContainerManager);
            }
        }

        private void ProcessContainedEntities(Stack<ContainerManagerComponent> containersToProcess, ref ulong totalMoney)
        {
            while (containersToProcess.TryPop(out var currentManager))
            {
                if (currentManager?.Containers == null)
                    continue;

                foreach (var container in currentManager.Containers.Values)
                {
                    foreach (var containedEntity in container.ContainedEntities)
                    {
                        totalMoney += GetEntityMoney(containedEntity);

                        if (_containerQuery.TryGetComponent(containedEntity, out var containedContainerManager))
                        {
                            containersToProcess.Push(containedContainerManager);
                        }
                    }
                }
            }
        }

        private ulong GetEntityMoney(EntityUid entity)
        {
            if (TryComp<EconomyAccountHolderComponent>(entity, out var accountHolder)
                && TryGetAccount(accountHolder.AccountID, out var account))
            {
                return account.Value.Comp.Balance <= 0
                    ? 0
                    : (ulong) account.Value.Comp.Balance;
            }

            if (TryComp<EconomyMoneyHolderComponent>(entity, out var moneyHolder))
                return moneyHolder.Balance;

            return 0;
        }

        //[Flags]
        //public enum HolderProccessType
        //{
        //    MoneyHolder,
        //    AccountHolder,
        //    Emagged,
        //    NotEmagged
        //}
    }
}
