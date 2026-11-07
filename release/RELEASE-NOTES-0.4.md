**Offline DAoC True 1.65 v0.4**: single-player Dark Age of Camelot as it was at patch 1.65 (Classic and Shrouded
Isles, November 2003), with a world full of gamebots, hired companions, and a new client built in Godot 4.

> **This is an AI-made project.** All of the code was written by AI tools; the project owner has no coding
> experience. Large parts of the game haven't been played through by a person. Expect bugs, and keep a backup of
> your save.

## Already playing v0.1, v0.2 or v0.3? Update in place (about 2.8 GB)

1. Quit the game, click **STOP SERVER**, and close the launcher.
2. Under **Assets** below, download **`Download-OfflineDAoC-True165-v0.4.zip`** (a few KB: just the downloader),
   extract it, and double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`**. If Windows SmartScreen appears, click
   **More info → Run anyway**.
3. Press **1 = Update my game**. It finds your `OfflineDAoC-True165` folder (or asks you to pick it), checks every
   file it will replace, and updates the game where it is. Your account, characters, companions, bots and settings
   stay; the files it replaces are backed up in `patch-backups\`. One update covers v0.1, v0.2 and v0.3.
4. Start the game as usual. The first **START SERVER** after the update brings your world up to date and can take a
   few minutes.

If it finds files that were changed on your PC, it changes nothing and can make a fresh install that brings your save
over.

## New players: the complete game (about 36 GB)

1. Download **`Download-OfflineDAoC-True165-v0.4.zip`** below and put it in a folder with a short path, such as
   `C:\Games\OfflineDAoC`. Right-click it, choose **Extract All…**, and open the extracted folder.
2. Double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`** and press **2 = New install**. It checks your PC, shows the
   download size and the folder it will use, and installs the game into a new folder, `OfflineDAoC-True165`. If it
   stops, run it again; it continues where it stopped.
3. Open that folder and double-click **`START OFFLINE DAOC TRUE 1.65.cmd`**. Click **START SERVER**, wait for
   **RUNNING**, then click **ENTER REALM**. Your account is created the first time.

You **don't** need to download the other files on this page by hand: the downloader fetches and checks them.

## What's new in 0.4

- **The Prima guides' world:** 978 spawns of 363 monsters the world was missing are back across 68 zones and
  dungeons (Hibernia's old frontier and the Shrouded Isles above all), the Cornwall camps the Prima Atlas lists are
  filled again, and 1,523 Atlas drop items that were not in the game drop from their monsters, with their period
  stats. Vendor trash is level 0 everywhere, as on the period pages.
- **Scripted encounters** as the period guides describe them: Cuinn's seedlings, Talvor's avenging spirit, the
  Emmisary of Logi's fireballs and doomwards, Forgemaster Brungar's Spirit of the Forge, Weaponmaster Zargin's
  students, and more.
- **Companions:** charming (a pre-fight charm, `/charm` and `/charm <companion name>`, and Hunters on request), and
  charming never empties a monster camp; `/pull` works in `/defensive` mode, never pulls with crowd control, and a plain
  `/pull` is the tank again; companions follow you over zone borders, show what their realm abilities do, and carry
  their spare weapons as you do.
- **Gamebots in your group** release after a few seconds when nobody can raise them, instead of lying dead for five
  minutes.
- **Starter classes** get their weapon lines' styles before level 5, as in the period.
- **The new client:** the companion GEAR page is a paper doll like your own inventory, the companion window no longer
  freezes the game when you train, equip or move a companion's items, and companion items show their con colour;
  guild emblems sit on the upper back of a cloak again, fire spells no longer leave a line of flames on the ground,
  water has no dark blocky squares where pools meet, and vendor trash shows grey in the backpack, vault and merchant
  lists.

The full list is in the [changelog](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/CHANGELOG.md).

## What you need

- 64-bit Windows 10 or 11, with a CPU that supports **AVX2** (most from 2014 onward). The downloader checks.
- A graphics card that supports **Vulkan**, with a current driver.
- 16 GB RAM recommended, and about 49 GB of free disk space for a new install.
- Nothing to install or turn on in Windows: .NET comes with the game, and the Windows ".NET Framework 3.5" feature,
  the Visual C++ Redistributable and an original DAoC client are **not** needed.

## More

- The full guide with troubleshooting: [docs/PLAY.md](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/docs/PLAY.md)
- Bugs: [open an issue](https://github.com/shadowofze/OfflineDAoC-True165/issues). Say what you were doing, and
  attach the logs from `new-client\save\Godot\app_userdata\MMOEngine\logs` (and `spell_log.txt`, `death_log.txt` there) if you can.
- The source code, and the guides for developers and AI assistants, are in this repository; start with the
  [README](https://github.com/shadowofze/OfflineDAoC-True165#readme).

Dark Age of Camelot is the property of Electronic Arts and Broadsword. This is a free, non-commercial fan project,
not affiliated with them.
