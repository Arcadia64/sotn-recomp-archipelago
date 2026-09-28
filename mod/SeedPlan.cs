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

    // func_800F16D0 works out the stage to load. RunMainEngine calls it when setting up a stage whose
    // overlay is already loaded, which is where the built-in randomizer applies its stage writes
    // too (Randomizer.ApplyInternalRandomizer). HandlePlay also calls it before the overlay is
    // loaded; writes then would land in the previous overlay (e.g. the file select) and be lost.
    const uint HandlePlayCallReturn = 0x800E4CE0;

    [PreHook("dra", "func_800F16D0")]
    static void OnStageLoad(CpuContext c, IMemory m)
    {
        if (c.RA == HandlePlayCallReturn) return;
        ApplyCurrentStage("stage load", m);
    }

    static void ApplyCurrentStage(string why, IMemory? m)
    {
        if (m == null || !Ready) return;
        if (SaveLink.Check(m) == SaveLink.Status.OtherSeed) return;

        int stage = m.ReadU8(Game.StageIdAddr);
        if (!_byStage.TryGetValue(stage, out var list)) return;
        foreach (var w in list)
        {
            if (w.Size == 1) m.WriteU8(w.Addr, (byte)w.Value);
            else m.WriteU16(w.Addr, w.Value);
        }
        Log.Info($"{why}: placed {list.Count} write(s) in {(Stage)stage} (0x{stage:X2})");
    }
}
