#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// The item tracker: the relics and key items, lit once you have them, the vessels you've collected, and with
// "All items" every other item too, each kind in its own section. While a save of this seed is being played,
// "have" is what that save has (plus items received and not given yet); otherwise it's what Archipelago says
// you've collected (received, and your own items at the locations you've checked). Hovering an item says where
// it came from, or where a hint says it is.
public sealed class ItemsPanel : IPanel
{
    public string Name => "Archipelago items";
    public string TitleKey => "panel.archipelago.items";
    public bool IsOpen { get; set; }

    const string SizeKey = "Archipelago.Items.IconSize", AllKey = "Archipelago.Items.All";
    int _iconSize;
    bool _all;

    static readonly long[] Relics = [.. Enumerable.Range(300, 18).Select(i => (long)i)];   // Soul of bat .. Merman statue
    static readonly long[] VladRelics = [325, 326, 327, 328, 329];
    static readonly long[] KeyItems = [203, 183, 241, 242];                                // Holy glasses, Spike breaker, Gold and Silver rings
    static readonly long[] Cards = [318, 319, 320, 321, 322];
    const long HolyGlasses = 203, SoulOfBat = 300, FormOfMist = 307, PowerOfMist = 308, GravityBoots = 312, LeapStone = 313;

    // "All items": every other item by kind, in the game's order.
    static readonly (string Title, string Id, long[] Items)[] AllItems =
    [
        ("Weapons", "weapons", Of(ItemType.Weapon1, ItemType.Weapon2)),
        ("Shields", "shields", Of(ItemType.Shield)),
        ("Helmets", "helmets", Of(ItemType.Helmet)),
        ("Armor", "armor", Of(ItemType.Armor)),
        ("Cloaks", "cloaks", Of(ItemType.Cloak)),
        ("Accessories", "accessories", Of(ItemType.Accessory)),
        ("Usable items", "usable", Of(ItemType.Usable)),
    ];

    static long[] Of(params ItemType[] types) =>
        [.. ItemData.All.Where(i => types.Contains(i.Type) && !KeyItems.Contains(i.Id)).Select(i => i.Id)];

    const uint Lit = 0xFFFFFFFF, Dim = 0x40FFFFFF, TileBackground = 0xFF201814, TileBorder = 0xFF4A3C34, TileBorderLit = 0xFF20B0F0;
    const uint Tick = 0xFF5AD25A, Cross = 0xFF6A6AE0, CountBackground = 0xD0000000;

    public ItemsPanel()
    {
        var view = RecompOne.Runtime.Runtime.View;
        _iconSize = Math.Clamp(view.GetInt(SizeKey, 40), 24, 72);
        _all = view.GetBool(AllKey, false);
    }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(380, 520), ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        if (!ApClient.HasSeed) ImGui.TextDisabled("Connect to track your seed's items.");
        else
        {
            var have = Owned.Get();
            if (UiOptions.Show) DrawControls(have.FromSave);
            Group("Relics", "relics", Relics, have, true);
            Group("Relics of Vlad", "vlad", VladRelics, have, true);
            Group("Key items", "keyitems", KeyItems, have, true);
            Group("Familiar cards", "cards", Cards, have, true);
            if (UiOptions.Section("Vessels", "vessels", true))
            {
                ImGui.Text($"Life Vessels: {have.Collected(ItemData.LifeVessel)}");
                ImGui.SameLine(0, 24);
                ImGui.Text($"Heart Vessels: {have.Collected(ItemData.HeartVessel)}");
            }
            if (_all)
                foreach (var (title, id, items) in AllItems) Group(title, id, items, have, false);
            DrawGoal(have);
        }

        IsOpen = open;
        ImGui.End();
    }

    void DrawControls(bool fromSave)
    {
        bool changed = ImGui.Checkbox("All items", ref _all);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Also show every weapon, shield, armor, accessory and usable item, by kind.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        changed |= ImGui.SliderInt("Icon size", ref _iconSize, 24, 72);
        if (changed)
        {
            var view = RecompOne.Runtime.Runtime.View;
            view.SetBool(AllKey, _all);
            view.SetInt(SizeKey, _iconSize);
            RecompOne.Runtime.Runtime.SaveView();
        }
        ImGui.TextDisabled(fromSave ? "What your save has." : "What you've collected in Archipelago (load your save to see what it has).");
    }

    void Group(string title, string id, long[] items, Owned have, bool defaultOpen)
    {
        if (!UiOptions.Section($"{title}  {items.Count(have.Has)} of {items.Length}", id, defaultOpen)) return;
        float size = _iconSize;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float avail = ImGui.GetContentRegionAvail().X;
        int perRow = Math.Max(1, (int)((avail + spacing) / (size + spacing)));
        for (int i = 0; i < items.Length; i++)
        {
            if (i % perRow != 0) ImGui.SameLine();
            Tile(items[i], have, size);
        }
        ImGui.Spacing();
    }

    static void Tile(long id, Owned have, float size)
    {
        string name = ItemData.Get(id)?.Name ?? $"Item {id}";
        bool owned = have.Has(id);
        var draw = ImGui.GetWindowDrawList();
        var iconAt = ImGui.GetCursorScreenPos();

        draw.AddRectFilled(iconAt, iconAt + new Vector2(size, size), TileBackground, 4f);
        uint texture = Icons.Get(name);
        float pad = size * 0.1f;
        if (texture != 0)
            draw.AddImage((nint)texture, iconAt + new Vector2(pad, pad), iconAt + new Vector2(size - pad, size - pad),
                Vector2.Zero, Vector2.One, owned ? Lit : Dim);
        else
        {
            string initials = string.Concat(name.Split(' ').Where(w => w.Length > 2).Select(w => char.ToUpperInvariant(w[0])));
            var text = ImGui.CalcTextSize(initials);
            draw.AddText(iconAt + new Vector2((size - text.X) / 2, (size - text.Y) / 2), owned ? Lit : Dim, initials);
        }
        draw.AddRect(iconAt, iconAt + new Vector2(size, size), owned ? TileBorderLit : TileBorder, 4f, ImDrawFlags.None, owned ? 2f : 1f);

        // How many, when more than one.
        int count = have.Count(id);
        if (count > 1)
        {
            string label = count.ToString();
            var text = ImGui.CalcTextSize(label);
            var corner = iconAt + new Vector2(size - text.X - 3, size - text.Y - 1);
            draw.AddRectFilled(corner - new Vector2(2, 0), corner + text + new Vector2(1, 0), CountBackground, 3f);
            draw.AddText(corner, Lit, label);
        }
        ImGui.Dummy(new Vector2(size, size));
        if (ImGui.IsItemHovered()) Tooltip(id, name, owned, have);
    }

    static void Tooltip(long id, string name, bool owned, Owned have)
    {
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(name);
        var sources = have.Sources(id);
        if (owned && have.FromSave && sources.Count == 0) ImGui.TextDisabled("In your save.");
        foreach (var source in sources) ImGui.TextDisabled(source);
        if (!owned || sources.Count == 0)
        {
            var hints = Messages.Hints.Where(h => h.ReceivingPlayer == ApClient.Slot && h.Item == id).ToList();
            foreach (var h in hints)
            {
                string where = $"{ApClient.LocationName(h.Location, h.FindingPlayer)}"
                    + (h.FindingPlayer == ApClient.Slot ? "" : $", in {ApClient.PlayerName(h.FindingPlayer)}'s world")
                    + (h.Entrance.Length > 0 ? $" ({h.Entrance})" : "");
                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(h.Found ? Messages.Green : Messages.LocationColour),
                    (h.Found ? "Hint (found): " : "Hint: ") + where);
            }
            if (!owned && hints.Count == 0) ImGui.TextDisabled("Not found yet.");
        }
        ImGui.EndTooltip();
    }

    // ---- the goal ----

    static void DrawGoal(Owned have)
    {
        if (!UiOptions.Section("Goal: defeat Dracula", "goal", true)) return;
        int vlad = VladRelics.Count(have.Has);
        Check(vlad == 5, $"The five relics of Vlad ({vlad} of 5)");
        Check(have.Has(HolyGlasses), "Holy glasses (to reach the inverted castle)");
        bool fly = have.Has(SoulOfBat) || have.Has(FormOfMist) && have.Has(PowerOfMist) || have.Has(GravityBoots) && have.Has(LeapStone);
        Check(fly, "A way to fly: Soul of bat, Form + Power of mist, or Gravity boots + Leap stone");
    }

    static void Check(bool done, string text)
    {
        var draw = ImGui.GetWindowDrawList();
        var at = ImGui.GetCursorScreenPos();
        float h = ImGui.GetTextLineHeight();
        var c = at + new Vector2(h / 2, h / 2);
        if (done)
        {
            draw.AddLine(c + new Vector2(-h * 0.3f, 0), c + new Vector2(-h * 0.05f, h * 0.25f), Tick, 2f);
            draw.AddLine(c + new Vector2(-h * 0.05f, h * 0.25f), c + new Vector2(h * 0.32f, -h * 0.3f), Tick, 2f);
        }
        else
        {
            draw.AddLine(c + new Vector2(-h * 0.25f, -h * 0.25f), c + new Vector2(h * 0.25f, h * 0.25f), Cross, 2f);
            draw.AddLine(c + new Vector2(-h * 0.25f, h * 0.25f), c + new Vector2(h * 0.25f, -h * 0.25f), Cross, 2f);
        }
        ImGui.Dummy(new Vector2(h, h));
        ImGui.SameLine();
        if (done) ImGui.TextWrapped(text);
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped(text);
            ImGui.PopStyleColor();
        }
    }

    // ---- what you have ----

    sealed class Owned
    {
        static (int Connection, int Received, int Checked) _for = (-1, -1, -1);
        static Dictionary<long, List<string>> _sources = [];

        IMemory? _save;                                  // the save being played, when it's this seed's
        readonly Dictionary<long, int> _onTheWay = [];   // received, not given to the save yet
        public bool FromSave => _save != null;

        public static Owned Get()
        {
            RefreshSources();
            var owned = new Owned();
            var m = RecompOne.Runtime.Runtime.Mem;
            if (m != null && Game.Available && m.ReadU8(Game.GameStateAddr) == (byte)GameState.Play
                && SaveLink.Check(m) == SaveLink.Status.ThisSeed)
            {
                owned._save = m;
                // Received, not given yet (the game isn't in normal play, say): they're on their way.
                var received = ApClient.Received;
                for (int i = SaveLink.ReceivedCount(m); i < received.Length; i++)
                    owned._onTheWay[received[i].Item] = owned._onTheWay.GetValueOrDefault(received[i].Item) + 1;
            }
            return owned;
        }

        public bool Has(long id) => Count(id) > 0;

        // How many you have: in the save (inventory and equipped) and on the way, or collected in Archipelago.
        public int Count(long id) => _save is { } m ? InSave(m, id) + _onTheWay.GetValueOrDefault(id) : Collected(id);

        // How many Archipelago has given you (vessels aren't kept in the save as items).
        public int Collected(long id) => _sources.TryGetValue(id, out var list) ? list.Count : 0;

        public IReadOnlyList<string> Sources(long id) => _sources.TryGetValue(id, out var list) ? list : [];

        // Where each of this slot's items came from.
        static void RefreshSources()
        {
            var received = ApClient.Received;
            var key = (ApClient.ConnectionId, received.Length, ApClient.CheckedCount);
            if (key == _for) return;
            _for = key;
            int slot = ApClient.Slot;
            var sources = new Dictionary<long, List<string>>();
            void Add(long item, string text)
            {
                if (!sources.TryGetValue(item, out var list)) sources[item] = list = [];
                list.Add(text);
            }
            foreach (var item in received)
            {
                if (item.Location == -2) Add(item.Item, "Starting item.");
                else if (item.Location < 0) Add(item.Item, "Given by the server.");
                else Add(item.Item, $"From {ApClient.PlayerName(item.Player)}: {ApClient.LocationName(item.Location, item.Player)}.");
            }
            foreach (var loc in LocationData.All)
                if (ApClient.IsChecked(loc.Id) && ApClient.TryGetScout(loc.Id, out var scout) && scout.Player == slot)
                    Add(scout.Item, $"Found at {loc.Name}.");
            _sources = sources;
        }

        // Equipped items: s32 per slot (both hands, head, body, cloak, two accessories).
        const uint RelicBase = 0x80097964, EquipmentAddr = 0x80097C00;
        const long FirstBodyItem = 169, LastBodyItem = 258;

        static int InSave(IMemory m, long id)
        {
            if (id >= ItemData.FirstRelic && id <= ItemData.LastRelic) return m.ReadU8(RelicBase + (uint)(id - ItemData.FirstRelic)) != 0 ? 1 : 0;
            if (id >= 1 && id < FirstBodyItem) return Inventory.GetHandCount((int)id) + Equipped(m, 0, 1, (int)id);
            if (id >= FirstBodyItem && id <= LastBodyItem)
            {
                int body = (int)(id - FirstBodyItem);
                return Inventory.GetBodyCount(body) + Equipped(m, 2, 6, body);
            }
            return 0;
        }

        static int Equipped(IMemory m, int first, int last, int value)
        {
            int n = 0;
            for (int slot = first; slot <= last; slot++)
                if (m.ReadU32(EquipmentAddr + 4 * (uint)slot) == (uint)value) n++;
            return n;
        }
    }
}
