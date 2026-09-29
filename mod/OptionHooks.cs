#nullable enable
using System.Collections.Generic;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Sotn;

namespace SotnArchipelago;

// Seed options whose patch bytes are code the recomp doesn't read back (docs/research/options-audit.md).
// Same convention as the recomp's own rewrites: each fix only acts when the patched bytes are in RAM
// (SeedPlan writes the seed's patch data as files load), so it follows the seed exactly.
static class OptionHooks
{
    static bool Active => SeedPlan.Ready;

    static bool FromStage(CpuContext c) => c.RA >= 0x80180000 && c.RA < 0x80200000;

    // ---- enemy_stats: stats box on every hit ----
    // Rom.py turns the Faerie scroll check in each stage's HitDetection into 'li v0,3' (owned and on).
    // Per-stage hooks in OptionData.g.cs report the scroll as owned for the length of the call.
    const uint FaerieScrollRelic = 0x80097973;
    const uint LiV0Is3 = 0x34020003;
    static int _faerieDepth;
    static byte _faerieSaved;

    public static void FaerieIn(IMemory m)
    {
        if (_faerieDepth > 0) { _faerieDepth++; return; }
        if (!Active) return;
        int stage = m.ReadU8(Game.StageIdAddr);
        if (!OptionData.FaerieForceSites.TryGetValue(stage, out var site) || m.ReadU32(site) != LiV0Is3) return;
        _faerieSaved = m.ReadU8(FaerieScrollRelic);
        m.WriteU8(FaerieScrollRelic, 3);
        _faerieDepth = 1;
    }

    public static void FaerieOut(IMemory m)
    {
        if (_faerieDepth == 0 || --_faerieDepth > 0) return;
        m.WriteU8(FaerieScrollRelic, _faerieSaved);
    }

    // ---- random_music: boss and event songs ----
    // Boss and cutscene code requests songs with 'ori vX,zero,0x3NN; sw vX,0x80097910' and plays them
    // with PlaySfx. Rom.py changes NN at each site; the recomp reads only a few back. So within a stage,
    // a request for a site's original song becomes the seed's song for that site (read from RAM).
    const uint SongRequest = 0x80097910;
    static Dictionary<int, List<MusicSite>>? _musicByStage;
    static uint _songBefore;
    static uint _lastRemapped;

    static bool TryRemapSong(IMemory m, uint song, out uint mapped)
    {
        mapped = song;
        if ((song & 0xFFFFFF00) != 0x300) return false;
        if (_musicByStage == null)
        {
            _musicByStage = [];
            foreach (var site in OptionData.MusicSites)
            {
                if (site.RecompReads) continue; // the recomp's rewrite plays the seed's song itself
                if (!_musicByStage.TryGetValue(site.Stage, out var list)) _musicByStage[site.Stage] = list = [];
                list.Add(site);
            }
        }
        if (!_musicByStage.TryGetValue(m.ReadU8(Game.StageIdAddr), out var sites)) return false;
        foreach (var site in sites)
        {
            if (site.Vanilla != (byte)song) continue;
            byte seeded = m.ReadU8(site.Addr);
            if (seeded == site.Vanilla) return false;
            mapped = 0x300u | seeded;
            return true;
        }
        return false;
    }

    public static void MusicIn(IMemory m) => _songBefore = m.ReadU32(SongRequest);

    public static void MusicOut(IMemory m)
    {
        if (!Active) return;
        uint song = m.ReadU32(SongRequest);
        if (song == _songBefore || !TryRemapSong(m, song, out var mapped)) return;
        m.WriteU32(SongRequest, mapped);
        _lastRemapped = mapped;
    }

    [PreHook("dra", "PlaySfx")]
    static void BeforePlaySfx(CpuContext c, IMemory m)
    {
        if (!Active || !FromStage(c)) return;
        // Already the seed's song (the request word we set, read back and played): leave it.
        if (c.A0 == _lastRemapped && m.ReadU32(SongRequest) == _lastRemapped) return;
        if (TryRemapSong(m, c.A0, out var mapped)) c.A0 = mapped;
    }

    // ---- infinite_wing_smash ----
    // Rom.py NOPs the timer decrement in ControlBatForm (0x801173C8); keep the timer from running out.
    const uint WingSmashDecrement = 0x801173C8;
    const uint WingSmashTimer = 0x80137FFC;

    [PreHook("dra", "ControlBatForm_dra")]
    static void BeforeControlBatForm(CpuContext c, IMemory m)
    {
        if (!Active || m.ReadU32(WingSmashDecrement) != 0) return;
        if (m.ReadU32(WingSmashTimer) == 1) m.WriteU32(WingSmashTimer, 2);
    }

    // ---- color_randomizer: wing smash trail palette ----
    // One of Rom.py's two wing-smash variants changes the palette immediate at 0x8011E438 (vanilla 0x8102).
    const uint WingSmashPalette = 0x8011E438;
    const ushort VanillaTrailPalette = 0x8102;
    static uint _trail;

    [PreHook("dra", "EntityWingSmashTrail_dra")]
    static void BeforeWingSmashTrail(CpuContext c, IMemory m) => _trail = c.A0;

    [PostHook("dra", "EntityWingSmashTrail_dra")]
    static void AfterWingSmashTrail(CpuContext c, IMemory m)
    {
        if (!Active || _trail == 0) return;
        ushort seeded = m.ReadU16(WingSmashPalette);
        if (seeded != VanillaTrailPalette && m.ReadU16(_trail + 0x16) == VanillaTrailPalette)
            m.WriteU16(_trail + 0x16, seeded);
    }

    // ---- color_randomizer: gravity boots beam ----
    // Rom.py changes the two colour immediates (0x8011E1AC, 0x8011E1B0) and which register each of the
    // 12 colour 'sb' instructions (0x8011E1C0..) stores. After the beam sets up its sprite pieces (step
    // 0), apply the patched instructions to them.
    const uint PrimBuf = 0x80086FEC;
    const uint PrimSize = 0x34;
    const uint BeamColour1 = 0x8011E1AC, BeamColour2 = 0x8011E1B0, BeamStores = 0x8011E1C0;
    static uint _beam;
    static ushort _beamStep;

    [PreHook("dra", "EntityGravityBootBeam_dra")]
    static void BeforeGravityBootBeam(CpuContext c, IMemory m)
    {
        _beam = c.A0;
        _beamStep = m.ReadU16(c.A0 + 0x2C);
    }

    [PostHook("dra", "EntityGravityBootBeam_dra")]
    static void AfterGravityBootBeam(CpuContext c, IMemory m)
    {
        if (!Active || _beam == 0 || _beamStep != 0 || m.ReadU16(_beam + 0x2C) == 0) return;
        byte colour1 = m.ReadU8(BeamColour1), colour2 = m.ReadU8(BeamColour2);
        ForEachPrim(m, _beam, prim =>
        {
            for (uint k = 0; k < 12; k++)
            {
                uint store = m.ReadU32(BeamStores + k * 4);
                uint rt = (store >> 16) & 0x1F;
                byte value = rt switch { 0 => 0, 4 => colour1, 5 => colour2, _ => m.ReadU8(prim + (store & 0xFFFF)) };
                m.WriteU8(prim + (store & 0xFFFF), value);
            }
        });
    }

    static void ForEachPrim(IMemory m, uint entity, System.Action<uint> action)
    {
        uint index = m.ReadU32(entity + 0x64);
        if (index == 0xFFFFFFFF) return;
        uint prim = PrimBuf + index * PrimSize;
        for (int guard = 0; prim != 0 && guard < 256; guard++)
        {
            action(prim);
            prim = m.ReadU32(prim);
        }
    }

    // ---- color_randomizer: Joseph's cloak ----
    // Rom.py hooks HandlePlay (0x800E4BA4 -> jal 0x80136C00) with a routine storing six colours into the
    // custom cloak settings. Do what the routine does: its 'li v1,colour' immediates are at 0x80136C08+16i.
    const uint CloakHook = 0x800E4BA4, CloakHookJal = 0x0C04DB00;
    const uint CloakRoutineColours = 0x80136C08, CloakSettings = 0x8003CAA8;

    [PostHook("dra", "HandlePlay")]
    static void AfterHandlePlay(CpuContext c, IMemory m)
    {
        if (!Active || m.ReadU32(CloakHook) != CloakHookJal) return;
        for (uint i = 0; i < 6; i++)
            m.WriteU8(CloakSettings + i * 4, m.ReadU8(CloakRoutineColours + i * 16));
    }

    // ---- color_randomizer: Hydro Storm (Richter) ----
    // Five colour immediates in RicEntityCrashHydroStorm; the seed's values come from the payload's RIC
    // data. Applied to the sprite pieces once the crash has set them up (step 0).
    static readonly (uint FileOffset, uint[] PrimBytes)[] HydroColours =
    [
        (0x2BFBC, [0x04, 0x05]), (0x2BFC8, [0x06]), (0x2BFD0, [0x10]), (0x2BFD8, [0x11]), (0x2BFE0, [0x12]),
    ];
    static uint _hydro;
    static ushort _hydroStep;

    [PreHook("ric", "RicEntityCrashHydroStorm")]
    static void BeforeHydroStorm(CpuContext c, IMemory m)
    {
        _hydro = c.A0;
        _hydroStep = m.ReadU16(c.A0 + 0x2C);
    }

    [PostHook("ric", "RicEntityCrashHydroStorm")]
    static void AfterHydroStorm(CpuContext c, IMemory m)
    {
        if (!Active || _hydro == 0 || _hydroStep != 0 || m.ReadU16(_hydro + 0x2C) == 0) return;
        var ric = SeedPlan.RichterOverlayBytes;
        if (ric.Count == 0) return;
        ForEachPrim(m, _hydro, prim =>
        {
            foreach (var (offset, bytes) in HydroColours)
                if (ric.TryGetValue(offset, out var colour))
                    foreach (var b in bytes) m.WriteU8(prim + b, colour);
        });
    }

    // ---- skip_nz1: one gear turn opens the Clock Tower door ----
    // Rom.py makes a gear finishing its turn write 0x000F (all four solved) to the puzzle mask.
    static uint _gear;
    static ushort _gearCount;

    [PreHook("nz1", "EntityWallGear")]
    static void BeforeGear(CpuContext c, IMemory m) => RememberGear(c, m);

    [PostHook("nz1", "EntityWallGear")]
    static void AfterGear(CpuContext c, IMemory m) => SolveOnTurn(m, 0x801A8A1C, 0x80180FD0);

    [PreHook("rnz1", "func_801A81C8")]
    static void BeforeReverseGear(CpuContext c, IMemory m) => RememberGear(c, m);

    [PostHook("rnz1", "func_801A81C8")]
    static void AfterReverseGear(CpuContext c, IMemory m) => SolveOnTurn(m, 0x801A8300, 0x80180F6C);

    static void RememberGear(CpuContext c, IMemory m)
    {
        _gear = c.A0;
        _gearCount = m.ReadU16(c.A0 + 0x80);
    }

    static void SolveOnTurn(IMemory m, uint patchSite, uint puzzleMask)
    {
        const uint LiV1Is15 = 0x2403000F;
        if (!Active || _gear == 0 || m.ReadU32(patchSite) != LiV1Is15) return;
        if (_gearCount == 1 && m.ReadU16(_gear + 0x80) == 0) m.WriteU16(puzzleMask, 0x000F);
    }

    // ---- starting_zone (reverse castle): no Richter cutscene in the Castle Keep ----
    // Rom.py makes TOP_EntityCutscene read castle flag 0x96 as set ('li v0,1' at 0x801AC1E8).
    const uint RichterCutsceneSite = 0x801AC1E8, LiV0Is1 = 0x34020001;
    const uint RichterCutsceneFlag = Progress.CastleFlagsAddr + 0x96;
    static bool _cutsceneSpoofed;

    [PreHook("top", "TOP_EntityCutscene")]
    static void BeforeKeepCutscene(CpuContext c, IMemory m)
    {
        _cutsceneSpoofed = false;
        if (!Active || m.ReadU32(RichterCutsceneSite) != LiV0Is1 || m.ReadU8(RichterCutsceneFlag) != 0) return;
        m.WriteU8(RichterCutsceneFlag, 1);
        _cutsceneSpoofed = true;
    }

    [PostHook("top", "TOP_EntityCutscene")]
    static void AfterKeepCutscene(CpuContext c, IMemory m)
    {
        if (_cutsceneSpoofed && m.ReadU8(RichterCutsceneFlag) == 1) m.WriteU8(RichterCutsceneFlag, 0);
        _cutsceneSpoofed = false;
    }

    // ---- F_GAME / F_GAME2: map colour, MP vessel sprite, Richter palettes ----
    // These files stream through a ring buffer to VRAM, 0x2000 bytes at a time, and are never whole in
    // RAM. func_801080DC uploads the next chunk; patch the chunk just before it goes.
    const uint CdLoadType = 0x80137F58, RingPosition = 0x80137F70, ChunkIndex = 0x80137F74;
    const uint CdFileLba = 0x800AC9F8;
    const int FGameLba = 25038, FGame2Lba = 25170;
    const int ChunkSize = 0x2000;

    [PreHook("dra", "func_801080DC")]
    static void BeforeGraphicsChunk(CpuContext c, IMemory m)
    {
        if (!Active || m.ReadU32(CdLoadType) != 1) return;
        uint buffer = m.ReadU32(RingPosition) switch { 7 => 0x801EE000u, 3 => 0x801EC000u, _ => 0u };
        if (buffer == 0) return;
        var file = (int)m.ReadU32(CdFileLba) switch
        {
            FGameLba => SeedPlan.GraphicsBytes("F_GAME"),
            FGame2Lba => SeedPlan.GraphicsBytes("F_GAME2"),
            _ => null,
        };
        int chunk = (int)m.ReadU32(ChunkIndex);
        if (file == null || !file.TryGetValue(chunk, out var writes)) return;
        foreach (var (offset, value) in writes)
            m.WriteU8(buffer + (uint)(offset - chunk * ChunkSize), value);
    }
}
