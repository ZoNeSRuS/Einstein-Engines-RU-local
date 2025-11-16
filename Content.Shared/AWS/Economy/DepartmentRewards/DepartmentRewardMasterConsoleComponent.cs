using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.AWS.Economy.DepartmentRewards;

/// <summary>
///     Marker component for the captain's master reward console.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DepartmentRewardMasterConsoleComponent : Component
{
    /// <summary>
    ///     Optional list of department identifiers that should be shown by this console.
    ///     When empty, every department tracked on the current map is displayed.
    /// </summary>
    [DataField("visibleDepartments")]
    public HashSet<string> VisibleDepartments { get; set; } = new();
}
