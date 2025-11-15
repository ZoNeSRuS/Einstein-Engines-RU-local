using Content.Shared.AWS.Economy.DepartmentRewards;
using Content.Shared.Containers.ItemSlots;

namespace Content.Client.AWS.Economy.DepartmentRewards;

public sealed class DepartmentRewardConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private DepartmentRewardConsoleWindow? _window;

    public DepartmentRewardConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    public void OnConfirmPressed()
    {
        SendMessage(new DepartmentRewardConsoleConfirmMessage());
    }

    public void OnFailPressed()
    {
        SendMessage(new DepartmentRewardConsoleFailMessage());
    }

    public void OnAuthEjectPressed()
    {
        SendMessage(new ItemSlotButtonPressedEvent(DepartmentRewardConsoleComponent.AuthorizationSlotId));
    }

    protected override void Open()
    {
        base.Open();

        _window = new DepartmentRewardConsoleWindow(this);
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not DepartmentRewardConsoleState consoleState)
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
