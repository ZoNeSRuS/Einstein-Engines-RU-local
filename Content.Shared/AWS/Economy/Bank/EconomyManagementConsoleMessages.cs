using Robust.Shared.Serialization;

namespace Content.Shared.AWS.Economy.Bank;

[Serializable, NetSerializable]
public sealed class EconomyManagementConsoleChangeParameterMessage(string accountID, EconomyBankAccountParam param, object value) : BoundUserInterfaceMessage
{
    public readonly string AccountID = accountID;
    public readonly EconomyBankAccountParam Param = param;
    public readonly object Value = value;
}

[Serializable, NetSerializable]
public sealed class EconomyManagementConsoleChangeHolderIDMessage(NetEntity holder, string newID) : BoundUserInterfaceMessage
{
    public readonly NetEntity AccountHolder = holder;
    public readonly string NewID = newID;
}

[Serializable, NetSerializable]
public sealed class EconomyManagementConsoleInitAccountOnHolderMessage(NetEntity holder) : BoundUserInterfaceMessage
{
    public readonly NetEntity AccountHolder = holder;
}

[Serializable, NetSerializable]
public sealed class EconomyManagementConsoleUserInterfaceState : BoundUserInterfaceState
{
    public bool Priveleged;
    public string? IDCardName;
    public NetEntity? AccountHolder;
    public string? HolderID;

    // Account that has been selected when performing the last action (this is kinda dumb yeah)
    public string? AccountID;
    public string? AccountName;
    public long? Balance;
    public ulong? Penalty;
    public bool? Blocked;
    public bool? CanReachPayDay;
    public string? JobName;
    public ulong? Salary;
}

[Serializable, NetSerializable]
public sealed class EconomyManagementConsolePayBonusMessage(string payer, float bonusPercent, List<string> accounts) : BoundUserInterfaceMessage
{
    public readonly string Payer = payer;
    public readonly float BonusPercent = bonusPercent;
    public readonly List<string> Accounts = accounts;
}
