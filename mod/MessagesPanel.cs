#nullable enable
using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;

namespace SotnArchipelago;

// Archipelago's Text Client in the game: the server's messages (chat, items found and sent, hints, players
// joining...) with filters, the hints for this slot, and a line to chat or send commands (!hint, !release...).
public sealed class MessagesPanel : IPanel
{
    public string Name => "Archipelago messages";
    public string TitleKey => "panel.archipelago.messages";
    public bool IsOpen { get; set; }

    const string FilterKey = "Archipelago.Messages.";
    static readonly (Messages.Kind Kind, string Label, string Tip, bool Default)[] Filters =
    [
        (Messages.Kind.Chat, "Chat", "What players say.", true),
        (Messages.Kind.MyItem, "My items", "Items you found for others, and items others found for you.", true),
        (Messages.Kind.OtherItem, "Others' items", "Items found and sent between other players.", false),
        (Messages.Kind.Hint, "Hints", "Hints as they're given out.", true),
        (Messages.Kind.Server, "Server", "Players joining and leaving, goals, releases, DeathLink.", true),
    ];

    readonly bool[] _show;
    string _input = "";
    bool _refocus;
    int _seenVersion = -1;
    bool _hintsOnlyMine = true, _hintsHideFound;

    public MessagesPanel()
    {
        var view = RecompOne.Runtime.Runtime.View;
        _show = Filters.Select(f => view.GetBool(FilterKey + f.Kind, f.Default)).ToArray();
        _hintsOnlyMine = view.GetBool(FilterKey + "HintsMine", true);
        _hintsHideFound = view.GetBool(FilterKey + "HintsHideFound", false);
    }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(640, 420), ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("##tabs"))
        {
            if (ImGui.BeginTabItem("Messages"))
            {
                DrawMessages();
                ImGui.EndTabItem();
            }
            int hints = Messages.Hints.Count(h => !h.Found && Involves(h));
            if (ImGui.BeginTabItem(hints > 0 ? $"Hints ({hints})###hints" : "Hints###hints"))
            {
                DrawHints();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        IsOpen = open;
        ImGui.End();
    }

    // ---- messages ----

    void DrawMessages()
    {
        if (UiOptions.Show)
        {
            bool changed = false;
            for (int i = 0; i < Filters.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                changed |= ImGui.Checkbox(Filters[i].Label, ref _show[i]);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(Filters[i].Tip);
            }
            if (changed) Save();
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear")) Messages.Clear();
        }

        float inputHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginChild("##log", new Vector2(0, -inputHeight), ImGuiChildFlags.Border))
        {
            bool atBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 2;
            foreach (var message in Messages.Snapshot())
            {
                if (!Shown(message.Kind)) continue;
                ImGui.TextDisabled(message.Time.ToString("HH:mm"));
                ImGui.SameLine();
                RichText(message.Parts);
            }
            // Stay at the bottom as messages come in, unless scrolled up to read.
            if (Messages.Version != _seenVersion)
            {
                _seenVersion = Messages.Version;
                if (atBottom) ImGui.SetScrollHereY(1f);
            }
        }
        ImGui.EndChild();
        DrawInput();
    }

    bool Shown(Messages.Kind kind)
    {
        if (kind == Messages.Kind.Command) return true; // answers to what you typed
        int i = Array.FindIndex(Filters, f => f.Kind == kind);
        return i < 0 || _show[i];
    }

    void DrawInput()
    {
        bool connected = ApClient.State == ConnectionState.Connected;
        if (!connected) ImGui.BeginDisabled();
        if (_refocus) { ImGui.SetKeyboardFocusHere(); _refocus = false; }
        float button = ImGui.CalcTextSize("Send").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - button);
        string hint = connected ? "Chat, or a command: !hint <item>, !remaining, !release, !help" : "Connect to chat and send commands";
        bool enter = ImGui.InputTextWithHint("##say", hint, ref _input, 512, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        bool send = ImGui.Button("Send");
        if (!connected) ImGui.EndDisabled();
        if ((enter || send) && connected && _input.Trim().Length > 0)
        {
            ApClient.Say(_input.Trim());
            _input = "";
            _refocus = true;
        }
    }

    // Coloured parts, wrapped at word boundaries to the window's width.
    static void RichText(Messages.Part[] parts)
    {
        float right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        float start = ImGui.GetCursorPosX();
        float x = start;
        bool lineStarted = false;
        uint normal = ImGui.GetColorU32(ImGuiCol.Text);
        foreach (var part in parts)
        {
            var lines = part.Text.Split('\n');
            for (int l = 0; l < lines.Length; l++)
            {
                if (l > 0) { NewLine(); }
                foreach (var word in Words(lines[l]))
                {
                    float w = ImGui.CalcTextSize(word).X;
                    string text = word;
                    if (lineStarted && x + w > right)
                    {
                        NewLine();
                        text = word.TrimStart();
                        if (text.Length == 0) continue;
                        w = ImGui.CalcTextSize(text).X;
                    }
                    if (lineStarted) ImGui.SameLine(0, 0);
                    else ImGui.SetCursorPosX(x);
                    ImGui.PushStyleColor(ImGuiCol.Text, part.Colour == 0 ? normal : part.Colour);
                    ImGui.TextUnformatted(text);
                    ImGui.PopStyleColor();
                    x += w;
                    lineStarted = true;
                }
            }
        }
        if (!lineStarted) ImGui.NewLine();

        void NewLine()
        {
            if (!lineStarted) ImGui.NewLine();
            lineStarted = false;
            x = start;
        }
    }

    // Words with their following spaces, so wrapping keeps the spacing.
    static System.Collections.Generic.IEnumerable<string> Words(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            int end = i;
            while (end < text.Length && text[end] != ' ') end++;
            while (end < text.Length && text[end] == ' ') end++;
            yield return text[i..end];
            i = end;
        }
    }

    // ---- hints ----

    static bool Involves(Messages.Hint h) => h.ReceivingPlayer == ApClient.Slot || h.FindingPlayer == ApClient.Slot;

    void DrawHints()
    {
        if (UiOptions.Show)
        {
            bool changed = ImGui.Checkbox("Only hints involving me", ref _hintsOnlyMine);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("Hide found", ref _hintsHideFound);
            if (changed) Save();
        }

        var hints = Messages.Hints.Where(h => (!_hintsOnlyMine || Involves(h)) && (!_hintsHideFound || !h.Found))
            .OrderBy(h => h.Found).ThenByDescending(h => h.Status).ToList();
        if (hints.Count == 0)
        {
            ImGui.TextDisabled(ApClient.HasSeed ? "No hints yet. Type !hint <item> in Messages to ask for one." : "Connect to see hints.");
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable
                                      | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##hints", 6, flags)) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("For");
        ImGui.TableSetupColumn("Location");
        ImGui.TableSetupColumn("In the world of");
        ImGui.TableSetupColumn("Entrance");
        ImGui.TableSetupColumn("Status");
        ImGui.TableHeadersRow();
        foreach (var h in hints)
        {
            ImGui.TableNextRow();
            Cell(ApClient.ItemName(h.Item, h.ReceivingPlayer), Messages.ItemColour(h.ItemFlags));
            Cell(Messages.PlayerPart(h.ReceivingPlayer));
            Cell(ApClient.LocationName(h.Location, h.FindingPlayer), Messages.LocationColour);
            Cell(Messages.PlayerPart(h.FindingPlayer));
            Cell(h.Entrance.Length > 0 ? h.Entrance : "Vanilla", h.Entrance.Length > 0 ? Messages.EntranceColour : Messages.Grey);
            int status = h.Found ? Messages.StatusFound : h.Status;
            Cell(Messages.StatusName(status), Messages.StatusColour(status));
        }
        ImGui.EndTable();
    }

    static void Cell(Messages.Part part) => Cell(part.Text, part.Colour);

    static void Cell(string text, uint colour)
    {
        ImGui.TableNextColumn();
        if (colour == 0) ImGui.TextWrapped(text);
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, colour);
            ImGui.TextWrapped(text);
            ImGui.PopStyleColor();
        }
    }

    void Save()
    {
        var view = RecompOne.Runtime.Runtime.View;
        for (int i = 0; i < Filters.Length; i++) view.SetBool(FilterKey + Filters[i].Kind, _show[i]);
        view.SetBool(FilterKey + "HintsMine", _hintsOnlyMine);
        view.SetBool(FilterKey + "HintsHideFound", _hintsHideFound);
        RecompOne.Runtime.Runtime.SaveView();
    }
}
