#nullable enable
using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;
using Sotn;

namespace SotnArchipelago;

// The Archipelago window: connection, the seed and save at a glance, details for bug reports, and the log.
public sealed class ArchipelagoPanel : IPanel
{
    const string ServerKey = "Archipelago.Server";
    const string SlotKey = "Archipelago.Slot";
    const string AutoConnectKey = "Archipelago.AutoConnect";

    // ImGui colours (0xAABBGGRR) for the status line.
    const uint Green = 0xFF5AD25A, Yellow = 0xFF40C8F0, Orange = 0xFF3CA0F0, Grey = 0xFF909090, Red = 0xFF5A5AE6;

    string _server;
    string _slot;
    string _password = "";
    bool _autoConnect;
    bool _autoScroll = true;

    public ArchipelagoPanel()
    {
        var view = RecompOne.Runtime.Runtime.View;
        _server = view.GetString(ServerKey, "archipelago.gg:38281");
        _slot = view.GetString(SlotKey, "");
        _autoConnect = view.GetBool(AutoConnectKey, true);
    }

    // When the game starts: connect to the last server and slot, if that's on (the password isn't kept, so a
    // room with one needs Connect by hand).
    public void ConnectOnStart()
    {
        if (!_autoConnect || ApClient.State != ConnectionState.Disconnected) return;
        if (_server.Trim().Length == 0 || _slot.Trim().Length == 0) return;
        Log.Info($"connecting to {_server} as {_slot} (automatic; turn it off in the Archipelago window)");
        ApClient.Connect(_server, _slot, _password);
    }

    // The map window (ArchipelagoMod registers both), opened from here.
    public MapPanel? Map { get; set; }

    public string Name => "Archipelago";
    public string TitleKey => "panel.archipelago";
    public bool IsOpen { get; set; }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        DrawStatusLine();
        DrawConnection();
        if (ApClient.HasSeed)
        {
            ImGui.Spacing();
            ImGui.SeparatorText("Seed");
            DrawSeed();
        }
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Details")) DrawDetails();
        ImGui.SeparatorText("Log");
        DrawLog();

        IsOpen = open;
        ImGui.End();
    }

    // ---- connection ----

    static void DrawStatusLine()
    {
        var state = ApClient.State;
        uint colour = state switch
        {
            ConnectionState.Connected => Green,
            ConnectionState.Connecting => Orange,
            _ => ApClient.Offline ? Yellow : Grey,
        };
        var at = ImGui.GetCursorScreenPos();
        float size = ImGui.GetTextLineHeight();
        ImGui.GetWindowDrawList().AddCircleFilled(at + new Vector2(size / 2, size / 2), size / 3, colour);
        ImGui.Dummy(new Vector2(size, size));
        ImGui.SameLine();
        ImGui.TextWrapped(ApClient.Status);
    }

    void DrawConnection()
    {
        var state = ApClient.State;
        bool editable = state == ConnectionState.Disconnected;

        if (!editable) ImGui.BeginDisabled();
        ImGui.InputText("Server", ref _server, 128);
        ImGui.InputText("Slot name", ref _slot, 64);
        ImGui.InputText("Password", ref _password, 64, ImGuiInputTextFlags.Password);
        if (!editable) ImGui.EndDisabled();

        if (state == ConnectionState.Disconnected)
        {
            bool ready = _server.Trim().Length > 0 && _slot.Trim().Length > 0;
            if (!ready) ImGui.BeginDisabled();
            if (ImGui.Button("Connect"))
            {
                SaveFields();
                ApClient.Connect(_server, _slot, _password);
            }
            if (!ready) ImGui.EndDisabled();
        }
        else if (ImGui.Button("Disconnect"))
        {
            ApClient.Disconnect();
        }
        ImGui.SameLine();
        if (ImGui.Checkbox("Connect when the game starts", ref _autoConnect))
        {
            RecompOne.Runtime.Runtime.View.SetBool(AutoConnectKey, _autoConnect);
            RecompOne.Runtime.Runtime.SaveView();
        }
    }

    void SaveFields()
    {
        var view = RecompOne.Runtime.Runtime.View;
        view.SetString(ServerKey, _server.Trim());
        view.SetString(SlotKey, _slot.Trim());
        RecompOne.Runtime.Runtime.SaveView();
    }

    // ---- the seed ----

    void DrawSeed()
    {
        ImGui.Text($"{ApClient.SlotName}, seed {ApClient.SeedName}");
        ImGui.Text($"{ApClient.CheckedCount} of {ApClient.LocationCount} locations checked");
        if (Map != null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(Map.IsOpen ? "Close map" : "Open map")) Map.IsOpen = !Map.IsOpen;
        }
        if (!SeedPlan.Ready) ImGui.TextDisabled("Getting the seed ready...");

        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null || !Game.Available) return;
        switch (SaveLink.Check(m))
        {
            case SaveLink.Status.ThisSeed:
                int received = ApClient.Received.Length, given = SaveLink.ReceivedCount(m), waiting = System.Math.Max(0, received - given) + ItemGiver.PendingDirect;
                ImGui.Text(waiting > 0 ? $"Items received: {received} ({waiting} waiting to be given)" : $"Items received: {received}, all given");
                break;
            case SaveLink.Status.OtherSeed:
                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(Red), "This save is from a different seed: nothing is placed, sent or received.");
                break;
            default:
                ImGui.TextDisabled("The save links to this seed once Alucard's game starts.");
                break;
        }
    }

    // ---- details, for bug reports ----

    public void DrawDetails()
    {
        if (ApClient.HasSeed)
        {
            ImGui.Text($"Slot {ApClient.Slot}, {ApClient.ScoutCount} locations scouted");
            if (SeedPlan.Ready) ImGui.Text($"Items placed from {(SeedPlan.FromPayload ? "the seed's data" : "scouts")}; {SeedPlan.UnsupportedCount} spot(s) given directly instead");
        }
        if (!Game.Available)
        {
            ImGui.TextDisabled("Game not running.");
            return;
        }
        int stage = GameWatch.StageId;
        ImGui.Text($"Game state: {GameWatch.State}");
        ImGui.Text(stage < 0 ? "Stage: none" : $"Stage: {(Stage)stage} (0x{stage:X2}), area {Game.Area}, room {Game.Room}");
        ImGui.Checkbox("Log flag changes", ref GameWatch.Enabled);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Logs every castle flag the game changes: useful for bug reports, noisy otherwise.");
    }

    // ---- log ----

    void DrawLog()
    {
        if (ImGui.SmallButton("Clear")) Log.Clear();
        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);

        if (ImGui.BeginChild("##aplog", Vector2.Zero, ImGuiChildFlags.Border))
        {
            foreach (var line in Log.Snapshot())
            {
                if (line.Colour is { } colour) ImGui.PushStyleColor(ImGuiCol.Text, colour);
                ImGui.TextWrapped(line.Text);
                if (line.Colour != null) ImGui.PopStyleColor();
            }
            if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                ImGui.SetScrollHereY(1.0f);
        }
        ImGui.EndChild();
    }
}
