# Symphony of the Night: Archipelago for SymphonyRecomp

Play Castlevania: Symphony of the Night in an [Archipelago](https://archipelago.gg) multiworld on
[SymphonyRecomp](https://github.com/BlackLabelHQ/SymphonyRecomp), the native PC recompilation of the PSX
game. No disc patching and no emulator: you play on your own unmodified US disc, and the mod connects to
the Archipelago server, places your seed's items and sends and receives checks.

Work in progress. See [PLAN.md](PLAN.md) for what's done and what's next.

## What you need

- SymphonyRecomp v0.5.1b or newer, set up with your US disc as its README describes.
- `archipelago.zip` (the mod) and `sotn.apworld` (the Archipelago world) from this project's releases.

## Setting up

1. Put `archipelago.zip` in SymphonyRecomp's `mods` folder. Either leave it zipped or unzip it into its own folder
   (`mods/archipelago/` with `mod.json` in it); the game reads both.
2. Start the game. In the menu bar (F1 shows it): **Mods** -> tick **Archipelago**. It stays enabled.

## Generating a seed

Whoever generates the multiworld installs `sotn.apworld` in Archipelago (Archipelago Launcher -> Install
APWorld) and uses it for the SotN player files. Options are the same as the SotN world by fdelduque, which this
world is based on; generate a template YAML from the launcher.

Seeds made with the original SotN world 0.8.16 also work for item placement, checks and items, but options that
change the game itself need this world's seeds (it sends the mod the data it needs).

## Playing

1. At the title screen: menu bar -> **Archipelago** -> server (e.g. `archipelago.gg:38281`), slot name,
   password -> **Connect**. Connect before starting or loading a game.
2. Start a new game (or load a save made on the same seed). The save is tied to the seed.

Items for other players look like a round **AP** badge coloured by importance: gold for progression, blue for
useful, grey for filler, red for traps. Picking one up shows whose item it is. Items you receive pop up and
appear in your inventory right away (only during normal play, never mid-menu or mid-cutscene). The
**Archipelago** window has the connection status and a colour-coded log.

## For developers

- `mod/` - the SymphonyRecomp mod (C#, compiled by the game at load time)
- `apworld/sotn/` - the Archipelago world, a fork of fdelduque's 0.8.16.1 with fixes and a data export for the mod
- `tools/` - table generator, packaging, and offline checks that compare the mod's placement with the AP patch
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
