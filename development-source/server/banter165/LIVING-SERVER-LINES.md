# Living Server lines in the banter library

Written by `tools/import_living_server_banter.py`; the `ls_*.json` files beside this page are its output.
Do not edit them by hand: change the tool or the source and run it again.

Living Server's lines are lower-case 2003 chat. Here each one is given a situation and filters of this
library, rewritten into its voice by rules, and checked by `validate_banter165.py`. A line that the rules
refuse, that talks about the world outside the game, or that names something that is not in the True 1.65
world is left out. A line that needs a system True 1.65 does not have is held: counted below, not written.

The rewriting is automatic. Samples of every file were read, not every line, and none of these lines has
been heard in a running game yet.

| | Lines |
|---|---|
| Read from Living Server (the chat corpus, its scenes and the lore bible; one per written line) | 20505 |
| Written into the library | 4454 |
| Held until the library has a way to say them | 14615 |
| Left out | 1436 |

## Files

| File | Lines |
|---|---|
| `ls_classes.json` | 673 |
| `ls_everyday.json` | 612 |
| `ls_frontier.json` | 254 |
| `ls_groups.json` | 135 |
| `ls_guild.json` | 279 |
| `ls_hunting.json` | 335 |
| `ls_lore_albion.json` | 45 |
| `ls_lore_hibernia.json` | 37 |
| `ls_lore_midgard.json` | 64 |
| `ls_lore_places.json` | 1289 |
| `ls_lore_shared.json` | 114 |
| `ls_realms.json` | 176 |
| `ls_replies.json` | 342 |
| `ls_trade_craft.json` | 99 |

## Held: what each waits for

| Waiting for | Lines |
|---|---|
| Raid talk that depends on the boss mechanics being the same here | 6129 |
| Lore that Living Server wrote itself (not from the game or a period source) | 2921 |
| Answers that follow the thread of a conversation | 1006 |
| Answers to a question about a named place | 923 |
| Bots that witness what the player does | 554 |
| Lines written about a named place (they need the place they are about) | 549 |
| Conversations of many lines between a cast of bots | 419 |
| Calls during a fight in the player's group | 375 |
| Beggars in the capitals | 207 |
| The gold spammer | 165 |
| Roleplayer characters | 140 |
| The time of day | 122 |
| Live rumours (a named monster is up, a keep is thin) | 116 |
| Bots that remember who they grouped with | 113 |
| Bots that remember the player | 111 |
| A calendar (holiday talk) | 93 |
| Bots that send the player a tell | 92 |
| Bots that lead a group | 81 |
| Answers the library has no situation for (are you real, away, back, a resurrection) | 74 |
| Talk in the player's own group | 66 |
| Bots that say hello and goodbye as they log in and out | 63 |
| What a monster does in a fight, its level or when it shows itself (it may differ in this world) | 63 |
| News the library has no situation for (relics returned, Darkness Falls, sightings) | 54 |
| Storylines between two bots | 51 |
| How far a bot is into its level | 20 |
| Bots that ask the player for a resurrection | 17 |
| News of the last keep taken | 14 |
| Who holds Darkness Falls | 13 |
| Bots that remember who killed them | 11 |
| Frontier alarms with a head count | 9 |
| News of the last relic taken | 8 |
| Server firsts | 5 |
| A condition the library cannot test (attacking) | 3 |
| A condition the library cannot test (in_rvr) | 2 |
| A condition the library cannot test (in_frontier) | 1 |
| A condition the library cannot test (near:audliten) | 1 |
| A condition the library cannot test (going:myrkwood) | 1 |
| A condition the library cannot test (near:nisse's lair) | 1 |
| A condition the library cannot test (near:cursed tomb) | 1 |
| A condition the library cannot test (near:vendo caverns) | 1 |
| A condition the library cannot test (near:spindelhalla) | 1 |
| A condition the library cannot test (near:iarnvidiur's lair) | 1 |
| A condition the library cannot test (near:tomb of mithra) | 1 |
| A condition the library cannot test (near:black mountains) | 1 |
| A condition the library cannot test (near:isle of glass) | 1 |
| A condition the library cannot test (near:stonehenge barrows) | 1 |
| A condition the library cannot test (near:connacht) | 1 |
| A condition the library cannot test (near:muire tomb) | 1 |
| A condition the library cannot test (near:spraggon den) | 1 |
| A condition the library cannot test (going:tomb of mithra) | 1 |
| A condition the library cannot test (near:salisbury plains) | 1 |
| A condition the library cannot test (going:krondon) | 1 |
| A condition the library cannot test (near:svealand) | 1 |
| A condition the library cannot test (going:spindelhalla) | 1 |
| A condition the library cannot test (going:cursed tomb) | 1 |
| A condition the library cannot test (near:lough derg) | 1 |
| A condition the library cannot test (going:muire tomb) | 1 |
| A condition the library cannot test (going:koalinth caverns) | 1 |
| A condition the library cannot test (raid_won_recent) | 1 |
| A condition the library cannot test (raid_wiped_recent) | 1 |

## Left out: why

| Reason | Lines |
|---|---|
| Talk about the world outside the game | 683 |
| Chat shorthand the rules do not know | 152 |
| The place or monster is not in the True 1.65 world | 134 |
| The library already says it | 73 |
| Someone or something that is not in the True 1.65 world | 67 |
| A slot the library cannot fill here ({camp}) | 55 |
| A way of talking the library does not use | 50 |
| A slot the library cannot fill here ({monsters}) | 30 |
| A slot the library cannot fill here ({need}) | 30 |
| A slot the library cannot fill here ({dungeon}) | 29 |
| A monster that is not in the True 1.65 world | 16 |
| A slot the library cannot fill here ({capital}) | 14 |
| A slot the library cannot fill here ({place}) | 13 |
| A slot the library cannot fill here ({target}) | 13 |
| Emotes, symbols or quotes | 11 |
| A slot the library cannot fill here ({keep}) | 11 |
| Refused by the library's rules: forbidden word | 10 |
| A slot the library cannot fill here ({frontier}) | 10 |
| A command or a symbol | 8 |
| Nothing left to say | 5 |
| A slot the library cannot fill here ({count}) | 4 |
| Too long or too short | 4 |
| A slot the library cannot fill here ({player}) | 3 |
| A slot the library cannot fill here ({other}) | 3 |
| A slot the library cannot fill here ({item}) | 2 |
| Stretched words | 2 |
| A place that is not in the True 1.65 world | 1 |
| Refused by the library's rules: forbidden phrase | 1 |
| Shouting | 1 |
| A line that does not read as a sentence | 1 |

## By kind of line

| Living Server kind / topic | Read | Written | Held | Left out |
|---|---|---|---|---|
| lore bible / monster rumour | 2151 | 547 | 1397 | 207 |
| lore bible / monster briefing | 2149 | 0 | 2149 | 0 |
| lore bible / monster aftermathWin | 2088 | 0 | 2088 | 0 |
| lore bible / monster aftermathLoss | 1892 | 0 | 1892 | 0 |
| reply / general | 1839 | 612 | 1068 | 159 |
| lore bible / place lore | 1334 | 414 | 810 | 110 |
| lore bible / place rumour | 1174 | 328 | 768 | 78 |
| lore bible / place tell | 923 | 0 | 923 | 0 |
| ambient / class | 688 | 631 | 0 | 57 |
| ambient / lore | 632 | 260 | 311 | 61 |
| scenes and debates / - | 419 | 0 | 419 | 0 |
| guild / social | 289 | 41 | 58 | 190 |
| ambient / realm_pride | 211 | 176 | 2 | 33 |
| ambient / places | 166 | 18 | 144 | 4 |
| guild / general | 147 | 110 | 2 | 35 |
| ambient / leveling | 112 | 64 | 2 | 46 |
| ambient / general | 100 | 52 | 42 | 6 |
| closing / general | 100 | 82 | 0 | 18 |
| group / group | 100 | 45 | 49 | 6 |
| ambient / newbie | 84 | 60 | 7 | 17 |
| ambient / veteran | 78 | 54 | 3 | 21 |
| ambient / holiday | 75 | 0 | 75 | 0 |
| roleplay / ambient | 66 | 0 | 66 | 0 |
| guild / crafting | 63 | 56 | 0 | 7 |
| rvr / frontier | 60 | 32 | 3 | 25 |
| spam / ad | 59 | 0 | 59 | 0 |
| greet / general | 57 | 0 | 57 | 0 |
| ambient / dungeon | 55 | 52 | 1 | 2 |
| lfg / lfm | 55 | 2 | 0 | 53 |
| rvr / frontier_alb | 55 | 25 | 19 | 11 |
| ambient / classes | 53 | 42 | 0 | 11 |
| rvr / frontier_mid | 49 | 27 | 16 | 6 |
| guild / leveling | 47 | 33 | 4 | 10 |
| lfg / lfg_self | 47 | 33 | 3 | 11 |
| rvr / frontier_hib | 47 | 24 | 16 | 7 |
| roleplay / mock | 45 | 0 | 45 | 0 |
| spam_react / react | 45 | 0 | 45 | 0 |
| ambient / capital | 41 | 27 | 8 | 6 |
| beggar / insult | 40 | 0 | 40 | 0 |
| rvr / battlegrounds | 39 | 11 | 17 | 11 |
| deed / silly_death | 38 | 0 | 38 | 0 |
| ambient / death | 37 | 28 | 3 | 6 |
| guild / rvr | 37 | 23 | 8 | 6 |
| spam_react / banned | 35 | 0 | 35 | 0 |
| ambient / places_here | 33 | 22 | 9 | 2 |
| deed / named_solo | 33 | 0 | 33 | 0 |
| rumour / named_up | 33 | 0 | 33 | 0 |
| closing / - | 32 | 31 | 0 | 1 |
| reply / player | 32 | 0 | 32 | 0 |
| ambient / grouping | 31 | 20 | 0 | 11 |
| beggar / beg | 31 | 0 | 31 | 0 |
| guild / raiding | 31 | 16 | 5 | 10 |
| rumour / keep_thin | 31 | 0 | 31 | 0 |
| ambient / banter | 29 | 17 | 0 | 12 |
| roleplay / answer | 29 | 0 | 29 | 0 |
| ambient / loot | 27 | 23 | 0 | 4 |
| ambient / mobs | 27 | 24 | 0 | 3 |
| bond / friends | 27 | 0 | 27 | 0 |
| deed / fell | 27 | 0 | 27 | 0 |
| deed / rvr_streak | 27 | 0 | 27 | 0 |
| storyline / side_scorn | 27 | 0 | 27 | 0 |
| ambient / crafting | 26 | 24 | 0 | 2 |
| deed / nickname | 26 | 0 | 26 | 0 |
| spam / tell | 26 | 0 | 26 | 0 |
| ambient / explorer | 25 | 23 | 0 | 2 |
| ambient / leveler | 25 | 14 | 2 | 9 |
| ambient / nolifer | 25 | 0 | 0 | 25 |
| ambient / socialite | 25 | 14 | 1 | 10 |
| bond / flake | 25 | 0 | 25 | 0 |
| ambient / crafter | 24 | 19 | 1 | 4 |
| ambient / daredevil | 24 | 21 | 0 | 3 |
| bond / reunion | 24 | 0 | 24 | 0 |
| deed / ganked | 24 | 0 | 24 | 0 |
| deed / named_group | 24 | 0 | 24 | 0 |
| deed / rep_named | 24 | 0 | 24 | 0 |
| deed / rep_silly | 24 | 0 | 24 | 0 |
| storyline / side_thanks | 24 | 0 | 24 | 0 |
| deed / rep_rvr | 23 | 0 | 23 | 0 |
| lfg / lfg | 23 | 19 | 0 | 4 |
| ambient / money | 22 | 18 | 0 | 4 |
| trade / crafting | 21 | 18 | 0 | 3 |
| deed_reply / named_solo | 19 | 0 | 19 | 0 |
| rvr / keeps | 18 | 10 | 7 | 1 |
| ambient / gripe | 17 | 9 | 0 | 8 |
| ambient / latenight | 17 | 0 | 17 | 0 |
| deed_reply / nickname | 17 | 0 | 17 | 0 |
| deed_reply / silly_death | 17 | 0 | 17 | 0 |
| lfg / join | 17 | 0 | 17 | 0 |
| rumour_reply / keep_thin | 17 | 0 | 17 | 0 |
| rumour_reply / named_up | 17 | 0 | 17 | 0 |
| session / ragequit_back | 17 | 0 | 17 | 0 |
| beggar / corner | 16 | 0 | 16 | 0 |
| combat / add | 16 | 0 | 16 | 0 |
| combat / pull | 16 | 0 | 16 | 0 |
| deed_reply / fell | 16 | 0 | 16 | 0 |
| deed_reply / ganked | 16 | 0 | 16 | 0 |
| deed_reply / rvr_streak | 16 | 0 | 16 | 0 |
| tell / hello_friend | 16 | 0 | 16 | 0 |
| ambient / travel | 15 | 11 | 1 | 3 |
| deed_reply / named_group | 15 | 0 | 15 | 0 |
| deed_reply / rep_named | 15 | 0 | 15 | 0 |
| tell / group_offer | 15 | 0 | 15 | 0 |
| ambient / shrouded_isles | 14 | 1 | 12 | 1 |
| ambient / town | 14 | 11 | 0 | 3 |
| bond_reply / flake | 14 | 0 | 14 | 0 |
| deed_reply / rep_silly | 14 | 0 | 14 | 0 |
| gossip / general | 14 | 0 | 14 | 0 |
| news / siege_defend | 14 | 0 | 14 | 0 |
| tell / guildmate | 14 | 0 | 14 | 0 |
| beggar / heard | 13 | 0 | 13 | 0 |
| bond_reply / friends | 13 | 0 | 13 | 0 |
| camp / neighbours | 13 | 10 | 0 | 3 |
| combat / kill | 13 | 0 | 13 | 0 |
| deed_reply / rep_rvr | 13 | 0 | 13 | 0 |
| tell / news_tell | 13 | 0 | 13 | 0 |
| ambient / boast | 12 | 9 | 0 | 3 |
| ambient / camp | 12 | 9 | 1 | 2 |
| ambient / humor | 12 | 7 | 1 | 4 |
| ambient / night | 12 | 0 | 12 | 0 |
| ambient / roles | 12 | 11 | 0 | 1 |
| beggar / beg_idle | 12 | 0 | 12 | 0 |
| camp / monsters | 12 | 4 | 1 | 7 |
| camp / pulls | 12 | 10 | 0 | 2 |
| camp / spawn | 12 | 8 | 0 | 4 |
| combat / low_hp | 12 | 0 | 12 | 0 |
| combat / wipe | 12 | 0 | 12 | 0 |
| grats / general | 12 | 2 | 1 | 9 |
| news / keep_lost | 12 | 0 | 12 | 0 |
| news / keep_won | 12 | 0 | 12 | 0 |
| news / relic_lost | 12 | 0 | 12 | 0 |
| news / siege_attack | 12 | 0 | 12 | 0 |
| ambient / bags | 11 | 11 | 0 | 0 |
| beggar / outraged | 11 | 0 | 11 | 0 |
| beggar / thanks_coin | 11 | 0 | 11 | 0 |
| combat / heal_big | 11 | 0 | 11 | 0 |
| combat / mez | 11 | 0 | 11 | 0 |
| combat / mez_broke | 11 | 0 | 11 | 0 |
| combat / oom | 11 | 0 | 11 | 0 |
| combat / player_down | 11 | 0 | 11 | 0 |
| combat / rest | 11 | 0 | 11 | 0 |
| combat / root | 11 | 0 | 11 | 0 |
| combat / taunt | 11 | 0 | 11 | 0 |
| news / df_won | 11 | 0 | 11 | 0 |
| news / enemy_seen | 11 | 0 | 11 | 0 |
| news / keep_other | 11 | 0 | 11 | 0 |
| news / raid_forming | 11 | 0 | 11 | 0 |
| news / raid_won | 11 | 0 | 11 | 0 |
| news / relic_back | 11 | 0 | 11 | 0 |
| news / siege_failed | 11 | 0 | 11 | 0 |
| news / siege_held | 11 | 0 | 11 | 0 |
| session / ragequit_tell | 11 | 0 | 11 | 0 |
| ambient / reallife | 10 | 0 | 0 | 10 |
| beggar / hoard | 10 | 0 | 10 | 0 |
| bond_reply / reunion | 10 | 0 | 10 | 0 |
| combat / displaced_heal | 10 | 0 | 10 | 0 |
| combat / rez | 10 | 0 | 10 | 0 |
| combat / stun | 10 | 0 | 10 | 0 |
| ding / general | 10 | 9 | 0 | 1 |
| group / holiday | 10 | 0 | 10 | 0 |
| group / lead_pull | 10 | 0 | 10 | 0 |
| news / df_lost | 10 | 0 | 10 | 0 |
| news / raid_wiped | 10 | 0 | 10 | 0 |
| news / relic_won | 10 | 0 | 10 | 0 |
| tell / after_group | 10 | 0 | 10 | 0 |
| tell / help_offer | 10 | 0 | 10 | 0 |
| trade / buying | 10 | 9 | 0 | 1 |
| alarm / general | 9 | 0 | 9 | 0 |
| ambient / ding | 9 | 8 | 0 | 1 |
| ambient / group | 9 | 9 | 0 | 0 |
| ambient / waiting | 9 | 8 | 0 | 1 |
| ambient / xp | 9 | 0 | 9 | 0 |
| death / general | 9 | 7 | 0 | 2 |
| reply / boast | 9 | 0 | 9 | 0 |
| reply / complaint | 9 | 0 | 9 | 0 |
| trade / selling | 9 | 8 | 1 | 0 |
| ambient / evening | 8 | 0 | 8 | 0 |
| beggar / donor_give | 8 | 0 | 8 | 0 |
| beggar / donor_refuse | 8 | 0 | 8 | 0 |
| camp / general | 8 | 5 | 0 | 3 |
| grudge / general | 8 | 0 | 8 | 0 |
| rez_ask / general | 8 | 0 | 8 | 0 |
| rez_thanks / general | 8 | 4 | 0 | 4 |
| ambient / daytime | 7 | 0 | 7 | 0 |
| beggar / thanks_item | 7 | 0 | 7 | 0 |
| combat / displaced_self | 7 | 0 | 7 | 0 |
| combat / mech_funus | 7 | 0 | 7 | 0 |
| grats / fifty | 7 | 0 | 0 | 7 |
| nemesis / general | 7 | 0 | 7 | 0 |
| session / brb | 7 | 0 | 7 | 0 |
| session / logoff | 7 | 0 | 7 | 0 |
| session / logon | 7 | 0 | 7 | 0 |
| thanks / group | 7 | 1 | 0 | 6 |
| ambient / lowlevel | 6 | 5 | 0 | 1 |
| ambient / rumor | 6 | 3 | 0 | 3 |
| ambient / solo | 6 | 6 | 0 | 0 |
| beggar / gone | 6 | 0 | 6 | 0 |
| beggar / tell | 6 | 0 | 6 | 0 |
| combat / displaced_jail | 6 | 0 | 6 | 0 |
| combat / epic_glare | 6 | 0 | 6 | 0 |
| combat / epic_messengers | 6 | 0 | 6 | 0 |
| combat / epic_twin_heal | 6 | 0 | 6 | 0 |
| reply / joke | 6 | 0 | 6 | 0 |
| reply / news | 6 | 0 | 6 | 0 |
| reply / rvr | 6 | 0 | 6 | 0 |
| beggar / beg_bot | 5 | 0 | 5 | 0 |
| beggar / spurned | 5 | 0 | 5 | 0 |
| combat / epic_breath | 5 | 0 | 5 | 0 |
| combat / epic_falls | 5 | 0 | 5 | 0 |
| combat / epic_frost_circle | 5 | 0 | 5 | 0 |
| combat / epic_griffon_pull | 5 | 0 | 5 | 0 |
| combat / epic_kvasir_cold | 5 | 0 | 5 | 0 |
| combat / epic_kvasir_scream | 5 | 0 | 5 | 0 |
| combat / epic_legion_wounded | 5 | 0 | 5 | 0 |
| combat / epic_stun | 5 | 0 | 5 | 0 |
| combat / mech_adds_first | 5 | 0 | 5 | 0 |
| combat / mech_cold_fire | 5 | 0 | 5 | 0 |
| combat / mech_hazard | 5 | 0 | 5 | 0 |
| combat / mech_sacristan | 5 | 0 | 5 | 0 |
| combat / mech_sphere | 5 | 0 | 5 | 0 |
| corpse / general | 5 | 5 | 0 | 0 |
| first / general | 5 | 0 | 5 | 0 |
| lfg / lead_lf_more | 5 | 0 | 5 | 0 |
| linkdead / back | 5 | 0 | 5 | 0 |
| reply / congrats | 5 | 2 | 0 | 3 |
| rez_offer / general | 5 | 0 | 5 | 0 |
| rvr / darkness_falls | 5 | 0 | 5 | 0 |
| session / back | 5 | 0 | 5 | 0 |
| tell / lead_invite | 5 | 0 | 5 | 0 |
| thanks / rez | 5 | 0 | 0 | 5 |
| ambient / battlegrounds | 4 | 1 | 3 | 0 |
| beggar / calm | 4 | 0 | 4 | 0 |
| beggar / sold | 4 | 0 | 4 | 0 |
| closing / interested | 4 | 0 | 0 | 4 |
| combat / displaced_heal_fire | 4 | 0 | 4 | 0 |
| combat / displaced_stuck | 4 | 0 | 4 | 0 |
| combat / epic_close_in | 4 | 0 | 4 | 0 |
| combat / epic_council_split | 4 | 0 | 4 | 0 |
| combat / epic_forn_shards | 4 | 0 | 4 | 0 |
| combat / epic_ghoul_kite | 4 | 0 | 4 | 0 |
| combat / epic_jail_team | 4 | 0 | 4 | 0 |
| combat / epic_jail_team_away | 4 | 0 | 4 | 0 |
| combat / epic_safeSpots | 4 | 0 | 4 | 0 |
| combat / epic_stomp | 4 | 0 | 4 | 0 |
| combat / mech_fight_home | 4 | 0 | 4 | 0 |
| combat / mech_prince_pets | 4 | 0 | 4 | 0 |
| combat / mech_ranged_adds | 4 | 0 | 4 | 0 |
| combat / mech_reckoner_room | 4 | 0 | 4 | 0 |
| combat / mech_sanguis | 4 | 0 | 4 | 0 |
| group / lead_handover | 4 | 0 | 4 | 0 |
| group / lead_sell | 4 | 0 | 4 | 0 |
| group / lead_town | 4 | 0 | 4 | 0 |
| linkdead / gone | 4 | 0 | 4 | 0 |
| reply / lfg | 4 | 0 | 4 | 0 |
| revenge / general | 4 | 0 | 4 | 0 |
| rez_ask / player | 4 | 0 | 4 | 0 |
| sickness / general | 4 | 3 | 0 | 1 |
| tell / lead_invite_asked | 4 | 0 | 4 | 0 |
| thanks / heal | 4 | 2 | 0 | 2 |
| ambient / darkness_falls | 3 | 3 | 0 | 0 |
| beggar / full | 3 | 0 | 3 | 0 |
| beggar / tell_group | 3 | 0 | 3 | 0 |
| beggar / thanks_bot | 3 | 0 | 3 | 0 |
| combat / displaced_heal_bane | 3 | 0 | 3 | 0 |
| combat / displaced_heal_pit | 3 | 0 | 3 | 0 |
| combat / displaced_heal_wind | 3 | 0 | 3 | 0 |
| combat / displaced_self_bane | 3 | 0 | 3 | 0 |
| combat / displaced_self_fire | 3 | 0 | 3 | 0 |
| combat / displaced_self_pit | 3 | 0 | 3 | 0 |
| combat / displaced_self_wind | 3 | 0 | 3 | 0 |
| combat / epic_face_him | 3 | 0 | 3 | 0 |
| group / lead_going_on | 3 | 0 | 3 | 0 |
| group / lead_last_bot | 3 | 0 | 3 | 0 |
| group / lead_move | 3 | 0 | 3 | 0 |
| group / lead_raid_welcome | 3 | 0 | 3 | 0 |
| group / lead_recruit_end | 3 | 0 | 3 | 0 |
| group / lead_recruit_none | 3 | 0 | 3 | 0 |
| group / lead_recruited | 3 | 0 | 3 | 0 |
| group / lead_take | 3 | 0 | 3 | 0 |
| group / lead_wait | 3 | 0 | 3 | 0 |
| group / lead_welcome | 3 | 0 | 3 | 0 |
| group / lead_where | 3 | 0 | 3 | 0 |
| lfg / invite | 3 | 0 | 3 | 0 |
| tell / lead_raid_invite | 3 | 0 | 3 | 0 |
| group / lead_afk_hold | 2 | 0 | 2 | 0 |
| group / lead_break | 2 | 0 | 2 | 0 |
| group / lead_player_left | 2 | 0 | 2 | 0 |
| group / lead_welcome_out | 2 | 0 | 2 | 0 |
| tell / lead_afk_let_go | 2 | 0 | 2 | 0 |
| thanks / trade | 2 | 1 | 0 | 1 |
| beggar / listed | 1 | 0 | 1 | 0 |
| combat / healer_oom | 1 | 0 | 1 | 0 |
| reply / chat | 1 | 0 | 1 | 0 |
