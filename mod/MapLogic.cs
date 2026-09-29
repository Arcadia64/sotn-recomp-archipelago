#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SotnArchipelago;

// The seed's logic, as the AP world exported it (slot_data["recomp"]["logic"], apworld Logic.py), and which
// locations the player can reach in logic with what they have. A rule is a list of item sets and holds if
// the player has every item of any one set ([[]] = always, [] = never). The same rules the seed was
// generated with; tools/verify_logic.py checks the export against Archipelago's own reachability.
static class MapLogic
{
    sealed record Rules(
        string Start,
        Dictionary<string, List<(string Target, string[][] Sets)>> Exits,
        Dictionary<long, (string Region, string[][] Sets)> Locations);

    static Rules? _rules;
    static int _rulesFor = -1;
    static (int Connection, int Received, int Checked) _computedFor = (-1, -1, -1);
    static HashSet<long> _reachable = [];

    // Whether this seed has the logic (seeds from before the export don't).
    public static bool Available
    {
        get { Refresh(); return _rules != null; }
    }

    // Locations reachable in logic now, checked or not.
    public static IReadOnlySet<long> Reachable
    {
        get { Refresh(); return _reachable; }
    }

    static void Refresh()
    {
        int connection = ApClient.ConnectionId;
        if (_rulesFor != connection) // slot data arrives with each new connection
        {
            _rules = Parse(ApClient.SlotData?["recomp"]?["logic"] as JsonObject);
            _rulesFor = connection;
            _computedFor = (-1, -1, -1);
        }
        var received = ApClient.Received;
        var key = (connection, received.Length, ApClient.CheckedCount);
        if (key == _computedFor) return;
        _computedFor = key;
        _reachable = _rules == null ? [] : Solve(_rules, Items(received));
    }

    // What the player has: everything the server sent (other worlds' finds, starting items) and our own
    // items at the locations we've checked (the server doesn't send those back).
    static HashSet<string> Items(NetworkItem[] received)
    {
        int slot = ApClient.Slot;
        var names = new HashSet<string>();
        foreach (var item in received) names.Add(ApClient.ItemName(item.Item, slot));
        foreach (var loc in LocationData.All)
            if (ApClient.IsChecked(loc.Id) && ApClient.TryGetScout(loc.Id, out var scout) && scout.Player == slot)
                names.Add(ApClient.ItemName(scout.Item, slot));
        return names;
    }

    // Rules only ever need items, never other regions, so one pass over the region graph is enough.
    static HashSet<long> Solve(Rules rules, HashSet<string> items)
    {
        bool Holds(string[][] sets) => sets.Any(set => set.All(items.Contains));

        var regions = new HashSet<string> { rules.Start };
        var queue = new Queue<string>(regions);
        while (queue.Count > 0)
        {
            if (!rules.Exits.TryGetValue(queue.Dequeue(), out var exits)) continue;
            foreach (var (target, sets) in exits)
                if (!regions.Contains(target) && Holds(sets))
                {
                    regions.Add(target);
                    queue.Enqueue(target);
                }
        }

        var reachable = new HashSet<long>();
        foreach (var (id, (region, sets)) in rules.Locations)
            if (regions.Contains(region) && Holds(sets)) reachable.Add(id);
        return reachable;
    }

    static Rules? Parse(JsonObject? json)
    {
        if (json == null) return null;
        try
        {
            var exits = new Dictionary<string, List<(string, string[][])>>();
            foreach (var (region, list) in json["regions"]!.AsObject())
                exits[region] = list!.AsArray().Select(e => (e![0]!.GetValue<string>(), Sets(e[1]))).ToList();
            var locations = new Dictionary<long, (string, string[][])>();
            foreach (var (id, entry) in json["locations"]!.AsObject())
                locations[long.Parse(id)] = (entry![0]!.GetValue<string>(), Sets(entry[1]));
            return new Rules(json["start"]!.GetValue<string>(), exits, locations);
        }
        catch (Exception ex)
        {
            Log.Error($"map: couldn't read the seed's logic: {ex.Message}");
            return null;
        }
    }

    static string[][] Sets(JsonNode? node) =>
        node!.AsArray().Select(set => set!.AsArray().Select(item => item!.GetValue<string>()).ToArray()).ToArray();
}
