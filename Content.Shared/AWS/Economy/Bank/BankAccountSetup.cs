using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Shared.AWS.Economy.Bank;

[DataDefinition]
public sealed partial class BankAccountSetup
{
    [DataField("accountID")]
    public string? AccountID;

    [DataField("generateAccountID")]
    public bool GenerateAccountID = false;

    [DataField("accountName")]
    public string? AccountName;

    [DataField("allowedCurrency")]
    public ProtoId<CurrencyPrototype>? AllowedCurrency;

    [DataField("balance")]
    public long? Balance;

    [DataField("penalty")]
    public ulong? Penalty;

    [DataField("blocked")]
    public bool? Blocked;

    [DataField("canReachPayDay")]
    public bool? CanReachPayDay;

    [DataField("accountTags")]
    public List<BankAccountTag>? AccountTags;
}
