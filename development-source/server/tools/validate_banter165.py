"""Validate bot banter files (banter165/*.json) without dotnet (owner 2026-11-10).

Same rules as GameServer/bots/autonomous/BanterLibrary165.cs - keep the two in step.

Usage:
    python -I validate_banter165.py [folder] [--stats]
Default folder: ../banter165 next to this tools folder. Exit code 1 when any problem is found.
"""
import json
import os
import re
import sys

CHANNELS = ["realm", "guild", "say", "group"]
REALMS = ["Albion", "Midgard", "Hibernia"]
CLASS_TYPES = ["tank", "melee", "healer", "support", "caster", "pet", "stealth", "archer"]
KINDS = ["open", "reply"]
ALWAYS = ["bot", "realm", "enemyrealm", "level", "class", "zone"]
REPLY_EXTRA = ["guild", "player", "target", "destination"]
BG_EXTRA = ["guild", "bg"]
KEEP_EXTRA = ["guild", "keep"]

SITUATIONS = {
    "idle": ["guild", "target", "destination"],
    "town": ["guild", "item"],
    "travel": ["guild", "destination"],
    "hunting": ["guild", "target"],
    "dungeon": ["guild", "target"],
    "lfg": ["guild", "target"],
    "group_hunt": ["guild", "target"],
    "death": ["guild", "target"],
    "resurrected": ["guild"],
    "levelup": ["guild"],
    "trading": ["guild", "item"],
    "crafting": ["guild"],
    "raid_forming": ["guild", "raid"],
    "raid_march": ["guild", "raid"],
    "raid_boss": ["guild", "raid", "boss"],
    "raid_victory": ["guild", "raid"],
    "raid_ended": ["guild", "raid"],
    "rally_horn": ["guild", "raid", "player"],
    "rvr_patrol": ["guild"],
    "rvr_siege": KEEP_EXTRA,
    "rvr_defend": KEEP_EXTRA,
    "rvr_relic": KEEP_EXTRA,
    "rvr_victory": KEEP_EXTRA,
    "rvr_defeat": KEEP_EXTRA,
    "battleground": BG_EXTRA,
    "bg_abermenai": BG_EXTRA,
    "bg_thidranki": BG_EXTRA,
    "bg_murdaigean": BG_EXTRA,
    "bg_caledonia": BG_EXTRA,
    "guild_join": ["guild"],
    "chat_close": ["guild"],
}
for _key in ["greeting", "farewell", "thanks", "ding", "insult", "banter", "stop", "named", "offtopic",
             "lfg", "help", "where", "what", "rvr", "raid", "bg", "trade", "dungeon", "general"]:
    SITUATIONS["reply_" + _key] = REPLY_EXTRA

ALL_PLACEHOLDERS = set(ALWAYS) | {p for extra in SITUATIONS.values() for p in extra}

FORBIDDEN_WORDS = {w.lower() for w in """
bot bots gamebot gamebots npc npcs ai server script claude opendaoc emulator offline
noob noobs newb idiot idiots moron morons stfu retard retarded shit fuck fucking
bitch bastard ass fag atlantis toa catacombs cata labyrinth agramon molvik leirvik
wilton killaloe cathal mauler maulers vampiir bainshee heretic heretics warlock warlocks
valkyrie valkyries oceanus stygia volcanus aerus artifact artifacts ml cl mythirian
politics president election bitcoin crypto football netflix internet facebook reddit
discord twitch youtube phone
""".split()}
FORBIDDEN_PHRASES = ["champion level", "master level", "new frontiers", "darkness rising", "trials of", "/go", "real life", "irl"]

LINE_KEYS = ["text", "situation", "channel", "realm", "level", "classType", "kind", "weight"]
MIN_LEN, MAX_LEN = 4, 160
PLACEHOLDER = re.compile(r"\{([a-z]+)\}")
WORD = re.compile(r"[A-Za-z']+")


def is_reply(s):
    return s.startswith("reply_")


def is_kindless(s):
    return is_reply(s) or s in ("chat_close", "guild_join")


def normalize(text):
    out, space = [], False
    for c in text.lower():
        if c.isalnum() or c in "{}":
            if space and out:
                out.append(" ")
            out.append(c)
            space = False
        else:
            space = True
    return "".join(out)


def strings(value, name, errors):
    if value is None:
        return None
    if isinstance(value, str):
        return [value]
    if not isinstance(value, list) or not value or not all(isinstance(v, str) for v in value):
        errors.append(f'"{name}" must be a string or a non-empty array of strings')
        return None
    return value


def check_text(text, situations, errors):
    if len(text) < MIN_LEN or len(text) > MAX_LEN:
        errors.append(f"text must be {MIN_LEN}-{MAX_LEN} characters (has {len(text)})")
    if text != text.strip():
        errors.append("text has leading or trailing spaces")
    if "  " in text:
        errors.append("text has a double space")
    if any(ord(c) < 0x20 or ord(c) > 0x7E for c in text):
        errors.append("text must be plain printable ASCII (no curly quotes, dashes, accents or tabs)")
    if '"' in text:
        errors.append("text may not contain double quotes")
    tokens = list(dict.fromkeys(PLACEHOLDER.findall(text)))
    stripped = PLACEHOLDER.sub("", text)
    if "{" in stripped or "}" in stripped:
        errors.append("text has a stray { or } (placeholders are lower-case, like {zone})")
    for token in tokens:
        if token not in ALL_PLACEHOLDERS:
            errors.append(f"unknown placeholder {{{token}}}")
            continue
        if token in ALWAYS:
            continue
        for s in situations:
            if s in SITUATIONS and token not in SITUATIONS[s]:
                errors.append(f"placeholder {{{token}}} is never filled in situation {s}")
    words = PLACEHOLDER.sub(" ", text)
    for w in WORD.findall(words):
        if w.strip("'").lower() in FORBIDDEN_WORDS:
            errors.append(f'forbidden word "{w}"')
    for phrase in FORBIDDEN_PHRASES:
        if re.search(r"(^|[^a-z])" + re.escape(phrase) + r"s?($|[^a-z])", words, re.IGNORECASE):
            errors.append(f'forbidden phrase "{phrase}"')


def check_line(line, defaults, errors):
    """Returns (situations, channel_list) or None. Appends problems to errors."""
    if not isinstance(line, dict):
        errors.append("each line must be an object")
        return None
    for k in line:
        if k not in LINE_KEYS:
            errors.append(f'unknown key "{k}"')
    for k in defaults:
        if k not in LINE_KEYS:
            errors.append(f'unknown key "{k}" in defaults')

    def field(name):
        return line[name] if name in line else defaults.get(name)

    text = line.get("text")
    if not isinstance(text, str):
        errors.append('"text" must be a string')
        return None
    situations = strings(field("situation"), "situation", errors) or []
    if not situations:
        errors.append('"situation" is required')
    for s in situations:
        if s not in SITUATIONS:
            errors.append(f'unknown situation "{s}"')
    if any(is_reply(s) for s in situations) and not all(is_reply(s) for s in situations):
        errors.append("reply_* situations cannot be mixed with other situations on one line")
    if len(set(situations)) != len(situations):
        errors.append("a situation is listed twice")
    channels = strings(field("channel"), "channel", errors)
    for c in channels or []:
        if c not in CHANNELS:
            errors.append(f'unknown channel "{c}"')
    for r in strings(field("realm"), "realm", errors) or []:
        if r not in REALMS:
            errors.append(f'unknown realm "{r}" (Albion, Midgard or Hibernia)')
    for t in strings(field("classType"), "classType", errors) or []:
        if t not in CLASS_TYPES:
            errors.append(f'unknown classType "{t}"')
    level = field("level")
    if level is not None:
        ok = (isinstance(level, list) and len(level) == 2 and all(isinstance(v, int) and not isinstance(v, bool) for v in level)
              and 1 <= level[0] <= level[1] <= 50)
        if not ok:
            errors.append('"level" must be [min, max] with 1 <= min <= max <= 50')
    kind = field("kind")
    if kind is not None:
        if kind not in KINDS:
            errors.append('"kind" must be "open" or "reply"')
        elif any(is_kindless(s) for s in situations) and "kind" in line:
            errors.append('"kind" is not used with reply_*, chat_close or guild_join lines; leave it out')
    weight = field("weight")
    if weight is not None:
        if isinstance(weight, bool) or not isinstance(weight, (int, float)) or weight <= 0 or weight > 10:
            errors.append('"weight" must be a number above 0 and at most 10')
    if "guild_join" in situations and channels and set(channels) != {"guild"}:
        errors.append("guild_join lines are guild channel only")
    if channels and set(channels) == {"group"} and not all(is_reply(s) for s in situations):
        errors.append("the group channel is only used for reply_* lines")
    check_text(text, situations, errors)
    return situations


def validate(folder):
    problems, per_situation, per_file, seen = [], {}, {}, {}
    if not os.path.isdir(folder):
        return [f"folder not found: {folder}"], per_situation, per_file
    names = sorted(n for n in os.listdir(folder) if n.lower().endswith(".json") and not n.startswith("_"))
    if not names:
        problems.append(f"no .json files in {folder}")
    for name in names:
        stem = name[:-5]
        if not re.fullmatch(r"[a-z0-9_]+\.json", name):
            problems.append(f"{name}: file name must be lower-case letters, digits and _ ending in .json")
        try:
            with open(os.path.join(folder, name), "r", encoding="utf-8-sig") as handle:
                data = json.load(handle)
        except (OSError, ValueError) as error:
            problems.append(f"{name}: invalid JSON ({error})")
            continue
        if not isinstance(data, dict):
            problems.append(f"{name}: the top level must be an object")
            continue
        for k in data:
            if k not in ("category", "description", "defaults", "lines"):
                problems.append(f'{name}: unknown top-level key "{k}"')
        if data.get("category") != stem:
            problems.append(f'{name}: "category" must equal the file name "{stem}"')
        defaults = data.get("defaults", {})
        if not isinstance(defaults, dict):
            problems.append(f'{name}: "defaults" must be an object')
            defaults = {}
        if "text" in defaults:
            problems.append(f'{name}: "defaults" may not set "text"')
        lines = data.get("lines")
        if not isinstance(lines, list) or not lines:
            problems.append(f'{name}: "lines" must be a non-empty array')
            continue
        good = 0
        for index, line in enumerate(lines, 1):
            errors = []
            situations = check_line(line, defaults, errors)
            if situations is not None and isinstance(line.get("text"), str):
                key = normalize(line["text"])
                if key in seen:
                    errors.append(f"duplicate of a line in {seen[key]}")
                else:
                    seen[key] = f"{name} line {index}"
            if errors:
                problems.extend(f"{name} line {index}: {e}" for e in errors)
                continue
            good += 1
            for s in situations:
                per_situation[s] = per_situation.get(s, 0) + 1
        per_file[name] = (good, len(lines))
    return problems, per_situation, per_file


def main(argv):
    stats = "--stats" in argv
    args = [a for a in argv if a != "--stats"]
    here = os.path.dirname(os.path.abspath(__file__))
    folder = args[0] if args else os.path.normpath(os.path.join(here, "..", "banter165"))
    problems, per_situation, per_file = validate(folder)
    for p in problems:
        print("ERROR", p)
    total = sum(g for g, _ in per_file.values())
    if stats:
        for name, (good, count) in sorted(per_file.items()):
            print(f"{name}: {good}/{count} lines ok")
        for s in SITUATIONS:
            print(f"  {s}: {per_situation.get(s, 0)}")
    print(f"{'FAILED' if problems else 'OK'}: {total} good lines in {len(per_file)} files, {len(problems)} problems ({folder})")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
