# Changelog

The newest version is first.

Offline DAoC True 1.65 grew out of [Offline DAoC](https://github.com/shadowofze/OfflineDAoC) (0.3 to 0.37c, now on
hold). Its changelog has the history of everything this version inherits.

## Coming in 0.5

- Necromancer bots play their servant like a Deathsight player: with the monster held by the servant or the group
  there is no snare and at most one debuff, then the power drain (its power goes to the group through the power
  gift), else their hardest-hitting spell. Before, they snared and debuffed most of a fight and picked damage spells
  at random, so a level 1 Tormenting Cackle could win over Soul Evacuation.
- The innkeeper / healer companion list: dismissing a companion no longer hides the one listed below it
  until your next hire (dismissing the third of four made the fourth vanish from the list).
- Deathsight Necromancer bots play as power batteries: the servant stays back and casts, and melees only once a
  monster hits it; power gifts to the group finish instead of being cut off (no more sit-stand-cast loop), down to
  30% of their own power in a fight; the armour debuff only when a groupmate melees, the snare only on a runner.
  The Necromancer no longer swings its staff when hit. Painworking and Death Servant servants still go in.
- Paladin and Warden bots keep chanting when a monster hits them (the chant dropped and was recast every pulse).
- Companions wait for you to stop moving before summoning a pet, servant or Bonedancer minion.
- Bots use crowd control like veterans: no snares, roots, mezzes, stuns or Nearsight on the monster being fought;
  snares on runners, stuns to interrupt or to peel a monster off a healer or caster, mez and root on adds nobody is
  attacking, long-reuse control only for three or more adds. Druids root incoming adds, not their own target;
  Animist root shrooms no longer count as damage; Minstrels and Healers stop stunning as filler. PvP unchanged.
- Gamebots with a full backpack sell at a merchant when no Realm Exchange is in reach instead of standing for 15
  minutes; a full bag ends the hunt; after a death they pick the nearest service again.
- Fewer empty camps: bots are counted by camp location, overlapping camps of the same monster are merged, camps
  that keep coming up empty fade for a while, and low-level bots are not sent to camps among much stronger monsters.
- Solo gamebots go around packs and the spot where they just died on the road instead of pulling them (per bot, for
  a few minutes; camps are never blocked).
- Bots no longer get stuck on the high ledge east of Brynhild, at a Camelot Hills / Salisbury border crossing or on a
  Camelot rooftop; lost escape spots on the Lough Derg ridge are back.
- The Castle Sauvage keep sentinel by the west wall faces the teleporter.
- Necromancer bots, round two: power gifts to the group go off (the servant was being called back mid-cast, turning
  between the Necromancer and the healer); no more standing idle at full power in a fight (a command aimed at a monster
  that had died blocked the servant); a held-back servant stays within casting range of the fight. Out of power a
  Deathsight Necromancer no longer spams its level 1 Tormenting Cackle: it keeps power for its drain, and when it
  cannot pay for it (or Save power holds it back) its servant goes into melee on that monster until it dies.
- Melee healers (Shamans, Friars, Wardens and others) no longer turn back and forth between the monster and the
  groupmate before a heal: they wait out their own swing, cast once, and turn back to the fight once. Their pets
  too: a Necromancer's servant giving power to a healer keeps facing its monster.
- Spell glows on monsters whose skeleton lacks the bone the spell names now sit on the nearest bone instead of
  the ground: a necroservant's and an abomination's cast sparkles are over their hands again, not at their feet.
- Companion AI options: under "Debuff before nuking" each debuff the companion knows has its own row - Automatic,
  Use or Skip - so you can pick which debuffs it casts before nuking (any number, just one, or none); the rows are
  greyed out while it is set to Never. Hovering a spell in the AI options (a debuff, a pet, a song) shows its
  tooltip as that companion casts it, and the options page scrolls when the list is long.
- Crafting: a CRAFT ALL button next to MAKE makes the chosen recipe again and again until you can't afford the
  materials or your backpack is full (it also stops if an item is interrupted). Not in 1.65, so it is a quality of
  life option, off by default: turn it on in Options > Crafting; while off the button is greyed out.
- **Crafting overhaul - a large deviation from 1.65.** Crafting no longer plays like the period game. The 1.65 rules
  for success chances, craft times, skill gains, recipe levels and secondary-skill requirements are kept, and nothing
  is free (every craft costs real gold and takes the normal time, for a GM account too), but:
  - Crafting works anywhere: no forge, lathe, alchemy table or hand tool is needed.
  - MAKE buys the missing materials of one craft at the merchant price when you can afford them. What you make by
    hand is not sold automatically.
  - The tradeskill window is rebuilt for every trade: each recipe shows the trade level it needs and what else it
    trains, a recipe your Leathercrafting / Clothworking / Metalworking is too low for says so instead of looking
    makeable, a note names the skill holding your best recipes back, and there are filters (best to train, have
    materials, trains a secondary skill, item type, material tier, difficulty, hide grey, can afford, sellable),
    sorting, recommended recipes, and each recipe's chances, craft time and cost per skill point.
  - The crafting assistant (optional, off by default: Options > Crafting): set a skill goal for any tradeskill or
    secondary skill and it picks the cheapest recipe per skill point as you go, buys the materials, crafts with the
    normal times, sells what it makes at the merchant price, and estimates the gold and time to the goal (a baseline
    at average luck and a live estimate that follows your luck).
  - The enchant helper (ENCHANT in the tradeskill window): pick an item and the spellcraft gems or alchemy tincture
    to put on it; it uses the gems and tinctures you have, buys the materials for the rest, crafts them with your own
    skill at the normal time and chances, and combines them with the normal rules (a failed overcharge can make the
    gems explode and kill you - the item is kept - so overcharging needs a tick box).
  - Crafter gamebots keep their own simplified crafting, and now get their crafted gear and weapons enchanted by
    their realm's spellcrafter and alchemist bots, within those bots' skill; the crafter pays the materials and a fee.
- Dropping an item on one of your companions in the world no longer equips it unless its companion window is open
  (boots dragged onto a companion by accident swapped out its own).
- Online world: each realm has a gamebot population goal, set in the launcher - type a number in a realm's box and
  press Enter; it is kept until you change it, and works while the server runs. A new game starts at 500 / 500 / 500;
  an updated game starts at the gamebots it already has, and keeps every one of them (and every character's
  companions, which the population system never touches). Gamebots
  log in and out over time like players (veterans, regulars, casual players and newcomers, and some who quit and come
  back weeks later), every level range stays filled, and new level 1s keep arriving (bots always start at level 1).
  Up to 1,000,000 gamebots are saved; past that, random batches retire for good, with their Realm Exchange listings
  (level 50s are protected). At most two logins and two logouts a second, a bot finishes what it is doing before it
  logs out, and everything runs live - nothing needs a restart. Offline gamebots keep their gold and items.
- Online world: gamebot names come in 15 styles by race, about 3.3 million in all; a retired bot's name is free again
  after 30 days, and players can't take a gamebot's name. The hire window suggests names from the same generator (it
  offered only six names per realm).
- Online world: the Horn of Rallying is also answered by saved level 50 gamebots who aren't online - the raid sets
  off at 300 answers, or 3 minutes after 200 - and the horn then has a 30-minute cooldown.
- Online world: when a gamebot guildmate logs in, your guild sees "Guild member X has logged in!", as for a player
  (with guild login messages on).
- Launcher: a number box per realm for its population goal and a World Population tab; the bot list shows the
  gamebots online now (a search finds saved ones too); the live counts go back to 0 when the server stops; the
  "+ Lv.1" button is gone.
- Companions: /grind is back, as an option (Options > Grinding, off by default; not in 1.65 - a quality of life
  option). Your companions pull for you while you stay put: a tank that can pull from range first, then an archer
  or caster, then the tank walks up; every pull is brought back to the group, and the next one waits until everyone
  has rested. /grind (name) makes that companion the puller; /grind status says what it is waiting for; /grind stop
  or moving ends it.
- Companions can't join your guild: they always wear your guild's name and emblem while they are with you (a /gc
  invite made a companion join and greet, then leave).
- Companions: a shield your companion wears but doesn't fight with says why - a companion trained in two-handed
  weapons keeps fighting with its two-hander, and without a one-handed weapon it can't use a shield; a weapon or
  armour piece it can't use says so as it goes into its bag (both were silent).
- Classes: a Mystic's casting stat is Piety, as for all its paths (Runemaster, Spiritmaster, Bonedancer); it was
  Intelligence.
- Combat: a Berserker shows its weapons in its Berserk form (they vanished until the next weapon switch).
- Crafting: an item's hover text shows its new spellcraft bonuses, proc or charges right after the combine (it kept
  the old text until the item was moved or worn).
- Horses: on a stable horse only a fresh press of Jump gets you off, and not in the first moment of the ride (a key
  held while mounting could drop you from the horse).
- Interface: the character sheet shows the names of your class's main stats in gold - an advanced class's primary,
  secondary and tertiary stats and its casting stat, a starting class's from its paths - and hovering a stat says
  what it gives your class.
- Interface: picking an item up and putting it back in the same slot no longer equips it (or takes it off); only a
  quick double-click in place does.
- Movement: /stick holds while you strafe, so you can circle your target as in the period; moving forward or back
  still breaks it.
- Movement: while you sit, the mouse and the turn keys turn the camera, not your character.
- Groups: a dead group member shows as *NAME* in red in the mini group window until raised.

## 0.4.1 (2026-11-07)

A fix for v0.4, applied in place like the other updates (from v0.1, v0.2, v0.3 or v0.4).

- Monsters added from the Prima guides in 0.4 show their own names, levels and looks. Many borrowed another monster's
  template, which overwrote them: Pog Mapog appeared as a second "Mapog" at level 62, siog raiders as siabra seekers,
  mantid warlocks as mantid protectors, and about 290 spawns in all; their loot followed the wrong name too.
- Thergus the Inspirer and the ogre squad leader show their own names; the Unseelie Envoy and his stallions, Celain
  Eirich and Weaponmaster Zargin keep the level, look and faction their encounters give them.
- Loot that sold for a flat 50c sells for its period value, or for what period loot of the same monster level fetched:
  gems like Black Opal (2g 48s), Blue Spinel and the tourmalines pay properly again, and about 30 other drops are priced
  by the level of the monsters that drop them.
- Trees in Avalon Marsh, Myrkwood Forest and Shannon Estuary stop you at their trunk again, as everywhere else - you could walk into them and stand inside the willows.
- The downloader reads a three-part version such as v0.4.1 in full when it updates a game (it showed only "v0.4").

## 0.4 (2026-11-07)

An update to v0.1, v0.2 and v0.3: the downloader updates an installed v0.1, v0.2 or v0.3 game in place (about 2.8 GB
instead of the full 36 GB), and keeps your account, characters, companions, bots and settings. The first START
SERVER after the update brings your world up to date. New players get the complete game, as before.

### Gamebots and companions

- Companions: no more freeze when you train a companion, equip it or move items in and out of its bag (and no small hitch every few seconds with its GEAR page open) - its window was redrawn once for every one of the server's 30-90 update messages instead of once.
- Companions: the GEAR page is a paper doll like your own inventory - the companion's figure in its gear (drag to turn it, drop an item on it to equip), the slots round it, the hovered item's name and condition under the figure, worn-out gear edged in red - and its bag shows all 10 slots without a scroll bar.
- Companions: item names in a companion's bag, under its paper doll and in the hover box of what it wears show their con colour to you, as in your own backpack (the hover box was always yellow); a long name in its bag ends in "..." inside its row instead of running out of the box.
- Companions and gamebots carry their spare weapons like your own character: a two-hander they are not using on the back, a spare one-hander on the hip (an unused dual-wield pair one on each hip), the shield put away while the two-hander is out.
- Companions: hovering a realm ability in a companion's REALM ABILITIES page shows what it does.
- Companions: /pull works in /defensive mode - the puller (/pull alone: your tank; /pull <companion name>: that companion) goes past the 350-unit range to tag the monster and comes back to you, and the rest of the group fights once the monster is within 350 units. A plain /pull is the tank again (it had started picking casters and archers over the tank). Make a button for a named puller with /macro pull /pull Isolde.
- Companions: in /defensive mode a /pull ends only when the monster is within 350 units of you - a puller that tags from range and runs back waits by you until then, and your own pet no longer runs out to a monster that is still far away.
- Companions and gamebots: /pull never uses crowd control. A Cabalist pulled with a root, and the group waited for a monster that couldn't come. The puller now tags with a debuff or nearsight spell, else its lightest damage spell; a bow or crossbow with ammo still comes first.
- Gamebots in your group: a dead gamebot waits for a raise only while someone in the group can raise it. With nobody to raise it, it releases after a few seconds and leaves the group, instead of lying dead for five minutes.
- Companions and gamebots fight with the weapon set they are trained in more. A companion carrying a two-hander and a one-hander with shield uses whichever line you trained higher, and keeps it after a relog (a Paladin trained in Two Handed went back to sword and shield). Handing it a weapon no longer overrides its training; both sets stay equipped.
- Companions: hired companions follow you over zone borders (Lyonesse to Cornwall and every other border) instead of stopping at the line until they were teleported to you.
- Companions: hovering a realm ability in a companion's REALM ABILITIES window shows its full description, as the trainer does for your own class, plus the companion's level in it, the next level's cost and its realm ability points.
- Companions: charming. A companion with charming on and no pet charms the monster you select before anyone starts on it, and /charm or /charm <companion name> charms your target right away (its current pet is let go first). Charms take only monsters at least green to the companion. Hunters can charm too: the charm switch in AI Options is off by default (they keep their wolf); on, they charm animals or insects instead. Macro: /macro charm /charm Isolde.
- Charming: /charm and a companion's pre-fight charm no longer fizzle without a word when the cast ends (the monster you had selected counted as the group already fighting it). When a charm you asked for fails or is resisted, the companion's player is told why.
- Charming: a companion that lets its pet go for a new one (/charm) no longer keeps both - the old pet was never freed and followed along as a second pet.
- Charming never empties a monster camp: a charmed monster is a copy, and its camp spawns a fresh one on its normal respawn time. The charmed one never respawns - not where it died or was let go. A freed pet still turns on its charmer; if it lives through that fight it leaves once it has been out of combat for 10 seconds. The same for players, gamebots and companions.

### Combat, monsters and classes

- Cornwall: the monsters the Prima Atlas lists there are back - the moor boogeys (Boogey Fort stood empty), Cornish giants and cliff spiderlings, and the empty camps of giant rooters, moor wolves, forest cats, muryans, bears, death stalkers and cliff spiders (168 in all), at the Atlas levels. Cliff spiderlings still drop nothing: their Atlas items are not in the game yet.
- Monsters the Prima guides list that the world was missing are back: 978 spawns of 363 monsters across 68 zones and dungeons (most in Hibernia's old frontier - Mount Collory, Cruachan Gorge, Breifine - and the Shrouded Isles), at the guides' levels, aggression and speed, placed where period players saw them, on ground the gamebots can reach (the Shrouded Isles ones checked on the real navmesh too) and never an aggressive one close to a town. Monsters no period source describes borrow a look from the real 1.64 client models. The full list is in tools/claude-version/true165/prima_batch_2026-11-07.
- Prima encounters, scripted as the period guides and players describe them: Cuinn calls the town and treant seedlings at 10% health; Talvor's spirit rises to avenge him; the Emmisary of Logi hurls fireballs and calls obsidian doomwards; Forgemaster Brungar's Spirit of the Forge; Weaponmaster Zargin's students and mastered weapons; a panicked syhr'phint flees to wake Sys'sro; the ixthiar creeper seeds the Vale of Balor; Celain Eirich returns at nine in the morning and raises the dead; lures that hide a greater anglator or a scourgin; the Star Lake procession; the Unseelie Envoy's convoy into Lough Gur and back; Arr carries its prey to its nest; Thergus the Inspirer's speech sends the ogre squads against the orc coast camp; Gormror's morvalt raid Dyrfjell and fill it with disease; and Hamorik's light show at the broken tower in Caldey, at night (he can drop the Dread Flail). Their period drops are in (rates are a choice where the period gives none).
- Wolf drops (wolf fang, torn wolf pelt, chunk of wolf meat) come one at a time instead of as one heavy "(20)" or "(100)" item; the ones already in bags become one each.
- Starter classes (Viking, Fighter, Guardian and the others) get their weapon lines' styles before level 5, as in the period: a Viking with Sword 2 has Whirling Blade, Sword 4 adds Frost Cut. Points spent before level 5 stay when you take your advanced class; a line your advanced class doesn't have gives its points back.

### World, items and crafting

- World: water no longer shows darker blocky squares where two pools or rivers meet - Lyonesse, Isle of Glass, Carlingford and Meath now, as Avalon Marsh in 0.3 (and the last squares left there).
- 1,523 Atlas drop items that were not in the game: gear with its period stats (never made up; 243 pieces with no period stat source were left out), and plain loot - pelts, gems, trinkets, food - at its period value (an estimated value only for vendor trash with no recorded price), each with an icon and tooltip, and at level 0 like period vendor trash (it cons gray). Monsters drop their Atlas loot: 3,369 drop rows on 1,179 monsters, never from a monster 15 or more levels below the item. Cliff spiderlings drop their Atlas items now.
- Vendor trash is level 0 everywhere, as on the period pages: older loot that still had a level now matches (a legionarius's Sphene was level 42 and conned purple to a level 22). Quest items and Darkness Falls seals keep their levels; prices and drops are unchanged.

### The new client

- Guild emblems: a guild cloak shows its emblem as one emblem on the upper back again (the shield already did) - it was stretched over the whole cloak, a skull's eyes two black blots down the back.
- Item names in the backpack, vault and merchant lists show level 0 items (vendor trash) in their con colour - grey - like their hover box, instead of white.
- Spells: fire hits (the wizard and cabalist fire bolts and Fire Storm, and the fire explosions) no longer leave a long line of flames on the ground beside the target - leftover template flames in the effect files were being drawn.

## 0.3 (2026-11-06)

An update to v0.1 and v0.2: the downloader updates an installed v0.1 or v0.2 game in place (about 2.8 GB instead of
the full 36 GB), and keeps your account, characters, companions, bots and settings. The first START SERVER after
the update brings your world up to date. New players get the complete game, as before.

### Gamebots and companions

- Groups: removing yourself from a group of bots that follow you releases them, instead of leaving them stuck following you where you could neither disband nor re-invite them.
- Companions: a companion that levels shows its new level in the group window at once ([#7](https://github.com/shadowofze/OfflineDAoC-True165/issues/7)).
- Companions: a hired companion with a charm spell never charms the monster your group is fighting, only an add that nobody in the group is on; if the group gets on it while the charm is being cast, the charm fizzles ([#13](https://github.com/shadowofze/OfflineDAoC-True165/issues/13)).
- Companions: a Minstrel companion with a charmed pet keeps its charm song first, instead of switching songs and letting the freed pet loose on itself. While you travel it twists the charm with its speed song on the move, renewing speed only when it is nearly over; idle and in combat it holds the charm alone. A Minstrel without a pet keeps its one-song rule.
- Companions: a Theurgist's elementals and an Animist's turrets count as your pets: companions join the fight they start and defend them, so after you root a monster and send your pets, your companions come in when the root breaks ([#13](https://github.com/shadowofze/OfflineDAoC-True165/issues/13)).
- Companions: companions no longer jump on a monster you are mesmerizing, stunning or rooting.
- Companions: a companion revived at a healer after its body faded comes back at full health; /pull and attack orders stand resting companions up; companions follow you at once after a teleport or a horse ride.
- Companions: a companion healer raises you first when you die, and sits to regain the power for it if it has to; buffs wait while a body nearby needs raising.
- Companions: buffers no longer keep retrying a concentration buff past the 20 concentration spells limit.
- Companions and gamebots: a mesmerized or rooted monster is no longer forgotten after a few seconds. In your group a held monster that isn't fighting is left alone, even when it is the last one, and attacked the moment its hold ends or breaks.
- Companions and gamebots: archers take their melee weapon when a monster reaches them and is attacking them, and keep shooting one that is busy with someone else.
- Gamebots you invite into your group work for you like your companions: they heal your pet, stay near you, come along through teleports, zone points and stable rides, raise you first when you die, and wait five minutes for a raise when they die (then they leave and go back to their own business, as they do when you kick them, disband or log off). They never take a companion slot. A gamebot more than 5 levels above you won't join.
- Healing: anyone under 40% health is healed first, the lowest first; then you, your pet and the companions.
- Companions and gamebots in your group follow behind or beside you, turned with the way you face, each in its own place, instead of a fixed spot that could be in front of you. Turning on the spot no longer sends them walking around you.
- Buffs: a companion's stronger buff replaces a weaker one (a potion or a lower rank), and charmed pets in the group get the shareable buffs too.
- Companions: a companion you remove from the group no longer pops back in before it goes to the inn; a respecced companion keeps only the spells its new specs give; after you are raised your companions follow you at once.
- Companions: companions ease into their place when the server corrects them in lag, instead of jumping onto you; a companion or gamebot changing its gear or swapping weapons (a Minstrel drawing a weapon as combat starts, a stealthed assassin you can see) no longer blinks out and back.
- Companions: hovering a companion's gear shows the item's full information, as for your own items, and hovering a style shows its endurance cost, threat and the rest; its stats show green when buffed and red when debuffed.
- Companions: an AI OPTIONS tab in the companion window for companions with choices - charm on or off, the pet a Cabalist or Enchanter summons (any pet it has learned, as each has its own perks; Bonedancers, Spiritmasters and Necromancers always use their best) or a Theurgist's element, the Bonedancer's three sub-pets (with the 75-point limit), and the songs or chants it uses in combat, out of combat and travelling.
- Monsters: a monster no longer vanishes when a hired companion hits it first, and a healer bot no longer vanishes when its group breaks up (fix from the rsandersjr fork).
- Companions: a Warden companion casts its speed chant (it has a cast time; [#14](https://github.com/shadowofze/OfflineDAoC-True165/issues/14), from the FriendMaple fork; an idle grouped Warden still keeps Bladeturn up). Animists use their mushrooms like a 1.65 player, with no caps on them ([#16](https://github.com/shadowofze/OfflineDAoC-True165/issues/16)).
- Companions: the AI OPTIONS tab gets class choices, each starting on Automatic (the companion's own choice): Aggro (hold it or shed it), Heal at, Debuff before nuking, Bolts, Berserk, Guard and protect, the Hunter's pet (Off sends it away), Open from stealth, a Role for Clerics, Druids, Healers, Shamans and Friars, Life transfer, Amnesia, an Animist's shrooms (which main shroom, and each kind of shroom on or off), and for Bards, Minstrels and Skalds a Fight choice (stay back on songs and spells, or melee).
- Companions and gamebots fight more like 1.65 players: damage dealers no longer taunt the monster off the tank (they detaunt when it turns on them) and step behind a monster the tank holds for their back and side styles; positional styles and style chains are used; casters debuff before they nuke and let the tank or their pet take the monster first; healers use group heals, group regeneration and Spread Heal when several are hurt.
- Companions and gamebots: assassins hide and open with Backstab or Perforate Artery; archers open a pull with Critical Shot; Thanes open with one bolt and then melee; Skalds keep their damage chant up in a fight and performers keep their fight song playing; Berserk is saved for hard fights; melee bots sprint after a monster that runs.
- Companions and gamebots: Quick Cast is used like a player uses it (an emergency heal, a stun or a finishing spell while being hit), and Necromancers use Facilitate Painworking before a cast while their servant is being hit. Life Transfer, Amnesia, Confusion, Nearsight and Cure Nearsight are used where 1.65 players used them.
- Companions: a tank with Guard fights with weapon and shield when it is the group's only tank and guards someone within reach; a companion keeps the weapon set you give it; bots no longer swap weapons back and forth (a bow on every pull try, archers at low endurance).
- Companions: companions rest below 70% power, out-of-power casters wait for power instead of charging in with a staff, companions keep their places while you only turn, healers stay near you when raising the dead, buffers rebuff right after a resurrection and stop retrying buffs that do not stick, and a companion's instrument is its best one.
- Companions: companions are summoned at the innkeeper only, show your guild name and emblem, catch up in experience when below your level (no per-kill cap), and use the realm abilities they were bought.
- Gamebots: bots spread out over their zones instead of clumping in a few camps (also camps on live spawns the period catalogue missed), no longer take a monster another bot or group is fighting, reach the battlegrounds through the portal keep teleporter, start their sieges with siege equipment from a siegecrafter, and no longer rely on town teleporters.
- /invite with a name finds the realm's world gamebots.
- Gamebots: low-level bots stop walking into spots where bots of their level keep dying (a pack of level 8-9 lunantishee and blackthorn south of Mag Mell killed low Hibernian bots thousands of times on their way to a merchant); a bot that dies on its way to a merchant picks another for a while.
- Gamebots: after a death a solo bot no longer hunts only green monsters until the server restarts - it steps back up after a level or half an hour without dying.
- Gamebots: assassins hide and open with Backstab or Perforate Artery instead of pulling with a thrown knife or crossbow; bots keep a smaller coin reserve so archers and stealthers can afford better bows, weapons and armor; casters pick staves for their own spell lines; solo Foresters hunt blue monsters, as the period guide teaches.
- Necromancer bots: a bot whose servant dies keeps 10% of its health, as a player does (it was left with 1 hit point).
- Chants and songs: a pulsing chant (a Paladin's endurance chant among them) no longer leaves its group with no endurance regeneration after deaths and resurrections - endurance could stay at 0% even sitting.
- Companions: the companion window no longer shows an old error message again after something works (TRAIN showed "Unknown hiring command.").
- Companions: hovering a companion's style that follows another (an Armsman's Opal Slash) names its opening style; it showed "(style not found 58)" when your own class has no such style.
- Gamebots: your hired companions no longer take gamebots' places - with three companions out, three gamebots stayed offline ("queued") because the companions were counted toward the gamebot population.
- Companions: a companion keeps the weapon it is trained in (an Armsman trained in slash and shield keeps his sword and shield when you give him a pike); buffers spend their power on buffs first and then rest until full, instead of sitting and standing between buffs.
- Companions (player report): Healers keep their concentration haste up and mez adds before healing unless someone is under 40%; healers and Shamans regain power while resting instead of sitting and standing; Shamans put acuity only on pure casters, keep endurance regeneration up and don't double another buffer's base buffs; heals are chosen by what they really heal at your companion's spec.
- Companions: new AI Options - "Taunt" (Automatic / Always / Never): on Automatic a companion taunts only when it is the main tank and above 40% health, and never off you when you tank; "Positioning" (Automatic / Flank / Stay in front) for side and back styles; "Save power" for casters in a fight a tank is holding.
- Pulling: /pull picks the companion with a ranged pull (bow, thrown weapon or low-aggro spell), which tags the monster once and brings it back; /pull <companion name> picks one yourself.
- Gamebots and companions: melee classes with a heal (Acolytes, Naturalists, Clerics, Friars and the rest) heal themselves between the monster's swings; bot spells work at the bot's real specialization like a player's (heals, buffs and debuffs, spell damage, charms, and +skill gear on spells).
- Gamebots: bots that level up while dead no longer freeze where they fell; bots are not sent to camps whose monsters con grey, never pull a grey monster on purpose (they still defend themselves), avoid a camp after dying on the road to it, step back out of reach of a monster blocking the road, give up a shopping trip after dying on the way, and move their bind away from deadly spawns. Crowded spawn spots and walled-off camps are recognised, and the death fallback looks wider for blue and green monsters before taking yellow.
- Groups of gamebots: tanks never break a mez, the puller takes the monster standing apart, the party follows a main assist, chain-pulls at 90% health and 80% power instead of resting to full, prefers a mezzer in its make-up, moves on when the camp bonus runs dry, and low-level groups find camps again.
- Healing and buffing: healers use their efficient heal by default (the big one in emergencies), start healing at 65%, put the tank first and use group heals for three or more hurt; buffs go to tanks, then melee, healers and casters, with no haste on casters and power regeneration only for those who use power. Cabalists open with their pet's focus shield and favour Sapphire (Amber early), Theurgists call pets in a group only when something gets loose (earth first solo), and casters in a group save their last half of power while the tank holds.
- Gamebots: new gamebots follow the period builds of their class (from the 2002-2004 Freddyshouse forum), Warriors and Thanes keep Slam for stopping a cast or peeling, and bots get the half-level specialization points of levels 41-49 as players do.

### Combat and monsters

- Combat: a monster on its last sliver of health no longer falls over before the hit that kills it.
- Monsters: levels, aggression and speed from the Prima guides, in the zones the guides cover (bosses, epic dungeons and Darkness Falls named monsters unchanged).
- Monsters: a body no longer plays its death again when you right-click its loot or it is sent again, and a body whose removal never arrived (an untargetable dead wolf in Avalon Marsh) sinks away after 45 seconds.
- Stealth: a stealthed monster stays hidden until you come within its aggro range or it attacks you, and is drawn normally once seen, as in 1.65 (it showed to everyone as a green ghost). Bots see stealthed monsters by the same rule.
- Combat: you are told why your swing doesn't come (stunned, mesmerized, casting, engaging, no target, or a rule refusing the attack).

### RvR, keeps and battlegrounds

- Darkness Falls: the Horn of Rallying shows the Darkness Falls raid greyed out with the reason while your realm cannot enter (it opens only to the realm holding the most keeps, as in 1.65).
- Battlegrounds: Thidranki has a keep lord.
- Movement: you can no longer walk up a keep wall step by step: a face steeper than 60 degrees no longer counts as ground (period: climbing keep walls was removed in 2001; from 1.42 only assassins with the Climb ability climb keeps, at their climb points).
- Battlegrounds: the central keeps of Caledon, Murdaigean and Abermenai stand where the period client puts them - moved and turned by its own patch tables (Thidranki's keep stays where it was). Their lords and guards no longer float beside the walls, and the keep doors line up with the server's.
- Siege: the 1.65 siege engines players build - trebuchet, ballista, ram and mangonel - are drawn with their own models (they shared their numbers with player figures and would have shown as a Saracen or an Elf).
- Frontiers: the gate forts and milegates of Emain Macha, Hadrian's Wall and Odin's Gate have their gates again - drawn, solid while shut, opening when the server opens them - and the relic keeps all their doors: the later client's models had them empty or missing. Thirteen frontier and world models now come from the real 1.64 client where the later copy differed (Hibernia's ruined towers and an Albion house were later remodels).
- Doors: a keep door or gate you target counts as in view when nothing stands between, so melee and siege engines can hit it from their full reach (they failed "not in view" beyond arm's length); a door that has no swing of its own disappears when it is opened or broken instead of still blocking.
- RvR: warbands follow a main assist and kill healers and casters first, and form around a healer with speed and crowd control; gamebots can spot stealthed players by the same rule players use; stealthers travel hidden, hunt alone or in pairs at the milegates and portal keep roads and pick on resting players; gamebots use realm abilities such as Purge, Determination, Mastery of Concentration and Bunker of Faith; boiling oil is not offered at keeps (not in 1.65).
- Siege: catapults, trebuchets and ballistas fire only with ammunition loaded (drag ammunition onto the engine or use /load; rams need none); Siegemasters and the portal keep medallion merchants sell stones and bolts, siegecrafters make the rest; gamebot crews craft, buy and restock ammunition before a siege. A crafted engine is light, medium or heavy by its wood.
- Battlegrounds: the battleground monsters removed earlier are back (Caledon, Thidranki), Thidranki Faste's lord stands in his place with his keep guards, and the Atlas Void Merchants are gone.
- Frontiers: keeps, gate forts and their doors follow the real 1.64 client (the later client's keep models moved some gates); Odin's Gate's second milegate is back. Milegates open for anyone and can be broken and repaired; gamebots walk through them. Keep doors port their own realm through and enemies must break them; border and portal keep doors can't be broken. Hasteners stand again at the frontier, border and portal keeps and in the housing zones; the portal and border keeps have their period guards (Bowmen, Men at Arms, elite guards, Viking Archers, Jarls and more).

### World, items and crafting

- World: two Albion banners floating in the air over Camelot Hills are gone.
- Looting: you can no longer pick up loot while dead.
- Realm Exchange: bots list only items with a real use (crafted items, magic equipment, items with procs or charges, crafting and salvage materials, tradable quest items, dyes and enamels, potions, poisons, ammunition, spellcraft gems, siege equipment, housing items). Vendor trash such as monster parts, loot gems and plain drops goes to a merchant instead.
- Loot: monsters that drop a moonstone now drop the moonstone gem; they used to drop the Hibernian epic quest item of the same name by mistake. The quest Moonstone can now be dropped, destroyed and sold to a merchant for a few copper, so bots sell off any they picked up.
- Items: weapons with broken damage fixed, and item stats set back to their 2003 values where the Prima strategy guides and the period item pages agree.
- Loot: drops from the Prima Atlas added to 361 monsters (93 of them dropped nothing before).
- World: the Abermenai battleground has its ground textures again (its whole map was flat white).
- Horses: on stable routes that turn sharply (more than 90 degrees, such as North Tir na Nog to Druim Ligen) the horse turns with the route instead of sliding sideways for most of the ride.
- Merchants: the Hibernian staff, weapon and shield merchants (Tir na Nog's weapon merchant among them) no longer sell caster staffs, blades, blunts and shields for 30 copper that you can't equip ("not of this realm"): those 120 items have their realm and price back.
- Account vaults: an account vault keeper in each capital (Camelot, Jordheim, Tir na Nog) lets your characters of one realm share items. Each realm has its own vault. This is a convenience addition that 1.65 did not have.
- Horse routes: horses no longer ride under the bridge near Druim Ligen or jump onto the bridge towers on the routes over the Druim Ligen, Innis Carthaig and Midgard river bridges.
- Experience: the camp bonus tops out at the period values, 30% in normal zones and 50% in dungeons, underwater and every RvR zone (the frontier zones and the battlegrounds), as before patch 1.71. It was 55% outdoors and 66% in dungeons.
- Crafting: bot crafters make only their own realm's items, so the Realm Exchange no longer offers armor or weapons from another realm that nobody in your realm can wear.
- Looting (a small quality-of-life addition, not in 1.64): a right click on a loot bag or an item on the ground picks it up even when a monster's body or an NPC stands on the same spot, and the new Pick Up Loot key (Shift+G) takes the nearest loot within reach, one item a press, without pointing at it or targeting it.
- Looting (a small quality-of-life addition, not in 1.65): AUTO LOOT is back as an option (Options > Interface > Loot, off by default) - on, coins and items from your kills go straight into your backpack. Gamebots and companions still walk to their bags.
- World: Avalon Marsh's water no longer shows dark squares and steps where its pools meet.
- Realm Exchange: listings run 12, 24 or 48 hours (48 by default) and an unsold item stays in your chest; listings can be sorted and show their time left. Bots prefer fair-priced player items, relist once at most, and keep the arrows and poisons they buy instead of selling them straight back.
- Horses: stable routes ride at one steady speed (no super-speed legs, no stop-and-drift at route points).
- Loot: monsters no longer drop items far above their level: level-3 phantom hounds dropped a level-49 "Gnawed Bone" worth almost 4 gold to a merchant; they now drop a plain gnawed bone, and such out-of-level gear drops are gone.
- Merchants: 36 merchants whose title did not match their goods show the right one - Adribard's Retreat's Theurgist and Sorcerer staff sellers were titled Shield Merchant and Armor Merchant; arrow, bolt, bow and crossbow sellers, armor and robe merchants elsewhere had the same mix-up.
- Raids: a realm raid's high-end boss loot is shared out again - a quarter straight to the players' bags, the rest to the raid's gamebots; in True 1.65 it had stayed on the ground.
- Horses: at a sharp bend of a stable route (Mularn to Svasud Faste) the horse turns into the new leg instead of sliding sideways back onto it.
- Doors: the healer doors and round doors of Jordheim open again in the server's game - clicking them never reached the server.
- Crafting: the 80 metalworking recipes that can never be made in a 1.65 world (tempered metal studs, wire, strapping and scales - their old components were retired in 1.65) are gone from the recipe list. Tempered saddlebags stay.
- Doors: keep, house and inn doors in almost every zone now reach the server's own door - the client numbered them by the fixture's row instead of its Unique Id, as the original client and the server's door table do. Doors the server keeps shut or open now show it.
- Realm Exchange: items dropped on MY LISTINGS no longer vanish after you have handled your companion's gear - the window showed nothing and gave no price box for ten minutes after a companion gear change (the items were safe in the listing chest all along). Listing, taking back and pricing now always go to the broker.
- Crafting: every recipe of the 1.64 client works (Midgard spellcraft runes, seal dust and the etched aquamarine eye among them), the 1.64 siege engines can be crafted, crafted items come out in your realm, and an unknown recipe says so instead of failing silently. The market taxidermists (Elkyrd in Caerwent, Bruhdor in Erikstaad, Urlaun in Meath) cleanse Daemon Diamond Seals from Darkness Falls.
- Merchants: Forgemaster Brungar sells his hammer from his own list (Aren and twelve other Midgard weapon merchants no longer do), dyes and other goods no longer show another realm, and every merchant sells its own realm's arrows.
- Items: every item can be traded and listed on the Realm Exchange, by you and by gamebots - quest items too. This suits a single-player server; anyone making a multiplayer version may want to revisit it. Bot training gear (training bows, throwing weapons and instruments) stays out of trade and the exchange, but a merchant buys it for 1 copper, so bots sell it once they own a real one instead of carrying it forever.
- Realm Exchange: each player has one listing chest of 100 slots (no more chest pages); a full chest says "Your 100 listing slots are full.", and listings left on an old chest page move into free slots. The server also takes a listing whichever page the window is on, and when the exchange refuses an item it now says why (wrong realm, too far from the broker, slot taken, and so on).

### Quests

- Quests: Bear Skins, Big Paw, Trapper's Joy, Ghoul Hair Belt, Missing Bard and the Berserker's Rasczel's Bane added from the period sources, each with its quest guide; Rhian's Dyes is Tyree's Dyes again (Tyree in Howth).
- Quests: The Growling Ghost branches as in the period. Help Growler (Growler's Necklace) and he gives you The Growler; slay him instead and Sergeant Alain sends you the long way to the same mace with Growler Mace and Mathien's Metal.
- Quests: every zone map shows the quests your realm offers there, and "bring the drops" steps point at the monster until you have the item; a quest monster is no longer spawned a second time while one is still there; an NPC's dead [links] answer with the recorded reply; tooltips list only 1.65 classes as usable and show no database notes.
- Quests: a quest's items drop only while you are on a quest that uses them, as in 1.65, and gamebots and companions no longer carry quest items they can't use, sell or trade. A quest item's information no longer shows another item's tooltip.

### The new client

- Chat: %t or %T in a chat line is replaced by your target's name (or "<nothing>" with no target), as in the 1.64 client - for example /s Healing %t.
- Interface: the status window gets its thin red border while you are in attack mode (melee), as in the 1.64 client.
- Quickbars: dropping a skill or spell you are holding onto a filled quickbar slot puts the one that was there on your pointer, as in the 1.64 client, instead of deleting it ([#6](https://github.com/shadowofze/OfflineDAoC-True165/issues/6)).
- Chat: the combat chat window no longer disappears when you click its top-right corner; that corner resizes it, as on the main chat window and in the 1.64 client ([#8](https://github.com/shadowofze/OfflineDAoC-True165/issues/8)). Hide or show either chat window in Options > Windows.
- Groups: a dead companion or group member stays shown as dead (no health) in the group window when the window is redrawn, instead of coming back at full health ([#10](https://github.com/shadowofze/OfflineDAoC-True165/issues/10)).
- Characters: companions and gamebots with some player looks no longer appear as an item - a Sylvan on look 12 was drawn as a bow (male) or a mace (female), and other looks of Sylvans, Valkyn, Frostalf, Shar, Half Ogres and more could show as armor pieces, shields or weapons ([#9](https://github.com/shadowofze/OfflineDAoC-True165/issues/9)).
- Camera: the 1.64 client's camera options. CAMERA FOLLOW (Options > Controls): off by default, as in the 1.64 client, a camera you turn with the left mouse button stays where you put it while you run, until Camera Reset; on, the camera swings back behind you as you move (as before). The Pan Camera key (bind it in Options > Controls > Camera) turns the camera around you instead of turning you. Camera Toggle (F11) switches to first person and back (zooming out also leaves it). Camera Reset now also puts the zoom back to normal, and the camera's zoom, tilt, angle and first person are kept for next time, as the 1.64 client kept them.
- Performance: fewer stutters when many fading or colour-changing spell effects play at once (each copy of such an effect no longer waits on the renderer), and the skills and spells pages no longer log an error on every skill update after they are rebuilt (both from a player's logs).
- Stability: a freeze that could hang the game for good when spell effects with particles (the Minstrel speed song's pulse among them) loaded in the background is fixed; those effects are now prepared on the main thread (from a player's logs: a 9-second stall, then no response).
- Chat: SMALL CHAT TEXT in Options > Chat, the 1.64 client's small chat font (UI SCALE in Options > Interface still makes all text bigger). Window transparency can now be set per window: right-click a window and its arrows change that window alone (the 1.64 client had one setting for all of them); ALPHA in Options > Interface still sets every window at once.
- Account vault: the account vault keepers open the vault window in the 1.64 style, with FIRST PAGE and SECOND PAGE buttons for the vault's two pages.
- Interface: the status window's health bars have the dark red background the other windows' bars have; the information window widens to fit a long item name instead of cutting it off.
- Interface: a click anywhere on a window brings it to the front, and an item's information opens beside the Realm Exchange instead of over its page buttons (they seemed to stop working after a right-click).
- Interface: item names in the backpack, vault and merchant lists always show in their con colour to you, not only when highlighted.
- Groups: the group window shows each member's endurance between health and power.
- Sound: new option MUTE WHEN NOT FOCUSED (Options > Audio, on by default) - the game goes quiet while you are in another window.
- NPC dialogue: the words you can answer with show bold and in [brackets], as in the 1.64 client, and long ones are never dropped (Kaere's "[Will you gather the components ...]" in Protecting Anniogel was missing).
- Quickbars: a weapon or instrument on a quickbar switches to it again (a Minstrel's instrument and weapon), as in the 1.64 client.
- Movement: going up and down stairs, your character, the camera and NPCs glide over the steps instead of jerking up and down; climbing itself is unchanged.
- Characters: a Highlander's kilt takes the look of the chest armour worn (a leather tunic, a leather kilt), as in the 1.64 client; the grey cloth kilt only without chest armour.
- Realm Exchange: click a column header to sort by name, level, quality or price (again for the other way); choose how long a listing runs (12, 24 or 48 hours) and see each listing's time left; SEARCH SIMILAR in the price box shows what the same item sells for; the chest page shows how many of its 100 slots are used.
- Maps: every zone map of your realm shows the quests you can take there (gold "!"), their turn-ins (silver "?") and your objectives (crimson), wherever you are - open another zone's map to see its quests. The marks are small gold-rimmed medallions; a legend under the map names them (Options > Interface > MAP LEGEND hides it), and hovering a mark says what it is. The realm maps show no quest marks. Options > Interface > QUEST MARKERS ON MAP turns the map marks off (the circles under NPCs in the world stay).
- Maps: the map list starts with your realm's two realm maps - Albion and Avalon (Shrouded Isles), Midgard and Aegir (Shrouded Isles), Hibernia and Hy Brasil (Shrouded Isles) - above the capital. Every zone is a block on them; click one to open its map.
- Maps: your realm's gamebots show as green dots on every zone map you pick from the list, wherever you are, and as small dots all over the realm maps (Options > Bots > REALM BOTS ON MAP turns them off). It stays light with thousands of bots: bots outside your zone move on the map every 12 seconds.
- Trainers: hovering a realm ability in the trainer window shows what it does, from the server's own description (the window never asked for it before).
- Realm Exchange: MY LISTINGS shows your 100 slots as five pages of 20 with a scroll bar (no more chest pages); anything the exchange refuses now says so in red in the window, with the reason ("That item cannot be traded.", "you are too far from the broker" ...), instead of just snapping back; CLAIM MONEY shows plainly whether money is waiting (NOTHING TO CLAIM when none is).
- Realm Exchange: dragging an item onto MY LISTINGS lists it again (the price box comes up) - the window's BROWSE / MY LISTINGS tabs now tell the broker which page you are on; before, a window opened on BROWSE and switched to MY LISTINGS left the broker on its search, and it refused the item without a word.

### The server

- Server: fewer stalls (the once-a-minute pause while bots planned shopping trips), and no speed-hack snap-back right after a stall.
- Commands: /battlegroup, /bc, /lfg, /combatstats, /xpstats, /checkconcolor, /realmtimer and /gtrange are not in 1.65 and are GM-only (the group window's "looking for group" works as in the period).
- Performance: two causes of server freezes in the bot camp and merchant searches are fixed, and packets to your client are no longer dropped while the bot map markers update.

## 0.2 (2026-11-01)

An update to v0.1: the downloader updates an installed v0.1 game in place (about 2.5 GB instead of the full 36 GB), and
keeps your account, characters, companions, bots and settings. New players get the complete game, as before.

### Gamebots and companions

- Gamebots buy and wield weapons of the line they trained, and dual wielders hold the trained weapon in their main hand.
- A gamebot that keeps dying with poor gear goes shopping for an upgrade it can afford, and bots on a shopping trip no
  longer stand at a merchant for 15 minutes.
- Strength buffs go to fighters and acuity buffs to casters, from gamebots and companions alike.
- A bot that levels shows its new level and con colour at once, and in the group window.
- Companions: a companion whose bow is taken away in the middle of a pull switches back to its melee weapons.
- Companions: you can give your own companions quest and no-trade items.
- Companions: a companion that dies shows as dead on your companion roster at once.
- Companions: your hired companions come with you at once when you use a teleporter or release to your bind point.
- Companions: reviving a companion at a healer after it died while casting no longer breaks its spell effects
  (reported as a client crash when reviving at Epona in Mag Mell).

### World and items

- Stable horses ride over the bridges on every horse route (Prydwen Bridge, the bridge into Camelot and the rest)
  instead of through or under them ([#1](https://github.com/shadowofze/OfflineDAoC-True165/issues/1)). Still a little buggy: crossing Prydwen Bridge, the horse can dip into
  the deck now and then.
- Horses: a lag spike no longer throws you off a stable horse in the middle of a route (both clients).
- Horses: stable horses no longer rubber-band along their route.
- Horses: gamebots ride horses sized for their race, as players do.
- Merchant caster staves have their focus levels again (lower spell power cost), and about 470 items regain bonuses
  from their 1.65 descriptions (instrument and weapon skills, resists, staff focus).
- Items: about 340 loot items show an icon that matches their name (a boar tusk is a tusk, raw pork is meat), from the
  1.64 client's own item art.
- Camelot: the walls around the round table area no longer show up white, and the palace's closed double door by
  Guard Eraedus is back ([#2](https://github.com/shadowofze/OfflineDAoC-True165/issues/2)).
- Missing doors, doorways and doorsteps now show in Caer Sidi, Muspelheim, Yggdra Forest, Odin's Gate, Trollheim and
  Tur Suil, and a Darkness Falls tunnel has its textures again.
- Sharper textures on the new traveler's inns (Avalon Marsh, West Svealand, Shannon Estuary) and on some grass
  (Iarnwood, Cothrom Gorge, World's End).
- NPCs stored a little below their floor now stand on it, as the original client drew them: Gothwaite Harbor's dye
  merchants were sunk to the neck ([#4](https://github.com/shadowofze/OfflineDAoC-True165/issues/4)), and three more
  NPCs in the Vale of Mularn and the Black Mountains stood in their floor.
- Half Ogre, Frostalf and Shar NPCs (about 150 of them, General Vaul among them) draw with their own classic figures
  instead of broken.
- World: floors, beams, pillars and furniture that drew in one flat colour have their textures again - the upstairs
  room above Camelot's Necromancer and Reaver trainers and other Camelot buildings, Hibernia's taverns and housing
  marketplaces, Norse huts, some Albion dungeon halls, keeps and buildings (47 models: a few broken texture
  coordinates in the original models flattened the whole surface).

### Housing (new)

- Player housing, as in the 1.64 client and with its own models: the 12 period housing zones (Caerwent, Old Sarum,
  Rilan, Dalton; Erikstaad, Arothi, Carlingford, Wyndham; Meath, Dunshire, Torrylin, Kilcullen) through the realms'
  housing gates; buying a lot and building a house with a deed; the houses' roof, wall, door and wood materials,
  porch, guild banner and shield; the garden; going inside; placing, rotating and taking back furniture; hookpoints
  (vaults, bindstones, tools, house merchants); house vaults; friend permissions; paying rent by dropping coins on
  your house; and the 1.64 housing windows (External / Internal Options, Garden Placement, Friend Level Permissions,
  Permissions List, Rotate Object).
- Lots, deeds and rent cost the period prices, the decorators carry their full stock, and stable routes run inside
  the housing zones. Hanging a weapon on a wall no longer leaves a copy in your bag.
- Housing is checked offline but not yet played through on a live server.

### Quests

- You can still hand in quest items after outleveling a quest you already have (for example the Necromancer quest
  Inconnu Poison).
- Quests: REMOVE QUEST in the quest journal asks "Do you really wish to remove this quest?" (YES / NO) before it
  gives the quest up.

### The new client

- Mouse Look Toggle, as in the original client, can be switched on in Options > Controls (off by default). Once on,
  right-click on open ground to turn mouse look on (the mouse turns you with no button held), and right-click again
  or press Esc to turn it off. Right-clicking NPCs and objects still works as before, and the key can be rebound.
- Controls: the 1.64 client's keys that did nothing now work - Follow, Stick and Face (on your target; press again
  to stop), Camera Reset, Look Up / Look Down, Target Group #2-8, Target Friend, Target Object, Last Attacker,
  Consider, Command Window (opens the chat line with "/"), Help (the server's command list), Chat Logging (to
  CHAT.LOG beside the game), Toggle Names (/hidenames and /shownames too) and the Perf. Meter. The Realm War Map key
  opens the war map too. Default keys the 1.65 key file gives an unknown modifier no longer fire on the bare key
  (Nearest Friend took Tab from Target Enemy, Chat Logging took L from the combat window); bind those in Options.
- Chat: Shift+Up and Shift+Down go through your last 10 sent lines, as in the original 1.64 client; Shift+Up with the
  chat line closed opens it with your last line. One minor convenience over the real 1.64 behavior: stepping past the
  newest line with Shift+Down brings back what you were typing (1.64 shows an empty line).
- `/qbar` works as in the 1.64 client: `/qbar #` shows quickbar bank # (1-10).
- Loading screens: the 1.64 client's classic loading bar and layout, with the loading picture at its own size, the
  glass bar in its bottom right corner and "Loading..." beside it.
- Loot: dropped loot shows the 1.64 client's classic bag instead of the later Catacombs one.
- Death: one death window with the release timer, its bar (the 1.64 client's green bar art) and /RELEASE, /RELEASE
  CITY and /RELEASE HOUSE, all inside the window. The separate Release Timer window is gone, and /RELEASE BIND (the
  same as /RELEASE) was removed. Crafting, salvage and repair timer bars stay inside their window too.
- Items: the Destroy Item key (Shift+D) now works, as in the 1.64 client - right-click an item to select it, press
  the key and answer "Do you really wish to destroy this item?" (indestructible items, relics and worn items stay).
- Interface: window transparency, as in the 1.64 client - right-click any window (the chat boxes too) for the Alpha
  panel and its two arrows, or set ALPHA in Options > Interface; text and icons stay solid.
- Spells: Shift+right-click one of your own buff icons to cancel it.
- Items: a highlighted item in your backpack, vault or a merchant's list, and the right-click box, show the item's name
  in its con colour to you.
- Trainers: a RESPEC button beside TRAIN in the training window respecs all your specializations; the server's own
  "CAUTION: All respec changes are final" box asks you first. The 1.64 client had only /respec all - this is a
  necessary and simple QoL addition.
- The window title is "Offline DAoC True 1.65 v0.2", and the launcher shows VERSION 0.2.

### The launcher and the downloader

- The launcher's Stop button stops the server again (it could keep running).
- One downloader for everyone: **1 = Update my game** updates an installed game in the folder it is in, and
  **2 = New install** installs the complete game into a new folder.
- An update checks every file it replaces first and changes nothing if one was changed on that PC (it can then make a
  fresh install and bring your save over). It backs up what it replaces in `patch-backups\`, undoes a failed update,
  and finishes an interrupted one on the next run. Your save, login and client settings are never touched.

## 0.1 (2026-11-01)

The first version of True 1.65: Classic and Shrouded Isles as they were at patch 1.65 (1 November 2003). The only
additions to the period game are the bot and companion systems and the Realm Exchange. It is early in development:
most of the systems below have been tested in autonomous bot runs, but not yet by players.

### Compared with Offline DAoC

- **One mode:** True 1.65 only. The launcher has no normal mode and refuses a world that isn't a True 1.65 one.
- **One client:** the new client. The original DAoC client is not supported, and there is no DXVK.
- **A fresh start:** nothing transfers from Offline DAoC, and there is no progress importer.
- **No Sluaghbinder:** the custom class is gone.

### The world, as in 1.65

- **Freeshard and post-1.65 content removed.** Removed rows are archived in the world database, not just deleted.
  Removed:
  - click teleporters;
  - town and frontier hasteners (they came in 1.70 and New Frontiers);
  - Atlas master trainers, item merchants, the buff merchant, the Market Explorer, account vault keepers and
    training dummies;
  - Atlas event controllers, "new mob" placeholders and helper NPCs;
  - the random "ROG" item drops;
  - the Atlas daily, weekly and monthly quests (deleted from the source).
- **Battlegrounds** as in the period game: Abermenai (15-19), Thidranki (20-24), Murdaigean (25-29) and Caledonia
  (30-35). Cathal Valley, a post-1.65 battleground, is removed.
- **Regions off:** the tutorial, Cathal Valley, the duplicate Summoner's Hall, the New Frontiers battlegrounds,
  Passage of Conflict and every Trials of Atlantis region.
- **Period drops:**
  - rebuilt from Uthgard's measured drops, period Allakhazam loot lists and period item pages, for about 1,950
    monsters;
  - 886 freeshard and dead loot rows removed: Atlas XP items, orbs, trophies, timed drops and items no source knows;
  - 1,334 period items built from their period pages;
  - the 8 period boss trophies stay (they came with housing in June 2003).
- **Kill tasks** (turning in monster items for XP) pay the period rewards: a fixed XP per item, and the period
  turn-in cap of 15 below level 40 and 25 for the level 40+ bounties (before: the freeshard 1 level per item, and 30).
  143 of the 227 values come from period pages; the other 84 are estimated from them. The freeshard turn-ins with no
  period source are removed: the level 11-19 "Dr" turn-ins, a stand-in for the Ghoul Hair Belt quest, a duplicate
  Giant Snow Crab Claw task, and the 10 items only they used.
- **Kill-task items drop again:** 140 kill tasks with dated period evidence have their item back on the monster that
  dropped it, at Uthgard's measured rate where known, otherwise 6% (the median Uthgard kill-task rate). 181 of the
  227 kill tasks can be completed.
- **Coin as in 1.65:** a monster carries coin or it doesn't. The chance comes from Uthgard's measured kills, and the
  amounts are unchanged.
- **Item stats:** about 7,200 corrected to the period item pages. Invented quest rewards were replaced by the real
  items.
- **Spawns:** 16,292 period spawns restored and 453 period spawns added. Spawns off the walkable ground were moved
  onto it. Levels and factions were corrected where two sources agree.
- **Merchants:** period stock from the period Allakhazam item and merchant pages (1,458 stock rows, 8 new lists), and
  7 missing period merchants added where CapnBry's radar saw them.
- **Crafting:**
  - the recipe list is the 1.64 client's (about 380 later recipes removed);
  - skills are per character;
  - no speed bonuses; quality is 94-99%; hand tools are needed; salvage gives the full yield;
  - frontier siege equipment is crafted (Siegecraft), not sold.
- **Travel:**
  - the Shrouded Isles entrances are the 1.64 client's jump boxes (Avalon Marsh, Gotar/Nalliten, Shannon Estuary);
  - the Darkness Falls entrances are the period ones;
  - frontier portal masters walk between their post and the pad;
  - horse routes follow bridge decks and dips;
  - there is no personal bind recall stone.

### Rules for players

- XP is 1x, with no free levels and no `/level`.
- Coin belongs to each character, not to the account. Each realm has 8 character slots.
- Base classes at creation; you choose your class at a trainer at level 5. A starting class and race with no class to
  promote to is refused.
- New characters start with a weapon and a training shield.
- No autoloot. Songs need their own instrument (drum, lute or flute).
- Charm is cast on a real target, with the original Dawn of Light level limit and resist rules. A freed charmed
  monster turns on its charmer.
- Cheat and convenience commands are for GM accounts only. That includes `/tele`, `/tc`, `/grind`, `/level`,
  `/mobs`, `/buff`, `/raid` and `/spawn`, and the launcher's "Teleport to" on a bot.
  [ALL SERVER COMMANDS.txt](ALL%20SERVER%20COMMANDS.txt) has the full list.
  - `/switch`, `/sell`, `/craftmacro` and `/faction` are GM only too: neither the 1.64 client's command help nor
    the DAoC Manual lists them. `/transfercorpse` is in both, so players keep it.
- No player horses: horses, horse vouchers, horse armour and barding are off every merchant. Horse travel is by the stablemasters'
  routes.
- `/task` follows the period rules: kill targets 3 levels below you to 1 above, period task XP, and plain guards say
  who gives tasks.
- The quest markers, the objective highlights and the quest guide (`/questguide`) stay.

### The traveler's inns (new)

- One inn per realm: near Caer Witrin (Albion), near Huginfell (Midgard) and near Connla (Hibernia). Aggressive
  monsters were moved away from the inns and the roads to them.
- The **innkeeper** hires companions.
- The **Horn of Rallying:** a level-50 player who has done its courier quest starts a PvE realm event there (a dragon,
  an epic dungeon, Summoner's Hall or Darkness Falls). PvE events never start by themselves; keep sieges and relic
  raids still do.
- The **Realm Exchange**, the auction house where gamebots and players buy and sell to each other, moved from the
  capitals to the inns. Valuable items ask more, within limits. Unsold bot
  listings are relisted at 90%, then 80%, then returned.
- Couriers at levels 5 and 50 bring the inn and horn quests, with map markers. Road patrols, wandering arrow and
  poison traders, and guards complete the inns.

### Hired companions (new; they replace `/spawn` and `/raid` for players)

- Hired at the innkeeper. Slots open at levels 5, 10, 15, 20, 30, 40 and 45. You choose the race, class, gender,
  look, height and name.
- They are persistent: they log in and out with you, ride your horse routes with you, and arrive where you are.
- You manage them in their own windows:
  - gear, with a 10-slot bag;
  - spec training, with a free respec;
  - realm abilities, stats and XP.

  A smith repairs their gear.
- They stop at your level, never loot, heal your pet, and talk about what is going on. A healer revives them for
  free.

### Gamebots on the player rules

- Bots use the player rules for crit, skill, resists, crowd control, speed, procs, dual wield, focus, concentration
  and regeneration.
- **Armour works like a player's.** Before, a bot's armour did nothing when it was hit.
- They start with their class's period starting weapon only, buy their armour from vendors when they can afford it,
  and plan trips for upgrades. A bot that walks to the merchant its plan names is served there at once, and it
  wields a weapon it buys straight away.
- Arrows, bolts and poisons come from their bags and are restocked on town errands.
- Their gear wears and is repaired, for coin, by a smith or by crafting.
- **Deaths cost them:** Constitution loss on a PvE release (once per death, at most 3 points), resurrection sickness,
  and a healer visit or a wait.
- They sit to rest at the player rates, and walk to their loot bags.
- They start as a base class and are promoted at a trainer at level 5.
- About 1 in 5 bots craft: they make real recipes, gain skill, and list what they make on the Realm Exchange.
  Siegecrafters supply the siege crews.
- Camps and pulls:
  - solo bots pull from range with a bow, a throwing weapon or a spell. They move into range and line of sight
    first; if a shot is refused as out of range or not visible, they step closer and try again, and after 4 tries
    they pull in melee. A shot that hits, misses or is resisted always alerts the monster, so it comes for the
    bot (before, many ranged pulls went unnoticed). A monster's AI used to wake only for a player who could see it,
    or a bot inside its aggro radius, so a bot shooting from range drew aggro the monster never acted on; now the
    monster comes for the bot, and the bot waits for a monster that is on its way. A monster that still takes no
    notice within 3 seconds is pulled the ordinary way;
  - camps are chosen by challenge colour;
  - grey targets are a last resort;
  - outleveled camps are left alone for 30 minutes.

### Also in Offline DAoC

These were built during this work and are shared with Offline DAoC.

- **Relic raids:**
  - the army goes to the shrine;
  - a chosen carrier runs home through its own gates;
  - sentinel keeps are taken first;
  - campaigns survive a restart.
- Solo bots pick up their own coin and loot again.
- Dead bots stay where they died. Gamebots face their melee target.

### The new client

- The period 1.64 pre-game: Choose Your Realm, the standing stones, the one-page creation with stat points, height,
  face and hair, and the summary page.
- The classic character figures and looks, and the 1.64 spell table.
- The companion windows: hire, roster, gear and train, with baseline spells.
- The Horn of Rallying window. The inns on the map, with their markers and turn-in marks.
- Models numbered past 2047. Riders seated on stable horses. Bots sitting to rest. Fighters facing their target.
  Objects that stay put across zone borders.
- The window title is "Offline DAoC True 1.65 v0.1".

### The launcher

- Fixed to True 1.65. START SERVER applies the world content and keeps every save. One ENTER REALM button starts the
  new client.
- A fresh install makes its own local login, so ENTER REALM works the first time and the server creates the account.
- Removed:
  - the XP rate controls;
  - the level-50 bot buttons (bots are added at level 1);
  - forced realm events (PvE events start from the Horn of Rallying);
  - the Realm Exchange tab (the Exchange is in game only);
  - the Reset Keeps & Relics button.

The known issues are listed in the [README](README.md#known-issues), and the fixes taken from
community forks are credited in [THIRD_PARTY.md](THIRD_PARTY.md#fork-credits).
