using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.AWS.Economy.DepartmentRewards;

[Prototype("departmentRewardTask")]
public sealed class DepartmentRewardTaskPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string DepartmentId = default!;

    [DataField(required: true)]
    public DepartmentRewardStage Stage;

    [DataField(required: true)]
    public LocId Title;

    [DataField(required: true)]
    public LocId Description;

    [DataField(required: true)]
    public int RewardAmount;

    [DataField]
    public float PenaltyMultiplier = 1.5f;
}
