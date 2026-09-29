# Running a test

All of this runs on this PC, in a normal (not elevated) PowerShell window, as your own Windows account,
starting in the project folder (the one with `PLAN.md`). Each step gets its own PowerShell window because
the server and the game both keep running. First-time setup of `ref\` is in `PLAN.md` ("Setting up `ref/`").

## 1. Start the Archipelago server

```powershell
cd ref\archipelago
```
```powershell
.\.venv\Scripts\python.exe MultiServer.py (Get-ChildItem output\AP_*.zip | Sort-Object LastWriteTime | Select-Object -Last 1).FullName --host 127.0.0.1 --port 38281
```

Leave that window open. It only listens on this PC (127.0.0.1), no password. Useful commands typed into that window:
- `/send Alucard Soul of Bat` - give the Alucard slot an item (tests receiving)
- `/players` - who is connected
- `/exit` - stop the server

## 2. Start the game

```powershell
cd ref\SymphonyRecomp
```
```powershell
dotnet run --no-build
```

## 3. Connect

In the game's menu bar (F1 shows it): **Archipelago** -> Server `localhost:38281`, Slot name `Alucard`, Password empty -> **Connect**.
Connect before starting a new game so the seed's options (like skipping the prologue) apply.

## Making a new seed

Player files are in `ref\archipelago\Players\` (`alucard.yaml` is the one you play, `maria.yaml` is a second slot nobody plays),
both for `game: Symphony of the Night (Recomp)`, this project's world.
Build this project's AP world into Archipelago first (from the project folder):

```powershell
py -3.12 tools\build_apworld.py; Copy-Item dist\sotn_recomp.apworld ref\archipelago\custom_worlds\ -Force
```

Then, in the `ref\archipelago` window:

```powershell
.\.venv\Scripts\python.exe Generate.py --skip_prog_balancing
```

The new seed lands in `ref\archipelago\output\`. Stop the server (`/exit`) and start it again (step 1); it picks the newest seed.

## After changing mod code

Copy the mod into the game's mods folder, laid out like the recomp's bundled mods (`mod.json` and
`mod-icon.png` at the top, the code in `source\`). From the project folder, in Git Bash:

```bash
tools/deploy-mod.sh
```

Then in the game: **Mods** -> the **Archipelago** entry's menu -> **Reload**, or restart the game.
For another copy of the recomp (a release download, say), pass its folder: `tools/deploy-mod.sh <folder>`.

## Checks that don't need the game

From the project folder, with the Archipelago venv (they need the game built for the compile check):

```powershell
dotnet build tools\modcheck                                                      # mod compiles against the game
ref\archipelago\.venv\Scripts\python.exe tools\verify_placement.py <seed zip> 1  # placement vs the AP patch
ref\archipelago\.venv\Scripts\python.exe tools\verify_payload.py <seed zip>      # slot_data payload vs the world's writes
ref\archipelago\.venv\Scripts\python.exe tools\verify_logic.py                   # map logic export vs Archipelago's own
py -3.12 tools\check_hooks.py [--game <folder>]                                   # every hook names a real function
```
