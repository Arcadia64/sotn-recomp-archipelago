using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SotnArchipelago;

// Usage: placement-check <scouts.json> <out.json>
// scouts.json: {"slot": 1, "scouts": [[location, item, player, flags], ...]}
// out.json: {"writes": [[location, stage, addr, value], ...], "unsupported": [[location, reason], ...],
//            "universe": [[location, stage, addr], ...]}   (every address a location's placement can touch)
var input = JsonNode.Parse(File.ReadAllText(args[0]))!;
int slot = input["slot"]!.GetValue<int>();

var writes = new JsonArray();
var unsupported = new JsonArray();
var universe = new JsonArray();

foreach (var node in input["scouts"]!.AsArray())
{
    var s = node!.AsArray();
    var scout = new NetworkItem(s[1]!.GetValue<long>(), s[0]!.GetValue<long>(), s[2]!.GetValue<int>(), s[3]!.GetValue<int>());
    var loc = LocationData.Get(scout.Location);
    if (loc == null) { unsupported.Add(new JsonArray(scout.Location, "not in LocationData")); continue; }

    var result = Placement.Compute(loc, scout, slot);
    foreach (var w in result.Writes) writes.Add(new JsonArray(loc.Id, w.Stage, w.Addr, w.Value));
    if (result.Unsupported != null) unsupported.Add(new JsonArray(loc.Id, result.Unsupported));

    var p = loc.Place;
    void Add(StageAddr a) => universe.Add(new JsonArray(loc.Id, a.Stage, a.Addr));
    foreach (var a in p.ItemTable) Add(a);
    foreach (var a in p.Addresses) Add(a);
    foreach (var a in p.BossDrop) Add(a);
    foreach (var a in p.Reward) Add(a);
    foreach (var a in p.RingIds) Add(a);
    foreach (var e in p.Entities)
        foreach (uint off in new uint[] { 0, 2, 4, 8 }) Add(new StageAddr(e.Stage, e.Addr + off));
}
foreach (var w in Placement.Always) writes.Add(new JsonArray(-1, w.Stage, w.Addr, w.Value));

var output = new JsonObject { ["writes"] = writes, ["unsupported"] = unsupported, ["universe"] = universe };
File.WriteAllText(args[1], output.ToJsonString());
Console.WriteLine($"{writes.Count} writes, {unsupported.Count} unsupported");
