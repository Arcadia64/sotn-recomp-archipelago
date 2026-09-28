#nullable enable
using System.Collections.Generic;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Sotn;

namespace SotnArchipelago;

// The seed's item placement for this slot, worked out from the server's scouts, and the
// stage-load hook that writes it into RAM.
static class SeedPlan
{
    // Item ids inside wall code the recomp didn't rewrite (config/sotn.json has no patch for these
    // functions), so writing RAM there changes nothing: NO1 func_us_801BE880, RNO1 func_801A7B34,
    // RNO3 func_801B2BF0. Spots that need one of these are treated as not placed. The NO3/NP3
    // rocks, CEN Holy glasses and RNZ1 Ring of Vlad ids are read back from RAM by RandoPatch.cs.
    static readonly HashSet<StageAddr> NotReadByRecomp =
    [
        new(0x01, 0x801BEAB0),
        new(0x21, 0x801A7D64),
        new(0x27, 0x801B2F04),
    ];

    static int _builtFor = -1;
    static readonly Dictionary<int, List<RamWrite>> _byStage = [];
    static readonly HashSet<long> _unsupported = [];

    public static bool Ready => _builtFor >= 0 && _builtFor == ApClient.ConnectionId;
    public static int UnsupportedCount => _unsupported.Count;

    // Spots whose item the mod can't place yet; the vanilla item stays and own items are given directly.
    public static bool IsUnsupported(long location) => Ready && _unsupported.Contains(location);

    // Game thread, every frame: build once all scouts are in.
    public static void Update()
    {
        if (ApClient.State != ConnectionState.Connected) return;
        if (Ready || !ApClient.ScoutsComplete) return;

        _byStage.Clear();
        _unsupported.Clear();
        int slot = ApClient.Slot;
        int writes = 0;
        foreach (var loc in LocationData.All)
        {
            if (!ApClient.TryGetScout(loc.Id, out var scout)) continue;
            var result = Placement.Compute(loc, scout, slot);
            if (result.Unsupported == null && result.Writes.Exists(w => NotReadByRecomp.Contains(new(w.Stage, w.Addr))))
                result.Unsupported = "wall item is fixed in code the recomp didn't rewrite";
            if (result.Unsupported == null)
            {
                foreach (var w in result.Writes) Add(w);
                writes += result.Writes.Count;
            }
            else
            {
                _unsupported.Add(loc.Id);
                Log.Info($"not placed yet: {loc.Name} ({result.Unsupported}); vanilla item stays, own items are given directly");
            }
        }
        foreach (var w in Placement.Always) Add(w);

        _builtFor = ApClient.ConnectionId;
        Log.Info($"placement ready: {writes} writes over {_byStage.Count} stages, {_unsupported.Count} spot(s) not placed yet");
        // Mid-game connect: place items in the stage already loaded. Only during normal play, when
        // the loaded overlay is the current stage's; otherwise the next stage load does it.
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m != null && m.ReadU8(Game.GameStateAddr) == (byte)GameState.Play && m.ReadU8(ItemGiver.EngineStepAddr) == ItemGiver.EngineNormal)
            ApplyCurrentStage("connected", m);
    }

    static void Add(RamWrite w)
    {
        if (!_byStage.TryGetValue(w.Stage, out var list)) _byStage[w.Stage] = list = [];
        list.Add(w);
    }

    // Stage overlays are loaded by the time this runs; the built-in randomizer applies its
    // stage writes from the same place (Randomizer.ApplyInternalRandomizer).
    [PreHook("dra", "func_800F16D0")]
    static void OnStageLoad(CpuContext c, IMemory m) => ApplyCurrentStage("stage load", m);

    static void ApplyCurrentStage(string why, IMemory? m)
    {
        if (m == null || !Ready) return;
        if (SaveLink.Check(m) == SaveLink.Status.OtherSeed) return;

        int stage = m.ReadU8(Game.StageIdAddr);
        if (!_byStage.TryGetValue(stage, out var list)) return;
        foreach (var w in list) m.WriteU16(w.Addr, w.Value);
        Log.Info($"{why}: placed {list.Count} write(s) in {(Stage)stage} (0x{stage:X2})");
    }
}
