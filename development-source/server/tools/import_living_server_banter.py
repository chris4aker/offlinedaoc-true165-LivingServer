#!/usr/bin/env python3
"""Rewrite Living Server's chat and lore lines as banter165 files.

  python -I tools/import_living_server_banter.py --living-server <checkout> --world-db <opendaoc-165.sqlite3.db>
      [--out banter165] [--share 0.5] [--only-share 0.25] [--ledger banter165/LIVING-SERVER-LINES.md] [--samples N]

Living Server (github.com/chris4aker/OfflineDAoC-LivingServer, branch integration/1.2) keeps about 8,400 written chat
lines, a lore bible and 419 chat scenes for the original Offline DAoC. This tool brings what the banter library can
say into it, under the library's own rules (BanterLibrary165.cs, validate_banter165.py):

- every line is given one of the library's situations and its filters (channel, realm, level, classType, only);
- the text is rewritten from lower-case 2003 chat into the library's voice: full words, capitals, a full stop;
- a line with "[a|b]" choices is taken once, in the first choice of each, from Living Server's source files;
- a line about one class or one place is tied to it with "only", so it is said only where it is true; a line about
  the night is tied to the night; an answer written for a boast, a joke or a question follows only such a line;
- left out: anything the library's rules refuse, talk about the world outside the game, words the library does not
  use, a place, monster or person that is not in the True 1.65 world, and a line the library already says;
- held (listed in the ledger, not written): anything that needs a system True 1.65 does not have, lore that Living
  Server wrote itself, and talk about what a monster does in a fight, which follows Living Server's own boss scripts;
- lore from the lore bible is written only when its words come from the entry's sourced facts (game data, Mythic's
  own lore, period player lore, the myths);
- the new lines of a situation share its airtime with the lines already there (--share, half by default), and all the
  lines of one zone or class get a quarter of that (--only-share), so the library keeps its own voice.

The files it writes start with ls_ and are rebuilt from scratch on every run; never edit them by hand. Copy them to
runtime/server/banter165 as well. The ledger lists what was written, what was left out and why, and what is held.
The rewriting is done by rules, not by hand: read a sample (--samples) after every change to them.
"""
import argparse
import collections
import glob
import json
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import validate_banter165 as rules  # noqa: E402  the library's own checks

REALM = {"alb": "Albion", "mid": "Midgard", "hib": "Hibernia"}
CLASSES = {
    "Albion": ["Armsman", "Cabalist", "Cleric", "Friar", "Infiltrator", "Mercenary", "Minstrel", "Necromancer", "Paladin",
               "Reaver", "Scout", "Sorcerer", "Theurgist", "Wizard"],
    "Midgard": ["Berserker", "Bonedancer", "Healer", "Hunter", "Runemaster", "Savage", "Shadowblade", "Shaman", "Skald",
                "Spiritmaster", "Thane", "Warrior"],
    "Hibernia": ["Animist", "Bard", "Blademaster", "Champion", "Druid", "Eldritch", "Enchanter", "Hero", "Mentalist",
                 "Nightshade", "Ranger", "Valewalker", "Warden"],
}
ALL_CLASSES = [c for group in CLASSES.values() for c in group]

# ---------------------------------------------------------------- where a Living Server line goes

# kinds that need a system the banter library does not have (and what that system is)
HELD_KINDS = {
    "beggar": "beggars in the capitals", "spam": "the gold spammer", "spam_react": "the gold spammer",
    "roleplay": "roleplayer characters", "deed": "bots that witness what the player does",
    "deed_reply": "bots that witness what the player does", "rumour": "live rumours (a named monster is up, a keep is thin)",
    "rumour_reply": "live rumours (a named monster is up, a keep is thin)", "storyline": "storylines between two bots",
    "bond": "bots that remember who they grouped with", "bond_reply": "bots that remember who they grouped with",
    "tell": "bots that send the player a tell", "session": "bots that say hello and goodbye as they log in and out",
    "linkdead": "bots that say hello and goodbye as they log in and out", "gossip": "bots that remember the player",
    "grudge": "bots that remember the player", "nemesis": "bots that remember who killed them",
    "revenge": "bots that remember who killed them", "first": "server firsts", "alarm": "frontier alarms with a head count",
    "rez_ask": "bots that ask the player for a resurrection", "rez_offer": "bots that ask the player for a resurrection",
    "combat": "calls during a fight in the player's group", "greet": "bots that remember the player",
}

AMBIENT_TOPIC = {
    "general": "idle", "class": "idle", "classes": "idle", "realm_pride": "idle", "lore": "idle", "loot": "hunting",
    "leveling": "hunting", "death": "death", "newbie": "idle", "grouping": "idle", "mobs": "hunting", "veteran": "idle",
    "camp": "hunting", "crafting": "crafting", "crafter": "crafting", "money": "idle", "bags": "idle", "gripe": "idle",
    "dungeon": "dungeon", "ding": "levelup", "banter": "idle", "humor": "idle", "boast": "idle", "explorer": "idle",
    "daredevil": "idle", "leveler": "idle", "socialite": "idle", "roles": "idle", "town": "town", "capital": "town",
    "group": "group_hunt", "travel": "travel", "lowlevel": "idle", "solo": "hunting", "xp": "hunting",
    "shrouded_isles": "idle", "waiting": "idle", "rumor": "idle", "battlegrounds": "battleground",
    "darkness_falls": "idle", "places": "idle", "places_here": "idle",
}
HELD_TOPICS = {"holiday": "a calendar (holiday talk)", "evening": "the evening (the library tells day from night only)"}
TIME_TOPIC = {"latenight": "night", "night": "night", "daytime": "day"}
NEED_TIME = {"night": "night", "!day": "night", "day": "day"}
# what an opening line asks for, and the answers written for it ("invites" and "only": { "thread": ... })
THREADS = {"boast", "joke", "complaint", "ask_opinion", "ask_choice", "ask_anyone", "ask", "ask_know", "ask_next",
           "ask_whereall", "ask_howareyou"}
DROP_TOPICS = {"reallife": "talk about the world outside the game", "nolifer": "talk about the world outside the game"}
GUILD_TOPIC = {"social": "idle", "general": "idle", "crafting": "crafting", "leveling": "hunting", "rvr": "rvr_patrol",
               "raiding": "idle"}
RVR_TOPIC = {"frontier_alb": ("rvr_patrol", "Albion"), "frontier_mid": ("rvr_patrol", "Midgard"),
             "frontier_hib": ("rvr_patrol", "Hibernia"), "frontier": ("rvr_patrol", None), "keeps": ("rvr_patrol", None),
             "battlegrounds": ("battleground", None), "darkness_falls": ("idle", None)}
REPLY_INTENT = {"greeting": "reply_greeting", "farewell": "reply_farewell", "thanks": "reply_thanks", "ding": "reply_ding",
                "congrats": "reply_ding", "abuse": "reply_insult", "banter": "reply_banter", "laugh": "reply_banter",
                "grouping": "reply_lfg", "help": "reply_help", "trading": "reply_trade", "rvr": "reply_rvr",
                "alarm": "reply_rvr", "dungeon": "reply_dungeon", "travel": "reply_where", "grinding": "reply_what",
                "classtalk": "reply_general", "unknown": "reply_general", "ignoreoutofworld": "reply_offtopic"}
# a bot answering another bot's line: the situation its opener was said in
REPLY_ANSWERS = {"chat": "idle", "lfg": "lfg", "trade": "trading",
                 "news_keep_won": "rvr_victory", "news_siege_held": "rvr_victory", "news_relic_won": "rvr_victory",
                 "news_keep_lost": "rvr_defeat", "news_siege_failed": "rvr_defeat", "news_relic_lost": "rvr_defeat",
                 "news_raid_won": "raid_victory", "news_raid_wiped": "raid_ended", "news_siege_defend": "rvr_defend",
                 "news_siege_attack": "rvr_siege"}
REPLY_TO_PLAYER = {"ask_where": "reply_where", "ask_advice": "reply_help", "ask_doing": "reply_what"}
NEWS_TOPIC = {"siege_defend": "rvr_defend", "siege_attack": "rvr_siege", "keep_won": "rvr_victory",
              "siege_held": "rvr_victory", "relic_won": "rvr_victory", "keep_lost": "rvr_defeat",
              "siege_failed": "rvr_defeat", "relic_lost": "rvr_defeat", "raid_forming": "raid_forming",
              "raid_won": "raid_victory", "raid_wiped": "raid_ended"}

NEED_SITUATION = {"in_dungeon": "dungeon", "in_capital": "town", "in_town": "town", "in_frontier": "rvr_patrol",
                  "in_rvr": "rvr_patrol", "in_battleground": "battleground", "at_camp": "hunting", "in_group": "group_hunt",
                  "traveling": "travel", "just_leveled": "levelup", "just_died": "death", "bag_full": "hunting",
                  "defending": "rvr_defend", "recruiting": "lfg", "has_listing": "trading"}
NEED_CLASSTYPE = {"is_healer": "healer", "is_caster": "caster", "is_support": "support", "is_tank": "tank", "can_rez": "healer"}
NEED_LEVEL = {"newbie": (1, 12), "veteran": (40, 50)}
NEED_IGNORE = {"solo", "outdoors", "!in_capital", "!in_combat", "!in_player_group", "!defending", "!bot_leads", "!can_rez"}
NEED_HELD = {
    "in_player_group": "talk in the player's own group", "lead_call": "bots that lead a group",
    "bot_leads": "bots that lead a group", "evening": "the evening (the library tells day from night only)",
    "knows_player": "bots that remember the player", "!knows_player": "bots that remember the player",
    "witness": "bots that witness what the player does", "!witness": "bots that witness what the player does",
    "player_nearby": "bots that witness what the player does", "player_leveling": "bots that witness what the player does",
    "xp_high": "how far a bot is into its level", "xp_mid": "how far a bot is into its level",
    "xp_low": "how far a bot is into its level", "df_ours": "who holds Darkness Falls", "!df_ours": "who holds Darkness Falls",
    "keep_news": "news of the last keep taken", "keep_won_recent": "news of the last keep taken",
    "keep_lost_recent": "news of the last keep taken", "relic_won_recent": "news of the last relic taken",
    "relic_lost_recent": "news of the last relic taken", "will_rez": "bots that resurrect the player",
}

# Living Server slots the library can fill, by the placeholder that fills them
SLOT = {"me": "bot", "class": "class", "level": "level", "realm": "realm", "enemy": "enemyrealm", "zone": "zone",
        "monster": "target", "item": "item", "keep": "keep", "player": "player", "target": "player",
        "destination": "destination"}

# ---------------------------------------------------------------- the voice

# talk about the world outside the game, the game as software, or Living Server systems: never said in this library
OUTSIDE = re.compile(r"\b(log|logs|logged|logging|logon|logoff|login|relog|session|keyboard|mouse|screen|monitor|kids?|mom|dad|"
                     r"wife|husband|gf|bf|roommate|dinner|lunch|breakfast|supper|coffee|pizza|beer|soda|playing|played|plays|"
                     r"player|players|character|characters|toon|toons|alt|alts|account|accounts|patch|patched|patches|nerf|"
                     r"nerfed|nerfs|gm|gms|banned|ban|typing|typed|typo|afk|brb|bbl|ld|linkdead|lag|lagged|laggy|lagging|"
                     r"vote|work|job|school|class tomorrow|homework|college|dorm|bed|bedtime|sleep|asleep|tv|movie|website|"
                     r"forum|forums|vault|mythic|devs?|developers?|macro|macros|hotbar|quickbar|ui|client|fps|crash|crashed|"
                     r"reboot|computer|pc|modem|dialup|dial-up|weekend|monday|tuesday|wednesday|thursday|friday|saturday|sunday|"
                     r"hours? straight|buffbot|buffbots|pst|tells?|faction|"
                     r"second account|two accounts|years ago|last year|months|real money|ebay|subscription|sub|game|games|"
                     r"gaming|gamer|pixels?|polygons?|graphics|beta|release|expansion|expansions|teleport|teleporter|"
                     r"teleporters|ported|porter|hastener|hasteners|rip|wtf|omg|sucks?|sucked|crap|damn|hell|pissed|"
                     r"halloween|christmas|thanksgiving|easter|valentines?|new years?|holiday|holidays|santa|"
                     r"respec|respecs|respecced|bug|bugs|bugged|exploit|exploits|cheat|cheats|cheater|hack|hacks|"
                     r"min|mins|minutes|hrs?|am|pm|oclock|o'clock|"
                     r"my (little |big |older |younger |baby )?(sister|brother|nephew|niece|son|daughter|parents|family|boss|dog|"
                     r"cat|neighbou?rs?|desk|chair|room|house|apartment|landlord|teacher|professor|pager|alarm|uncle|aunt|"
                     r"grandma|grandpa|cousin|ping|connection)|the family|engaged|wedding|boss finally|told my boss|"
                     r"finals|semester|grades|midterms?|essay|due this|study|studying|exams?|aced|my test|project due|"
                     r"on call|coworkers?|paperwork|inbox|notepad|accountant|spreadsheet|screenshots?|online|offline|"
                     r"download|downloading|downloaded|video|ram|upgraded|slideshow|heater|grocery|takeout|chinese|food is|"
                     r"snacks?|cake|birthday|fantasy book|(go|went|cold|freezing|snowing|raining|dark|hot|nice|sunny) (\w+ )?outside|"
                     r"outside more|stay in|stayed in|snowing like|cleaned|laundry|dishes|chores|errands|traffic|commute|"
                     r"guild notes|update|rankings|loot tables?|pathing|quest text|zone line|zoning|zoned|level up sound|"
                     r"wrong key|map shows|on the map|window|button|click|clicked|promised|headphones|speakers|volume|"
                     r"dude|lowbies?|housing|play|dog|lawn|garage|dentist|quiz|upstairs|windows|printed|worked a|"
                     r"(fantasy|mystery|good|great) book|stood up|got up|hail|feet|legs|wrists?|couch|kitchen|fridge|"
                     r"doorbell|bathroom|shower|nap|headache|flu|sick day|doctor|bus|car|drive|drove|driving|flight|"
                     r"airport|vacation|trip to|store|shopping|mall|paycheck|payday|overtime|meeting|office|commute|"
                     r"internet|email|phone|call from|voicemail|radio|song|album|band|cd|dvd|show on|episode|"
                     r"who is on|names on|guild list|full roster|on (in|for) (a|an|\w+) (minute|minutes|hours?)|be on|get on|"
                     r"to be on|not on tonight|old guides|death spam|yo)\b", re.I)
# what a monster does in a fight, when it shows itself and how strong it is come from Living Server's own world and
# boss scripts; a rumour about them could be untrue here
FIGHT = re.compile(r"\b(kill (him|her|it|them) (first|twice|more than once)|heals? off|unkillable|never fights alone|"
                   r"get up with (him|her)|come with (the|him|her)|calls? the|they all come|grows? .* back|breath|"
                   r"resists?|nukes?|nuked|berserk|takes? (less|no) damage|one damage|heal(s|ed)? (him|it|her)|healed by|"
                   r"fairies first|throws? people|apparitions?|copy of himself|grows heads|tanks (hold|have|get)|"
                   r"shield node|hits? like|immune|aggro|only comes? out|never attacks? first|do not attack first|"
                   r"does not attack first|attacks? first|level (\d+|ninety|eighty|seventy|sixty)|\d+ levels|"
                   r"eighty levels|seventy something|takes heat|outside draws|inside the ruins|goes berserk)\b", re.I)
# guild "social" talk is half about life outside the game: a line stays only when it is plainly about the world
IN_WORLD = re.compile(r"\b(guild|guildmates?|group|groups|level|levels|leveling|experience|raid|raids|camp|camps|tank|tanks|"
                      r"healer|healers|pull|pulls|con|named|keep|keeps|frontier|realm|craft|crafting|armor|gear|loot|drop|"
                      r"drops|quest|quests|spec|ding|dungeon|mob|mobs|wipe|wiped|adds?|relic|relics|coin|gold|silver|"
                      r"merchant|horse|bind|rez|mez|stealthers?)\b", re.I)

PHRASES = [  # ordered; applied to lower-case text, word boundaries added
    (r"w/o", "without"), (r"w/", "with "), (r"8 ?man", "eight-man"), (r"fg", "full group"), (r"2h", "two-hander"),
    (r"2 ?handed", "two-handed"), (r"1h", "one-hander"), (r"dw", "dual wield"),
    (r"lf1m", "looking for one more"), (r"lf2m", "looking for two more"), (r"lf3m", "looking for three more"),
    (r"lfm", "looking for more"), (r"lfg", "LFG"), (r"lf", "looking for"), (r"wts", "WTS"), (r"wtb", "WTB"),
    (r"wtt", "looking to trade"), (r"oom", "out of power"),
    (r"pbaoe", "PBAE"), (r"pbae", "PBAE"), (r"aoe", "AE"), (r"ae", "AE"), (r"dots", "damage over time"), (r"dot", "damage over time"),
    (r"dd", "direct damage"), (r"hots", "heals over time"), (r"hot", "hot"),
    (r"rvr", "the frontier"), (r"pve", "hunting"), (r"bgs", "battlegrounds"), (r"bg", "battleground"),
    (r"df", "Darkness Falls"), (r"si", "the Shrouded Isles"), (r"thid", "Thidranki"), (r"murd", "Murdaigean"),
    (r"cale", "Caledonia"), (r"aber", "Abermenai"), (r"tnn", "Tir na Nog"), (r"jord", "Jordheim"),
    (r"emain", "Emain Macha"), (r"odins gate", "Odin's Gate"), (r"hadrians wall", "Hadrian's Wall"),
    (r"at the broker", "on the Exchange"), (r"brokers?", "Exchange"),
    (r"albs", "Albs"), (r"mids", "Mids"), (r"hibs", "Hibs"), (r"alb", "Alb"), (r"hib", "Hib"),
    (r"xp", "experience"), (r"exp", "experience"), (r"rps", "realm points"), (r"rp", "realm points"), (r"hp", "health"),
    (r"dmg", "damage"), (r"dps", "damage"), (r"lvls", "levels"), (r"lvl", "level"), (r"plat", "platinum"),
    (r"aggros", "aggro"), (r"agro", "aggro"),
    (r"rezzes", "resurrections"), (r"rezzed", "raised"), (r"rezzing", "raising"), (r"rezzer", "healer"), (r"ress", "rez"),
    (r"mezzed", "mesmerized"), (r"mezzes", "mesmerizes"), (r"mezz", "mez"),
    (r"buffed", "buffed"), (r"debuff", "debuff"), (r"guildies", "guildmates"), (r"guildie", "guildmate"),
    (r"grp", "group"), (r"grps", "groups"), (r"inv", "invite"), (r"invs", "invites"), (r"ppl", "people"),
    (r"tyvm", "thank you"), (r"ty", "thanks"), (r"thx", "thanks"), (r"thanx", "thanks"), (r"np", "no trouble"),
    (r"yw", "you are welcome"), (r"gl", "good luck"), (r"gj", "well done"), (r"gn", "good night"),
    (r"gratz", "grats"), (r"gz", "grats"), (r"wb", "welcome back"), (r"omw", "on my way"), (r"otw", "on my way"),
    (r"pls", "please"), (r"plz", "please"), (r"sec", "moment"), (r"kk", "all right"), (r"k", "all right"),
    (r"ok", "all right"), (r"okay", "all right"), (r"u", "you"), (r"ur", "your"), (r"r", "are"), (r"y", "why"),
    (r"em", "them"), (r"'em", "them"), (r"ya", "you"), (r"yeah", "yes"), (r"yup", "yep"),
    (r"nope", "no"), (r"dunno", "do not know"), (r"gonna", "going to"), (r"wanna", "want to"),
    (r"gotta", "have to"), (r"kinda", "rather"), (r"sorta", "rather"), (r"lemme", "let me"), (r"gimme", "give me"),
    (r"cmon", "come on"), (r"c'mon", "come on"), (r"yall", "you all"), (r"y'all", "you all"), (r"cuz", "because"),
    (r"bc", "because"), (r"tho", "though"), (r"thru", "through"), (r"til", "until"), (r"till", "until"),
    (r"imo", "I think"), (r"tbh", "truth be told"), (r"btw", "by the way"), (r"atm", "right now"), (r"asap", "at once"),
    (r"nvm", "never mind"), (r"idk", "I do not know"), (r"vs", "against"), (r"b4", "before"), (r"2nd", "second"),
    (r"1st", "first"), (r"3rd", "third"), (r"cya", "see you"), (r"you guys", "you all"), (r"guys", "folks"), (r"guy", "fellow"),
    (r"anymore", "any more"), (r"alot", "a lot"), (r"everyones", "everyone's"), (r"someones", "someone's"),
    (r"nobodys", "nobody's"), (r"anyones", "anyone's"), (r"mhm", "yes"), (r"yea", "yes"), (r"nite", "night"),
    (r"thats", "that is"), (r"whats", "what is"),
]
CLASS_SHORT = {"merc": "Mercenary", "mercs": "Mercenaries", "theurg": "Theurgist", "theurgs": "Theurgists", "zerker": "Berserker",
               "zerkers": "Berserkers", "zerk": "Berserker", "zerks": "Berserkers", "infil": "Infiltrator",
               "infils": "Infiltrators", "necro": "Necromancer", "necros": "Necromancers", "sorc": "Sorcerer",
               "sorcs": "Sorcerers", "eld": "Eldritch", "elds": "Eldritches", "ment": "Mentalist", "ments": "Mentalists",
               "cab": "Cabalist", "cabs": "Cabalists", "cabby": "Cabalist", "sm": "Spiritmaster", "sms": "Spiritmasters",
               "bd": "Bonedancer", "bds": "Bonedancers", "bm": "Blademaster", "bms": "Blademasters", "vw": "Valewalker",
               "vws": "Valewalkers", "sb": "Shadowblade", "sbs": "Shadowblades", "rm": "Runemaster", "rms": "Runemasters",
               "ns": "Nightshade", "champ": "Champion", "champs": "Champions", "chanter": "Enchanter",
               "chanters": "Enchanters", "pally": "Paladin", "pallies": "Paladins", "wiz": "Wizard", "wizzy": "Wizard",
               "shammy": "Shaman", "mincer": "Minstrel", "mincers": "Minstrels", "arms": "Armsman"}
CONTRACTIONS = {
    "im": "I am", "i'm": "I am", "ive": "I have", "i've": "I have", "ill": "I will", "i'll": "I will", "i'd": "I would",
    "dont": "do not", "don't": "do not", "cant": "cannot", "can't": "cannot", "wont": "will not", "won't": "will not",
    "isnt": "is not", "isn't": "is not", "arent": "are not", "aren't": "are not", "wasnt": "was not", "wasn't": "was not",
    "werent": "were not", "weren't": "were not", "doesnt": "does not", "doesn't": "does not", "didnt": "did not",
    "didn't": "did not", "havent": "have not", "haven't": "have not", "hasnt": "has not", "hasn't": "has not",
    "hadnt": "had not", "hadn't": "had not", "couldnt": "could not", "couldn't": "could not", "wouldnt": "would not",
    "wouldn't": "would not", "shouldnt": "should not", "shouldn't": "should not", "aint": "is not", "ain't": "is not",
    "thats": "that is", "that's": "that is", "whats": "what is", "what's": "what is", "theres": "there is",
    "there's": "there is", "heres": "here is", "here's": "here is", "wheres": "where is", "where's": "where is",
    "whos": "who is", "who's": "who is", "hows": "how is", "how's": "how is", "hes": "he is", "he's": "he is",
    "shes": "she is", "she's": "she is", "it's": "it is", "youre": "you are", "you're": "you are", "theyre": "they are",
    "they're": "they are", "we're": "we are", "youve": "you have", "you've": "you have", "theyve": "they have",
    "they've": "they have", "weve": "we have", "we've": "we have", "youll": "you will", "you'll": "you will",
    "theyll": "they will", "they'll": "they will", "we'll": "we will", "itll": "it will", "it'll": "it will",
    "thatll": "that will", "that'll": "that will", "he'll": "he will", "she'll": "she will",
    "let's": "let us", "youd": "you would", "you'd": "you would", "theyd": "they would", "they'd": "they would",
    "we'd": "we would", "he'd": "he would", "she'd": "she would", "itd": "it would", "it'd": "it would",
    "id": "I would",
}
OWNED = ("own|ice|heads?|walls|side|name|reputation|axes|coin|wearer|plate|magic|snow|health|temper|stones|complaints|"
         "knees|king|rank|pair|legends|keep|people|range|spring|teeth|claws|den|lair|nest|hoard|master|mother|young|"
         "tail|eyes|weight|share|place|price|charm|bite|way back|way home|way here|way out|way in|way to")
# words of a later way of talking than the library's
REGISTER = re.compile(r"\b(cool|sweet|totally|for real|so real|neat|is a thing|a thing|aw|whoa|happy dance|yikes|oops|gulp|"
                      r"mm|i mean|on the nose|metric|efficiency|designed|invented|type|porting|sandwich|stamps|day off|"
                      r"book report|sink|my partner|listening to|clicking|buttons|compass|summer cannot|yay|heya|woot)\b", re.I)
# news of the hour, said in the lore bible as a standing rumour: untrue on any day it did not happen
NEWS = re.compile(r"\b(ago|just now|tonight|today|right now|this morning|last night|yesterday|out again|is up|are up|"
                  r"came through|is back|are back|on the move|was open|all night)\b", re.I)
QUESTION = re.compile(r"^(How (do|does|did|is|are|many|much|long|far|come)|Why (do|does|did|is|are|would)|Where (do|does|did|is|are|can)|"
                      r"Who (is|has|wants|needs|else|can)|What (is|are|do|does|level)|Does|Do you|Did (anyone|you|we)|"
                      r"Is (there|it|that|anyone)|Are (there|you|we)|Can (anyone|someone|you|I|we)|Anyone|Anybody|"
                      r"Any (interest|healers?|groups?|takers|chance|ideas?|tips|advice|room|luck|one)|"
                      r"Ever (notice|seen|wonder)|Has anyone|Should I|Which) ")
# said in neither voice
EMPTY = re.compile(r"\b(lol|lmao|rofl|roflmao|haha+|hehe+|hah|lolol|jk|j/k)\b[,.!]?", re.I)
# after the rewrite, a short word that is not one of these is chat shorthand the rules above do not know: leave the line out
# words that are part of a name (Cursed Forest, the Green Knight) but ordinary words when they stand alone
NOT_NAMES = set("""forest valley tomb cave caves hills hill mountain mountains marsh swamp glacier gorge grove estuary fortress
vale keep castle city mine mines plains caverns cavern lair gate wall falls isles isle king queen lord lady prince princess
knight emperor captain chieftain lieutenant master mistress elder ancient black white green grey gray blue dark cursed
shining moving ones north south east west staff hammer cauldron spear scabbard horn realm exchange rallying frontiers
frontier great grand high lost sister brother father mother giant spirit ghost guardian warden champion hero wizard
summoner spectral corrupt primal host blight sorrow academy apparitions bartender bonds bonk bright brutal castellan
choir circlet conservator cook council creeper crone crusher daggers dare demand deny diplomats downs enchantments
enchantress escort explorer fester fisherman geomancer glen golden grin harmony herder hermit inflict inquisitor
isolationists ladies leather leprosy lookout magic marshal masher mutilator mutilators nipper nock pebble
pester prototype pulse purity revenant ruckus ruthless saffron savant scar scryer scurry scuttle secrets smack smite
snarls songbird spat speaker squirt statues straw stripe suffering sunstone tailor tapestry townfolk treasure twirl
twisting void whirl wight wrist miles lyrists sept troika overseer priestess patrol node major seer hunter
commander skeletal golem emissary trader keeper herbalist leader matriarch""".split())
# the same for the single words of the names in the lore bible and the world: these are also dictionary words, so
# they are written with a capital only inside the full name
NOT_NAMES |= set("""abandoned abigail abolishment accursed aching acid aegis agony aisling alar albino albs alburn alchemist alder alec
ales alvar ambassador amma angor animist animists anna annihilate annihilation annihilator anxious apocalypse appeal
apprentice aqueducts arachnid arbor arcanes archdruid archivist arena armorer arni aroon ashen assistant association
asta asthore athar atlas august aura aurora avenger azure baldric banak bandit bane banish barbaric bard bards
barlow baron barra barrow barrows bash beath bedfellows behemoth beni bennet berserker berserkers betrayer billy
bint birk birn blair blake blan blights blindness bolo bomb bombard boreal borealis borg bouncer bound brach bracken
bran branch branches brawler brede brent brewer brimstone bristle brit brogan bronze brut buri burial burl burr
burrow burton buzz bysen cabalist cabalists cackler cain camber carl carr caryl cass catacombs cauld celt celts chad
chaes chamberlain chancellor chief chlorosis chosen chthonic circa clan claret cleric clerics cliffs cluster coated
colk collin command concealed conflict conqueror constable cookery cora corporal county courier crater cree crimson
croaker cronk crucible cruss crypt crystal cycle dagger dain dale dales dalton dame damned darac darby darg darkness
dash dawning deben decadent deceit decorator decorators delta demon demonic denizens denys depths derry desire
despair destroyer destructive develin devourer diamond dian dink diplomat director dirge disenchanter dismal
dispersal disturbances domal dooley doom dory downfall druid druids duer duke dummy duros dutch dwarf dyemaker dyer
earl earth ebony echoes eldritch eldritches eldritchs elementalist elementalists elf elves emendation emerald emma
enamel enchanter enchanters enchanting energy enlightened enlightenment ensign erne eternal ethel event excellence
extinguish faerie fairy falcon fames famine fanatics fanged farl fash fate feathers fellowship finfoot flame flayer
fogou folly forgotten fras frat frederik friar friars frit funerary gage gala gale galena gaol gatekeeper gemma
genius gerb germanic gest gilbert gill gilling glades glass glaze gleek glimmer glitter globule glom glover glum
glutton gnat gneiss gnome goblin goby goetia gors gospel gossamer grace graham gram granddaddy grank granny grant
gremlin gris grotto growler gruff grum guardhouse guardsman hagi hako hale harish harry haunted haven hayward
heather heavenly hector hellhag hellos helminth hemlock herald heretic heretical hickey hidden highlander
highlanders history hoarfrost hoges holt hopeless horned hounds housing huntsman idol illusions impossible inconnu
incursion infection infirmity informant information innes inquisitors insidious insurrection intoners island
isolationist ither jack jailer james jane jarl jasper jewel jordan josh journeyman julienne june kedger keeler keena
kell kelly kensington kern kiley klippe knobs kobold kobolds kran kyle labyrinth lade lame lance landing lane laney
larch launch laura lecherous legion lene leptus leviathan librarian lich lieutenants lina lions loch longbeard
longlegs lora loral loran lords lucida lug luke lunn lunt lura madness mage maggot magister maid maire mandra manius
manus marc marlin marquis marus matron maud mechanism melancholic mentalist mentalists mercal mercenary mercenarys
mercer merle merlin mica mide mids midsummer migration milo mincer minstrel minstrels mirrors monger moraine morg
morgen morin morth mount mouth muires murmur myron naid nain nash navar necklace necromancer necromancers
necromantic neese negate nelson netmaker nightshade nightshades nisse noble noma norma ogress organic osier ostein
ottar outland overman pain paladin paladins parliament passage passages pater pathfinder pelterer penda perry perses
phut pico pict picts piercer pikeman plight plot plutonian post prattler precedence present prison prose protecting
protector proving provisioner pyramid quieting quietus quinton radiation raker rana rasa ravine realization reaver
reavers reawakening rebel rebellion reckoner redbeard refuge regin regnal repeats revelation rhos ria rial ridder
riden rift ripper ritual rogers rois rory rose routh runes ryder sabine sable sacristan sailor salix salma sals
salter sanctuary sanctum sapper savior screak scrip seamstress searing seary seedling sentinel serf sergeant
sergeants settler shade shadowy shafts shaitan shaman shamans shambling shaw shelly shore shrouded shuck sibyl sile
silencer sima sissy skal skirr sloan slone smasher smirky smithy smyth snap solas sorcerer sorcerers soul spicket
spindler spire stableboy stannary stap stark stein steward stillman stokes stonemason strait strangler strider
sulphine superior supply swift sylvan sylvans symbol tabor taes taise tait tambor tangi tara tear templar thane
thanes thatcher thegn theurgist theurgists thieves thir thousand thrain throne throttler tibia timothy tine tingler
toby tole toller tombs topi tora torrent tory touched tradesman traitor trapper tribulation trill troll trolls
tunnel tusker tweedle twilight ulla umbral unbegotten uncreation undercroft underground underneath underviewer
unfortunate unkempt unnatural unrest unrestful unsee unsleeping unused ursine vasa veil vein vera viewer vigilant
viking vikings villages ville viola vitality vortex vortices wade walker warrens weathers weaver welsh welss whit
widower willie withe wizard wizards woodsman workroom workshop wraith wrath wretch wyne wynn""".split())
# class names that are also ordinary words: written with a capital only in a line that is about that class
PLAIN_CLASSES = {"Healer", "Hero", "Champion", "Savage", "Scout", "Ranger", "Hunter", "Warden", "Warrior"}
SHORT_OK = set("""a i an as at be by do go he if in is it me my no of on or so to up us we am ah oh eh ha ho ay aye yes
the and but for not you all any can had her was one our out day get has him his how its let may new now old see two way who
did yet too use say she off own why try ask big end far few got lot man men put ran run saw set sit six ten top war win won
are ago air arm art axe bad bag bar bat bit bow box boy bug cat cow cry cut den die dig dog dry due ear eat egg elf era eve
eye fan fat fed fee fey fit fix fly foe fog fun fur gap god gut hag hat hay hex hid hit hot hug hut ice ink inn jar jaw joy
key kin kit lad law lay led leg lie lip log low mad map mud nap net nor nut oak oar odd oil ore owe owl pay pet pie pig pin
pit pot pup rat raw red rez rib rid rig rim rip rob rod rot row rub rug rum rye sad sea sky sly son sow spy sum sun tap tax
tea tie tin tip toe ton tow toy tug urn van vow wax web wed wet wit woe yew add age aid aim ale apt arc ash bay bed bee beg
bet bid bog bud bun cap cub cup dew dim din dip dye ebb eel elk elm ewe fen fig fin fir foe gem gin hem hen hip hop hum ire
ivy jab jig jog jot keg kid lap lid lit lop mat mix mob mow mug nag nod oat opt pad pan paw pea peg pen per ply pod pry rag
ram rap ray sap sip sir sob sod tab tag tan tar thy tub vat vie wag wig yak yap yon zip ad ax ox vs AE est mez its'""".split())
CAPITAL_ALWAYS = ["Albion", "Midgard", "Hibernia", "Camelot", "Jordheim", "Tir na Nog", "Darkness Falls", "Shrouded Isles",
                  "Avalon", "Odin", "Thor", "Loki", "Freya", "Freyja", "Hel", "Bragi", "Tyr", "Heimdall", "Asgard", "Bifrost",
                  "Yggdrasil", "Ragnarok", "Valhalla", "Arawn", "Annwn", "Dagda", "Lugh", "Morrigan", "Danu", "Nimue",
                  "Merlin", "Arthur", "Excalibur", "Lancelot", "Guinevere", "Mordred", "Vortigern", "Uther", "Bedivere",
                  "Briton", "Britons", "Saracen", "Saracens", "Avalonian", "Avalonians", "Highlander", "Highlanders",
                  "Inconnu", "Norseman", "Norsemen", "Troll", "Trolls", "Dwarf", "Dwarves", "Kobold", "Kobolds", "Valkyn",
                  "Celt", "Celts", "Firbolg", "Firbolgs", "Elf", "Elves", "Lurikeen", "Lurikeens", "Sylvan", "Sylvans",
                  "Albs", "Mids", "Hibs", "Albion's", "Midgard's", "Hibernia's", "Thidranki", "Murdaigean", "Caledonia",
                  "Abermenai", "Roman", "Romans", "Rome", "Christmas", "Samhain", "Beltane", "Tuatha", "Fomorian", "Fomorians",
                  "Siabra", "Drakoran", "Tomte", "Morvalt", "Hy Brasil", "Aegir", "Aegirhamn", "Domnann", "Caer Gothwaite",
                  "Wearyall", "Gothwaite", "PBAE", "Viking", "Vikings", "Norse", "Hibernian", "Hibernians", "Greek", "Saxon",
                  "Saxons", "Welsh", "Irish", "Pict", "Picts", "Legion", "Behemoth", "Stungard", "Bodgar", "Gaea", "Nuada",
                  "Mag Mell", "Mag Tuired", "Realm Exchange", "Exchange", "Black Mountains", "Iarnwood", "Juggernaut",
                  "Mjollnir", "Heimdall", "Fenris", "Skadi", "Ymir", "Modi", "Hrungnir", "Balor", "Badb", "Beltaine"]


class Voice:
    """Lower-case chat to the library's voice. rewrite() returns (text, None) or (None, why it was left out)."""

    def __init__(self, names, full_names, words, library_words, never_names, library_lower, persons):
        """names: proper names as they are written (a one-word name here is trusted: a zone, a keep, a lore entry,
        a capital the library writes); words: single words that may be names; library_words: every word the library
        uses; never_names: words seen written in lower case; library_lower: those the library writes in lower case."""
        self.library_words = library_words
        self.proper = {}
        sure = [c for c in ALL_CLASSES if c not in PLAIN_CLASSES]
        for name in list(CAPITAL_ALWAYS) + sure + [c + "s" for c in sure] + ["Mercenaries", "Eldritches"]:
            self.proper[name.lower()] = name
        for name in sorted(names):
            name = name.strip()
            if " " in name and len(name) >= 6 and name[0].isupper() and re.fullmatch(r"[A-Za-z' -]+", name):
                self.proper.setdefault(name.lower(), name)
            for word in re.findall(r"[A-Z][a-z']+", name):
                low = word.lower()
                bare = low[:-2] if low.endswith("'s") else low
                if len(low) >= 4 and bare not in library_lower and bare not in NOT_NAMES and bare not in SHORT_OK \
                        and bare.capitalize() not in PLAIN_CLASSES and bare.rstrip("s").capitalize() not in PLAIN_CLASSES:
                    self.proper.setdefault(low, word)
        for name in sorted(full_names):
            if " " in name and len(name) >= 6 and re.fullmatch(r"[A-Za-z' -]+", name):
                self.proper.setdefault(name.lower(), name)
        for word in sorted(words):
            low = word.lower()
            bare = low[:-2] if low.endswith("'s") else low
            if bare in never_names or bare in NOT_NAMES or bare.capitalize() in PLAIN_CLASSES or bare.rstrip("s").capitalize() in PLAIN_CLASSES:
                continue
            if len(low) >= 4 and re.fullmatch(r"[A-Z][a-z']+", word):
                self.proper.setdefault(low, word)
        # single names of persons and gods (not peoples, classes or places): these own things ("Loki's")
        self.persons = set(persons) | {"Odin", "Thor", "Loki", "Heimdall", "Merlin", "Arthur", "Nuada", "Lugh", "Arawn",
                                       "Bragi", "Tyr", "Midgard", "Albion", "Hibernia", "Nisse", "Legion"}
        # longest first, so "Odin's Gate" wins over "Odin"
        keys = sorted(self.proper, key=len, reverse=True)
        self.proper_re = re.compile(r"(?<![A-Za-z'])(" + "|".join(re.escape(k) for k in keys) + r")(?![A-Za-z])", re.I)
        self.phrase_res = [(re.compile(r"(?<![A-Za-z0-9'{])" + pattern + r"(?![A-Za-z0-9'}])", re.I), text) for pattern, text in PHRASES]
        self.class_re = re.compile(r"(?<![A-Za-z'{])(" + "|".join(sorted(CLASS_SHORT, key=len, reverse=True)) + r")(?!'[a-rt-z]|[A-Za-z}])", re.I)
        self.contraction_re = re.compile(r"(?<![A-Za-z'{])(" + "|".join(re.escape(k) for k in sorted(CONTRACTIONS, key=len, reverse=True)) +
                                         r")(?![A-Za-z'}])", re.I)

    def name(self, match):
        written = self.proper[match.group(1).lower()]
        # "the Otherworld" in the middle of a sentence; the start of one gets its capital later
        return "the " + written[4:] if written.startswith("The ") else written

    def owner(self, match):
        name = self.proper.get(match.group(1))
        return name + "'s" if name and name in self.persons else match.group(0)

    def rewrite(self, text, allowed_slots, classes=(), careful=False, subjects=()):
        """careful: the text was written with its apostrophes (the lore bible), so a bare "its" is the possessive."""
        t = text.strip()
        if re.search(r"[*<>@#_=+\\|~^\[\]\"]|:\)|:\(|;\)|:p\b|\bxd\b|\$#", t, re.I):
            return None, "emotes, symbols or quotes"
        if re.search(r"[A-Z]{4,}", re.sub(r"\{[a-z_]+\}", "", t)):
            return None, "shouting"
        if "/" in t or "%" in t:
            return None, "a command or a symbol"
        if any(ord(ch) > 126 for ch in t):
            return None, "not plain ASCII"
        slots = [slot for slot in re.findall(r"\{([a-z_]+)\}", t) if slot != "q"]
        for slot in slots:
            if slot not in SLOT or SLOT[slot] not in allowed_slots:
                return None, "a slot the library cannot fill here ({%s})" % slot
        t = re.sub(r"\{q\},? ?", "", t)      # Living Server's optional opener ("hey", "so"): left off
        t = re.sub(r"\{([a-z_]+)\}", lambda m: "{" + SLOT[m.group(1)] + "}", t)
        t = EMPTY.sub("", t)
        t = re.sub(r"\.{2,}", ".", t)
        t = re.sub(r"\s+", " ", t).strip(" ,")
        if not t:
            return None, "nothing left to say"
        low = t.lower()
        if OUTSIDE.search(re.sub(r"\{[a-z]+\}", " ", low)):
            return None, "talk about the world outside the game"
        if REGISTER.search(low):
            return None, "a way of talking the library does not use"
        t = low
        # "a mid" is a Midgard fighter; "mid fight" is not
        t = re.sub(r"\b(a|the|any|every|no|some|most|each|proper|real|true|sane|young|those|these|small|tight|big|of|for|one) mid\b"
                   r"(?! (?:fight|pull|cast|swing|level|levels|air|sentence|teens|twenties|thirties|forties|way|range))", r"\1 Mid", t)
        for regex, replacement in self.phrase_res:
            t = regex.sub(replacement, t)
        t = self.class_re.sub(lambda m: CLASS_SHORT[m.group(1).lower()], t)
        # typed without its apostrophe, "its" is nearly always "it is"; the possessive is kept before what a thing owns
        if careful:
            t = re.sub(r"\bits (?=(?:a|an|the|not|just|only|always|never|still|so|too|all|been|going|like|that|why|how|"
                       r"where|what|here|there|worth|hard|easy|true|over|done|warm|cold|dead|gone|empty|bad|good|"
                       r"big|fine|safe|said|called|in|on|at|up)\b)", "it is ", t)
        else:
            t = re.sub(r"(?<!on )(?<!of )(?<!in )(?<!with )(?<!for )(?<!by )(?<!from )\bits (?!(?:%s)\b)" % OWNED, "it is ", t)
        t = re.sub(r"\bid (been|seen|never|already|just|heard|forgotten)\b", r"I had \1", t)
        t = re.sub(r"(^|[,.!?] )lets (?=[a-z])", r"\1let us ", t)
        t = re.sub(r"\b(what|who|he|she|it|that|there|where|how|everyone|someone|nobody)'?s( \w+)? (been|got)\b", r"\1 has\2 \3", t)
        t = self.contraction_re.sub(lambda m: CONTRACTIONS[m.group(1).lower()], t)
        # "isnt it" opened out is "is it not"
        t = re.sub(r"(^|[,.!?] )(is|are|was|were|do|does|did|would|could|should|has|have|will) not (it|he|she|they|we|you|that|this|there)\b", r"\1\2 \3 not", t)
        t = re.sub(r"(?<![A-Za-z'{])i(?![A-Za-z'}])", "I", t)
        t = self.proper_re.sub(self.name, t)
        # a monster whose name is an ordinary word (Scuttle, Pester) is named where the line is plainly about it
        for name in subjects:
            t = re.sub(r"(^|[.!?] |\b(?:heard|said|is|say|says|that|think|call|calls|called|ask|asked|named) )%s(?='s|s? (?:is|was|went|got|has|had|"
                       r"the|never|only|still|keeps|stole|wants|will|would|does|did)\b)" % name.lower(), lambda m: m.group(1) + name, t)
        t = re.sub(r"\b(a|an|the|his|her|old|demon|dwarven) ([a-z]{3,}(?<!ie)(?<!ve)(?<![vs]))s ((?:first|second|third|last|own|old) )?"
                   r"(son|sons|daughter|daughters|map|hoard|key|heart|power|shadow|forge|sisters?|brothers?|horn|name|blood|"
                   r"hand|crown|seat|throne|grave|ghost|wife|father|mother)\b", r"\1 \2's \3\4", t)
        for name in classes:
            if name in PLAIN_CLASSES:
                t = re.sub(r"(?<![A-Za-z'])%s(s?)(?![A-Za-z])" % name.lower(), name + r"\1", t)
        # numbers a speaker would say as words
        t = re.sub(r"(?<![\w{])([1-9])(?![\w}])", lambda m: ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine"][int(m.group(1)) - 1], t)
        # a name typed without its apostrophe: "lokis first company"
        t = re.sub(r"(?<![A-Za-z'])([a-z]{4,})s(?= [a-z])", self.owner, t)
        t = re.sub(r"([!?])[!?]+", r"\1", t)
        t = re.sub(r"\s+([,.!?])", r"\1", t)
        t = re.sub(r",(?=[^\s])", ", ", t)
        t = re.sub(r"\s+", " ", t).strip(" ,")
        if not t:
            return None, "nothing left to say"
        if t[-1] not in ".!?":
            t += "."
        # a capital after every sentence end and at the start (a placeholder starts with its own value)
        t = re.sub(r"(^|[.!?] )([a-z])", lambda m: m.group(1) + m.group(2).upper(), t)
        t = re.sub(r"^(All right|Yes|Yep|Nah) (?=[a-z{I])", r"\1, ", t)
        # a question that was typed without its mark
        if t.endswith(".") and len(re.findall(r"[.!?]", t)) == 1 and QUESTION.match(t):
            t = t[:-1] + "?"
        t = re.sub(r"\s{2,}", " ", t)
        words = re.sub(r"\{[a-z]+\}", " ", t)
        for word in re.findall(r"[A-Za-z']+", words):
            bare = word.strip("'")
            if len(bare) <= 3 and bare.lower() not in SHORT_OK and bare.lower() not in self.library_words and bare not in self.proper.values():
                return None, "chat shorthand the rules do not know"
            if re.search(r"(.)\1\1", bare):
                return None, "stretched words"
        if re.search(r"\d[a-z]|[a-z]\d", words, re.I):
            return None, "chat shorthand the rules do not know"
        if not rules.MIN_LEN <= len(t) <= rules.MAX_LEN:
            return None, "too long or too short"
        errors = []
        rules.check_text(t, [], errors)
        errors = [e for e in errors if "never filled" not in e]
        if errors:
            return None, "refused by the library's rules: " + re.sub(r'"[^"]*"', "", errors[0]).strip()
        return t, None


# ---------------------------------------------------------------- reading Living Server

CHOICE = re.compile(r"\[([^\[\]]*\|[^\[\]]*)\]")


def say(template, choose):
    """One way to say a Living Server line with "[a|b]" parts (the same joining as its build_corpus.py)."""
    pieces, out = CHOICE.split(template), []
    for index, piece in enumerate(pieces):
        out.append(choose(index // 2, piece.split("|")) if index % 2 else piece)
    return re.sub(r"\s+", " ", "".join(out)).strip().replace(" ,", ",").replace(" ?", "?").replace(" !", "!")


def plain(text):
    return re.sub(r"[^a-z0-9{}]+", " ", text.lower()).strip()


def first_choices(folder):
    """Every way a template can be said -> the way that takes the first choice of each part.

    The writer's first choices of a line agree with each other; a later mix of them often does not ("a firbolg are
    gentle"), and the built corpus keeps a sample of the mixes, so the first-choice wording is taken from the sources."""
    import itertools
    firsts = {}
    for path in sorted(glob.glob(os.path.join(folder, "*.lines"))):
        for raw in open(path, encoding="utf-8"):
            text = raw.strip()
            if not text or text[0] in "#@" or "[" not in text:
                continue
            groups = [m.group(1).split("|") for m in CHOICE.finditer(text)]
            total = 1
            for group in groups:
                total *= len(group)
            if not groups or total > 50000:
                continue
            first = say(text, lambda index, choices: choices[0])
            for combo in itertools.product(*groups):
                firsts.setdefault(plain(say(text, lambda index, choices: combo[index])), first)
    return firsts


def variants(lines, firsts):
    """One Living Server family is one written line with choices in it: its first-choice wording, once."""
    families = collections.OrderedDict()
    for line in lines:
        families.setdefault(line.get("family") or line["id"], []).append(line)
    for family in families.values():
        line = dict(family[0])
        wording = firsts.get(plain(line["text"]))
        if wording is None and len(family) > 1:
            line["_mixed"] = True     # a line with choices whose source was not found: its wording cannot be trusted
        elif wording:
            line["text"] = wording
        yield line, True


def place_line(line, places):
    """(situations, filters) for one corpus line, or (None, ("held"|"dropped", reason))."""
    kind, topic = line["kind"], line.get("topic") or ""
    f = {}
    realm = REALM.get(line.get("realm") or "")
    if realm:
        f["realm"] = realm
    levels = line.get("levels") or [1, 50]
    lo, hi = max(1, levels[0]), min(50, levels[1])
    reply = False
    if kind in HELD_KINDS:
        return None, ("held", HELD_KINDS[kind])
    if kind == "ambient":
        if topic in HELD_TOPICS:
            return None, ("held", HELD_TOPICS[topic])
        if topic in DROP_TOPICS:
            return None, ("dropped", DROP_TOPICS[topic])
        situation = AMBIENT_TOPIC.get(topic)
        if topic in TIME_TOPIC:
            situation = "idle"
            f.setdefault("only", {})["time"] = [TIME_TOPIC[topic]]
    elif kind == "guild":
        situation = GUILD_TOPIC.get(topic)
        f["channel"] = "guild"
    elif kind == "rvr":
        situation, forced = RVR_TOPIC.get(topic, (None, None))
        if forced:
            f["realm"] = forced
    elif kind == "lfg":
        situation = "lfg" if topic in ("lfg_self", "lfm", "lfg") else None
        if topic == "join":
            situation, reply = "lfg", True
        if situation is None:
            return None, ("held", "bots that lead a group")
    elif kind == "trade":
        situation = "crafting" if topic == "crafting" else "trading"
    elif kind == "group":
        situation = "group_hunt" if topic == "group" else None
        if situation is None:
            return None, ("held", "bots that lead a group")
    elif kind == "camp":
        situation = "hunting"
    elif kind == "closing":
        situation = "chat_close"
    elif kind == "ding":
        situation = "levelup"
    elif kind == "grats":
        situation, reply = "levelup", True
        if topic == "fifty":
            lo, hi = 50, 50
    elif kind in ("death", "corpse", "sickness"):
        situation = "death"
    elif kind == "rez_thanks":
        situation = "resurrected"
    elif kind == "thanks":
        situation = "reply_thanks"
    elif kind == "news":
        situation = NEWS_TOPIC.get(topic)
        if situation is None:
            return None, ("held", "news the library has no situation for (relics returned, Darkness Falls, sightings)")
    elif kind == "reply":
        answers = line.get("answers") or []
        intent = line.get("intent") or ""
        if intent in ("botcheck", "away", "back", "rez"):
            return None, ("held", "answers the library has no situation for (are you real, away, back, a resurrection)")
        if intent in REPLY_INTENT:
            situation = REPLY_INTENT[intent]
        elif answers and answers[0] in REPLY_ANSWERS:
            situation, reply = REPLY_ANSWERS[answers[0]], True
        elif answers and answers[0] in REPLY_TO_PLAYER:
            situation = REPLY_TO_PLAYER[answers[0]]
        elif answers and any(a in THREADS for a in answers):
            # an answer to what another speaker's opening line asked for; its situations are those of the openers
            situation, reply = "idle", True
            f.setdefault("only", {})["thread"] = sorted(a for a in answers if a in THREADS)
        elif answers:
            return None, ("held", "answers that follow the thread of a conversation")
        else:
            return None, ("held", "answers that follow the thread of a conversation")
    else:
        situation = None
    if situation is None:
        return None, ("held", "no situation in the library for %s/%s" % (kind, topic or "-"))

    class_types = []
    for need in line.get("needs") or []:
        if need in NEED_HELD:
            return None, ("held", NEED_HELD[need])
        if need.startswith("holiday_"):
            return None, ("held", "a calendar (holiday talk)")
        if need in NEED_TIME:
            f.setdefault("only", {})["time"] = [NEED_TIME[need]]
            continue
        if need.startswith("at:"):
            zones = places.get(need[3:]) if not need[3:].startswith("_") else None
            if not zones:
                return None, ("dropped", "a place that is not in the True 1.65 world")
            f.setdefault("only", {})["zone"] = zones
            continue
        if need in NEED_IGNORE:
            continue
        if need in NEED_SITUATION:
            if not rules.is_reply(situation) and situation not in ("chat_close",):
                situation = NEED_SITUATION[need]
            continue
        if need in NEED_CLASSTYPE:
            class_types.append(NEED_CLASSTYPE[need])
            continue
        if need in NEED_LEVEL:
            lo, hi = max(lo, NEED_LEVEL[need][0]), min(hi, NEED_LEVEL[need][1])
            continue
        return None, ("held", "a condition the library cannot test (%s)" % need.lstrip("!"))
    if lo > hi:
        return None, ("dropped", "no level it could be said at")
    if (lo, hi) != (1, 50):
        f["level"] = [lo, hi]
    if class_types:
        f["classType"] = sorted(set(class_types))
    classes = [c for c in (line.get("class") or []) if c in ALL_CLASSES]
    if line.get("class") and not classes:
        return None, ("dropped", "a class that is not in patch 1.65")
    if classes:
        f.setdefault("only", {})["class"] = classes
    if line.get("about") and "zone" not in f.get("only", {}):
        zones = sorted({zone for regex, key in places.get("_named", []) if key in places and regex.search(line["text"]) for zone in places[key]})
        if not zones:
            return None, ("dropped", "a place that is not in the True 1.65 world")
        if "?" in line["text"]:
            return None, ("dropped", "a question about going to a place (these lines are said in the place itself)")
        f.setdefault("only", {})["zone"] = zones
        # said in the place, whatever the speaker is doing there; "idle" is the situation every speaker falls back to
        if situation not in ("dungeon", "hunting", "travel"):
            situation = "idle"
    if f.get("channel") == "guild" and (rules.is_reply(situation) or situation == "chat_close"):
        f.pop("channel")
    if reply and not rules.is_kindless(situation):
        f["kind"] = "reply"
    elif line.get("invites") in THREADS and not rules.is_kindless(situation):
        f["invites"] = line["invites"]
    if "thread" in f.get("only", {}) and f.get("kind") != "reply":
        return None, ("held", "answers that follow the thread of a conversation")
    return [situation], f


CONTENT = re.compile(r"[a-z']{4,}")
STOP = set("""that this with from they them their there here have were been will would could should about into over under than
then when what where which while because through after before again still just only also even very much more most some such
your yours mine ours each every both either neither other another those these whose been being does done doing gone goes
came come comes went take took takes make made makes like know knew known heard hear says said told tell tells think thought
swear swears claim claims story stories rumour rumours rumor rumors someone somebody anyone nobody never ever always once
back down away well good great little long old young first last next same sure maybe really quite place there's it's keep
kept look looks looked want wants wanted need needs needed give gave given find found finds call called calls name named
around near upon across until since though although without within they're you're that's what's dont cant wont isnt""".split())


def grounded(line, sourced, invented):
    """A lore line is kept when its telling words come from the entry's sourced facts and none only from invented ones."""
    words = {w.strip("'") for w in CONTENT.findall(re.sub(r"\[|\]|\|", " ", line.lower()))} - STOP
    words = {w[:-1] if w.endswith("s") and len(w) > 5 else w for w in words}
    if not words:
        return False
    if words & invented:
        return False
    return len(words & sourced) >= max(2, (len(words) + 1) // 2)


def stems(text):
    words = {w.strip("'") for w in CONTENT.findall(text.lower())} - STOP
    return {w[:-1] if w.endswith("s") and len(w) > 5 else w for w in words}


def prose(text, capitals, lower, every):
    """Sort the words of properly written text: written with a capital inside a sentence, or written in lower case."""
    for sentence in re.split(r"(?<=[.!?:;(]) +", text):
        found = re.findall(r"[A-Za-z']+", sentence)
        for index, word in enumerate(found):
            if every is not None:
                every.add(word.lower())
            if word.islower():
                lower.add(word)
            elif index > 0 and word[0].isupper():
                capitals.add(word)


def expand(template):
    """'[a|b] x [c|]' -> the first choice of each (one telling per written line)."""
    return re.sub(r"\[([^\]|]*)(?:\|[^\]]*)*\]", lambda m: m.group(1), template)


# ---------------------------------------------------------------- main

FILES = {  # file stem -> (description, which placed lines go there)
    "ls_everyday": "Everyday talk from Living Server, rewritten: idling, towns, the road, parting words.",
    "ls_classes": "Class talk from Living Server, rewritten: each line is said only by the class it is about.",
    "ls_realms": "Realm pride and realm talk from Living Server, rewritten: every line is said in its own realm only.",
    "ls_hunting": "Hunting, dungeons, dying and levelling from Living Server, rewritten.",
    "ls_groups": "Looking for a group and life in a hunting group from Living Server, rewritten.",
    "ls_guild": "Guild channel talk from Living Server, rewritten.",
    "ls_frontier": "Frontier patrols, sieges and battlegrounds from Living Server, rewritten.",
    "ls_trade_craft": "Trading and crafting talk from Living Server, rewritten.",
    "ls_replies": "Answers to a player from Living Server, rewritten.",
    "ls_threads": "Answers to what another speaker's line asked for (a boast, a joke, a complaint, a question), from Living Server, rewritten.",
    "ls_places": "Talk about named places from Living Server, rewritten: said in that place, and to a player who asks about it.",
    "ls_lore_albion": "Albion's lore, myths and places as its people tell them, from Living Server, rewritten.",
    "ls_lore_midgard": "Midgard's lore, myths and places as its people tell them, from Living Server, rewritten.",
    "ls_lore_hibernia": "Hibernia's lore, myths and places as its people tell them, from Living Server, rewritten.",
    "ls_lore_shared": "Lore every realm tells (the frontier, Darkness Falls, the old world), from Living Server, rewritten.",
    "ls_lore_places": "Lore of named places and monsters from Living Server's lore bible, rewritten: sourced lines only, each said only in its own zone.",
}


def file_for(line, situations, f, lore_place=False):
    if lore_place:
        return "ls_lore_places"
    kind, topic = line["kind"], line.get("topic") or ""
    s = situations[0]
    if "thread" in f.get("only", {}):
        return "ls_threads"
    if line.get("about") and "zone" in f.get("only", {}):
        return "ls_places"
    if kind == "ambient" and topic == "lore":
        return {"Albion": "ls_lore_albion", "Midgard": "ls_lore_midgard", "Hibernia": "ls_lore_hibernia"}.get(f.get("realm"), "ls_lore_shared")
    if rules.is_reply(s):
        return "ls_replies"
    if f.get("channel") == "guild":
        return "ls_guild"
    if "class" in f.get("only", {}):
        return "ls_classes"
    if s in ("rvr_patrol", "rvr_siege", "rvr_defend", "rvr_relic", "rvr_victory", "rvr_defeat", "battleground"):
        return "ls_frontier"
    if kind == "ambient" and topic == "realm_pride":
        return "ls_realms"
    if s in ("hunting", "dungeon", "death", "resurrected", "levelup"):
        return "ls_hunting"
    if s in ("lfg", "group_hunt"):
        return "ls_groups"
    if s in ("trading", "crafting"):
        return "ls_trade_craft"
    if s in ("raid_forming", "raid_victory", "raid_ended"):
        return "ls_hunting"
    return "ls_everyday"


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--living-server", required=True, help="a checkout of chris4aker/OfflineDAoC-LivingServer")
    parser.add_argument("--world-db", required=True, help="the True 1.65 world database (database/unpack_db.py)")
    parser.add_argument("--out", default=os.path.normpath(os.path.join(HERE, "..", "banter165")))
    parser.add_argument("--share", type=float, default=0.5, help="share of a situation's airtime the new lines take together")
    parser.add_argument("--only-share", type=float, default=0.25,
                        help="airtime of all the lines of one zone or one class, as a share of the library's own lines there")
    parser.add_argument("--ledger", default=None)
    parser.add_argument("--samples", type=int, default=0, help="print this many random rewritten lines per file")
    args = parser.parse_args()

    data = os.path.join(args.living_server, "source", "server", "GameServer", "bots", "autonomous", "data")
    corpus = json.load(open(os.path.join(data, "chat", "corpus.json"), encoding="utf-8"))
    place_data = json.load(open(os.path.join(data, "chat", "places.json"), encoding="utf-8"))["places"]

    world = sqlite3.connect("file:" + args.world_db + "?mode=ro", uri=True)
    zone_names = {name for (name,) in world.execute("select Name from Zones") if name}
    zones_of_region = collections.defaultdict(list)
    zone_by_id = {}
    for zone_id, region, name in world.execute("select ZoneID, RegionID, Name from Zones"):
        zones_of_region[region].append(name)
        zone_by_id[zone_id] = name
    monsters = collections.defaultdict(set)   # lower-case monster name -> zone names it lives in
    region_of_zone = {}
    for zone_id, region, name, ox, oy, w, h in world.execute("select ZoneID, RegionID, Name, OffsetX, OffsetY, Width, Height from Zones"):
        region_of_zone.setdefault(region, []).append((name, ox * 8192, oy * 8192, w * 8192, h * 8192))
    for name, region, x, y in world.execute("select Name, Region, X, Y from Mob"):
        if not name:
            continue
        for zone, ox, oy, w, h in region_of_zone.get(region, []):
            if ox <= x < ox + w and oy <= y < oy + h:
                monsters[name.lower()].add(zone)
                break
    keeps = [name for (name,) in world.execute("select Name from Keep") if name]

    # the library's own lines: its vocabulary, its duplicates, its weight per situation
    library_words, never_names, library_lower, seen, own_weight = set(), set(), set(), {}, collections.Counter()
    names, full_names, words, capitals = set(zone_names) | set(keeps), set(), set(), set()
    for path in sorted(glob.glob(os.path.join(args.out, "*.json"))):
        if os.path.basename(path).startswith(("ls_", "_")):
            continue
        content = json.load(open(path, encoding="utf-8"))
        defaults = content.get("defaults", {})
        for entry in content["lines"]:
            seen[rules.normalize(entry["text"])] = os.path.basename(path)
            prose(re.sub(r"\{[a-z]+\}", " ", entry["text"]), capitals, library_lower, library_words)
            situations = entry.get("situation", defaults.get("situation"))
            for situation in ([situations] if isinstance(situations, str) else situations):
                own_weight[situation] += entry.get("weight", defaults.get("weight", 1))

    places = {}   # Living Server place key -> zone names in this world
    for place in place_data:
        zones = [z for z in place.get("zones", []) if z in zone_names]
        if zones:
            places[place["key"]] = zones
        for alias in place.get("aliases", []):
            places.setdefault("_named", []).append((re.compile(r"(?<![a-z'])" + re.escape(alias.lower()) + r"(?![a-z])"), place["key"]))

    lore_dir = os.path.join(data, "lore")
    lore_entries = []
    for sub in ("locations", "bosses"):
        for path in sorted(glob.glob(os.path.join(lore_dir, sub, "*.json"))):
            entry = json.load(open(path, encoding="utf-8"))
            entry["_kind"] = sub
            lore_entries.append(entry)
            names.add(entry["name"])
            names.update(entry.get("npcNames", []))
            # the lore bible's facts are written in full prose: a word they only ever write with a capital is a name
            for fact in entry.get("facts", []):
                prose(fact["text"], words, never_names, None)
    names.update(capitals)
    never_names |= library_lower
    # a name the lore bible takes from its own world's data must be in this world too; a name from myth need not be
    from_world, from_myth = set(), set()
    for entry in lore_entries:
        for fact in entry.get("facts", []):
            caught = set()
            prose(fact["text"], caught, set(), None)
            (from_world if fact.get("provenance") == "game" else from_myth).update(w.lower() for w in caught)
    in_world = {w.lower() for (name,) in world.execute("select distinct Name from Mob") if name for w in re.findall(r"[A-Za-z']+", name)}
    in_world |= {w.lower() for name in list(zone_names) + keeps + CAPITAL_ALWAYS + ALL_CLASSES + list(capitals) for w in re.findall(r"[A-Za-z']+", name)}
    for (name,) in world.execute("select distinct Name from NPCTemplate"):
        in_world.update(w.lower() for w in re.findall(r"[A-Za-z']+", name or ""))
    unknown = {w for w in from_world - from_myth - in_world - NOT_NAMES - never_names if len(w) >= 4}

    def strangers(text):
        return any(word in unknown for word in re.findall(r"[a-z']+", text.lower()))

    for (name,) in world.execute("select distinct Name from Mob"):
        if name and name[0].isupper():
            words.update(w for w in re.findall(r"[A-Za-z']+", name) if w[0].isupper())
            if " " in name:
                full_names.add(name)

    # a monster of the lore bible that is not in this world is not talked about either
    gone = set()
    for entry in lore_entries:
        if entry["_kind"] == "bosses" and not any(n.lower() in monsters for n in entry.get("npcNames", [entry["name"]])):
            gone.add(entry["name"].lower())
            first = entry["name"].split()[0].lower()
            if len(first) >= 5 and first not in NOT_NAMES and first not in never_names:
                gone.add(first)
    gone_re = re.compile(r"(?<![a-z'])(" + "|".join(re.escape(g) for g in sorted(gone, key=len, reverse=True)) + r")(?![a-z])", re.I)

    persons = {e["name"] for e in lore_entries if e["_kind"] == "bosses" and re.fullmatch(r"[A-Z][a-z]+", e["name"])}
    voice = Voice(names, full_names, words, library_words, never_names, library_lower, persons)
    waiting = []
    ledger = collections.defaultdict(collections.Counter)   # group -> outcome -> count
    reasons = collections.defaultdict(collections.Counter)  # (group, "held"|"dropped") -> reason -> count
    out = collections.defaultdict(list)

    def add(group, stem, text, situations, f):
        key = rules.normalize(text)
        if key in seen:
            ledger[group]["dropped"] += 1
            reasons[(group, "dropped")]["the library already says it"] += 1
            return
        seen[key] = stem
        entry = {"text": text, "situation": situations[0] if len(situations) == 1 else situations}
        for k in ("kind", "channel", "realm", "level", "classType", "only", "invites"):
            if k in f:
                entry[k] = f[k]
        errors = []
        if rules.check_line(entry, {}, errors) is None or errors:
            ledger[group]["dropped"] += 1
            reasons[(group, "dropped")]["refused by the library's rules: " + re.sub(r'"[^"]*"|\{[^}]*\}', "", errors[0]).strip()] += 1
            del seen[key]
            return
        out[stem].append(entry)
        ledger[group]["written"] += 1

    # ---- the chat corpus
    for line, first in variants(corpus["lines"], first_choices(os.path.join(data, "chat"))):
        group = "%s / %s" % (line["kind"], line.get("topic") or "-")
        if first:
            ledger[group]["lines"] += 1
        situations, f = place_line(line, places)
        if situations is None:
            if first:
                ledger[group][f[0]] += 1
                reasons[(group, f[0])][f[1]] += 1
            continue
        allowed = set(rules.ALWAYS) | set(rules.SITUATIONS[situations[0]])
        lore = line.get("topic") == "lore"
        if lore and FIGHT.search(line["text"]):
            ledger[group]["held"] += 1
            reasons[(group, "held")]["what a monster does in a fight, its level or when it shows itself (it may differ in this world)"] += 1
            continue
        text, why = (None, "a monster that is not in the True 1.65 world") if gone_re.search(line["text"]) else \
            (None, "someone or something that is not in the True 1.65 world") if lore and strangers(line["text"]) else \
            (None, "a line of mixed choices whose source wording was not found") if line.get("_mixed") else \
            (None, "talk about the world outside the game") if line["kind"] == "guild" and line.get("topic") == "social" \
            and not IN_WORLD.search(line["text"]) else voice.rewrite(line["text"], allowed, f.get("only", {}).get("class", ()))
        if text is None:
            if first:
                ledger[group]["dropped"] += 1
                reasons[(group, "dropped")][why] += 1
            continue
        if "thread" in f.get("only", {}):
            waiting.append((group, line, text, f))
            continue
        add(group, file_for(line, situations, f), text, situations, f)

    # an answer written for a thread is said in the situations where an opening line invites it
    invited = collections.defaultdict(set)
    for entries in out.values():
        for entry in entries:
            if "invites" in entry:
                invited[entry["invites"]].update([entry["situation"]] if isinstance(entry["situation"], str) else entry["situation"])
    for group, line, text, f in waiting:
        f["only"]["thread"] = [t for t in f["only"]["thread"] if invited.get(t)]
        situations = sorted({s for t in f["only"]["thread"] for s in invited[t]})
        if not situations:
            ledger[group]["held"] += 1
            reasons[(group, "held")]["answers that follow the thread of a conversation"] += 1
            continue
        add(group, "ls_threads", text, situations, f)

    ledger["scenes and debates / -"]["lines"] = len(corpus.get("scenes", []))
    ledger["scenes and debates / -"]["held"] = len(corpus.get("scenes", []))
    reasons[("scenes and debates / -", "held")]["conversations of many lines between a cast of bots"] = len(corpus.get("scenes", []))

    # ---- the lore bible
    FIGHT_WHY = "what a monster does in a fight, its level or when it shows itself (it may differ in this world)"
    by_id = {e["id"]: e for e in lore_entries}
    for entry in lore_entries:
        location = entry if entry["_kind"] == "locations" else by_id.get(entry.get("locationId"))
        zones = []
        if location is not None:
            zones = [zone_by_id.get(z) for z in location.get("zones", [])] or [n for r in location.get("regions", []) for n in zones_of_region.get(r, [])]
            zones = sorted({z for z in zones if z})
        realm = entry.get("realm")
        facts = entry.get("facts", []) + (location.get("facts", []) if location is not None and location is not entry else [])
        sourced = set().union(*[stems(x["text"]) for x in facts if x.get("provenance") != "original"] or [set()]) | stems(entry["name"])
        invented = set().union(*[stems(x["text"]) for x in facts if x.get("provenance") == "original"] or [set()]) - sourced
        here = True
        if entry["_kind"] == "bosses":
            lives = set()
            for name in entry.get("npcNames", [entry["name"]]):
                lives |= monsters.get(name.lower(), set())
            here = bool(lives)
            if lives:
                zones = sorted(lives)
        subjects = [e["name"] for e in lore_entries if e["_kind"] == "bosses" and re.fullmatch(r"[A-Z][a-z]+", e["name"])
                    and (e is entry or e.get("locationId") == entry["id"])]
        bot_lines = entry.get("botLines") or {}
        for part, written in bot_lines.items():
            group = "lore bible / %s %s" % ("place" if entry["_kind"] == "locations" else "monster", part)
            for template in written:
                ledger[group]["lines"] += 1
                if part in ("briefing", "aftermathWin", "aftermathLoss"):
                    ledger[group]["held"] += 1
                    reasons[(group, "held")]["raid talk that depends on the boss mechanics being the same here"] += 1
                    continue
                if part == "tell":
                    ledger[group]["held"] += 1
                    reasons[(group, "held")]["answers about a place that give levels and monster lists from Living Server's own world"] += 1
                    continue
                if not here or not zones:
                    ledger[group]["dropped"] += 1
                    reasons[(group, "dropped")]["the place or monster is not in the True 1.65 world"] += 1
                    continue
                if not grounded(template, sourced, invented):
                    ledger[group]["held"] += 1
                    reasons[(group, "held")]["lore that Living Server wrote itself (not from the game or a period source)"] += 1
                    continue
                f = {"only": {"zone": zones}}
                if realm in ("Albion", "Midgard", "Hibernia"):
                    f["realm"] = realm
                situations = ["dungeon", "hunting", "idle", "travel"]
                told = expand(template)
                if FIGHT.search(told) or NEWS.search(told):
                    ledger[group]["held"] += 1
                    reasons[(group, "held")][FIGHT_WHY if FIGHT.search(told) else "live rumours (a named monster is up, a keep is thin)"] += 1
                    continue
                text, why = (None, "a monster that is not in the True 1.65 world") if gone_re.search(told) else \
                    (None, "a line that does not read as a sentence") if re.match(r"(heard|someone said|word is) (beat|kill|go|ask) ", told) else \
                    (None, "someone or something that is not in the True 1.65 world") if strangers(told) else \
                    voice.rewrite(told, set(rules.ALWAYS), careful=True, subjects=subjects)
                if text is None:
                    ledger[group]["dropped"] += 1
                    reasons[(group, "dropped")][why] += 1
                    continue
                add(group, "ls_lore_places", text, situations, f)

    # ---- weights: the new lines of a situation take --share of its airtime together, so the library keeps its voice.
    # Lines said only in one zone or by one class are counted apart: all the lines of one zone (or class) together get
    # --only-share of what the library's own lines have there, so a zone rich in lore does not talk of nothing else.
    def situations_of(entry):
        return [entry["situation"]] if isinstance(entry["situation"], str) else entry["situation"]

    def groups_of(entry):
        return [(key, value) for key, values in sorted(entry["only"].items()) for value in values]

    new_count, only_count = collections.Counter(), collections.Counter()
    for entries in out.values():
        for entry in entries:
            for situation in situations_of(entry):
                if "only" in entry:
                    for group in groups_of(entry):
                        only_count[(situation, group)] += 1
                else:
                    new_count[situation] += 1
    for entries in out.values():
        for entry in entries:
            if "only" in entry:
                weight = min(args.only_share * own_weight[s] / only_count[(s, g)] if own_weight[s] else 1.0
                             for s in situations_of(entry) for g in groups_of(entry))
            else:
                weight = min((args.share / (1 - args.share)) * own_weight[s] / new_count[s] if own_weight[s] else 1.0
                             for s in situations_of(entry))
            weight = max(0.01, min(1.0, round(weight, 3)))
            if weight != 1.0:
                entry["weight"] = weight

    # ---- write
    for path in glob.glob(os.path.join(args.out, "ls_*.json")):
        os.remove(path)
    import random
    rng = random.Random(1065)
    for stem in sorted(out):
        body = {"category": stem, "description": FILES[stem], "lines": out[stem]}
        with open(os.path.join(args.out, stem + ".json"), "w", encoding="utf-8", newline="\n") as handle:
            handle.write("{\n  \"category\": %s,\n  \"description\": %s,\n  \"lines\": [\n" % (json.dumps(stem), json.dumps(FILES[stem])))
            handle.write(",\n".join("    " + json.dumps(entry, ensure_ascii=True) for entry in out[stem]))
            handle.write("\n  ]\n}\n")
        print("%s: %d lines" % (stem, len(out[stem])))
        for entry in rng.sample(out[stem], min(args.samples, len(out[stem]))):
            print("    ", json.dumps(entry))

    total = collections.Counter()
    for counts in ledger.values():
        total.update(counts)
    print("Living Server lines read: %d; written %d, held %d, left out %d" % (total["lines"], total["written"], total["held"], total["dropped"]))
    if args.ledger:
        write_ledger(args.ledger, ledger, reasons, out, total)
    return 0


def write_ledger(path, ledger, reasons, out, total):
    held = collections.Counter()
    dropped = collections.Counter()
    for (group, outcome), counter in reasons.items():
        (held if outcome == "held" else dropped).update(counter)
    lines = ["# Living Server lines in the banter library", "",
             "Written by `tools/import_living_server_banter.py`; the `ls_*.json` files beside this page are its output.",
             "Do not edit them by hand: change the tool or the source and run it again.", "",
             "Living Server's lines are lower-case 2003 chat. Here each one is given a situation and filters of this",
             "library, rewritten into its voice by rules, and checked by `validate_banter165.py`. A line that the rules",
             "refuse, that talks about the world outside the game, or that names something that is not in the True 1.65",
             "world is left out. A line that needs a system True 1.65 does not have is held: counted below, not written.", "",
             "The rewriting is automatic. Samples of every file were read, not every line, and none of these lines has",
             "been heard in a running game yet.", "",
             "| | Lines |", "|---|---|",
             "| Read from Living Server (the chat corpus, its scenes and the lore bible; one per written line) | %d |" % total["lines"],
             "| Written into the library | %d |" % total["written"],
             "| Held until the library has a way to say them | %d |" % total["held"],
             "| Left out | %d |" % total["dropped"], "", "## Files", "", "| File | Lines |", "|---|---|"]
    for stem in sorted(out):
        lines.append("| `%s.json` | %d |" % (stem, len(out[stem])))
    lines += ["", "## Held: what each waits for", "", "| Waiting for | Lines |", "|---|---|"]
    for reason, count in held.most_common():
        lines.append("| %s | %d |" % (reason[0].upper() + reason[1:], count))
    lines += ["", "## Left out: why", "", "| Reason | Lines |", "|---|---|"]
    for reason, count in dropped.most_common():
        lines.append("| %s | %d |" % (reason[0].upper() + reason[1:], count))
    lines += ["", "## By kind of line", "", "| Living Server kind / topic | Read | Written | Held | Left out |", "|---|---|---|---|---|"]
    for group in sorted(ledger, key=lambda g: (-ledger[g]["lines"], g)):
        c = ledger[group]
        lines.append("| %s | %d | %d | %d | %d |" % (group, c["lines"], c["written"], c["held"], c["dropped"]))
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    sys.exit(main())
