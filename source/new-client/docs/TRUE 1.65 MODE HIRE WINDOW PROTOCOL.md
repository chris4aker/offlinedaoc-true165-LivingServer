# True 1.65 Mode traveler's-inn hire window: server protocol

Agreed between the server and the client, 2026-10-30 (server build mode-165 2786ef0). One shared protocol for the original client and the
new client. Built in the new client: `godot/game/companions/` (`companion_net.gd`, the `srv_*_window.gd` windows).

## Server -> client

DebugMode packet 0x21, fixed 128-byte body: [0]=0 [1]=0x48 ('H') [2]=1 (version) [3]=op. Multi-byte values are little-endian.

| op | meaning | fields |
|---|---|---|
| 1 | open | [4] mode (0 innkeeper, 1 healer), [5] used, [6] open, [7] next slot level |
| 2 | close | - |
| 3 | text | [4] slot, [5..126] ASCII, NUL-terminated |
| 4 | numbers, kind 0 (hire page) | [5] race, [6] gender (0 m, 1 f), [7] class, [8] face, [9] face count, [10] hair, [11] hair count, [12] hair colour, [13] colour count, [14..15] model, [16..19] cost in copper, [20] hire enabled |
| 4 | numbers, kind 1 (roster row) | [5] row, [6] state (0 at inn, 1 with you, 2 dead), [7] level, [8] class, [9] race, [10] gender, [11] buttons (1 summon, 2 dismiss, 4 release, 8 revive), [12..13] model, [14..15] object id, [16..17] companion id |
| 5 | slots | [4] used, [5] open, [6] total (7), [7] next level |
| 6 | preview | [4..5] preview NPC object id |

Text slots (op 3): 0 title, 1 race, 2 class, 3 gender, 4 face, 5 hair, 6 name, 7 cost, 8 problem, 9 slots line,
10-12 blurb; roster row r: 16+3r name, 17+3r "Class level", 18+3r where.

## Client -> server (slash commands)

`/companionui race- | race+ | class- | class+ | gender | face- | face+ | hair- | hair+ | look | name | hire | tab N |
name <text> | summon <row> | dismiss <row> | release <row> | revive <row> | close`. RELEASE uses the server's confirm
dialog. GM `/companionui test` opens the window with sample data.

## Rules (owner)

Companion slots open at levels 5, 10, 15, 20, 30, 40, 45 (7 companions, a full group at 45); first hire free, later ones
about 2 h of coin income at the slot level. No tactics bar, no stance tab (only /aggressive and /defensive, group-wide),
no companion bag (equipment only, via the trade window). Revive: healer mode, free. The preview NPC is optional for the
new client: it draws its own classic figure from the numbers in op 4.

## 2026-10-30: one LOOK picker (owner: "merge the buttons into one option")

Classic figures have no separate face and hair: each look is a fixed face + hair pair (a monsters.csv row drawn with the
race/sex's figure). The FACE and HAIR rows are replaced by one LOOK row:
- `/companionui look-` / `look+` step through the race/sex's classic looks; `look` (NEW LOOK) picks a random one.
- The looks: every monsters.csv row whose col 2 (monnifs id) is the race/sex's figure, one per distinct face (col 18) +
  hair (col 4) + head geom (col 20), in id order (Briton male: 20). The model sent is that row's id.
- Text slot 4 = "Look n of N"; slot 5 unused (empty). Numbers [8] look index, [9] look count; [10] / [11] 0.
- face+/face-/hair+/hair- may stay as aliases of look+/look-.
New client: SrvHireWindow shows RACE / CLASS / GENDER / LOOK (+ NEW LOOK); figure.json "looks" holds every such row.

## 2026-11-05: delves, base stats, companion choices, exchange time left, quest givers (reset day C15-C18)

Agreed between the client and server chats.
- `/companionui delve <row> item <slot>` and `/companionui delve <row> style <line> <level>`: the server answers each with
  an ordinary DetailWindow (SendCustomTextWindow: the player's own item / style delve lines), always - an empty window
  when there is nothing. The client keeps it for the gear / style hover box.
- op 10 grows to 81 bytes: [63..78] the 8 stats unbuffed (u16, the order of [8..23]), [79..80] the base armour factor.
  The client colours a stat green above its base, red below.
- op 14, a companion's choices, one message a row, sent after every companion window fill and after any choice verb:
  [4] row, [5] kind (1 charm, 2 pet, 3 sub-pet, 4 song / chant), [6] slot, [7] chosen index, [8] number of choices,
  [9..10] used u16, [11..12] cap u16 (the Bonedancer's x / 75), [13..] the choice names, each zero-ended. A closing op 14
  with kind 0xFF ends the list. Song slots: 1 in combat, 2 out of combat, 3 travelling (Paladin chants 1-2).
  Verbs: `charm <row> on|off`, `pet <row> <i>`, `subpet <row> <slot> <i>`, `song <row> <slot> <i>`; a refusal comes as
  op 3 text slot 8. New client: the companion window's AI OPTIONS tab (shown only when the rows exist; any
  later AI choice is a kind 5 and up: its first name is the row's caption, the rest its choices (two: buttons,
  more: a list), picked with `option <row> <kind> <slot> <i>` - no new client code).
- op 14 rows added in 0.6: the Role rows (kinds 13-15) gain choice 3 "Melee and heal, no attack spells"; casters get
  kind 29 "Save power" (key savepower: 0 Automatic, 1 Off, 2 Strict) in place of kind 26, which stays for everyone
  else, and kind 30 "Attack spells" (key attackspells: 0 Automatic, 1 Area spells only, 2 None - buffs only).
- NPCCreate (0xDA) flags3 bit 0x40: the living is a gamebot or a hired companion (GameBot) - the client draws its name
  as a player's from the first frame. Other flags3 bits as before (0x01 lesson, 0x02 lore, 0x20 pending).
- op 30, the companion stance: [4] 0 aggressive (the default; every login), 1 defensive, 2 passive. Sent on
  /aggressive, /defensive and /passive; the group window's stance button cycles them in that order. A client that
  doesn't know a value shows aggressive.
- op 15, the Realm Exchange chest page's time left: [4] count, then 3 bytes each - the listing's place 0-99, minutes
  left (u16); 0 not offered, 0xFFFF no limit. Sent when the chest opens or changes page, after a price and after a
  duration whisper. Broker whispers: `sort <name|level|quality|price> <asc|desc>`, `duration <place 0-99> <12|24|48>`.
- Quest givers, the whole realm: map markers (the relic-marker packet) with ids 0xFFFC0001..0xFFFCFFFF, one per NPC
  showing the player "quest available"; sent at login, changes every 30 s. The client draws them as the gold "!" on
  every zone map (turn-ins 0xFFFD...., objectives 0xFFFE.... as before).
