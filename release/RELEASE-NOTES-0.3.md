**Offline DAoC True 1.65 v0.3**: single-player Dark Age of Camelot as it was at patch 1.65 (Classic and Shrouded
Isles, November 2003), with a world full of gamebots, hired companions, and a new client built in Godot 4.

> **This is an AI-made project.** All of the code was written by AI tools; the project owner has no coding
> experience. Large parts of the game haven't been played through by a person. Expect bugs, and keep a backup of
> your save.

## Already playing v0.1 or v0.2? Update in place (about 2.8 GB)

1. Quit the game, click **STOP SERVER**, and close the launcher.
2. Under **Assets** below, download **`Download-OfflineDAoC-True165-v0.3.zip`** (a few KB: just the downloader),
   extract it, and double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`**. If Windows SmartScreen appears, click
   **More info → Run anyway**.
3. Press **1 = Update my game**. It finds your `OfflineDAoC-True165` folder (or asks you to pick it), checks every
   file it will replace, and updates the game where it is. Your account, characters, companions, bots and settings
   stay; the files it replaces are backed up in `patch-backups\`. One update covers both v0.1 and v0.2.
4. Start the game as usual. The first **START SERVER** after the update brings your world up to date and can take a
   few minutes.

If it finds files that were changed on your PC, it changes nothing and can make a fresh install that brings your save
over.

## New players: the complete game (about 36 GB)

1. Download **`Download-OfflineDAoC-True165-v0.3.zip`** below and put it in a folder with a short path, such as
   `C:\Games\OfflineDAoC`. Right-click it, choose **Extract All…**, and open the extracted folder.
2. Double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`** and press **2 = New install**. It checks your PC, shows the
   download size and the folder it will use, and installs the game into a new folder, `OfflineDAoC-True165`. If it
   stops, run it again; it continues where it stopped.
3. Open that folder and double-click **`START OFFLINE DAOC TRUE 1.65.cmd`**. Click **START SERVER**, wait for
   **RUNNING**, then click **ENTER REALM**. Your account is created the first time.

You **don't** need to download the other files on this page by hand: the downloader fetches and checks them.

## What's new in 0.3

- **Companions and gamebots fight like 1.65 players:** positional styles and style chains, debuffs before nukes,
  stealth openers, Quick Cast, sensible taunting, crowd control that isn't broken, healers that put the tank first and
  raise you first. Companions get an **AI OPTIONS** tab (charm, pet choice, taunt, positioning, roles and more), and
  gamebots you invite into your group work for you like your companions.
- **RvR as in 1.64:** keeps, gate forts, milegates and their doors from the real 1.64 client; battleground keeps
  where the period client puts them; siege engines that need ammunition, can be crafted, and are drawn with their
  own models; warbands that follow a main assist.
- **Crafting:** every recipe of the 1.64 client works, crafted items come out in your realm, and the 80
  metalworking recipes that can't be made in 1.65 are gone from the list.
- **World and items:** monster levels, aggression and drops from the Prima guides; quest items drop only while you
  are on the quest; out-of-level drops are gone; merchants sell their own realm's goods under the right titles; the
  camp bonus tops out at the period values; an account vault keeper in each capital (a convenience addition).
- **Quests:** new quests from period sources, The Growling Ghost's two branches, and every zone map of your realm shows
  the quests you can take there.
- **Realm Exchange:** every item can be traded and listed, quest items too; one chest of 100 listings per player,
  shown as five pages of 20; sort the columns, choose 12, 24 or 48-hour listings, see each listing's time left, and
  search for similar items. Bots list only items with a real use.
- **The new client:** realm maps and gamebot dots on the maps, the 1.64 camera options, small chat text and
  per-window transparency, the AUTO LOOT option (off by default) and a Pick Up Loot key, realm ability descriptions
  in the trainer, and a fix for a freeze when some spell effects loaded.
- **Bug reports fixed:** [#6](https://github.com/shadowofze/OfflineDAoC-True165/issues/6),
  [#7](https://github.com/shadowofze/OfflineDAoC-True165/issues/7),
  [#8](https://github.com/shadowofze/OfflineDAoC-True165/issues/8),
  [#9](https://github.com/shadowofze/OfflineDAoC-True165/issues/9),
  [#10](https://github.com/shadowofze/OfflineDAoC-True165/issues/10),
  [#13](https://github.com/shadowofze/OfflineDAoC-True165/issues/13),
  [#14](https://github.com/shadowofze/OfflineDAoC-True165/issues/14) and
  [#16](https://github.com/shadowofze/OfflineDAoC-True165/issues/16). Thanks to everyone who reported them, and to
  the rsandersjr and FriendMaple forks for fixes taken from them.

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
