using System;
using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.AWS.Skills;

[Serializable, NetSerializable, DataDefinition]
public sealed partial class SkillContainer
{
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public Dictionary<ProtoId<SkillPrototype>, Enum> Skills = new();

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public Dictionary<ProtoId<SkillPrototype>, List<Enum>> UnblockedSkillLevels = new();

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public Dictionary<ProtoId<SkillPrototype>, List<Enum>> BlockedSkillLevels = new();

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public Dictionary<ProtoId<SkillPrototype>, Enum> DefaultSkillLevels = new();

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public int AdditionalSkillPoints { get; set; } = 0;

    public SkillContainer Clone()
    {
        var clone = new SkillContainer
        {
            AdditionalSkillPoints = AdditionalSkillPoints,
        };

        foreach (var (proto, level) in Skills)
            clone.Skills[proto] = level;

        foreach (var (proto, levels) in UnblockedSkillLevels)
            clone.UnblockedSkillLevels[proto] = new List<Enum>(levels);

        foreach (var (proto, levels) in BlockedSkillLevels)
            clone.BlockedSkillLevels[proto] = new List<Enum>(levels);

        foreach (var (proto, level) in DefaultSkillLevels)
            clone.DefaultSkillLevels[proto] = level;

        return clone;
    }
}
