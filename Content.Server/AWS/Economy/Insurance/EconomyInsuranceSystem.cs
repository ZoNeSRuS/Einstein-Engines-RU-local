using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Forensics;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.AWS.Economy.Bank;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.AWS.Economy.Insurance;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.Random;
using Content.Shared.Roles;
using Robust.Server.GameObjects;
using Content.Server.AWS.Economy.Bank;
using Content.Server.Popups;
using Content.Shared.Forensics.Components;

namespace Content.Server.AWS.Economy.Insurance;

public sealed class EconomyInsuranceSystem : EconomyInsuranceSystemShared
{
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly EconomyBankAccountSystem _economy = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawn, after: new[] { typeof(SpawnPointSystem) });
        SubscribeLocalEvent<EconomyInsuranceServerComponent, ComponentAdd>(OnComponentAdd);

        SubscribeLocalEvent<EconomyInsuranceTerminalComponent, EconomyInsuranceTerminalUpdateEvent>(OnTerminalUpdate);
        SubscribeLocalEvent<EconomyInsuranceTerminalComponent, EconomyInsuranceEditMessage>(OnEditMessage);

        SubscribeLocalEvent<EconomySallaryPostEvent>(OnSallaryPost);
    }

    private void OnSallaryPost(EconomySallaryPostEvent args)
    {
        if (!TryGetServer(out var server))
            return;

        const string ntmedicalId = "NT-Medical"; // hardcode
        const string ntccId = "NT-CentCom";

        foreach (var (id, insuranceInfo) in server.Comp.InsuranceInfo)
        {
            if (_prototype.TryIndex(insuranceInfo.InsuranceProto, out var prototype) && prototype.Cost != 0)
            {
                var payerAccountId = insuranceInfo.DefaultFreeInsuranceProto == insuranceInfo.InsuranceProto ?
                    ntccId : insuranceInfo.PayerAccountId;

                var cost = Math.Max(0, prototype.Cost);
                if (_economy.TryGetAccount(payerAccountId, out var account)
                    && account.Value.Comp.Balance <= cost)
                {
                    insuranceInfo.InsuranceProto = "NonStatus";
                    UpdateIconOnCardsById(insuranceInfo.Id);

                    continue;
                }

                _economy.TrySendMoney(payerAccountId, ntmedicalId, (ulong) prototype.Cost,
                    Loc.GetString("economy-insurance-postsallary-payforinsurance", ("name", insuranceInfo.InsurerName)),
                    out _);
            }
        }
    }

    private void OnTerminalUpdate(Entity<EconomyInsuranceTerminalComponent> entity, ref EconomyInsuranceTerminalUpdateEvent args)
    {
        if (!TryGetServer(out var server))
            return;

        var rights = EconomyInsuranceTerminalRights.Its;
        var insuranceEnt = args.InsertedInsurance;
        Dictionary<int, EconomyInsuranceInfo> infos;

        if (insuranceEnt is not null)
            if (CanEditAnyInsurance(entity))
            {
                infos = server.Comp.InsuranceInfo;
                rights = EconomyInsuranceTerminalRights.Full;
            }
            else
            {
                infos = new();

                if (server.Comp.InsuranceInfo.TryGetValue(insuranceEnt.Value.Comp.InsuranceInfoId, out var value))
                    infos.Add(insuranceEnt.Value.Comp.InsuranceInfoId, value);
            }
        else
            infos = new();

        _userInterface.SetUiState(entity.Owner, EconomyInsuranceTerminalUiKey.Key,
            new EconomyInsuranceUserInterfaceState(insuranceEnt?.Comp?.InsuranceInfoId ?? 0, rights, infos));
    }

    private void OnEditMessage(Entity<EconomyInsuranceTerminalComponent> entity, ref EconomyInsuranceEditMessage args)
    {
        if (!TryGetTerminalInsertedInsurance(entity, out var insertedInsurance))
            return;

        var receivedInsuranceInfo = args.Info;

        if (!TryGetInsuranceInfo(receivedInsuranceInfo.Id, out var fetchedInfo))
            return;

        if (!_prototype.TryIndex(receivedInsuranceInfo.InsuranceProto, out var receivedInsuranceInfoProto))
            return;

        var matchesDefaultInsurance = receivedInsuranceInfo.InsuranceProto == fetchedInfo.DefaultFreeInsuranceProto;

        if (!receivedInsuranceInfoProto.CanBeBought && !matchesDefaultInsurance)
            receivedInsuranceInfo.InsuranceProto = fetchedInfo.InsuranceProto;

        var currentInsuranceProtoId = fetchedInfo.InsuranceProto;

        if (CanOnlyEditInsuranceProto(entity, receivedInsuranceInfo.Id))
            fetchedInfo.InsuranceProto = receivedInsuranceInfo.InsuranceProto;

        if (CanEditAnyInsurance(entity))
        {
            fetchedInfo.DNA = receivedInsuranceInfo.DNA;
            fetchedInfo.InsuranceProto = receivedInsuranceInfo.InsuranceProto;
            fetchedInfo.InsurerName = receivedInsuranceInfo.InsurerName;
            fetchedInfo.PayerAccountId = receivedInsuranceInfo.PayerAccountId;
        }

        var insuranceChanged = fetchedInfo.InsuranceProto != currentInsuranceProtoId;

        if (insuranceChanged)
        {
            var updatedMatchesDefaultInsurance = fetchedInfo.InsuranceProto == fetchedInfo.DefaultFreeInsuranceProto;

            if (!updatedMatchesDefaultInsurance)
            {
                if (!_prototype.TryIndex(fetchedInfo.InsuranceProto, out var updatedInsuranceProto))
                {
                    fetchedInfo.InsuranceProto = currentInsuranceProtoId;
                    UpdateTerminalUserInterface(entity);
                    return;
                }

                var currentCost = 0;
                if (currentInsuranceProtoId == fetchedInfo.DefaultFreeInsuranceProto)
                    currentCost = 0;
                else if (_prototype.TryIndex(currentInsuranceProtoId, out var currentInsuranceProto))
                    currentCost = System.Math.Max(0, currentInsuranceProto.Cost);

                var newCost = System.Math.Max(0, updatedInsuranceProto.Cost);

                if (newCost > currentCost)
                {
                    var difference = (ulong) (newCost - currentCost);
                    if (!_economy.TrySendMoney(fetchedInfo.PayerAccountId, "NT-Medical", difference, "NT-Medical",
                            out var error))
                    {
                        fetchedInfo.InsuranceProto = currentInsuranceProtoId;
                        _popupSystem.PopupEntity(error, entity);
                        insuranceChanged = false;
                    }
                }
            }
        }

        if (insuranceChanged)
        {
            if (_prototype.TryIndex(fetchedInfo.InsuranceProto, out var newInsuranceProto))
                _popupSystem.PopupEntity(Loc.GetString("economy-insurance-terminal-change-success",
                    ("insurance", newInsuranceProto.Name)), entity);
            else
                _popupSystem.PopupEntity(Loc.GetString("economy-insurance-terminal-change-success-generic"), entity);
        }

        UpdateIconOnCardsById(fetchedInfo.Id);
        UpdateTerminalUserInterface(entity);
    }

    private void OnComponentAdd(EntityUid uid, EconomyInsuranceServerComponent component, ref ComponentAdd args)
    {
        if (TryGetServer(out var ent) && (ent.Owner != uid && ent.Comp == component))
        {
            RemComp(uid, component);
            DebugTools.Assert("Only one supported server can be exists at once in the world");
        }
    }

    private void OnPlayerSpawn(PlayerSpawningEvent ev)
    {
        if (ev.SpawnResult is null || ev.HumanoidCharacterProfile is null)
            DebugTools.Assert("Unable to proccess insurance on player spawn!");

        var playerUid = ev.SpawnResult.Value;
        var profile = ev.HumanoidCharacterProfile;
        var job = ev.Job;

        var preparedData = PerformPrepareData(playerUid, profile, job);

        if (preparedData is null)
            DebugTools.Assert($"Unable to proccess insurance by getting necessary components");

        if (!TryCreateInsuranceRecord(preparedData.InsurancePrototype,
                preparedData.InsurerName,
                preparedData.InsurerAccountId,
                preparedData.InsurerDna,
                out var economyInsuranceInfo,
                out var error))
        {
            DebugTools.Assert($"Unable to create insurance record for {playerUid}!\n{error}");
            return;
        }

        economyInsuranceInfo.DefaultFreeInsuranceProto = preparedData.DefaultFreePrototype;

        var cardUid = preparedData.CardUid;

        var insuranceComponent = EnsureComp<EconomyInsuranceComponent>(cardUid);
        insuranceComponent.InsuranceInfoId = economyInsuranceInfo.Id;

        UpdateIcon((cardUid, insuranceComponent));
    }

    private PreparedInsurerData? PerformPrepareData(EntityUid playerUid, HumanoidCharacterProfile profile, ProtoId<JobPrototype>? job)
    {
        if (job is null)
            return null;

        if (!TryComp<DnaComponent>(playerUid, out var dnaComponent))
            return null;

        if (!_inventorySystem.TryGetSlotEntity(playerUid, "id", out var pdaUid))
            return null;

        if (!TryComp<PdaComponent>(pdaUid, out var pdaComponent) || pdaComponent.ContainedId is null)
            return null;

        var cardUid = pdaComponent.ContainedId;

        if (!TryComp<IdCardComponent>(cardUid, out var cardComponent) || cardComponent.FullName is null)
            return null;

        if (!TryComp<EconomyAccountHolderComponent>(cardUid, out var accountHolderComponent))
            return null;

        var insurance = MatchDefaultInsuranceProto(job.Value);

        return new(cardUid.Value, insurance, insurance, cardComponent.FullName, accountHolderComponent.AccountID, dnaComponent.DNA);
    }

    private ProtoId<EconomyInsurancePrototype> MatchDefaultInsuranceProto(ProtoId<JobPrototype> job)
    {
        var proto = _prototype.Index<EconomyInsuranceDefaultPrototype>("NanoTrasenDefault");

        if (proto.Presets.TryGetValue(job, out var entry))
            return entry;

        return "NonStatus";
    }

    private ProtoId<EconomyInsurancePrototype> GetMatchedInsurance(HumanoidCharacterProfile profile, ProtoId<JobPrototype> job)
    {
        return profile.Insurance;
    }

    [PublicAPI]
    public bool TryCreateInsuranceRecord(ProtoId<EconomyInsurancePrototype> insuranceProto,
        string insurerName,
        string payerAccountId,
        string insurerDna,
        [NotNullWhen(true)] out EconomyInsuranceInfo? economyInsuranceInfo,
        [NotNullWhen(false)] out string? error)
    {
        error = null;
        economyInsuranceInfo = null;

        if (!TryGetServer(out var server))
        {
            error = "Not found server";
            return false;
        }

        var comp = server.Comp;


        // pervious logic
        //if (comp.InsuranceInfo.Any(x => x.DNA == insurerDna || x.InsurerName == insurerName))
        //{
        //    error = "Already exists record with provided data";
        //    return false;
        //}


        var id = GetNextId(server);
        var insuranceRecord = CreateInsuranceRecord(server, id, insuranceProto, insurerName, payerAccountId, insurerDna);

        economyInsuranceInfo = insuranceRecord;

        return true;
    }

    [PublicAPI]
    public bool TryChangeInfoId(int currentId, int newId, [NotNullWhen(false)] out string? error)
    {
        error = "";

        if (!TryGetServer(out var server))
        {
            error = "Not found server";
            return false;
        }

        if (!server.Comp.InsuranceInfo.TryGetValue(currentId, out var info))
        {
            error = "not found info";
            return false;
        }

        info.Id = newId;

        server.Comp.InsuranceInfo.Remove(currentId);
        server.Comp.InsuranceInfo.Add(newId, info);

        // alpha temp version
        foreach (var comp in EntityQuery<EconomyInsuranceComponent>().Where(x => x.InsuranceInfoId == currentId))
            Dirty(comp.Owner, comp);

        return true;
    }

    [PublicAPI]
    public void UpdateIcon(Entity<EconomyInsuranceComponent> entity)
    {
        if (TryGetInsuranceInfo(entity.Comp.InsuranceInfoId, out var info) &&
            (_prototype.TryIndex(info.InsuranceProto, out var prototype)))
        {
            entity.Comp.IconPrototype = prototype.Icon;
            Dirty(entity);
        }
    }

    [PublicAPI]
    public void UpdateIconOnCardsById(int insuranceInfoId)
    {
        foreach (var comp in EntityQuery<EconomyInsuranceComponent>().Where(x => x.InsuranceInfoId == insuranceInfoId))
            if (comp is not null)
                UpdateIcon((comp.Owner, comp));
    }

    //[PublicAPI]
    //public void UpdateInsuranceInfo(int insuranceInfoId, EconomyInsuranceInfo newInsuranceInfo)
    //{

    //} // maybe in future

    private int GetNextId(EconomyInsuranceServerComponent component)
    {
        int lastGenerated = 0;
        while (lastGenerated == 0 || component.InsuranceInfo.ContainsKey(lastGenerated))
        {
            lastGenerated = _random.Next(111, 999);
        }

        return lastGenerated;
    }

    private EconomyInsuranceInfo CreateInsuranceRecord(EconomyInsuranceServerComponent serverComponent,
        int id,
        ProtoId<EconomyInsurancePrototype> insuranceProto,
        string insurerName,
        string payerAccountId,
        string insurerDna)
    {
        EconomyInsuranceInfo economyInsuranceInfo = new(id, insuranceProto, insurerName, payerAccountId, insurerDna);
        serverComponent.InsuranceInfo.Add(id, economyInsuranceInfo);

        return economyInsuranceInfo;
    }

    private record PreparedInsurerData(
        EntityUid CardUid,
        ProtoId<EconomyInsurancePrototype> InsurancePrototype,
        ProtoId<EconomyInsurancePrototype> DefaultFreePrototype,
        string InsurerName,
        string InsurerAccountId,
        string InsurerDna);
}
