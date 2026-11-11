# Living Server for True 1.65: the talk layer

**Status: in development. It builds and its automated tests pass. Nobody has played it yet.** See
[What has been checked](#what-has-been-checked) before relying on anything here.

Living Server is [chris4aker's add-on](https://github.com/chris4aker/OfflineDAoC-LivingServer) for the original
Offline DAoC (0.37): gamebots with personalities and voices that talk, argue, remember you and hold grudges. This
branch carries its first part, the **talk layer**, onto Offline DAoC True 1.65.

- Carried from: Living Server Beta 1.1.3 (`v1.1.3-beta`, commit `dd2e856`).
- Carried onto: `shadowofze/OfflineDAoC-True165` main at `bfdb8d8` (after v0.5, the work for 0.6).
- Where True 1.65's rules and Living Server's choices differ, True 1.65's rules win (owner, 2026-11-11).

## What the talk layer adds

| Part | What you see |
|---|---|
| Bot voices | 34,532 written lines in 2003 chat style, chosen by a gamebot's realm, class, level, personality and what it is doing. |
| Personalities | Each gamebot is a Leveler, Socialite, Daredevil, Explorer, Crafter or No-lifer. This sets how it writes and how much it talks. |
| Scenes and debates | 393 realm chat scenes with a cast of bots: smite against rejuv, buffbots, zergs, relic panic. |
| Storylines | Pairs of bots live out a drama over days: a kill steal, a guild split, a ninja loot. |
| Answers | Bots answer what you say in /say, /group, /b, /trade and by /send, and follow the thread of the talk. |
| Memory of you | Bots remember grouping with you, your rezzes and your insults. They greet you, send a tell now and then, and gossip. |
| Your deeds | Bots that see you solo a named monster or go on a kill streak tell the realm. |
| Server life | Grats on a level. Off until switched on: server firsts, a dead bot asking you for a rez, frontier alarms, a bot's nemesis. |
| Rez from a stranger | With `bot_living_death` on, a gamebot that can resurrect raises a dead player of its realm nearby. You still get the accept box. |
| Rumours | True ones: a rare named monster that has just come back. |
| In your group | A gamebot in your group talks in /group and calls the fight: pull, adds, out of power, wipe. |
| Characters | A few beggars on capital corners, a gold spammer that gets banned, a few roleplayers. |
| Holidays | Around the holidays on your PC's calendar the chat changes. |
| Friends | Gamebots remember the bots they grouped with and who stood them up. |

Commands: `/personality` (a gamebot's type), `/lswhy` (why a bot said or didn't say something), and for GMs
`/lstest` (fires a Living Server moment on demand, so each can be checked in game).

## How it joins True 1.65

- **The banter library and the voices share the chat.** For an unprompted /say or realm line a coin
  (`AutonomousBotChatCoordinator.BanterShare`, 0.5) picks which is asked first; either stands in when the other has
  nothing that fits. Replies to a banter line come from the banter library. When you talk to a bot, the voices answer
  first and the banter library answers where they have nothing, before the old formal lines. Realm Exchange adverts
  are answered from either. `BotBanter165`'s own events (horn rallies, realm notices, guild chat) are untouched.
- **Hired companions are never part of it.** In True 1.65 a hired companion is a persistent bot too, so the ported
  code speaks only for `GameBot.IsWorldGamebot` and reads `AutonomousBotRegistry.WorldSnapshot()`. A companion keeps
  `CompanionChat165`, its own return lines and its own /say.
- **Gamebots leave only through the population system.** A bot that rage quits after losing a debate asks
  `AutonomousBotLifecycle.RequestLogout`; it is never deleted directly, and that system decides when it plays again.
- **No realm-wide /lfg.** True 1.65 has no /lfg, so gamebots don't post there. Their LFG lines go to /broadcast in
  town and to the realm's talk.
- **/faction stays GM-only for players**, as True 1.65 has it. You read the realm's talk; you answer in /say,
  /group, /gu, /b, /trade or by /send. Joining a debate or taking a side in a storyline needs /faction, so a normal
  account can watch those but not take part.
- **Seven lines were taken out of the corpus:** six about Cathal Valley (a battleground from after patch 1.65) and
  one that mentions /lfg. The rest passed a check against the banter validator's out-of-period list
  (`validate_banter165.py`): no Trials of Atlantis or later terms, classes or zones.
- **Bots follow the player rules.** Beggars receive real coin from players and bots and nothing is created. A bot on
  a siege, a raid, a relic run or a horse route turns an invitation down and says why.

## Settings

Server properties (the `ServerProperty` table). The defaults are Living Server's own, with one exception:
personalities are on, because the voices are chosen by them. As in Living Server, the chat parts are on and the
parts that change what a bot does (firsts, death, alarms, nemesis) are off until you switch them on.

| Setting | Default | What it does |
|---|---|---|
| `bot_voices_enabled` | on | The written lines. Off: True 1.65's chat as it was (banter library first). |
| `bot_voices_debates` | on | Realm chat scenes and debates. |
| `bot_voices_tells` | on | A bot that remembers you sends a tell now and then. |
| `bot_voices_chat_rate` | 2 | How much bots talk on their own, 0 (only answer) to 3. |
| `bot_personalities_enabled` | on | Personality types. Here they set voice, chattiness and town breaks only. |
| `bot_living_memory` | on | Bots remember you. |
| `bot_living_grats` | on | Grats on a level. |
| `bot_living_firsts` | off | Server firsts are announced. |
| `bot_living_death` | off | Rez requests and thanks; gamebots raise dead players nearby. |
| `bot_living_alarms` | off | Frontier bots call out enemies they see. |
| `bot_living_nemesis` | off | A bot remembers who killed it. |
| `bot_living_deeds` | on | Bots tell the realm what they saw you do. |
| `bot_living_storylines` | on | Dramas over days. |
| `bot_living_characters` | on | The gold spammer and the roleplayers. |
| `bot_living_rumours` | on | True rumours. |
| `bot_living_holidays` | on | Holiday chatter. |
| `bot_living_beggars` | on | Beggars in the capitals. |
| `bot_group_bonds` | on | Bots remember who they grouped with. It does not change who groups with whom yet. |
| `bot_schedule_mode` | off | Leave off. True 1.65's population system decides who is online. |
| `bot_living_breaks` | off | Leave off for the same reason. |
| `bot_living_body_language` | off | Wired in towns only; not ready. |
| `bot_guilds_enabled` and the other `bot_guild_*` | off | Bot-run guilds. The code is here but switched off and not joined to True 1.65's guild code. |

There is no launcher tab for these yet; until there is, a setting is changed in the `ServerProperty` table.

## Not carried over

**Left behind on purpose** (they break True 1.65's rules, or True 1.65 has its own):
- Epic Special Forces (gamebots handed epic gear), XP by level range, quest XP by level, faster power regeneration.
- The Atlas daily quests and the Hearth quest chains.
- Bot logins and logouts by schedule, "brb" breaks and linkdeads: True 1.65's population system does this.
- The 2003 name generator, quest markers, guild dues, flanking: True 1.65 has its own.
- The Setup installer, the launcher's Living Server tab and everything for the original client.

**Here but switched off** (they compile so the talk layer builds; they need their own pass):
- Bot-run guilds and Living Server's guild chat. Gamebots in your guild still talk as True 1.65 has them.
- Body language.
- Personality tastes in camp, dungeon and goal choice, and friends looking for each other when groups form. These
  would change how True 1.65's gamebots hunt, so they wait for a play test.

**Later parts of Living Server:**
- Epic encounters: raid tactics and restored boss mechanics.
- From Living Server 1.2, once it is released: bot-led groups, RvR perception, the lore, raid skill.
- Reactions to incoming fire, the route fixes and the diagnostics.

## What has been checked

- **Build:** the whole solution builds with 0 errors (.NET 10).
- **Automated tests:** 5,572 tests run and pass, against 4,113 in True 1.65 before the port. One of True 1.65's own
  tests, `UT_BotNameService.Claims_FiftyThousandPerRealmUniqueAndNeverThrow`, fails on some runs; it did so in 2 of 6
  runs on unmodified True 1.65 too, so it is not part of this work.
- **Server start:** the server starts on True 1.65's clean world (Linux, without the navigation library and the
  launcher's content step), creates its new tables, loads 34,532 voice lines and 3,865 banter lines, and logs in
  gamebots for four minutes with the same errors and warnings as unmodified True 1.65.
- **Place names:** 145 of the 146 zone names the voice lines use exist in True 1.65's world.

**Not checked:**
- Nothing has been seen in game. No player was connected in any test, and bots only talk when a player can hear, so
  no chat line has been said by a running server.
- How the banter library and the voices sound together.
- Server load at 500 to 1,000 gamebots per realm.
- The new client: whether every chat channel and the rez box show as expected.

## Working on it

- [tools/living-server-port/PORTED-FILES.md](../tools/living-server-port/PORTED-FILES.md) lists every file: copied
  unchanged from Living Server, copied with the one mechanical rename, edited for True 1.65, and the True 1.65 files
  that were changed.
- The mechanical rename in copied files: `IsAutonomousWorldBot` became `IsWorldGamebot`, and
  `AutonomousBotRegistry.Snapshot()` became `AutonomousBotRegistry.WorldSnapshot()`.
- Voice lines: edit the `.lines` files in `development-source/server/GameServer/bots/autonomous/data/chat`, then
  run `python tools/bot-voices/build_corpus.py` to rebuild `corpus.json`. The format is in
  [bot-voices/DESIGN.md](bot-voices/DESIGN.md); the other documents there were written for Offline DAoC 0.37.
- Living Server tests that test parts not carried over were left out or trimmed; the list is in PORTED-FILES.md.
