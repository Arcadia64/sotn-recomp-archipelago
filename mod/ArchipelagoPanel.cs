using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;
using Sotn;

namespace SotnArchipelago;

public sealed class ArchipelagoPanel : IPanel
{
    const string ServerKey = "Archipelago.Server";
    const string SlotKey = "Archipelago.Slot";

    string _server;
    string _slot;
    string _password = "";
    bool _autoScroll = true;

    public ArchipelagoPanel()
    {
        var view = RecompOne.Runtime.Runtime.View;
        _server = view.GetString(ServerKey, "archipelago.gg:38281");
        _slot = view.GetString(SlotKey, "");
    }

    public string Name => "Archipelago";
    public bool IsOpen { get; set; }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(480, 560), ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        DrawConnection();
        ImGui.Separator();
        DrawStatus();
        ImGui.Separator();
        DrawLog();

        IsOpen = open;
        ImGui.End();
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
        ImGui.TextWrapped(ApClient.Status);

        if (state == ConnectionState.Connected)
        {
            ImGui.Text($"Seed {ApClient.SeedName}  |  checked {ApClient.CheckedCount}/{ApClient.LocationCount}  |  scouted {ApClient.ScoutCount}");
            ImGui.Text(SeedPlan.Ready
                ? $"Items placed ({SeedPlan.UnsupportedCount} special spot(s) not placed yet)"
                : "Items not placed yet (waiting for scouts)");

            var m = RecompOne.Runtime.Runtime.Mem;
            if (m != null && Game.Available)
            {
                var link = SaveLink.Check(m);
                string save = link switch
                {
                    SaveLink.Status.ThisSeed => "linked to this seed",
                    SaveLink.Status.OtherSeed => "FROM A DIFFERENT SEED (paused)",
                    _ => "not linked yet (links once Alucard's game starts)",
                };
                ImGui.Text($"Save: {save}");
                if (link == SaveLink.Status.ThisSeed)
                    ImGui.Text($"Items given: {SaveLink.ReceivedCount(m)}/{ApClient.Received.Length} received, {ItemGiver.PendingDirect} direct waiting");
            }
        }
    }

    void SaveFields()
    {
        var view = RecompOne.Runtime.Runtime.View;
        view.SetString(ServerKey, _server.Trim());
        view.SetString(SlotKey, _slot.Trim());
        RecompOne.Runtime.Runtime.SaveView();
    }

    public void DrawStatus()
    {
        if (!Game.Available)
        {
            ImGui.TextDisabled("Game not running.");
            return;
        }

        int stage = GameWatch.StageId;
        ImGui.Text($"State: {GameWatch.State}");
        ImGui.Text(stage < 0 ? "Stage: none" : $"Stage: {(Stage)stage} (0x{stage:X2})  area {Game.Area}  room {Game.Room}");
        ImGui.Checkbox("Log flag changes", ref GameWatch.Enabled);
        if (ImGui.Checkbox("Always skip prologue (testing)", ref Prologue.SkipSetting)) Prologue.SaveSetting();
    }

    void DrawLog()
    {
        if (ImGui.Button("Clear log")) Log.Clear();
        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);

        if (ImGui.BeginChild("##aplog", Vector2.Zero, ImGuiChildFlags.Border))
        {
            foreach (var line in Log.Snapshot())
                ImGui.TextWrapped(line);
            if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                ImGui.SetScrollHereY(1.0f);
        }
        ImGui.EndChild();
    }
}
