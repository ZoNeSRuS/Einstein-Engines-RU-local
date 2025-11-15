using System.Collections.Generic;
using Content.Server.AWS.Economy.Bank;
using Content.Server.AWS.Economy.CargoBridge;
using Content.Server.Cargo.Components;
using Content.Shared.AWS.Economy.Cargo;
using Content.Shared.Cargo.BUI;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    [Dependency] private readonly EconomyBankAccountSystem _economyBankAccount = default!;

    private EntityQuery<EconomyThalerCargoComponent> _cargoAccountQuery = default!;
    private ISawmill _awsBridgeSawmill = default!;
    private readonly HashSet<EntityUid> _pendingAwsAccountSync = new();

    partial void InitializeAwsBridge()
    {
        _awsBridgeSawmill = Logger.GetSawmill("cargo.aws_bridge");
        _cargoAccountQuery = GetEntityQuery<EconomyThalerCargoComponent>();

        SubscribeLocalEvent<StationBankAccountComponent, ComponentStartup>(OnStationBankStartup);
        SubscribeLocalEvent<EconomyThalerCargoComponent, ComponentStartup>(OnCargoAccountStartup);
        SubscribeLocalEvent<EconomyThalerCargoComponent, ComponentShutdown>(OnCargoAccountShutdown);
    }

    private void OnStationBankStartup(EntityUid uid, StationBankAccountComponent component, ref ComponentStartup args)
    {
        TrySyncStationBalance(uid, component);
        TryResolvePendingAwsAccount(uid, component);
    }

    private void OnCargoAccountStartup(EntityUid uid, EconomyThalerCargoComponent component, ref ComponentStartup args)
    {
        if (!TryComp(uid, out StationBankAccountComponent? bank))
            return;

        if (string.IsNullOrWhiteSpace(component.AccountId))
        {
            _pendingAwsAccountSync.Add(uid);
            return;
        }

        TrySyncStationBalance(uid, bank, component);
        _pendingAwsAccountSync.Remove(uid);
        UpdateBankAccount(uid, bank, 0);
    }

    private void OnCargoAccountShutdown(EntityUid uid, EconomyThalerCargoComponent component, ref ComponentShutdown args)
    {
        _pendingAwsAccountSync.Remove(uid);
    }

    partial void BeforeCargoBankUpdate(EntityUid station, StationBankAccountComponent component, ref int amount, ref bool handled)
    {
        TryResolvePendingAwsAccount(station, component);

        if (!_cargoAccountQuery.TryGetComponent(station, out var cargoAccount))
            return;

        if (!TryApplyAccountDelta(station, component, cargoAccount, amount))
            return;

        handled = true;
    }

    partial void AfterCargoBankUpdate(EntityUid station, StationBankAccountComponent component, int amount, bool handled)
    {
        TrySyncStationBalance(station, component);
    }

    private bool TryResolvePendingAwsAccount(EntityUid station, StationBankAccountComponent? bank = null)
    {
        if (!_pendingAwsAccountSync.Contains(station))
            return false;

        if (!_cargoAccountQuery.TryGetComponent(station, out var cargoAccount))
        {
            _pendingAwsAccountSync.Remove(station);
            return false;
        }

        if (string.IsNullOrWhiteSpace(cargoAccount.AccountId))
            return false;

        if (!_economyBankAccount.IsValidAccount(cargoAccount.AccountId))
            return false;

        if (bank == null && !TryComp(station, out bank))
        {
            _pendingAwsAccountSync.Remove(station);
            return false;
        }

        _pendingAwsAccountSync.Remove(station);
        TrySyncStationBalance(station, bank!, cargoAccount);
        UpdateBankAccount(station, bank!, 0);
        return true;
    }

    partial void EnsureAwsBalanceSync(EntityUid station)
    {
        if (!_cargoAccountQuery.TryGetComponent(station, out var cargoAccount))
            return;

        if (!TryComp(station, out StationBankAccountComponent? bank))
            return;

        if (!TryResolvePendingAwsAccount(station, bank))
        {
            TrySyncStationBalance(station, bank, cargoAccount);
            UpdateBankAccount(station, bank, 0);
        }
    }

    private bool TryApplyAccountDelta(EntityUid station, StationBankAccountComponent bank, EconomyThalerCargoComponent cargoAccount, int delta)
    {
        if (delta == 0)
        {
            TrySyncStationBalance(station, bank, cargoAccount);
            return true;
        }

        if (string.IsNullOrWhiteSpace(cargoAccount.AccountId))
        {
            _awsBridgeSawmill.Warning($"Station {station} has EconomyThalerCargoComponent without an AccountId.");
            return false;
        }

        if (!_economyBankAccount.TryChangeAccountBalance(cargoAccount.AccountId, delta))
        {
            _awsBridgeSawmill.Warning($"Failed to adjust AWS account {cargoAccount.AccountId} by {delta} for station {station}.");
            return false;
        }

        TrySyncStationBalance(station, bank, cargoAccount);
        return true;
    }

    private void TrySyncStationBalance(EntityUid station, StationBankAccountComponent bank)
    {
        if (!_cargoAccountQuery.TryGetComponent(station, out var cargoAccount))
            return;

        TrySyncStationBalance(station, bank, cargoAccount);
    }

    private void TrySyncStationBalance(EntityUid station, StationBankAccountComponent bank, EconomyThalerCargoComponent cargoAccount)
    {
        if (string.IsNullOrWhiteSpace(cargoAccount.AccountId))
            return;

        if (!_economyBankAccount.TryGetAccount(cargoAccount.AccountId, out var account))
        {
            _awsBridgeSawmill.Warning($"Unable to locate AWS account {cargoAccount.AccountId} for station {station}.");
            return;
        }

        var accountBalance = account.Value.Comp.Balance;
        var newBalance = accountBalance > int.MaxValue
            ? int.MaxValue
            : accountBalance < int.MinValue
                ? int.MinValue
                : (int) accountBalance;

        if (bank.Balance == newBalance)
            return;

        bank.Balance = newBalance;
    }

    partial void ShouldSkipCargoPassiveIncome(EntityUid station, StationBankAccountComponent bank, ref bool skip)
    {
        if (_cargoAccountQuery.HasComponent(station))
            skip = true;
    }

    partial void AdjustCargoInterfaceState(EntityUid station, StationCargoOrderDatabaseComponent orderDatabase, StationBankAccountComponent bankAccount, ref CargoConsoleInterfaceState state)
    {
        if (!_cargoAccountQuery.TryGetComponent(station, out var account))
            return;

        state = new CargoConsoleAwsInterfaceState(
            account.AccountId,
            state.Count,
            state.Capacity,
            state.Balance,
            orderDatabase.Orders,
            account.Currency.Id);
    }
}
