#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Sotn;

namespace SotnArchipelago;

// What this seed writes into RAM, per stage, and the stage-load hook that writes it.
//
// Seeds from this project's AP world carry the patch's own bytes in slot_data["recomp"]
// (apworld/sotn/Recomp.py): those are written as they are, exactly as the disc patch would have
// them, and the recomp's rewrites read what they need back from RAM. Seeds from the unmodified AP
// world have no such data; Placement.cs works the item placement out from the scouts instead.
// Either way, other players' items are then given the AP look (Placement Look.ApItem).
static class SeedPlan
{
    const int PayloadVersion = 1;
    const int DraStage = -1;          // DRA is always loaded: written at connect and on every stage load
    const uint OverlayBase = 0x80180000;
    const uint DraBase = 0x800A0000;
    const long LibrarianLocation = 70;
    const int LibraryStage = 0x02;

    // Where the AP patch makes the Librarian sell an item instead of a relic: shop entry data and
    // 14 code edits plus injected routines in LIB (Rom.py replace_shop_relic_with_item). The recomp
    // reads 0x801B2B08 as the relic-owned offset, which that patch overwrites with a jump, so these
    // are held back; the item is sold as a normal shop entry instead (Special.ShopEntryWithItem).
    static readonly (uint From, uint To)[] LibrarianItemPatch =
    [
        (0x8018134C, 0x8018134F), (0x801814D4, 0x801814D5), (0x801B2B08, 0x801B2B0F), (0x801B2B80, 0x801B2B83),
        (0x801B3050, 0x801B3053), (0x801B317C, 0x801B3183), (0x801B3638, 0x801B363B), (0x801B369C, 0x801B369F),
        (0x801B3730, 0x801B3733), (0x801B3750, 0x801B375F), (0x801B431C, 0x801B4323), (0x801B43C0, 0x801B43C3),
        (0x801B4F10, 0x801B4F13), (0x801B4FB4, 0x801B4FB7), (0x801B59F4, 0x801B59F7), (0x801D4600, 0x801D4727),
    ];

    static int _builtFor = -1;
    static bool _fromPayload;
    static readonly Dictionary<int, List<RamWrite>> _byStage = [];
    static readonly HashSet<long> _unsupported = [];
    static readonly Dictionary<int, long> _byPickupFlag = [];
    static readonly Dictionary<int, List<long>> _directDrops = [];

    public static bool Ready => _builtFor >= 0 && _builtFor == ApClient.ConnectionId;
    public static bool FromPayload => _fromPayload;
    public static int UnsupportedCount => _unsupported.Count;

    // The Librarian's Jewel of Open slot sells an item (own or another player's) as a normal shop
    // entry (Special.ShopEntryWithItem), rather than a relic.
    public static bool LibrarianSellsItem { get; private set; }

    // Spots whose item the mod can't place yet; the vanilla item stays and own items are given directly.
    public static bool IsUnsupported(long location) => Ready && _unsupported.Contains(location);

    // Item-table pickup flag index (g_CastleFlags+0x100 bit number) -> location.
    public static long? LocationForPickupFlag(int flag) => _byPickupFlag.TryGetValue(flag, out var id) ? id : null;

    // Other players' items in this stage that drop straight from a wall, boss or cutscene.
    public static IReadOnlyList<long> DirectDropsIn(int stage) =>
        _directDrops.TryGetValue(stage, out var list) ? list : Array.Empty<long>();

    // Game thread, every frame: build once all scouts are in.
    public static void Update()
    {
        if (ApClient.State != ConnectionState.Connected) return;
        if (Ready || !ApClient.ScoutsComplete) return;

        _byStage.Clear();
        _unsupported.Clear();
        _byPickupFlag.Clear();
        _directDrops.Clear();
        ApLook.Reset();
        int slot = ApClient.Slot;

        bool librarianHoldsRelic = ApClient.TryGetScout(LibrarianLocation, out var lib)
            && lib.Player == slot && lib.Item >= ItemData.FirstRelic && lib.Item <= ItemData.LastRelic;
        _fromPayload = ApClient.SlotData?["recomp"] is JsonObject payload && ReadPayload(payload, !librarianHoldsRelic);

        foreach (var loc in LocationData.All)
        {
            if (!ApClient.TryGetScout(loc.Id, out var scout)) continue;
            IndexPickups(loc, scout, slot);

            bool own = scout.Player == slot;
            // Already in the patch bytes. The Librarian's relic is too; an item there is not (held back).
            if (_fromPayload && own && (loc.Id != LibrarianLocation || librarianHoldsRelic)) continue;

            // Stock seeds: everything from Placement. Payload seeds: other players' items (over the
            // patch's money bags and Secret boots) and an item in the Librarian's slot.
            var result = Placement.Compute(loc, scout, slot, Look.ApItem);
            if (result.Unsupported != null)
            {
                _unsupported.Add(loc.Id);
                Log.Info($"not placed yet: {loc.Name} ({result.Unsupported}); checked on arrival, own items are given directly");
                continue;
            }
            foreach (var w in result.Writes) Add(w);
        }
        if (!_fromPayload) foreach (var w in Placement.Always) Add(w);

        LibrarianSellsItem = ApClient.TryGetScout(LibrarianLocation, out _) && !librarianHoldsRelic
            && !_unsupported.Contains(LibrarianLocation);
        _builtFor = ApClient.ConnectionId;
        Log.Info($"placement ready ({(_fromPayload ? "patch data from the seed" : "worked out from scouts")}): "
               + $"{_byStage.Count} stage(s), {_unsupported.Count} spot(s) not placed yet");

        // Mid-game connect: place items in the stage already loaded. Only during normal play, when
        // the loaded overlay is the current stage's; otherwise the next stage load does it.
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null) return;
        ApplyDra(m);
        if (m.ReadU8(Game.GameStateAddr) == (byte)GameState.Play && m.ReadU8(ItemGiver.EngineStepAddr) == ItemGiver.EngineNormal)
            ApplyCurrentStage("connected", m);
    }

    // slot_data["recomp"] from apworld/sotn/Recomp.py: {"version": 1, "files": {key: [[offset, hex], ...]}}
    static bool ReadPayload(JsonObject payload, bool holdBackLibrarianItemPatch)
    {
        int version = payload["version"]?.GetValue<int>() ?? 0;
        if (version != PayloadVersion)
        {
            Log.Error($"seed's recomp data is version {version}, this mod reads version {PayloadVersion}; working items out from scouts instead");
            return false;
        }
        if (payload["files"] is not JsonObject files) return false;

        int bytes = 0, skipped = 0;
        foreach (var (key, runs) in files)
        {
            int stage;
            uint baseAddr;
            if (key == "DRA") { stage = DraStage; baseAddr = DraBase; }
            else if (SpecialData.Zones.TryGetValue(key, out var zone)) { stage = zone.Stage; baseAddr = OverlayBase; }
            else { skipped += CountBytes(runs); continue; } // other files (graphics, SEL): not placed

            foreach (var run in runs!.AsArray())
            {
                uint offset = run![0]!.GetValue<uint>();
                var data = Convert.FromHexString(run[1]!.GetValue<string>());
                for (int i = 0; i < data.Length; i++)
                {
                    uint addr = baseAddr + offset + (uint)i;
                    if (holdBackLibrarianItemPatch && stage == LibraryStage && InLibrarianItemPatch(addr)) { skipped++; continue; }
                    Add(new RamWrite(stage, addr, data[i], 1));
                    bytes++;
                }
            }
        }
        Log.Info($"seed patch data: {bytes} bytes to place, {skipped} not applicable to the recomp");
        return true;
    }

    static int CountBytes(JsonNode? runs)
    {
        int n = 0;
        if (runs is JsonArray a) foreach (var r in a) n += r![1]!.GetValue<string>().Length / 2;
        return n;
    }

    static bool InLibrarianItemPatch(uint addr)
    {
        foreach (var (from, to) in LibrarianItemPatch)
            if (addr >= from && addr <= to) return true;
        return false;
    }

    static void IndexPickups(LocationInfo loc, NetworkItem scout, int slot)
    {
        var p = loc.Place;
        foreach (var flag in p.PickupFlags)
            _byPickupFlag[ApLook.PickupFlagIndex(flag, p.PickupBit)] = loc.Id;

        if (scout.Player == slot) return;
        // Other players' items that drop straight from a wall, boss or cutscene carry no pickup flag.
        void Direct(int stage)
        {
            if (!_directDrops.TryGetValue(stage, out var list)) _directDrops[stage] = list = [];
            if (!list.Contains(loc.Id)) list.Add(loc.Id);
        }
        if (p.NoOffset || loc.VanillaItem == "Holy glasses" || loc.Id == 40)
            foreach (var a in p.Addresses) Direct(a.Stage);
        foreach (var a in p.BossDrop) Direct(a.Stage);
        foreach (var a in p.Reward) Direct(a.Stage);
    }

    static void Add(RamWrite w)
    {
        if (!_byStage.TryGetValue(w.Stage, out var list)) _byStage[w.Stage] = list = [];
        list.Add(w);
    }

    // func_800F16D0 works out the stage to load. RunMainEngine calls it when setting up a stage whose
    // overlay is already loaded, which is where the built-in randomizer applies its stage writes
    // too (Randomizer.ApplyInternalRandomizer). HandlePlay also calls it before the overlay is
    // loaded; writes then would land in the previous overlay (e.g. the file select) and be lost.
    const uint HandlePlayCallReturn = 0x800E4CE0;

    [PreHook("dra", "func_800F16D0")]
    static void OnStageLoad(CpuContext c, IMemory m)
    {
        if (c.RA == HandlePlayCallReturn || !Ready) return;
        ApplyDra(m);
        ApplyCurrentStage("stage load", m);
    }

    static void ApplyDra(IMemory m)
    {
        if (!Ready || SaveLink.Check(m) == SaveLink.Status.OtherSeed) return;
        ApLook.WriteArt(m);
        if (_byStage.TryGetValue(DraStage, out var list)) Write(m, list);
    }

    static void ApplyCurrentStage(string why, IMemory m)
    {
        if (SaveLink.Check(m) == SaveLink.Status.OtherSeed) return;
        int stage = m.ReadU8(Game.StageIdAddr);
        if (!_byStage.TryGetValue(stage, out var list)) return;
        Write(m, list);
        Log.Info($"{why}: placed {list.Count} write(s) in {(Stage)stage} (0x{stage:X2})");
    }

    static void Write(IMemory m, List<RamWrite> list)
    {
        foreach (var w in list)
        {
            if (w.Size == 1) m.WriteU8(w.Addr, (byte)w.Value);
            else m.WriteU16(w.Addr, w.Value);
        }
    }
}
