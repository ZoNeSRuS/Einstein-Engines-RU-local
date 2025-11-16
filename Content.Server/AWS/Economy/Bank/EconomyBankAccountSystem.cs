using Content.Shared.Interaction;
using Content.Shared.VendingMachines;
using Content.Server.VendingMachines;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Content.Shared.AWS.Economy;
using Content.Server.Popups;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;
using Content.Shared.Access.Components;
using Content.Server.Access.Components;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using System.Diagnostics.CodeAnalysis;
using Content.Server.Station.Systems;
using Robust.Server.GameStates;
using Content.Shared.Store;
using Content.Shared.Access.Systems;
using System.Linq;
using Content.Shared.Roles;
using Content.Shared.AWS.Economy.Bank;
using System.Security.Principal;
using Robust.Shared;
using Content.Server.GameTicking;
using Content.Shared.Mind.Components;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Toolshed.TypeParsers;
using System;
using System.Collections.ObjectModel;
using Content.Shared.NameIdentifier;

namespace Content.Server.AWS.Economy.Bank
{
    public sealed class EconomyBankAccountSystem : EconomyBankAccountSystemShared
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
        [Dependency] private readonly VendingMachineSystem _vendingMachine = default!;
        [Dependency] private readonly INetManager _netManager = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly TransformSystem _transformSystem = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly StationSystem _stationSystem = default!;
        [Dependency] private readonly PvsOverrideSystem _pvsOverrideSystem = default!;
        [Dependency] private readonly MetaDataSystem _metaDataSystem = default!;
        [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;

        private double _salaryMultiplier = 1.0;

        public double SalaryMultiplier => _salaryMultiplier;

#pragma warning disable RA0028
        public override void Initialize()
        {
            SubscribeLocalEvent<EconomyAccountHolderComponent, ComponentInit>(OnAccountComponentInit);
            SubscribeLocalEvent<EconomyBankAccountComponent, ComponentRemove>(OnBankComponentRemove);

            SubscribeLocalEvent<EconomyBankTerminalComponent, InteractUsingEvent>(OnTerminalInteracted);

            SubscribeLocalEvent<EconomyBankATMComponent, GotEmaggedEvent>(OnATMEmagged);
            SubscribeLocalEvent<EconomyBankATMComponent, InteractUsingEvent>(OnATMInteracted);
            SubscribeLocalEvent<EconomyBankATMComponent, EconomyBankATMWithdrawMessage>(OnATMWithdrawMessage);
            SubscribeLocalEvent<EconomyBankATMComponent, EconomyBankATMTransferMessage>(OnATMTransferMessage);

            SubscribeLocalEvent<EconomyManagementConsoleComponent, EconomyManagementConsoleChangeParameterMessage>(OnManagementConsoleParameterMessage);
            SubscribeLocalEvent<EconomyManagementConsoleComponent, EconomyManagementConsoleChangeHolderIDMessage>(OnManagementConsoleChangeHolderIDMessage);
            SubscribeLocalEvent<EconomyManagementConsoleComponent, EconomyManagementConsoleInitAccountOnHolderMessage>(OnManagementConsoleInitAccountOnHolderMessage);
            SubscribeLocalEvent<EconomyManagementConsoleComponent, EconomyManagementConsolePayBonusMessage>(OnManagementConsolePayBonusMessage);
        }

        private ulong ScaleSalary(ulong value)
        {
            var adjusted = Math.Round(value * _salaryMultiplier);
            adjusted = Math.Clamp(adjusted, 0d, ulong.MaxValue);
            return (ulong) adjusted;
        }

        public void ApplySalaryMultiplier(double multiplier)
        {
            if (multiplier < 0)
                multiplier = 0;

            _salaryMultiplier *= multiplier;

            var query = EntityQueryEnumerator<EconomyBankAccountComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.Salary is not { } salary)
                    continue;

                var adjusted = Math.Round(salary * multiplier);
                adjusted = Math.Clamp(adjusted, 0d, ulong.MaxValue);
                comp.Salary = (ulong) adjusted;
                Dirty(uid, comp);
            }
        }

        #region Account management
        /// <summary>
        /// Creates a new bank account entity. If account already exists - fetch it instead, but still return false.
        /// </summary>
        /// <returns>Whether the account was successfully created.</returns>
        [PublicAPI]
        public bool TryCreateAccount(string accountID,
                                     string accountName,
                                     ProtoId<CurrencyPrototype> allowedCurrency,
                                     long balance,
                                     ulong penalty,
                                     bool blocked,
                                     bool canReachPayDay,
                                     List<BankAccountTag>? accountTags,
                                     ProtoId<JobPrototype>? jobName,
                                     ulong? salary,
                                     MapCoordinates? cords,
                                     out Entity<EconomyBankAccountComponent> account)
        {
            // Return if account with this id already exists.
            if (TryGetAccount(accountID, out var foundAccount))
            {
                account = foundAccount.Value;
                return false;
            }

            var spawnCords = cords ?? MapCoordinates.Nullspace;
            var accountEntity = Spawn(null, spawnCords);
            _metaDataSystem.SetEntityName(accountEntity, accountID);
            var accountComp = EnsureComp<EconomyBankAccountComponent>(accountEntity);

            accountComp.AccountID = accountID;
            accountComp.AccountName = accountName;
            accountComp.AllowedCurrency = allowedCurrency;
            accountComp.Balance = balance;
            accountComp.Penalty = penalty;
            accountComp.Blocked = blocked;
            accountComp.CanReachPayDay = canReachPayDay;
            accountComp.AccountTags = accountTags ?? [];
            accountComp.JobName = jobName;
            accountComp.Salary = salary;

            account = (accountEntity, accountComp);
            _pvsOverrideSystem.AddGlobalOverride(accountEntity);
            Dirty(account);

            return true;
        }

        /// <summary>
        /// Enables a card or a bank account (described in setup) for usage.
        /// </summary>
        [PublicAPI]
        public bool TryActivate(Entity<EconomyAccountHolderComponent> entity, [NotNullWhen(true)] out Entity<EconomyBankAccountComponent>? activatedAccount)
        {
            activatedAccount = null;
            if (!_prototypeManager.TryIndex(entity.Comp.AccountIdByProto, out EconomyAccountIdPrototype? proto))
                return false;

            // Setup standard starting values for account details
            var accountID = GenerateAccountId(proto.Prefix, proto.Streak, proto.NumbersPerStreak, proto.Descriptior);
            var accountName = entity.Comp.AccountName;
            var balance = 0L;
            ProtoId<JobPrototype>? jobName = null;
            ulong? salary = null;

            EconomySallariesPrototype? salariesPrototype = null;
            if (_prototypeManager.TryIndex("NanotrasenDefaultSallaries", out EconomySallariesPrototype? indexedPrototype))
                salariesPrototype = indexedPrototype;

            if (TryComp<IdCardComponent>(entity, out var idCardComponent))
                accountName = idCardComponent.FullName ?? entity.Comp.AccountName;

            if (TryComp<PresetIdCardComponent>(entity, out var presetIdCardComponent) &&
                presetIdCardComponent.JobName is { } job &&
                TryGetSalaryJobEntry(job, "NanotrasenDefaultSallaries", out var jobEntry))
            {
                jobName = job;
                var coefficient = salariesPrototype?.Coef.Next(_random) ?? 100;
                var randomizedSalary = (ulong) Math.Round(jobEntry.Value.Sallary * (coefficient / 100d));
                salary = ScaleSalary(randomizedSalary);
                balance = (long) Math.Round(jobEntry.Value.StartMoney * _random.NextDouble(0.5, 1.5));
            }

            var station = _stationSystem.GetOwningStation(entity);
            var cords = station != null ? _transformSystem.GetMapCoordinates(station.Value) : MapCoordinates.Nullspace;

            // Setup values are always coming first if they can
            var accountSetup = entity.Comp.AccountSetup;
            accountID = accountSetup.GenerateAccountID || accountSetup.AccountID is null
                ? accountID
                : accountSetup.AccountID;
            accountName = accountSetup.AccountName ?? accountName;
            balance = accountSetup.Balance ?? balance;

            TryCreateAccount(
                accountID,
                accountName,
                accountSetup.AllowedCurrency ?? "Thaler",
                balance,
                accountSetup.Penalty ?? 0,
                accountSetup.Blocked ?? false,
                accountSetup.CanReachPayDay ?? true,
                accountSetup.AccountTags ?? [],
                jobName,
                salary,
                cords,
                out var account);

            activatedAccount = account;
            entity.Comp.AccountID = accountID;
            entity.Comp.AccountName = accountName;
            Dirty(entity);
            return true;
        }

        /// <summary>
        /// Tries to set the parameter chosen in arguments to a given value.
        /// </summary>
        /// <param name="param">Parameter to be changed.</param>
        /// <param name="value">New value of the parameter, must be of the same type as a changed value.</param>
        [PublicAPI]
        public bool TrySetAccountParameter(string accountID, EconomyBankAccountParam param, object value)
        {
            if (!TryGetAccount(accountID, out var entity))
                return false;

            if (accountID == "NT-CentCom")
                return false;

            var account = entity.Value.Comp;
            switch (param)
            {
                case EconomyBankAccountParam.AccountName:
                    if (value is not string name)
                        return false;
                    account.AccountName = name;
                    break;
                case EconomyBankAccountParam.Blocked:
                    if (value is not bool blocked)
                        return false;
                    account.Blocked = blocked;
                    break;
                case EconomyBankAccountParam.CanReachPayDay:
                    if (value is not bool canReachPayDay)
                        return false;
                    account.CanReachPayDay = canReachPayDay;
                    break;
                case EconomyBankAccountParam.JobName:
                    if (value is not string jobName)
                        return false;
                    account.JobName = jobName;
                    break;
                case EconomyBankAccountParam.Salary:
                    if (value is not ulong salary)
                        return false;
                    account.Salary = salary;
                    break;
                default:
                    return false;
            }

            Dirty(entity.Value);
            return true;
        }

        [PublicAPI]
        public bool TryWithdraw(Entity<EconomyAccountHolderComponent> accountHolder, EntityUid atmUid, EconomyBankATMComponent atm, ulong sum, [NotNullWhen(false)] out string? errorMessage)
        {
            errorMessage = null;
            if (!TryGetAccount(accountHolder.Comp.AccountID, out var account))
            {
                errorMessage = Loc.GetString("economybanksystem-transaction-error-notfoundaccout");
                return false;
            }

            if (account.Value.Comp.Blocked)
            {
                errorMessage = Loc.GetString("economybanksystem-transaction-error-account-blocked", ("accountId", account.Value.Comp.AccountID));
                return false;
            }

            if (sum > 0 &&
                sum <= long.MaxValue &&
                account.Value.Comp.Balance >= (long) sum)
            {
                Withdraw(accountHolder, atmUid, atm, sum);
                return true;
            }
            errorMessage = Loc.GetString("economybanksystem-transaction-error-notenoughmoney");
            return false;
        }

        [PublicAPI]
        public Entity<EconomyMoneyHolderComponent> DropMoneyHolder(EntProtoId<EconomyMoneyHolderComponent> entId, ulong amount, MapCoordinates pos)
        {
            var entity = Spawn(entId, pos);
            var comp = Comp<EconomyMoneyHolderComponent>(entity);

            comp.Balance = amount;

            Dirty(entity, comp);
            return (entity, comp);
        }

        [PublicAPI]
        public bool TrySendMoney(EntityUid fromHolderUid, EconomyMoneyHolderComponent fromHolder, Entity<EconomyBankAccountComponent> recipientAccount, ulong amount, [NotNullWhen(false)] out string? errorMessage, string? reason = null)
        {
            errorMessage = null;

            if (recipientAccount.Comp.Blocked)
            {
                errorMessage = Loc.GetString("economybanksystem-transaction-error-account-blocked", ("accountId", recipientAccount.Comp.AccountID));
                return false;
            }

            // if this is a fake money holder
            if (fromHolder.Emagged)
            {
                var holderId = GetMoneyHolderIdentifier(fromHolderUid, fromHolder);
                var deducted = Math.Min(fromHolder.Balance, amount);
                if (deducted > 0)
                    fromHolder.Balance -= deducted;

                errorMessage = Loc.GetString("economybanksystem-log-terminal-error", ("holderId", holderId));

                TryAddLog(recipientAccount,
                   new EconomyBankAccountLogField(_gameTiming.CurTime, errorMessage));

                Dirty(fromHolderUid, fromHolder);
                return false;
            }

            return TryTransferMoney(fromHolderUid, fromHolder, recipientAccount, amount, reason);
        }

        [PublicAPI]
        public bool TrySendMoney(EntityUid fromHolderUid, EconomyMoneyHolderComponent fromHolder, Entity<EconomyAccountHolderComponent> recipientAccountHolder, ulong amount, [NotNullWhen(false)] out string? errorMessage, string? reason = null)
        {
            errorMessage = null;

            if (fromHolder.Balance < amount)
            {
                errorMessage = Loc.GetString("economybanksystem-transaction-error-notenoughmoney");
                return false;
            }

            if (!TryGetAccount(recipientAccountHolder.Comp.AccountID, out var recipientAccount))
            {
                errorMessage = Loc.GetString("economybanksystem-transaction-error-notfoundaccout", ("accountId", recipientAccountHolder.Comp.AccountID));
                return false;
            }

            return TrySendMoney(fromHolderUid, fromHolder, recipientAccount.Value, amount, out errorMessage, reason);
        }

        [PublicAPI]
        public bool TrySendMoney(EntityUid fromHolderUid, EconomyMoneyHolderComponent fromHolder, string recipientAccountId, ulong amount, [NotNullWhen(false)] out string? errorMessage, string? reason = null)
        {
            errorMessage = null;

            if (!TryGetAccount(recipientAccountId, out var account))
            {
                errorMessage = Loc.GetString("economybanksystem-transaction-error-notfoundaccout", ("accountId", recipientAccountId));
                return false;
            }

            return TrySendMoney(fromHolderUid, fromHolder, account.Value, amount, out errorMessage, reason);
        }

        [PublicAPI]
        public bool TrySendMoney(Entity<EconomyBankAccountComponent> fromAccount, Entity<EconomyBankAccountComponent> recipientAccount, ulong amount, string? reason, [NotNullWhen(false)] out string? errorMessage, bool allowOverdraw = false)
        {
            errorMessage = null;

            if (fromAccount.Comp.Blocked || recipientAccount.Comp.Blocked)
            {
                var blockedAccountID = fromAccount.Comp.Blocked ? fromAccount.Comp.AccountID : recipientAccount.Comp.AccountID;
                errorMessage = Loc.GetString("economybanksystem-transaction-error-account-blocked", ("accountId", blockedAccountID));
                return false;
            }

            if (!allowOverdraw)
            {
                if (fromAccount.Comp.Balance < 0 || (ulong) fromAccount.Comp.Balance < amount)
                {
                    errorMessage = Loc.GetString("economybanksystem-transaction-error-notenoughmoney");
                    return false;
                }
            }

            return TryTransferMoney(fromAccount, recipientAccount, amount, reason, allowOverdraw);
        }

        [PublicAPI]
        public bool TrySendMoney(EconomyAccountHolderComponent fromAccountHolder, EconomyAccountHolderComponent recipientAccountHolder, ulong amount, string? reason, [NotNullWhen(false)] out string? errorMessage)
        {
            errorMessage = null;

            return TrySendMoney(fromAccountHolder.AccountID, recipientAccountHolder.AccountID, amount, reason, out errorMessage);
        }

        [PublicAPI]
        public bool TrySendMoney(EconomyAccountHolderComponent fromAccountHolder, string recipientAccountId, ulong amount, string? reason, [NotNullWhen(false)] out string? errorMessage)
        {
            errorMessage = null;

            return TrySendMoney(fromAccountHolder.AccountID, recipientAccountId, amount, reason, out errorMessage);
        }

        private (Entity<EconomyBankAccountComponent>?, Entity<EconomyBankAccountComponent>?) GetAccountsById(string id, string id2)
        {
            TryGetAccount(id, out var firstAccount);
            TryGetAccount(id2, out var secondAccount);

            return (firstAccount, secondAccount);
        }

        [PublicAPI]
        public bool TrySendMoney(string fromAccountId, string recipientAccountId, ulong amount, string? reason, [NotNullWhen(false)] out string? errorMessage)
        {
            errorMessage = null;

            var (fromBankAccount, recipientAccount) = GetAccountsById(fromAccountId, recipientAccountId);

            if (fromBankAccount is null)
            {
                errorMessage = Loc.GetString("economybanksystem-transaction-error-notfoundaccout", ("accountId", fromAccountId));
                return false;
            }

            if (recipientAccount is null)
            {
                errorMessage = Loc.GetString("economybanksystem-transaction-error-notfoundaccout", ("accountId", recipientAccountId));
                return false;
            }

            return TrySendMoney(fromBankAccount.Value, recipientAccount.Value, amount, reason, out errorMessage);
        }

        /// <summary>
        /// Adds a new log to the account.
        /// </summary>
        [PublicAPI]
        public bool TryAddLog(string accountID, EconomyBankAccountLogField log)
        {
            if (!TryGetAccount(accountID, out var account))
                return false;

            return TryAddLog(account.Value, log);
        }

        /// <summary>
        /// Adds a new log to the account.
        /// </summary>
        [PublicAPI]
        public bool TryAddLog(Entity<EconomyBankAccountComponent> account, EconomyBankAccountLogField log)
        {
            account.Comp.Logs.Add(log);
            Dirty(account);

            return true;
        }

        /// <summary>
        /// Changes the balance of the account by the provided delta.
        /// </summary>
        /// <param name="delta">Positive value credits the account, negative debits it.</param>
        /// <param name="logMessage">Optional log entry that will be appended on success.</param>
        [PublicAPI]
        public bool TryChangeAccountBalance(string accountID, long delta, string? logMessage = null)
        {
            if (delta == 0)
                return true;

            if (delta == long.MinValue)
                return false;

            if (!TryGetAccount(accountID, out var account))
                return false;

            var addition = delta > 0;
            var amount = addition ? (ulong) delta : (ulong) (-delta);

            if (!TryChangeAccountBalance(accountID, amount, addition))
                return false;

            Dirty(account.Value);

            if (!string.IsNullOrEmpty(logMessage))
                TryAddLog(account.Value, new EconomyBankAccountLogField(_gameTiming.CurTime, logMessage));

            return true;
        }

        private bool TryChangeAccountBalance(string accountID, ulong amount, bool addition = true, bool allowOverdraw = false)
        {
            if (!TryGetAccount(accountID, out var entity))
                return false;

            if (amount == 0)
                return true;

            if (amount > long.MaxValue)
                return false;

            var signedAmount = (long) amount;
            var account = entity.Value.Comp;

            if (!addition)
            {
                if (!allowOverdraw)
                {
                    if (account.Balance < 0 || (ulong) account.Balance < amount)
                        return false;
                }
                else if (account.Balance < long.MinValue + signedAmount)
                {
                    return false;
                }

                account.Balance -= signedAmount;
            }
            else
            {
                if (account.Balance > long.MaxValue - signedAmount)
                    return false;

                account.Balance += signedAmount;
            }

            Dirty(entity.Value);
            RefreshInsertedAtmUis(accountID);
            return true;
        }

        /// <summary>
        /// Transfer money from one account to another (with logs).
        /// </summary>
        /// <returns>True if the transfer was successful, false otherwise.</returns>
        private bool TryTransferMoney(string senderID, string receiverID, ulong amount, string? reason = null)
        {
            if (!TryGetAccount(senderID, out var senderEntity) ||
                !TryGetAccount(receiverID, out var receiverEntity))
                return false;

            return TryTransferMoney(senderEntity.Value, receiverEntity.Value, amount, reason);
        }

        private bool TryTransferMoney(EntityUid moneyHolderUid, EconomyMoneyHolderComponent moneyHolder, Entity<EconomyBankAccountComponent> receiverEntity, ulong amount, string? reason = null)
        {
            if (amount <= 0)
                return false;

            var receiver = receiverEntity.Comp;

            if (moneyHolder.Balance < amount)
                return false;

            if (amount > long.MaxValue || receiver.Balance > long.MaxValue - (long) amount)
                return false;

            moneyHolder.Balance -= amount;
            receiver.Balance += (long) amount;

            var holderId = GetMoneyHolderIdentifier(moneyHolderUid, moneyHolder);
            var receiverLog = Loc.GetString("economybanksystem-log-insert-holder",
                ("amount", amount), ("currencyName", receiver.AllowedCurrency), ("holderId", holderId));
            if (reason != null)
                receiverLog += $" {reason}";

            receiver.Logs.Add(new(_gameTiming.CurTime, receiverLog));

            Dirty(moneyHolderUid, moneyHolder);
            Dirty(receiverEntity);
            RefreshInsertedAtmUis(receiver.AccountID);
            return true;
        }

        private bool TryTransferMoney(Entity<EconomyBankAccountComponent> senderEntity, Entity<EconomyBankAccountComponent> receiverEntity, ulong amount, string? reason = null, bool allowOverdraw = false)
        {
            if (amount <= 0)
                return false;

            if (amount > long.MaxValue)
                return false;

            var signedAmount = (long) amount;
            var sender = senderEntity.Comp;
            var receiver = receiverEntity.Comp;

            if (!allowOverdraw)
            {
                if (sender.Balance < 0 || (ulong) sender.Balance < amount)
                    return false;
            }
            else if (sender.Balance < long.MinValue + signedAmount)
            {
                return false;
            }

            if (receiver.Balance > long.MaxValue - signedAmount)
                return false;

            sender.Balance -= signedAmount;
            receiver.Balance += signedAmount;

            var senderLog = Loc.GetString("economybanksystem-log-send-to",
                        ("amount", amount), ("currencyName", receiver.AllowedCurrency), ("accountId", receiver.AccountID));
            var receiverLog = Loc.GetString("economybanksystem-log-send-from",
                        ("amount", amount), ("currencyName", receiver.AllowedCurrency), ("accountId", sender.AccountID));
            if (reason != null)
            {
                senderLog += $" {reason}";
                receiverLog += $" {reason}";
            }
            sender.Logs.Add(new(_gameTiming.CurTime, senderLog));
            receiver.Logs.Add(new(_gameTiming.CurTime, receiverLog));

            Dirty(senderEntity);
            Dirty(receiverEntity);
            RefreshInsertedAtmUis(sender.AccountID);
            RefreshInsertedAtmUis(receiver.AccountID);
            return true;
        }

        [PublicAPI]
        public bool TryForceDebit(string accountID, ulong amount, string? logMessage = null)
        {
            if (amount == 0)
                return true;

            if (!TryChangeAccountBalance(accountID, amount, addition: false, allowOverdraw: true))
                return false;

            if (!string.IsNullOrEmpty(logMessage) && TryGetAccount(accountID, out var account))
                TryAddLog(account.Value, new EconomyBankAccountLogField(_gameTiming.CurTime, logMessage));

            return true;
        }

        private void Withdraw(Entity<EconomyAccountHolderComponent> accountHolder, EntityUid atmUid, EconomyBankATMComponent atm, ulong sum)
        {
            ref var component = ref accountHolder.Comp;

            if (!TryChangeAccountBalance(component.AccountID, sum, false))
                return;

            var pos = _transformSystem.GetMapCoordinates(atmUid);
            DropMoneyHolder(component.MoneyHolderEntId, sum, pos);

            if (TryGetAccount(accountHolder.Comp.AccountID, out var account))
            {
                var log = new EconomyBankAccountLogField(_gameTiming.CurTime, Loc.GetString("economybanksystem-log-withdraw",
                ("amount", sum), ("currencyName", account.Value.Comp.AllowedCurrency)));
                account.Value.Comp.Logs.Add(log);
                Dirty(account.Value);
            }

            Dirty(accountHolder);
        }

        private void Withdraw(string accountID, EntityUid ent, ulong sum)
        {
            if (!TryChangeAccountBalance(accountID, sum, false))
                return;

            var pos = _transformSystem.GetMapCoordinates(ent);
            DropMoneyHolder("ThalerHolder", sum, pos); // hardcoded for now

            if (TryGetAccount(accountID, out var account))
            {
                var log = new EconomyBankAccountLogField(_gameTiming.CurTime, Loc.GetString("economybanksystem-log-withdraw",
                ("amount", sum), ("currencyName", account.Value.Comp.AllowedCurrency)));
                account.Value.Comp.Logs.Add(log);
                Dirty(account.Value);
            }
        }
        #endregion

        private string GenerateAccountId(string prefix, uint strik, uint numbersPerStrik, string? descriptor)
        {
            var res = prefix;

            for (int i = 0; i < strik; i++)
            {
                string formedStrik = "";

                for (int num = 0; num < numbersPerStrik; num++)
                {
                    formedStrik += _random.Next(0, 10);
                }

                res = res.Length == 0 ? formedStrik : res + descriptor + formedStrik;
            }

            return res;
        }

        private void OnAccountComponentInit(Entity<EconomyAccountHolderComponent> entity, ref ComponentInit args)
        {
            // if has id card comp, then it will be initialized in the other place.
            if (entity.Comp.AccountSetup is null || HasComp<IdCardComponent>(entity))
                return;

            TryActivate(entity, out _);
        }

        private void OnBankComponentRemove(Entity<EconomyBankAccountComponent> entity, ref ComponentRemove args)
        {
            _pvsOverrideSystem.RemoveGlobalOverride(entity);
        }

        private void OnATMWithdrawMessage(EntityUid uid, EconomyBankATMComponent atm, EconomyBankATMWithdrawMessage args)
        {
            if (!TryGetATMInsertedAccount(atm, out var bankAccount))
                return;

            string? error;

            TryWithdraw(bankAccount.Value, uid, atm, args.Amount, out error);
            UpdateATMUserInterface((uid, atm), error);
        }

        private void OnATMTransferMessage(EntityUid uid, EconomyBankATMComponent atm, EconomyBankATMTransferMessage args)
        {
            if (!TryGetATMInsertedAccount(atm, out var bankAccount))
                return;

            string? error;

            TrySendMoney(bankAccount, args.RecipientAccountId, args.Amount, null, out error);
            UpdateATMUserInterface((uid, atm), error);
        }

        private void OnATMEmagged(EntityUid uid, EconomyBankATMComponent component, ref GotEmaggedEvent args)
        {
            if (HasComp<EmaggedComponent>(uid) || args.Handled)
                return;

            var listMoney = component.EmagDropMoneyValues;
            var listMoneyCount = listMoney.Count;

            if (listMoneyCount == 0)
                return;

            if (component.EmagDropMoneyHolderRandomCount == 0)
                return;

            var moneyHolderCount = _random.Next(1, component.EmagDropMoneyHolderRandomCount + 1);
            var mapPos = _transformSystem.GetMapCoordinates(uid);

            for (int i = 0; i < moneyHolderCount; i++)
            {
                var droppedEnt = DropMoneyHolder(component.MoneyHolderEntId,
                    listMoney[_random.Next(0, listMoneyCount)], mapPos);
                droppedEnt.Comp.Emagged = true;
            }

            _audioSystem.PlayPvs(component.EmagSound, uid);
            args.Handled = true;
        }

        private string GetMoneyHolderIdentifier(EntityUid uid, EconomyMoneyHolderComponent component)
        {
            if (TryComp<NameIdentifierComponent>(uid, out var nameIdentifier))
                return $"Th-{nameIdentifier.Identifier:0000}";

            return "Th-????";
        }

        private void OnATMInteracted(EntityUid uid, EconomyBankATMComponent component, InteractUsingEvent args)
        {
            var usedEnt = args.Used;

            if (!TryComp<EconomyMoneyHolderComponent>(usedEnt, out var economyMoneyHolderComponent))
                return;

            args.Handled = true;

            var amount = economyMoneyHolderComponent.Balance;
            if (!TryGetATMInsertedAccount(component, out var insertedAccountHolder))
                return;

            string? error = null;
            var success = TrySendMoney(usedEnt, economyMoneyHolderComponent, insertedAccountHolder.Value, amount, out error);

            if (success)
            {
                if (_netManager.IsServer)
                    _popupSystem.PopupEntity(Loc.GetString("economybanksystem-atm-moneyentering"), uid, type: PopupType.Medium);

                QueueDel(usedEnt);
            }
            else
            {
                if (_netManager.IsServer && !string.IsNullOrEmpty(error))
                    _popupSystem.PopupEntity(error, uid, type: PopupType.Medium);

                if (economyMoneyHolderComponent.Emagged)
                    QueueDel(usedEnt);
            }

            UpdateATMUserInterface((uid, component), error);
        }

        private void OnTerminalInteracted(EntityUid uid, EconomyBankTerminalComponent component, InteractUsingEvent args)
        {
            var amount = component.Amount;
            var usedEnt = args.Used;

            if (amount <= 0)
                return;

            if (!TryComp<EconomyMoneyHolderComponent>(usedEnt, out var economyMoneyHolderComponent) &
                !TryComp<EconomyAccountHolderComponent>(usedEnt, out var economyBankAccountHolderComponent))
                return;

            if (!TryGetAccount(component.LinkedAccount, out var receiverAccount))
            {
                var error = Loc.GetString("economyBankTerminal-component-vending-error-no-account");
                _popupSystem.PopupEntity(error, uid, type: PopupType.MediumCaution);
                return;
            }

            string? purchaseReason = null;
            VendingMachineComponent? vendingMachineComponent = null;

            if (TryComp<VendingMachineComponent>(uid, out var machineComponent))
            {
                vendingMachineComponent = machineComponent;
                if (!TryBuildVendingPurchaseReason(uid, machineComponent, out purchaseReason, out var vendingError))
                {
                    var error = vendingError ?? Loc.GetString("economyBankTerminal-component-vending-error");
                    _popupSystem.PopupEntity(error, uid, type: PopupType.MediumCaution);
                    return;
                }
            }

            if (economyMoneyHolderComponent is not null)
            {
                if (!TrySendMoney(usedEnt, economyMoneyHolderComponent, component.LinkedAccount, amount, out var err, purchaseReason))
                {
                    //SS14RU - start
                    TriggerVendingPaymentFailEffect(uid, vendingMachineComponent);
                    //SS14RU - end
                    _popupSystem.PopupEntity(err, uid, type: PopupType.MediumCaution);
                    return;
                }
            }
            else if (economyBankAccountHolderComponent is not null)
            {
                if (!TrySendMoney(economyBankAccountHolderComponent, component.LinkedAccount, amount, purchaseReason, out var err))
                {
                    //SS14RU - start
                    TriggerVendingPaymentFailEffect(uid, vendingMachineComponent);
                    //SS14RU - end
                    _popupSystem.PopupEntity(err, uid, type: PopupType.MediumCaution);
                    return;
                }
            }

            UpdateTerminal((uid, component), 0, string.Empty);

            if (vendingMachineComponent is not null)
            {
                if (!TryTransactionFromVendingMachine(uid, args.User, vendingMachineComponent, out _))
                {
                    Withdraw(receiverAccount.Value.Comp.AccountID, uid, amount);
                    var error = Loc.GetString("economyBankTerminal-component-vending-error");
                    _popupSystem.PopupEntity(error, uid, type: PopupType.MediumCaution);
                    return;
                }
            }

            _popupSystem.PopupEntity(Loc.GetString("economybanksystem-transaction-success", ("amount", amount), ("currencyName", receiverAccount.Value.Comp.AllowedCurrency)), uid, type: PopupType.Medium);
        }

        //SS14RU - start
        private void TriggerVendingPaymentFailEffect(EntityUid uid, VendingMachineComponent? vendingMachine)
        {
            if (vendingMachine is null)
                return;

            _vendingMachine.Deny(uid, vendingMachine);
        }
        //SS14RU - end


        private bool TryBuildVendingPurchaseReason(EntityUid uid, VendingMachineComponent vendingMachine, [NotNullWhen(true)] out string? reason, out string? error)
        {
            reason = null;
            error = null;

            if (vendingMachine.SelectedItemId is not { } selectedItemId)
            {
                error = Loc.GetString("economyBankTerminal-component-vending-error");
                return false;
            }

            if (!vendingMachine.Inventory.TryGetValue(selectedItemId, out var entry) || entry.Price <= 0)
            {
                error = Loc.GetString("economyBankTerminal-component-vending-error");
                return false;
            }

            if (!_prototypeManager.TryIndex(selectedItemId, out var proto))
            {
                error = Loc.GetString("economyBankTerminal-component-vending-error");
                return false;
            }

            if (TryComp<NameIdentifierComponent>(uid, out var nameIdentifierComponent))
            {
                reason = Loc.GetString("economybanksystem-log-reason-purchase-entname",
                    ("itemName", proto.Name), ("entName", nameIdentifierComponent.FullIdentifier));
            }
            else
            {
                reason = Loc.GetString("economybanksystem-log-reason-purchase",
                    ("itemName", proto.Name));
            }

            return true;
        }

        private bool TryTransactionFromVendingMachine(EntityUid uid, EntityUid user, VendingMachineComponent vendingMachine, [NotNullWhen(true)] out string? itemName)
        {
            itemName = null;
            if (vendingMachine.SelectedItemId is not { } selectedItemID)
                return false;

            if (!vendingMachine.Inventory.TryGetValue(selectedItemID, out var entry) || entry.Price <= 0)
                return false;

            itemName = vendingMachine.SelectedItemId;
            vendingMachine.SelectedItemId = null;
            _vendingMachine.AuthorizedVend(uid, user, vendingMachine.SelectedItemInventoryType, selectedItemID, vendingMachine);
            return !vendingMachine.Denying;
        }

        private void OnManagementConsoleChangeHolderIDMessage(Entity<EconomyManagementConsoleComponent> ent, ref EconomyManagementConsoleChangeHolderIDMessage args)
        {
            if (!TryComp<AccessReaderComponent>(ent, out var accessReader) || ent.Comp.CardSlot.Item is not { } idCard)
                return;

            // Check for privileges
            if (!_accessReaderSystem.IsAllowed(idCard, ent.Owner, accessReader))
                return;

            var holder = GetEntity(args.AccountHolder);
            if (!TryComp<EconomyAccountHolderComponent>(holder, out var holderComp))
                return;

            // Change the holder ID
            if (!TryGetAccount(args.NewID, out var account))
                return;

            holderComp.AccountID = account.Value.Comp.AccountID;
            holderComp.AccountName = account.Value.Comp.AccountName;
            Dirty(holder, holderComp);
            UpdateManagementConsoleUserInterface(ent, account.Value.Comp);
        }

        private void OnManagementConsoleInitAccountOnHolderMessage(Entity<EconomyManagementConsoleComponent> ent, ref EconomyManagementConsoleInitAccountOnHolderMessage args)
        {
            if (!TryComp<AccessReaderComponent>(ent, out var accessReader) || ent.Comp.CardSlot.Item is not { } idCard)
                return;

            // Check for privileges
            if (!_accessReaderSystem.IsAllowed(idCard, ent.Owner, accessReader))
                return;

            // Initialize account on holder
            var holder = GetEntity(args.AccountHolder);

            if (!TryComp<EconomyAccountHolderComponent>(holder, out var holderComp) || !TryActivate((holder, holderComp), out var account))
                return;

            holderComp.AccountID = account.Value.Comp.AccountID;
            holderComp.AccountName = account.Value.Comp.AccountName;
            Dirty(holder, holderComp);
            UpdateManagementConsoleUserInterface(ent, account.Value.Comp);
        }

        private void OnManagementConsoleParameterMessage(Entity<EconomyManagementConsoleComponent> ent, ref EconomyManagementConsoleChangeParameterMessage args)
        {
            if (!TryComp<AccessReaderComponent>(ent, out var accessReader) || ent.Comp.CardSlot.Item is not { } idCard)
                return;

            // Check for priveleges
            if (!_accessReaderSystem.IsAllowed(idCard, ent.Owner, accessReader))
                return;

            if (!TryGetAccount(args.AccountID, out var account))
                return;

            TrySetAccountParameter(args.AccountID, args.Param, args.Value);
            UpdateManagementConsoleUserInterface(ent, account.Value.Comp);
        }

        private void OnManagementConsolePayBonusMessage(Entity<EconomyManagementConsoleComponent> ent, ref EconomyManagementConsolePayBonusMessage args)
        {
            // Check for priveleges
            if (!TryComp<AccessReaderComponent>(ent, out var accessReader) || ent.Comp.CardSlot.Item is not { } idCard)
                return;

            if (!_accessReaderSystem.IsAllowed(idCard, ent.Owner, accessReader))
                return;

            // Validate accounts and operation itself
            if (!TryGetAccount(args.Payer, out var payerAccount))
                return;

            var accounts = GetAccounts(EconomyBankAccountMask.ByTags, new List<BankAccountTag> { BankAccountTag.Personal });
            var accountList = args.Accounts;
            var intersectedAccounts = accounts.Where(account => accountList.Contains(account.Value.Comp.AccountID)).GetEnumerator();

            Dictionary<Entity<EconomyBankAccountComponent>, ulong> accountsToPay = new();
            ulong total = 0;
            while (intersectedAccounts.MoveNext())
            {
                var account = intersectedAccounts.Current.Value.Comp;
                if (account.Salary is null)
                    continue;

                var bonus = (ulong) (account.Salary * args.BonusPercent);
                total += bonus;
                accountsToPay.Add(intersectedAccounts.Current.Value, bonus);
            }

            var payerBalance = payerAccount.Value.Comp.Balance;
            if (payerBalance < 0 || (ulong) payerBalance < total)
                return;

            // Proceed to payment
            var reason = Loc.GetString("economybanksystem-log-reason-bonus");
            foreach (var kvp in accountsToPay)
            {
                var account = kvp.Key.Comp;
                var bonus = kvp.Value;

                TrySendMoney(payerAccount.Value.Comp.AccountID, account.AccountID, bonus, reason, out _);
            }

            UpdateManagementConsoleUserInterface(ent, null);
        }

        [PublicAPI]
        private EconomySallaryInfo? PaySalaries(ProtoId<EconomySallariesPrototype> salaryProto,
            EconomyPayDayRuleType type = EconomyPayDayRuleType.Adding)
        {
            if (!_prototypeManager.TryIndex(salaryProto, out var sallariesProto))
                return null;

            if (!TryGetAccount(sallariesProto.PayerAccountId, out var payerAccount))
                return null;

            var accounts = GetAccounts();
            var enumerator = AllEntityQuery<EconomyBankAccountComponent>();

            ulong decremedSum = 0;
            ulong payedSum = 0;

            List<EconomyBankAccountComponent> affectedAccounts = new();
            List<EconomyBankAccountComponent> unableProccess = new();
            List<EconomyBankAccountComponent> blockedAccounts = new();

            foreach (var (_, accountEntity) in accounts)
            {
                var account = accountEntity.Comp;

                if (account.Blocked || !account.CanReachPayDay)
                {
                    unableProccess.Add(account);
                    continue;
                }

                EconomySallariesJobEntry? entry = null;

                foreach (var item in sallariesProto.Jobs)
                {
                    if (item.Key.Id == accountEntity.Comp.JobName)
                        entry = item.Value;
                }

                if (entry is null)
                {
                    unableProccess.Add(account);
                    continue;
                }

                ulong sallary = (ulong) sallariesProto.Coef.Next(_random) * entry.Value.Sallary / 100;
                string? err;
                var reason = Loc.GetString("economybanksystem-log-reason-payday");

                switch (type)
                {
                    case EconomyPayDayRuleType.Adding:
                        if (TrySendMoney(payerAccount.Value.Comp.AccountID, account.AccountID, sallary, reason, out err))
                        {
                            affectedAccounts.Add(account);
                            payedSum += sallary;
                        }
                        break;
                    case EconomyPayDayRuleType.Decrementing:
                        if (!TrySendMoney(account.AccountID, payerAccount.Value.Comp.AccountID, sallary, reason, out err))
                        {
                            if (TrySetAccountParameter(account.AccountID, EconomyBankAccountParam.Blocked, true))
                                blockedAccounts.Add(accountEntity);

                            unableProccess.Add(account);
                            break;
                        }

                        decremedSum += sallary;
                        break;
                    default:
                        break;
                }
            }

            return new(payedSum, decremedSum, affectedAccounts, unableProccess, blockedAccounts);

            //notify that we blocked, or we cant proccess any payment
        }

        private record EconomySallaryInfo(
            ulong AddedSum,
            ulong DecremedSum,
            List<EconomyBankAccountComponent> AffectedAccounts,
            List<EconomyBankAccountComponent> UnableProccess,
            List<EconomyBankAccountComponent> WereBlockedInProccess);

        private void RefreshInsertedAtmUis(string accountId)
        {
            var query = EntityQueryEnumerator<EconomyBankATMComponent>();
            while (query.MoveNext(out var atmUid, out var atm))
            {
                if (atm.CardSlot.Item is not { } card)
                    continue;

                if (!TryComp<EconomyAccountHolderComponent>(card, out var holder))
                    continue;

                if (holder.AccountID != accountId)
                    continue;

                UpdateATMUserInterface((atmUid, atm));
            }
        }
    }
}
