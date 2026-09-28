using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// Logs every change to the flags that location checks are built from, so we can
// confirm what each pickup, boss and relic actually changes before sending checks.
static class GameWatch
{
    // Beyond this many changes in one frame, assume a save load or reset and log one summary line.
    const int BulkThreshold = 32;

    static readonly byte[] _castleFlags = new byte[Progress.CastleFlagCount];
    static readonly uint[] _timeAttack = new uint[Progress.TimeAttackCount];
    static readonly byte[] _relics = new byte[Inventory.RelicCount];

    static GameState _state = (GameState)0xFF;
    static int _stage = -1;
    static bool _baselined;
    static int _changesThisFrame;

    public static bool Enabled = true;

    public static GameState State => _state;
    public static int StageId => _stage;

    public static void Tick(IMemory m, long frame)
    {
        var state = (GameState)m.ReadU8(Game.GameStateAddr);
        if (state != _state)
        {
            Log.Info($"game state {_state} -> {state}");
            _state = state;
        }

        int stage = m.ReadU8(Game.StageIdAddr);
        if (stage != _stage)
        {
            if (state == GameState.Play) Log.Info($"stage {Describe(_stage)} -> {Describe(stage)}");
            _stage = stage;
        }

        // Only diff during play. Outside play (title, file select, loading a save) keep the
        // snapshot current without logging, so a save load doesn't flood the log.
        bool diff = Enabled && _baselined && state == GameState.Play;
        _changesThisFrame = 0;

        for (int i = 0; i < _castleFlags.Length; i++)
        {
            byte v = m.ReadU8(Progress.CastleFlagsAddr + (uint)i);
            if (v == _castleFlags[i]) continue;
            if (diff) Report($"castle flag 0x{i:X3}: 0x{_castleFlags[i]:X2} -> 0x{v:X2}");
            _castleFlags[i] = v;
        }

        for (int i = 0; i < _timeAttack.Length; i++)
        {
            uint v = m.ReadU32(Progress.TimeAttackAddr + (uint)(i * 4));
            if (v == _timeAttack[i]) continue;
            if (diff) Report($"time attack {(TimeAttackEvent)i} ({i}): {_timeAttack[i]} -> {v}");
            _timeAttack[i] = v;
        }

        for (int i = 0; i < _relics.Length; i++)
        {
            byte v = m.ReadU8(Game.StatusAddr + (uint)i);
            if (v == _relics[i]) continue;
            if (diff) Report($"relic {(Relic)i} ({i}): 0x{_relics[i]:X2} -> 0x{v:X2}");
            _relics[i] = v;
        }

        if (_changesThisFrame > BulkThreshold)
            Log.Info($"frame {frame}: {_changesThisFrame} changes in one frame (probably a load), rest not shown");

        _baselined = true;
    }

    static void Report(string message)
    {
        _changesThisFrame++;
        if (_changesThisFrame <= BulkThreshold)
            Log.Info($"stage {Describe(_stage)}: {message}");
    }

    static string Describe(int stage) => stage < 0 ? "none" : $"{(Stage)stage} (0x{stage:X2})";
}
