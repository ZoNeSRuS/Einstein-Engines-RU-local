using Content.Shared.AWS.Economy.DepartmentRewards;

namespace Content.Client.AWS.Economy.DepartmentRewards;

public sealed class DepartmentRewardMasterConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private DepartmentRewardMasterConsoleWindow? _window;

    public DepartmentRewardMasterConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new DepartmentRewardMasterConsoleWindow();
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not DepartmentRewardMasterConsoleState consoleState)
            return;

        _window?.UpdateState(consoleState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
    }
}
