# Changelog

The newest version is first.

Offline DAoC True 1.65 grew out of [Offline DAoC](https://github.com/shadowofze/OfflineDAoC) (0.3 to 0.37c, now on
hold). Its changelog has the history of everything this version inherits.

## Coming in 0.3

- Combat: a monster on its last sliver of health no longer falls over before the hit that kills it.
- World: two Albion banners floating in the air over Camelot Hills are gone.
- Looting: you can no longer pick up loot while dead.
- Groups: removing yourself from a group of bots that follow you releases them, instead of leaving them stuck following you where you could neither disband nor re-invite them.
- Chat: %t or %T in a chat line is replaced by your target's name (or "<nothing>" with no target), as in the 1.64 client - for example /s Healing %t.
- Interface: the status window gets its thin red border while you are in attack mode (melee), as in the 1.64 client.
- Companions: a companion that levels shows its new level in the group window at once ([#7](https://github.com/shadowofze/OfflineDAoC-True165/issues/7)).
- Quickbars: dropping a skill or spell you are holding onto a filled quickbar slot puts the one that was there on your pointer, as in the 1.64 client, instead of deleting it ([#6](https://github.com/shadowofze/OfflineDAoC-True165/issues/6)).
- Chat: the combat chat window no longer disappears when you click its top-right corner; that corner resizes it, as on the main chat window and in the 1.64 client ([#8](https://github.com/shadowofze/OfflineDAoC-True165/issues/8)). Hide or show either chat window in Options > Windows.
- Groups: a dead companion or group member stays shown as dead (no health) in the group window when the window is redrawn, instead of coming back at full health ([#10](https://github.com/shadowofze/OfflineDAoC-True165/issues/10)).
- Characters: companions and gamebots with some player looks no longer appear as an item - a Sylvan on look 12 was drawn as a bow (male) or a mace (female), and other looks of Sylvans, Valkyn, Frostalf, Shar, Half Ogres and more could show as armor pieces, shields or weapons ([#9](https://github.com/shadowofze/OfflineDAoC-True165/issues/9)).
- Camera: the 1.64 client's camera options. CAMERA FOLLOW (Options > Controls): off by default, as in the 1.64 client, a camera you turn with the left mouse button stays where you put it while you run, until Camera Reset; on, the camera swings back behind you as you move (as before). The Pan Camera key (bind it in Options > Controls > Camera) turns the camera around you instead of turning you. Camera Toggle (F11) switches to first person and back (zooming out also leaves it). Camera Reset now also puts the zoom back to normal, and the camera's zoom, tilt, angle and first person are kept for next time, as the 1.64 client kept them.
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
- Realm Exchange: bots list only items with a real use (crafted items, magic equipment, items with procs or charges, crafting and salvage materials, tradable quest items, dyes and enamels, potions, poisons, ammunition, spellcraft gems, siege equipment, housing items). Vendor trash such as monster parts, loot gems and plain drops goes to a merchant instead.
- Loot: monsters that drop a moonstone now drop the moonstone gem; they used to drop the Hibernian epic quest item of the same name by mistake. The quest Moonstone can now be dropped, destroyed and sold to a merchant for a few copper, so bots sell off any they picked up.
- Companions and gamebots in your group follow behind or beside you, turned with the way you face, each in its own place, instead of a fixed spot that could be in front of you. Turning on the spot no longer sends them walking around you.
- Buffs: a companion's stronger buff replaces a weaker one (a potion or a lower rank), and charmed pets in the group get the shareable buffs too.
- Companions: a companion you remove from the group no longer pops back in before it goes to the inn; a respecced companion keeps only the spells its new specs give; after you are raised your companions follow you at once.
- Items: weapons with broken damage fixed, and item stats set back to their 2003 values where the Prima strategy guides and the period item pages agree.
- Loot: drops from the Prima Atlas added to 361 monsters (93 of them dropped nothing before).
- Monsters: levels, aggression and speed from the Prima guides, in the zones the guides cover (bosses, epic dungeons and Darkness Falls named monsters unchanged).
- Quests: Bear Skins, Big Paw, Trapper's Joy, Ghoul Hair Belt, Missing Bard and the Berserker's Rasczel's Bane added from the period sources, each with its quest guide; Rhian's Dyes is Tyree's Dyes again (Tyree in Howth).
- Quests: The Growling Ghost branches as in the period. Help Growler (Growler's Necklace) and he gives you The Growler; slay him instead and Sergeant Alain sends you the long way to the same mace with Growler Mace and Mathien's Metal.
- Performance: fewer stutters when many fading or colour-changing spell effects play at once (each copy of such an effect no longer waits on the renderer), and the skills and spells pages no longer log an error on every skill update after they are rebuilt (both from a player's logs).
- Stability: a freeze that could hang the game for good when spell effects with particles (the Minstrel speed song's pulse among them) loaded in the background is fixed; those effects are now prepared on the main thread (from a player's logs: a 9-second stall, then no response).
- World: the Abermenai battleground has its ground textures again (its whole map was flat white).
- Horses: on stable routes that turn sharply (more than 90 degrees, such as North Tir na Nog to Druim Ligen) the horse turns with the route instead of sliding sideways for most of the ride.
- Chat: SMALL CHAT TEXT in Options > Chat, the 1.64 client's small chat font (UI SCALE in Options > Interface still makes all text bigger). Window transparency can now be set per window: right-click a window and its arrows change that window alone (the 1.64 client had one setting for all of them); ALPHA in Options > Interface still sets every window at once.
- Account vault: the account vault keepers open the vault window in the 1.64 style, with FIRST PAGE and SECOND PAGE buttons for the vault's two pages.
- Merchants: the Hibernian staff, weapon and shield merchants (Tir na Nog's weapon merchant among them) no longer sell caster staffs, blades, blunts and shields for 30 copper that you can't equip ("not of this realm"): those 120 items have their realm and price back.
- Account vaults: an account vault keeper in each capital (Camelot, Jordheim, Tir na Nog) lets your characters of one realm share items. Each realm has its own vault. This is a convenience addition that 1.65 did not have.
- Horse routes: horses no longer ride under the bridge near Druim Ligen or jump onto the bridge towers on the routes over the Druim Ligen, Innis Carthaig and Midgard river bridges.
- Experience: the camp bonus tops out at the period values, 30% in normal zones and 50% in dungeons, underwater and every RvR zone (the frontier zones and the battlegrounds), as before patch 1.71. It was 55% outdoors and 66% in dungeons.
- Crafting: bot crafters make only their own realm's items, so the Realm Exchange no longer offers armor or weapons from another realm that nobody in your realm can wear.
- Interface: the status window's health bars have the dark red background the other windows' bars have; the information window widens to fit a long item name instead of cutting it off.
- Interface: a click anywhere on a window brings it to the front, and an item's information opens beside the Realm Exchange instead of over its page buttons (they seemed to stop working after a right-click).
- Interface: item names in the backpack, vault and merchant lists always show in their con colour to you, not only when highlighted.
- Groups: the group window shows each member's endurance between health and power.
- Sound: new option MUTE WHEN NOT FOCUSED (Options > Audio, on by default) - the game goes quiet while you are in another window.
- NPC dialogue: the words you can answer with show bold and in [brackets], as in the 1.64 client, and long ones are never dropped (Kaere's "[Will you gather the components ...]" in Protecting Anniogel was missing).
- Monsters: a body no longer plays its death again when you right-click its loot or it is sent again, and a body whose removal never arrived (an untargetable dead wolf in Avalon Marsh) sinks away after 45 seconds.
- Looting (a small quality-of-life addition, not in 1.64): a right click on a loot bag or an item on the ground picks it up even when a monster's body or an NPC stands on the same spot, and the new Pick Up Loot key (Shift+G) takes the nearest loot within reach, one item a press, without pointing at it or targeting it.
- Looting (a small quality-of-life addition, not in 1.65): AUTO LOOT is back as an option (Options > Interface > Loot, off by default) - on, coins and items from your kills go straight into your backpack. Gamebots and companions still walk to their bags.
- World: Avalon Marsh's water no longer shows dark squares and steps where its pools meet.
- Quickbars: a weapon or instrument on a quickbar switches to it again (a Minstrel's instrument and weapon), as in the 1.64 client.
- Movement: going up and down stairs, your character, the camera and NPCs glide over the steps instead of jerking up and down; climbing itself is unchanged.
- Companions: companions ease into their place when the server corrects them in lag, instead of jumping onto you; a companion or gamebot changing its gear or swapping weapons (a Minstrel drawing a weapon as combat starts, a stealthed assassin you can see) no longer blinks out and back.
- Characters: a Highlander's kilt takes the look of the chest armour worn (a leather tunic, a leather kilt), as in the 1.64 client; the grey cloth kilt only without chest armour.
- Companions: hovering a companion's gear shows the item's full information, as for your own items, and hovering a style shows its endurance cost, threat and the rest; its stats show green when buffed and red when debuffed.
- Companions: an AI OPTIONS tab in the companion window for companions with choices - charm on or off, the pet a Cabalist or Enchanter summons (any pet it has learned, as each has its own perks; Bonedancers, Spiritmasters and Necromancers always use their best) or a Theurgist's element, the Bonedancer's three sub-pets (with the 75-point limit), and the songs or chants it uses in combat, out of combat and travelling.
- Realm Exchange: click a column header to sort by name, level, quality or price (again for the other way); choose how long a listing runs (12, 24 or 48 hours) and see each listing's time left; SEARCH SIMILAR in the price box shows what the same item sells for; the chest page shows how many of its 100 slots are used.
- Maps: every zone map of your realm shows the quests you can take there (gold "!"), their turn-ins (silver "?") and your objectives (crimson), wherever you are - open another zone's map to see its quests. The marks are small gold-rimmed medallions; a legend under the map names them (Options > Interface > MAP LEGEND hides it), and hovering a mark says what it is. The realm maps show no quest marks. Options > Interface > QUEST MARKERS ON MAP turns the map marks off (the circles under NPCs in the world stay).
- Monsters: a monster no longer vanishes when a hired companion hits it first, and a healer bot no longer vanishes when its group breaks up (fix from the rsandersjr fork).
- Companions: a Warden companion casts its speed chant (it has a cast time; [#14](https://github.com/shadowofze/OfflineDAoC-True165/issues/14), from the FriendMaple fork; an idle grouped Warden still keeps Bladeturn up). Animists use their mushrooms like a 1.65 player, with no caps on them ([#16](https://github.com/shadowofze/OfflineDAoC-True165/issues/16)).
- Companions: the AI OPTIONS tab gets class choices, each starting on Automatic (the companion's own choice): Aggro (hold it or shed it), Heal at, Debuff before nuking, Bolts, Berserk, Guard and protect, the Hunter's pet (Off sends it away), Open from stealth, a Role for Clerics, Druids, Healers, Shamans and Friars, Life transfer, Amnesia, an Animist's shrooms (which main shroom, and each kind of shroom on or off), and for Bards, Minstrels and Skalds a Fight choice (stay back on songs and spells, or melee).
- Companions and gamebots fight more like 1.65 players: damage dealers no longer taunt the monster off the tank (they detaunt when it turns on them) and step behind a monster the tank holds for their back and side styles; positional styles and style chains are used; casters debuff before they nuke and let the tank or their pet take the monster first; healers use group heals, group regeneration and Spread Heal when several are hurt.
- Companions and gamebots: assassins hide and open with Backstab or Perforate Artery; archers open a pull with Critical Shot; Thanes open with one bolt and then melee; Skalds keep their damage chant up in a fight and performers keep their fight song playing; Berserk is saved for hard fights; melee bots sprint after a monster that runs.
- Companions and gamebots: Quick Cast is used like a player uses it (an emergency heal, a stun or a finishing spell while being hit), and Necromancers use Facilitate Painworking before a cast while their servant is being hit. Life Transfer, Amnesia, Confusion, Nearsight and Cure Nearsight are used where 1.65 players used them.
- Companions: a tank with Guard fights with weapon and shield when it is the group's only tank and guards someone within reach; a companion keeps the weapon set you give it; bots no longer swap weapons back and forth (a bow on every pull try, archers at low endurance).
- Companions: companions rest below 70% power, out-of-power casters wait for power instead of charging in with a staff, companions keep their places while you only turn, healers stay near you when raising the dead, buffers rebuff right after a resurrection and stop retrying buffs that do not stick, and a companion's instrument is its best one.
- Companions: companions are summoned at the innkeeper only, show your guild name and emblem, catch up in experience when below your level (no per-kill cap), and use the realm abilities they were bought.
- Stealth: a stealthed monster stays hidden until you come within its aggro range or it attacks you, and is drawn normally once seen, as in 1.65 (it showed to everyone as a green ghost). Bots see stealthed monsters by the same rule.
- Quests: every zone map shows the quests your realm offers there, and "bring the drops" steps point at the monster until you have the item; a quest monster is no longer spawned a second time while one is still there; an NPC's dead [links] answer with the recorded reply; tooltips list only 1.65 classes as usable and show no database notes.
- Realm Exchange: listings run 12, 24 or 48 hours (48 by default) and an unsold item stays in your chest; listings can be sorted and show their time left. Bots prefer fair-priced player items, relist once at most, and keep the arrows and poisons they buy instead of selling them straight back.
- Darkness Falls: the Horn of Rallying shows the Darkness Falls raid greyed out with the reason while your realm cannot enter (it opens only to the realm holding the most keeps, as in 1.65).
- Gamebots: bots spread out over their zones instead of clumping in a few camps (also camps on live spawns the period catalogue missed), no longer take a monster another bot or group is fighting, reach the battlegrounds through the portal keep teleporter, start their sieges with siege equipment from a siegecrafter, and no longer rely on town teleporters.
- Horses: stable routes ride at one steady speed (no super-speed legs, no stop-and-drift at route points).
- Server: fewer stalls (the once-a-minute pause while bots planned shopping trips), and no speed-hack snap-back right after a stall.
- Battlegrounds: Thidranki has a keep lord.
- /invite with a name finds the realm's world gamebots.
- Gamebots: low-level bots stop walking into spots where bots of their level keep dying (a pack of level 8-9 lunantishee and blackthorn south of Mag Mell killed low Hibernian bots thousands of times on their way to a merchant); a bot that dies on its way to a merchant picks another for a while.
- Gamebots: after a death a solo bot no longer hunts only green monsters until the server restarts - it steps back up after a level or half an hour without dying.
- Gamebots: assassins hide and open with Backstab or Perforate Artery instead of pulling with a thrown knife or crossbow; bots keep a smaller coin reserve so archers and stealthers can afford better bows, weapons and armor; casters pick staves for their own spell lines; solo Foresters hunt blue monsters, as the period guide teaches.
- Necromancer bots: a bot whose servant dies keeps 10% of its health, as a player does (it was left with 1 hit point).
- Chants and songs: a pulsing chant (a Paladin's endurance chant among them) no longer leaves its group with no endurance regeneration after deaths and resurrections - endurance could stay at 0% even sitting.
- Loot: monsters no longer drop items far above their level: level-3 phantom hounds dropped a level-49 "Gnawed Bone" worth almost 4 gold to a merchant; they now drop a plain gnawed bone, and such out-of-level gear drops are gone.
- Maps: the map list starts with your realm's two realm maps - Albion and Avalon (Shrouded Isles), Midgard and Aegir (Shrouded Isles), Hibernia and Hy Brasil (Shrouded Isles) - above the capital. Every zone is a block on them; click one to open its map.
- Maps: your realm's gamebots show as green dots on every zone map you pick from the list, wherever you are, and as small dots all over the realm maps (Options > Bots > REALM BOTS ON MAP turns them off). It stays light with thousands of bots: bots outside your zone move on the map every 12 seconds.
- Companions: the companion window no longer shows an old error message again after something works (TRAIN showed "Unknown hiring command.").
- Companions: hovering a companion's style that follows another (an Armsman's Opal Slash) names its opening style; it showed "(style not found 58)" when your own class has no such style.
- Merchants: 36 merchants whose title did not match their goods show the right one - Adribard's Retreat's Theurgist and Sorcerer staff sellers were titled Shield Merchant and Armor Merchant; arrow, bolt, bow and crossbow sellers, armor and robe merchants elsewhere had the same mix-up.
- Raids: a realm raid's high-end boss loot is shared out again - a quarter straight to the players' bags, the rest to the raid's gamebots; in True 1.65 it had stayed on the ground.
- Gamebots: your hired companions no longer take gamebots' places - with three companions out, three gamebots stayed offline ("queued") because the companions were counted toward the gamebot population.
- Trainers: hovering a realm ability in the trainer window shows what it does, from the server's own description (the window never asked for it before).
- Movement: you can no longer walk up a keep wall step by step: a face steeper than 60 degrees no longer counts as ground (period: climbing keep walls was removed in 2001; from 1.42 only assassins with the Climb ability climb keeps, at their climb points).
- Horses: at a sharp bend of a stable route (Mularn to Svasud Faste) the horse turns into the new leg instead of sliding sideways back onto it.
- Doors: the healer doors and round doors of Jordheim open again in the server's game - clicking them never reached the server.
- Crafting: the 80 metalworking recipes that can never be made in a 1.65 world (tempered metal studs, wire, strapping and scales - their old components were retired in 1.65) are gone from the recipe list. Tempered saddlebags stay.
- Battlegrounds: the central keeps of Caledon, Murdaigean, Thidranki and Abermenai stand where the 1.65 client put them - turned and placed from its own tables, with Caledon's and Thidranki's second keep models. Their lords and guards no longer float beside the walls, and the keep doors line up with the server's.

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
