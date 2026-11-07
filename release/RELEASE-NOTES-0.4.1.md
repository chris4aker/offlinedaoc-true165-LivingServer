**Offline DAoC True 1.65 v0.4.1**: a fix for v0.4. Single-player Dark Age of Camelot as it was at patch 1.65 (Classic
and Shrouded Isles, November 2003), with a world full of gamebots, hired companions, and a new client built in Godot 4.

> **This is an AI-made project.** All of the code was written by AI tools; the project owner has no coding
> experience. Large parts of the game haven't been played through by a person. Expect bugs, and keep a backup of
> your save.

## What's fixed

- **Monsters added from the Prima guides in 0.4 show their own names, levels and looks.** Many borrowed another
  monster's template, which overwrote them: Pog Mapog appeared as a second "Mapog" at level 62, siog raiders as siabra
  seekers, mantid warlocks as mantid protectors, about 290 spawns in all, and their loot followed the wrong name too.
- Thergus the Inspirer and the ogre squad leader show their own names; the Unseelie Envoy and his stallions, Celain
  Eirich and Weaponmaster Zargin keep the level, look and faction their encounters give them.
- **Loot that sold for a flat 50c sells for its period value**, or for what period loot of the same monster level
  fetched: gems like Black Opal (2g 48s), Blue Spinel and the tourmalines pay properly again.
- **Trees in Avalon Marsh, Myrkwood Forest and Shannon Estuary stop you at their trunk again**, as everywhere else.
- The downloader reads a three-part version such as v0.4.1 in full.

Everything new in 0.4 is in the [v0.4 release notes](https://github.com/shadowofze/OfflineDAoC-True165/releases/tag/v0.4)
and the [changelog](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/CHANGELOG.md).

## Already playing v0.1, v0.2, v0.3 or v0.4? Update in place (about 2.8 GB)

1. Quit the game, click **STOP SERVER**, and close the launcher.
2. Under **Assets** below, download **`Download-OfflineDAoC-True165-v0.4.1.zip`** (a few KB: just the downloader),
   extract it, and double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`**. If Windows SmartScreen appears, click
   **More info → Run anyway**.
3. Press **1 = Update my game**. It finds your `OfflineDAoC-True165` folder (or asks you to pick it), checks every
   file it will replace, and updates the game where it is. Your account, characters, companions, bots and settings
   stay; the files it replaces are backed up in `patch-backups\`. One update covers v0.1, v0.2, v0.3 and v0.4.
4. Start the game as usual. The first **START SERVER** after the update brings your world up to date and can take a
   few minutes.

If it finds files that were changed on your PC, it changes nothing and can make a fresh install that brings your save
over.

## New players: the complete game (about 36 GB)

1. Download **`Download-OfflineDAoC-True165-v0.4.1.zip`** below and put it in a folder with a short path, such as
   `C:\Games\OfflineDAoC`. Right-click it, choose **Extract All…**, and open the extracted folder.
2. Double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`** and press **2 = New install**. It checks your PC, shows the
   download size and the folder it will use, and installs the game into a new folder, `OfflineDAoC-True165`. If it
   stops, run it again; it continues where it stopped.
3. Open that folder and double-click **`START OFFLINE DAOC TRUE 1.65.cmd`**. Click **START SERVER**, wait for
   **RUNNING**, then click **ENTER REALM**. Your account is created the first time.

You **don't** need to download the other files on this page by hand: the downloader fetches and checks them.

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
