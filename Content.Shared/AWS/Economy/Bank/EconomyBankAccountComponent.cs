using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared.Store;
using Content.Shared.Roles;

namespace Content.Shared.AWS.Economy.Bank
{
    /// <summary>
    /// This component is used to define the account. This component should not be created manually. Work with it through <see cref="EconomyBankAccountSystemShared"></cref> or the server system.
    /// </summary>
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(EconomyBankAccountSystemShared))]
    public sealed partial class EconomyBankAccountComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public string AccountID = "NO VALUE";

        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public string AccountName = "UNEXPECTED USER";

        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public ProtoId<CurrencyPrototype> AllowedCurrency = "Thaler";

        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public long Balance = 0;

        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public ulong Penalty = 0;

        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public bool Blocked;

        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public bool CanReachPayDay = true;

        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public ProtoId<JobPrototype>? JobName;

        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public ulong? Salary;

        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public List<EconomyBankAccountLogField> Logs = new();

        [ViewVariables(VVAccess.ReadWrite)]
        [AutoNetworkedField]
        public List<BankAccountTag> AccountTags = new();
    }

    [Serializable, NetSerializable]
    public struct EconomyBankAccountLogField
    {
        public EconomyBankAccountLogField(TimeSpan logTime, string logText)
        {
            Date = logTime;
            Text = logText;
        }
        public TimeSpan Date;
        public string Text;
    }
}
