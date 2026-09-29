# How it works

There are two parts: the apworld, used when generating, and the mod, which runs in the game. Neither one touches
your disc.

## Generation

The apworld is fdelduque's SotN world with some fixes. After Archipelago places the items, the apworld works out
every change the original BizHawk patch would have made to the disc (item tables, relic spots, drops, option
tweaks). Instead of writing a patch file, it stores those changes in the slot data. It also exports the logic so
the in-game map can show what's reachable.

## In game

When you connect, the mod gets the slot data, what's at each of your locations, and any items sent to you. It
saves a copy in `archipelago-seeds` so you can play offline later.

SymphonyRecomp lets mods hook the game's functions. Every time a stage loads, the mod writes that stage's changes
into memory, so the game finds the seed's items where the vanilla ones would be. Items for other players are
swapped for a placeholder that's drawn as an AP badge. Picking one up sends the check instead of giving you
anything.

Checks come from the game's own flags: items picked up, walls broken, boss kill times, the enemy list for
enemysanity, and Dracula for the goal. The mod reads them a few times a second and sends anything new, so nothing
gets lost if you played offline.

Items from other players are given one at a time, and only during normal gameplay (not in menus, cutscenes or
room transitions). The number already given is stored in your save, so loading an older save gets you everything
back.

## Saves

The mod uses a few unused bytes in your save to store:
- which seed and slot the save belongs to;
- how many items you've received;
- whether you've beaten Dracula.

That's how a save always plays with its own seed, either from the server or from the offline copy.

## The windows

- **Map:** location positions come from the game's stage data, reachability from the exported logic, and
  explored rooms from the pause map.
- **Item tracker:** reads your save, or what the server says you've collected when you're not in game.
- **Text client:** shows the server's messages and your hints.

## What it touches

- Network: only the AP server you connect to. No downloads, updates or telemetry.
- Files:
  - `archipelago-seeds`: the last 10 seeds;
  - the recomp's `interface.ini`: its settings;
  - `mods\.cache`: the compiled mod.
- Your save: just the bytes listed above.
- It turns off the recomp's built-in randomizer while you play an AP seed, since that would overwrite your seed's
  items.
- It stores your current zone and room on the server for trackers, same as the BizHawk client.

## Testing

`tools/` has offline tests that check:
- the mod places items exactly like the original patch;
- the slot data matches what the apworld generates;
- the map's logic matches Archipelago's;
- every option generates;
- progression balancing doesn't break anything.

See [TESTING.md](../TESTING.md).
