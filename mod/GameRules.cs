#nullable enable
using System.Collections.Generic;
using System.Text.Json.Nodes;
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// Per-frame game-side rules from the BizHawk client, adapted: DeathLink, heal_at_save_rooms, soft-lock
// escapes, the Cave demon wall, and the zone/room keys the tracker can read.
static class GameRules
{
    const int PastIntroFlag = 0x34;
    const int PrologueStage = 0x1F;
    const int CaveStage = 0x25;          // RCHI
    const int CaveDemonWallFlag = 0x59;  // set = the demon wall in the Cave is already broken

    const uint HpAddr = 0x80097BA0;
    const uint HpMaxAddr = 0x80097BA4;
    const uint MpAddr = 0x80097BB0;
    const uint MpMaxAddr = 0x80097BB4;
    const uint PlayerStepAddr = 0x80073404;     // PLAYER.step: 16 = dying, 17 = fairy revive
    const uint PlayerStepSubAddr = 0x80073406;  // PLAYER.step_s
    const ushort PlayerStepDying = 0x10;
    const uint DemoTimerAddr = 0x80072EFC;      // frames a cutscene still plays input for Alucard (0 = none)
    const uint RoomFingerprintAddr = 0x80073084;
    const uint RelicBase = 0x80097964;
    const int LibraryCard = 166;                // hand item

    // A death we caused is not sent back out.
    const long OwnKillGraceFrames = 60 * 20;

    static GameState _lastState = (GameState)0xFF;
    static int _lastStage = -1;
    static ushort _lastRoom;
    static int _reportedStage = -1;
    static ushort _reportedRoom;
    static int _reportedFor = -1;
    static long _killedByLinkAt = -100000;
    static string? _pendingDeath;
    static bool _remindedAtMenu;

    public static void Tick(IMemory m, long frame)
    {
        var state = (GameState)m.ReadU8(Game.GameStateAddr);
        int stage = m.ReadU8(Game.StageIdAddr);
        ushort room = m.ReadU16(RoomFingerprintAddr);
        bool connected = ApClient.HasSeed;

        RemindIfNotConnected(state, connected);

        bool inAlucardGame = state == GameState.Play && m.ReadU8(Progress.CastleFlagsAddr + PastIntroFlag) == 1;
        bool linked = connected && SaveLink.Check(m) == SaveLink.Status.ThisSeed;

        if (linked)
        {
            SendDeathIfDied(m, state, frame);
            if (inAlucardGame)
            {
                ApplyDeathLink(m, frame);
                AutoHeal(m);
                OpenShortcuts(m, stage);
                if (stage != _lastStage) OnStageChanged(m, _lastStage, stage);
                ReportPosition(stage, room);
            }
        }

        _lastState = state;
        if (state == GameState.Play)
        {
            _lastStage = stage;
            _lastRoom = room;
        }
    }

    // ---- DeathLink ----

    public static void OnDeathLink(string cause) => _pendingDeath = cause;

    static void ApplyDeathLink(IMemory m, long frame)
    {
        if (_pendingDeath == null || ApClient.OptionInt("death_link") == 0 || CheckTracker.GoalReached(m)) { _pendingDeath = null; return; }
        if (!SafeToKill(m)) return;

        Log.Info($"DeathLink: {_pendingDeath}");
        ApClient.ShowToast("DeathLink", _pendingDeath);
        // As the game's own instant death (Player.InstantDeath): the dying step from its start. Only the
        // step, with step_s left where it was, can leave Alucard frozen.
        m.WriteU32(HpAddr, 0);
        m.WriteU16(PlayerStepAddr, PlayerStepDying);
        m.WriteU16(PlayerStepSubAddr, 0);
        _killedByLinkAt = frame;
        _pendingDeath = null;
    }

    // Normal play only: never mid-transformation (the BizHawk client soft-locks when killed mid wing
    // smash), in a menu, during a room transition or a cutscene, or while already dying.
    static bool SafeToKill(IMemory m)
    {
        if (m.ReadU8(ItemGiver.EngineStepAddr) != ItemGiver.EngineNormal) return false;
        if (m.ReadU8(Game.MenuOpenAddr) != 0 || m.ReadU8(Game.MapOpenAddr) != 0) return false;
        if (m.ReadU16(DemoTimerAddr) != 0) return false;
        if (m.ReadU32(HpAddr) == 0) return false;
        byte step = m.ReadU8(PlayerStepAddr);
        if (step is 16 or 17) return false;
        var status = (PlayerStatus)m.ReadU32(Player.StatusFlagsAddr);
        return (status & (PlayerStatus.Transform | PlayerStatus.Dead)) == 0;
    }

    // A real death ends in the Game Over screen (a fairy revive doesn't). The end of the Richter
    // prologue also passes through Game Over, but before Alucard's game has started.
    static void SendDeathIfDied(IMemory m, GameState state, long frame)
    {
        if (state != GameState.GameOver || _lastState != GameState.Play) return;
        if (m.ReadU8(Progress.CastleFlagsAddr + PastIntroFlag) != 1 || _lastStage == PrologueStage) return;
        if (ApClient.OptionInt("death_link") == 0) return;
        if (frame - _killedByLinkAt < OwnKillGraceFrames) return;

        string where = ZoneData.Names.TryGetValue(_lastStage, out var zone) ? $" in {zone}" : "";
        Log.Info($"DeathLink: sending our death{where}");
        ApClient.SendDeath($"{ApClient.SlotName} died{where}");
    }

    // ---- heal_at_save_rooms ----

    static void AutoHeal(IMemory m)
    {
        if (ApClient.OptionInt("heal_at_save_rooms") == 0) return;
        if ((m.ReadU8(Game.CanSaveAddr) & Game.CanSaveMask) != Game.CanSaveMask) return;
        uint hpMax = m.ReadU32(HpMaxAddr), mpMax = m.ReadU32(MpMaxAddr);
        if (m.ReadU32(HpAddr) != hpMax) m.WriteU32(HpAddr, hpMax);
        if (m.ReadU32(MpAddr) != mpMax) m.WriteU32(MpAddr, mpMax);
    }

    // ---- caverns_back_door, colosseum_back_door, open_shortcuts ----
    // Rom.py makes the doors, levers and blocks involved read their castle flag as set ('li v0,1'); the
    // recomp doesn't read those code bytes, so set the flags themselves, as opening them in play would.
    const int EntranceToCaverns = 0x30, EntranceToMarble = 0x31, EntranceWarp = 0x32;
    const int ChapelStatue = 0x60, ColosseumToChapel = 0xB1;
    const int CastleEntranceAfterAlchemyLab = 0x07; // NP3

    static void OpenShortcuts(IMemory m, int stage)
    {
        if (ApClient.OptionInt("open_shortcuts") > 0)
            foreach (int flag in new[] { EntranceToCaverns, EntranceToMarble, EntranceWarp, ChapelStatue, ColosseumToChapel })
                SetFlag(m, flag);

        // open_after_alchemy_lab (1): from the entrance after visiting the Alchemy Laboratory; open_from_start (2).
        int no4 = ApClient.OptionInt("caverns_back_door");
        if (no4 == 2 || no4 == 1 && stage == CastleEntranceAfterAlchemyLab) SetFlag(m, EntranceToCaverns);

        if (ApClient.OptionInt("colosseum_back_door") > 0) SetFlag(m, ColosseumToChapel);
    }

    static void SetFlag(IMemory m, int flag)
    {
        uint addr = Progress.CastleFlagsAddr + (uint)flag;
        if (m.ReadU8(addr) == 0) m.WriteU8(addr, 1);
    }

    // ---- stage changes ----

    static void OnStageChanged(IMemory m, int from, int to)
    {
        string? fromName = ZoneData.Names.GetValueOrDefault(from);
        string? toName = ZoneData.Names.GetValueOrDefault(to);

        // Places you can walk into but not out of without a movement relic: give a Library card.
        bool trap = fromName == "Underground Caverns" && toName == "Abandoned Mine"
                 || fromName == "Marble Gallery" && toName == "Center Cube";
        if (trap && !CanEscape(m))
        {
            GameApi.AddToInventory(LibraryCard, (int)EquipKind.Hand);
            Log.Info($"gave a Library card so you can leave {toName}");
            ApClient.ShowToast("Archipelago", "Got a Library card to escape");
        }

        if (to == CaveStage && m.ReadU8(Progress.CastleFlagsAddr + CaveDemonWallFlag) == 0)
            m.WriteU8(Progress.CastleFlagsAddr + CaveDemonWallFlag, 1);
    }

    static bool CanEscape(IMemory m)
    {
        bool Has(int relic) => m.ReadU8(RelicBase + (uint)relic) != 0;
        if (Has(0)) return true;                  // Soul of bat
        if (Has(7) && Has(8)) return true;        // Form of mist + Power of mist
        if (Has(12) && Has(13)) return true;      // Gravity boots + Leap stone
        return Inventory.GetHandCount(LibraryCard) > 0;
    }

    // ---- tracker ----

    // Same keys and values as the BizHawk client: the AP zone name and the room fingerprint.
    static void ReportPosition(int stage, ushort room)
    {
        int slot = ApClient.Slot;
        if (_reportedFor != ApClient.ConnectionId) { _reportedFor = ApClient.ConnectionId; _reportedStage = -1; _reportedRoom = 0; }
        if (stage != _reportedStage && ZoneData.Names.TryGetValue(stage, out var zone))
        {
            ApClient.SetDataStorage($"sotn_zone_{slot}", JsonValue.Create(zone)!);
            _reportedStage = stage;
        }
        if (room != _reportedRoom)
        {
            ApClient.SetDataStorage($"sotn_room_{slot}", JsonValue.Create(room)!);
            _reportedRoom = room;
        }
    }

    // ---- reminder ----

    static void RemindIfNotConnected(GameState state, bool connected)
    {
        if (state != GameState.MainMenu) { _remindedAtMenu = false; return; }
        if (connected || _remindedAtMenu) return;
        _remindedAtMenu = true;
        ApClient.ShowToast("Archipelago", "Not connected. A new game needs the connection (menu bar > Archipelago > Connection); saves you've played on this PC can also be loaded offline.");
    }
}
