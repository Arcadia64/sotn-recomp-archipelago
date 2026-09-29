#nullable enable
using System.Collections.Generic;
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// Gives Alucard the items the server sends, and own items the game can't hand out (OwedItems).
// Only while it's safe (normal play, not paused, alive), one item at a time so each gets its
// own message. The count of server items already given is saved with the game (SaveLink), so
// loading an older save gives back anything received after it.
static class ItemGiver
{
    const int GrantInterval = 20; // frames between items

    public const uint EngineStepAddr = 0x8003C9A4; // g_GameEngineStep
    public const byte EngineNormal = 1;             // normal play (3 = room transition, see Sotn.Stages)
    const int StuckReportFrames = 300;              // say why items are waiting after ~5 seconds
    const uint PlayerStepAddr = 0x80073404;   // PLAYER.step: 16 = dying, 17 = fairy revive
    const uint HpAddr = 0x80097BA0;
    const uint HpMaxAddr = 0x80097BA4;
    const uint HeartsAddr = 0x80097BA8;
    const uint HeartsMaxAddr = 0x80097BAC;
    const uint MpAddr = 0x80097BB0, MpMaxAddr = 0x80097BB4;
    const uint MagicVesselPatchAddr = 0x800FE0F4, MagicVesselPatchWord = 0x10400003; // magic_vessels (Rom.py)
    const uint RelicBase = 0x80097964;
    const long FirstCard = 318, LastCard = 322;
    const long FirstBodyItem = 169;

    static readonly Queue<(long Item, string Why)> _direct = new();
    static string _directFor = "";   // the seed the queued direct items belong to (ApClient.SeedIdentity)
    static long _lastGrantFrame;
    static long _waitingSince = -1;
    static long _lastStuckReport = -1;

    public static int PendingDirect => _direct.Count;

    public static void QueueDirect(long item, string why)
    {
        string seed = ApClient.SeedIdentity;
        if (seed != _directFor) _direct.Clear();
        _directFor = seed;
        _direct.Enqueue((item, why));
    }

    public static void Tick(IMemory m, long frame)
    {
        if (!ApClient.HasSeed) return; // offline: the items received so far, from the seed cache
        if (frame - _lastGrantFrame < GrantInterval) return;
        if (_direct.Count > 0 && ApClient.SeedIdentity != _directFor) _direct.Clear(); // another seed now

        var received = ApClient.Received;
        bool pending = _direct.Count > 0 || SaveLink.ReceivedCount(m) < received.Length || OwedItems.Next(m) != null;
        string? blocked = WhyNotSafe(m);
        if (blocked == null && SaveLink.Claim(m) != SaveLink.Status.ThisSeed) blocked = "save not linked to this seed";
        if (blocked != null)
        {
            ReportStuck(pending, blocked, frame);
            return;
        }
        _waitingSince = -1;

        int given = SaveLink.ReceivedCount(m);
        if (given < received.Length)
        {
            var item = received[given];
            Give(m, item.Item);
            SaveLink.SetReceivedCount(m, given + 1);
            _lastGrantFrame = frame;
            string from = item.Player == ApClient.Slot ? "yourself" : ApClient.PlayerName(item.Player);
            string name = ApClient.ItemName(item.Item, ApClient.Slot);
            Log.Item($"Received {name} from {from}", item);
            ApClient.ShowToast($"Received ({ItemClass.Name(item)})", $"{name} from {from}");
            return;
        }

        if (_direct.Count > 0)
        {
            var (id, why) = _direct.Dequeue();
            Give(m, id);
            _lastGrantFrame = frame;
            Announce($"Got {ApClient.ItemName(id, ApClient.Slot)} ({why})");
            return;
        }

        if (SeedPlan.Ready && OwedItems.Next(m) is { } owed)
        {
            Give(m, owed.Item);
            OwedItems.MarkGiven(m, owed);
            _lastGrantFrame = frame;
            Announce($"Got {ApClient.ItemName(owed.Item, ApClient.Slot)} ({owed.Why})");
        }
    }

    static void Announce(string text)
    {
        Log.Info(text);
        ApClient.ShowToast("Archipelago", text);
    }

    // null when it's safe to change the inventory, otherwise the reason it isn't.
    static string? WhyNotSafe(IMemory m)
    {
        var state = (GameState)m.ReadU8(Game.GameStateAddr);
        if (state != GameState.Play) return $"game state {state}";
        if (m.ReadU8(Progress.CastleFlagsAddr + 0x34) != 1) return "Alucard's game hasn't started";
        byte engine = m.ReadU8(EngineStepAddr);
        if (engine != EngineNormal) return $"engine step {engine}";
        if (m.ReadU8(Game.MenuOpenAddr) != 0) return "menu open";
        if (m.ReadU8(Game.MapOpenAddr) != 0) return "map open";
        if (m.ReadU32(HpAddr) == 0) return "HP is 0";
        byte step = m.ReadU8(PlayerStepAddr);
        return step is 16 or 17 ? $"player step {step} (dying)" : null;
    }

    static void ReportStuck(bool pending, string reason, long frame)
    {
        if (!pending) { _waitingSince = -1; return; }
        if (_waitingSince < 0) _waitingSince = frame;
        if (frame - _waitingSince < StuckReportFrames) return;
        if (_lastStuckReport >= 0 && frame - _lastStuckReport < StuckReportFrames * 12) return;
        _lastStuckReport = frame;
        Log.Info($"items waiting to be given: {reason}");
    }

    static void Give(IMemory m, long id)
    {
        if (id >= ItemData.FirstRelic && id <= ItemData.LastRelic)
        {
            uint addr = RelicBase + (uint)(id - ItemData.FirstRelic);
            // Owned and switched on, except familiar cards, which start switched off (BizHawk client).
            if (m.ReadU8(addr) == 0) m.WriteU8(addr, (byte)(id >= FirstCard && id <= LastCard ? 1 : 3));
            return;
        }
        if (id == ItemData.HeartVessel)
        {
            m.WriteU32(HeartsMaxAddr, m.ReadU32(HeartsMaxAddr) + 5);
            m.WriteU32(HeartsAddr, m.ReadU32(HeartsAddr) + 5);
            // magic_vessels: as for one picked up, where the recomp adds this in the vessel code
            // (RandoPatch.MagicMaxUp_Pre, when the patched word is in RAM): max MP +3, MP refilled.
            if (m.ReadU32(MagicVesselPatchAddr) == MagicVesselPatchWord)
            {
                uint mp = m.ReadU32(MpMaxAddr) + 3;
                m.WriteU32(MpMaxAddr, mp);
                m.WriteU32(MpAddr, mp);
            }
            return;
        }
        if (id == ItemData.LifeVessel)
        {
            uint max = m.ReadU32(HpMaxAddr) + 5;
            m.WriteU32(HpMaxAddr, max);
            m.WriteU32(HpAddr, max);
            return;
        }
        if (id >= 1 && id < FirstBodyItem)
        {
            GameApi.AddToInventory((int)id, (int)EquipKind.Hand);
            return;
        }
        if (id >= FirstBodyItem && id <= 258)
        {
            int body = (int)(id - FirstBodyItem);
            GameApi.AddToInventory(body, (int)Inventory.KindOf(body));
            return;
        }
        Log.Info($"item {id} has no in-game effect, skipped");
    }
}
