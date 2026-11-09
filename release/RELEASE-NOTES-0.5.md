**Offline DAoC True 1.65 v0.5**: single-player Dark Age of Camelot as it was at patch 1.65 (Classic and Shrouded
Isles, November 2003), with a world full of gamebots, hired companions, and a new client built in Godot 4.

> **This is an AI-made project.** All of the code was written by AI tools; the project owner has no coding
> experience. Large parts of the game haven't been played through by a person. Expect bugs, and keep a backup of
> your save.

## What's new in 0.5

- **A living online world:** set a gamebot population goal per realm in the launcher, and gamebots log in and out
  over time like players (veterans, regulars, casual players, newcomers, and some who quit and come back), with every
  level range kept filled and new level 1s arriving all the time. Up to 1,000,000 gamebots are saved. An updated game
  keeps every gamebot it has and every character's companions. Gamebots get 3.3 million race-styled names, and saved
  level 50s answer the Horn of Rallying.
- **Crafting overhaul, a large deviation from 1.65:** crafting works anywhere without tools, MAKE buys the missing
  materials, the tradeskill window is rebuilt with filters and recommendations, an optional crafting assistant trains a
  skill to a goal, and an enchant helper puts spellcraft gems or alchemy tinctures on an item. Every craft still costs
  real gold and takes the normal 1.65 time. Crafter gamebots get their gear enchanted by their realm's crafters.
- **Quality of life options, all off by default in Options:** CRAFT ALL, the crafting assistant, and /grind (your
  companions pull for you while you stay put).
- **Gamebots play their classes better:** Necromancers, Paladins, Wardens and melee healers, crowd control like
  veterans, fewer empty camps, and fewer bots stuck on ledges.
- **Character sheet:** your class's main stats are named in gold, and hovering a stat says what it gives your class.
- **Fixes from player reports:** putting an item back in its slot no longer equips it, /stick holds while you strafe,
  sitting no longer turns your character, a Berserker keeps its weapons in Berserk form, a stable horse no longer drops
  you by itself, dead group members show in red, chanting companions no longer sit and stand over and over in /grind, /grind finds monsters as far as your puller can shoot, companions say why they won't use a shield and no longer join your
  guild, a Mystic casts from Piety, and enchanted items show their new stats at once.
- **More fixes from player reports:** a quest that asks for several of one item takes them all, quest NPCs no longer
  stand beside a twin, quests where a character turns on you can be finished again, every item a monster drops can
  be sold, enemy-realm kills pay experience as well as realm
  points, Abermenai's ground has its detail, a stunned or mesmerized character no longer runs on the spot, and the
  Target Enemy key picks characters of the other realms.

The full list is in the [changelog](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/CHANGELOG.md).

## Already playing an earlier version? Update in place (about 2.8 GB)

1. Quit the game, click **STOP SERVER**, and close the launcher.
2. Under **Assets** below, download **`Download-OfflineDAoC-True165-v0.5.zip`** (a few KB: just the downloader),
   extract it, and double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`**. If Windows SmartScreen appears, click
   **More info → Run anyway**.
3. Press **1 = Update my game**. It finds your `OfflineDAoC-True165` folder (or asks you to pick it), checks every
   file it will replace, and updates the game where it is. Your account, characters, companions, bots and settings
   stay; the files it replaces are backed up in `patch-backups\`. One update covers every earlier version, v0.1 to
   v0.4.1.
4. Start the game as usual. The first **START SERVER** after the update brings your world up to date and can take a
   few minutes.

If it finds files that were changed on your PC, it changes nothing and can make a fresh install that brings your save
over.

## New players: the complete game (about 36 GB)

1. Download **`Download-OfflineDAoC-True165-v0.5.zip`** below and put it in a folder with a short path, such as
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
