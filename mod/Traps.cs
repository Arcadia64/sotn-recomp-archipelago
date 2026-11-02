#nullable enable
using System.Collections.Generic;
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// This game's traps (trap_percentage in the apworld). Each one hits Alucard the way an enemy's attack of that
// element does: the game reads PLAYER.hitParams and hitPoints each frame (HandleDamage), so the effect, its
// length, the animation and gear that resists it are all the game's own. A hit for 0 still takes 1 HP (not for
// stone), so nothing goes off at 1 HP: a trap never kills by itself.
//
// One at a time, a few seconds apart, in normal movement only. A hit is ignored while Alucard is invincible and
// could leave him stuck in a form or a special move, so the trap waits instead.
static class Traps
{
    const int SmallHit = 2;   // DAMAGEKIND_2, added to the element in hitParams
    static readonly Dictionary<long, int> Hits = new()
    {
        [430] = 0x80 | SmallHit,    // Poison trap: ELEMENT_POISON
        [431] = 0x200 | SmallHit,   // Stone trap: ELEMENT_STONE
        [432] = 0x100 | SmallHit,   // Curse trap: ELEMENT_CURSE
    };

    const uint HitPointsAddr = 0x80073416, HitParamsAddr = 0x8007341C;
    const uint HpAddr = 0x80097BA0;
    const ushort LastMovementStep = 4;      // Player_Stand, Walk, Crouch, Fall, Jump
    const int PastIntroFlag = 0x34;
    const int FramesBetween = 60 * 5;

    static readonly Queue<(long Item, string? From)> _pending = new();
    static string _pendingFor = "";
    static long _lastFrame = -FramesBetween;

    // By the item, not its flags: the server's /send gives an item without them.
    public static bool IsTrap(long item) => Hits.ContainsKey(item);

    // A trap you found yourself (from: null), or one sent to you.
    public static void Queue(long item, string? from)
    {
        string seed = ApClient.SeedIdentity;
        if (seed != _pendingFor) _pending.Clear();
        _pendingFor = seed;
        _pending.Enqueue((item, from));
    }

    // A trap among the received items. Loading an older save gives its items again, but a trap only goes off
    // the first time it's received; the count is kept with the seed, not the save.
    public static void OnReceived(int index, long item, string from)
    {
        if (index < ApClient.TrapsHandled) return;
        ApClient.TrapsHandled = index + 1;
        Queue(item, from);
    }

    public static void Tick(IMemory m, long frame)
    {
        if (_pending.Count == 0 || frame - _lastFrame < FramesBetween) return;
        if (!ApClient.HasSeed || ApClient.SeedIdentity != _pendingFor) { _pending.Clear(); return; }
        if (SaveLink.Check(m) != SaveLink.Status.ThisSeed) return;
        if (m.ReadU8(Game.GameStateAddr) != (byte)GameState.Play) return;
        if (CheckTracker.GoalReached(m)) { _pending.Clear(); return; }
        if (!Safe(m)) return;

        var (item, from) = _pending.Dequeue();
        _lastFrame = frame;
        string name = ApClient.ItemName(item, ApClient.Slot);
        string text = from == null ? $"{name}!" : $"{name} from {from}!";
        Log.Info(text);
        ApClient.ShowToast("Trap", text);
        if (!Hits.TryGetValue(item, out int hit)) return;
        m.WriteU16(HitPointsAddr, 0);
        m.WriteU16(HitParamsAddr, (ushort)hit);
    }

    static bool Safe(IMemory m)
    {
        if (m.ReadU8(Progress.CastleFlagsAddr + PastIntroFlag) != 1 || !GameRules.SafeToKill(m)) return false;
        if (m.ReadU32(HpAddr) <= 1) return false;
        if (m.ReadU16(Player.StepAddr) > LastMovementStep) return false;
        if (((PlayerStatus)m.ReadU32(Player.StatusFlagsAddr) & PlayerStatus.Stone) != 0) return false;
        if (Player.GetTimer(AluTimer.Invincible) != 0 || Player.GetTimer(AluTimer.InvincibleConsumables) != 0) return false;
        return m.ReadU16(HitParamsAddr) == 0;   // a real hit landing this frame comes first
    }
}
