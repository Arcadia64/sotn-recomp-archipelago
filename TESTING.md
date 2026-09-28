# Running a test

All of this runs on this PC, in a normal (not elevated) PowerShell window, as your own Windows account.
Each step gets its own PowerShell window because the server and the game both keep running.

## 1. Start the Archipelago server

```powershell
cd <project folder>\ref\archipelago
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
cd <project folder>\ref\SymphonyRecomp
```
```powershell
dotnet run --no-build
```

## 3. Connect

In the game's menu bar: **Archipelago** -> Server `localhost:38281`, Slot name `Alucard`, Password empty -> **Connect**.
Connect before starting a new game so the seed's options (like skipping the prologue) apply.

## Making a new seed

Player files are in `ref\archipelago\Players\` (`alucard.yaml` is the one you play, `maria.yaml` is a second slot nobody plays).
After editing them, in the `ref\archipelago` window:

```powershell
.\.venv\Scripts\python.exe Generate.py --skip_prog_balancing
```

The new seed lands in `ref\archipelago\output\`. Stop the server (`/exit`) and start it again (step 1); it picks the newest seed.

## After changing mod code

Copy the mod into the game's mods folder (from the project folder):

```powershell
Copy-Item <project folder>\mod\*.cs, <project folder>\mod\mod.json <project folder>\ref\SymphonyRecomp\mods\archipelago\ -Force
```

Then in the game: **Mods** -> the **Archipelago** entry's menu -> **Reload**, or restart the game.
(`tools/deploy-mod.sh` does the same copy and also removes files that were deleted from `mod\`.)
