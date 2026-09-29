# Symphony of the Night: Archipelago for SymphonyRecomp

Play Castlevania: Symphony of the Night in an [Archipelago](https://archipelago.gg) multiworld on
[SymphonyRecomp](https://github.com/BlackLabelHQ/SymphonyRecomp), the native PC recompilation of the PSX
game. No disc patching and no emulator: you play on your own unmodified US disc, and the mod connects to
the Archipelago server, places your seed's items and sends and receives checks.

Work in progress. See [PLAN.md](PLAN.md) for what's done and what's next.

## What you need

- SymphonyRecomp v0.5.1b or newer, set up with your US disc as its README describes.
- `archipelago.zip` (the mod) and `sotn_recomp.apworld` (the Archipelago world) from this project's releases.

## Setting up

1. Put `archipelago.zip` in SymphonyRecomp's `mods` folder. Either leave it zipped or unzip it into its own folder
   (`mods/archipelago/` with `mod.json` in it); the game reads both.
2. Start the game. In the menu bar (F1 shows it): **Mods** -> tick **Archipelago**. It stays enabled.

## Generating a seed

Whoever generates the multiworld installs `sotn_recomp.apworld` in Archipelago (Archipelago Launcher -> Install
APWorld). Its game is **Symphony of the Night (Recomp)**: player files say `game: Symphony of the Night (Recomp)`
(generate a template from the launcher). Options are the same as fdelduque's Symphony of the Night world (for
BizHawk), which this world is based on. The two are separate games and can be installed side by side; this mod
only plays seeds from this world. There is no patch file: the mod gets everything from the server.

The [SotN PopTracker pack](https://github.com/Michpem/SOTN-AP-MapTracker) works with this world as it is.

## Playing

1. At the title screen: menu bar -> **Archipelago** -> server (e.g. `archipelago.gg:38281`), slot name,
   password -> **Connect**.
2. Start a new game, or load a save. The save is tied to its seed.

- A **new game** needs the connection: it waits on the file select screen until you're connected.
- A **save you've played on this PC** can also be loaded without connecting: the mod keeps each seed it has
  connected to in `archipelago-seeds\` next to the game. Checks you make offline are sent, and items for you
  received, the next time you connect.
- A **save from a different seed** than the connected server's plays without Archipelago (nothing placed, sent
  or received) and says so on screen. Connect to its server, or disconnect to play it offline, and it switches
  over on the spot.

Items for other players look like a round **AP** badge coloured by importance: gold for progression, blue for
useful, grey for filler, red for traps. Picking one up shows whose item it is. Items you receive pop up and
appear in your inventory right away (only during normal play, never mid-menu or mid-cutscene). The
**Archipelago** window has the connection status and a colour-coded log, and its **Map** button opens a map of
your seed's locations: green where you can go now in logic, red not yet, grey checked, with your position
and the location names (and, if you like, what's there) on hover.

## For developers

- `mod/` - the SymphonyRecomp mod (C#, compiled by the game at load time)
- `apworld/sotn_recomp/` - the Archipelago world ("Symphony of the Night (Recomp)"), from fdelduque's 0.8.16.1
  with fixes, the BizHawk parts removed, and the data export for the mod
- `tools/` - table generator, packaging, and offline checks (the mod's placement against fdelduque's patch, the
  payload against the world's own writes, hooks against a built game)
- `docs/research/` - notes on the AP world, the game's memory and the recomp
- [TESTING.md](TESTING.md) - running a local server and the game; [PLAN.md](PLAN.md) - design, status, PC setup

## Credits

- The SotN Archipelago world: fdelduque, with location and item groups from Darvitz.
- SymphonyRecomp and RecompOne: BlackLabelHQ. Integrated randomizer and randomizer support in the recomp:
  MottZilla, eldri7ch.
- Randomizer research the AP world builds on: Wild Mouse (sotn.io), MottZilla, eldri7ch, TalicZealot,
  Forat Negre, CRAZY4BLADES, and the sotn-decomp contributors.
- SotN Archipelago map tracker: Michpem, DorkmasterFlek.
- [Archipelago](https://github.com/ArchipelagoMW/Archipelago).

MIT licence ([LICENSE](LICENSE)); the AP world in `apworld/` keeps Archipelago's MIT licence
([apworld/LICENSE](apworld/LICENSE)). This project isn't affiliated with Konami, BlackLabelHQ or the Archipelago team.
