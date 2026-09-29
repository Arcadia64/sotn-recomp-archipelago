# Symphony of the Night (Recomp) setup guide

## What you need

- [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases) 0.6.0 or newer.
- [SymphonyRecomp](https://github.com/BlackLabelHQ/SymphonyRecomp/releases) v0.5.1b or newer, set up with your US
  disc image (SLUS-00067) as its README describes.
- From this project's releases: `sotn_recomp.apworld` (this world) and `archipelago.zip` (the mod).

## Installing

1. **The world** (whoever generates the multiworld needs it): in the Archipelago Launcher choose **Install APWorld**
   and pick `sotn_recomp.apworld`, then restart the launcher. It can sit next to fdelduque's Symphony of the Night
   world; they are separate games.
2. **The mod** (each player): put `archipelago.zip` in SymphonyRecomp's `mods` folder, zipped or unzipped into its own
   folder. Start the game, open the menu bar (F1) and check **Mods**: Archipelago should be listed and ticked.

## Generating

In the Archipelago Launcher choose **Generate Template Options** and edit the "Symphony of the Night (Recomp)" file
(`game: Symphony of the Night (Recomp)`). Put it in the `Players` folder with the other players' files and run
**Generate**. The seed is the `.zip` in the `output` folder; host it with the Archipelago server, or upload it to
the Archipelago website to host it there.

There is no patch file for this game: the mod gets everything it needs from the server when it connects.

## Playing

1. Start SymphonyRecomp.
2. In the menu bar (F1): **Archipelago** -> enter the server (for example `archipelago.gg:38281`), your slot name
   and the password if there is one -> **Connect**. From then on the mod connects by itself when the game starts.
3. Then start a new game or load your save. A new game waits for the connection (the seed comes from the server).
   A save always plays with the seed it was started with: saves of seeds you've played on this PC also work
   offline, and sync (checks sent, items received) as soon as you connect. Beating Dracula offline counts too:
   the server is told the next time you connect.

Commands such as `!hint`, `!release` or `!collect` can be sent from the Archipelago Text Client, connected to the
same slot.

## Tracker

The [SotN PopTracker pack](https://github.com/Michpem/SOTN-AP-MapTracker) works with this world: connect it to the
same server and slot.

## Troubleshooting

- **Archipelago isn't in the menu bar:** open **Mods**. A red mark next to Archipelago means it failed to build; the
  game's console window says why.
- **"InvalidGame" when connecting:** the slot was generated with fdelduque's world (for BizHawk). Generate with this
  world instead.
- Anything else: the console window logs everything the mod does; include it when reporting a problem.
