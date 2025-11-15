using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Containers;

namespace Content.Shared.AWS.Economy.DepartmentRewards;

public abstract partial class SharedDepartmentRewardConsoleSystem : EntitySystem
{
    [Dependency] protected readonly ItemSlotsSystem ItemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DepartmentRewardConsoleComponent, ComponentInit>(OnConsoleInit);
        SubscribeLocalEvent<DepartmentRewardConsoleComponent, ComponentRemove>(OnConsoleShutdown);
        SubscribeLocalEvent<DepartmentRewardConsoleComponent, EntInsertedIntoContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<DepartmentRewardConsoleComponent, EntRemovedFromContainerMessage>(OnSlotChanged);
    }

    private void OnConsoleInit(EntityUid uid, DepartmentRewardConsoleComponent component, ComponentInit args)
    {
        ItemSlots.AddItemSlot(uid, DepartmentRewardConsoleComponent.AuthorizationSlotId, component.CardSlot);
    }

    private void OnConsoleShutdown(EntityUid uid, DepartmentRewardConsoleComponent component, ComponentRemove args)
    {
        ItemSlots.RemoveItemSlot(uid, component.CardSlot);
    }

    private void OnSlotChanged(EntityUid uid, DepartmentRewardConsoleComponent component, ContainerModifiedMessage args)
    {
        if (args.Container.ID != component.CardSlot.ID)
            return;

        OnAuthorizationSlotChanged(uid, component);
    }

    protected virtual void OnAuthorizationSlotChanged(EntityUid uid, DepartmentRewardConsoleComponent component)
    {
    }
}
