import json, re, sqlite3, csv, os, collections
CV = r"C:\Users\thedo\Desktop\Hiatus\DAOC HIATUS\new class test - CLAUDE VERSION"
OUT = os.path.join(CV, r"tools\claude-version\true165\prima_sweep_2026-11-06")
os.makedirs(OUT, exist_ok=True)
S = r"C:\Users\thedo\AppData\Local\Temp\claude\ba7757758c08ada0\fa736472-70a7-4d43-a75a-e79a3dd4a852\scratchpad"
PT = os.path.join(CV, r"development-source\research\period-data\prima-tools\out")
DB = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db').replace(chr(92), '/')
c = sqlite3.connect('file:' + DB + '?mode=ro', uri=True)
names = {r[0].lower().strip() for r in c.execute("select Name from ItemTemplate where Name is not null")}
def n(s): return re.sub(r"[^a-z]", "", (s or "").lower())
res = json.load(open(os.path.join(S, "sweep_results.json"), encoding="utf-8"))

# 1. every verdict, one row per Prima name
cols = ["zone", "prima_name", "verdict", "prima_levels", "removed_rows", "live_name_or_where", "locations", "stats",
        "model_or_template", "drops", "coin", "evidence"]
with open(os.path.join(OUT, "all_zone_verdicts.csv"), "w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f); w.writerow(cols)
    for r in res:
        for m in r["monsters"]:
            w.writerow([r["label"]] + [m.get(k, "") for k in cols[1:]])
missing = {}   # (atlas section norm, monster norm) -> label
for r in res:
    sec = r["label"].split(": ", 1)[1]
    for m in r["monsters"]:
        if m["verdict"].startswith("MISSING"):
            missing[(n(sec), n(m["prima_name"]))] = r["label"]
with open(os.path.join(OUT, "missing_monsters.csv"), "w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f); w.writerow(cols)
    for r in res:
        for m in r["monsters"]:
            if m["verdict"].startswith("MISSING"):
                w.writerow([r["label"]] + [m.get(k, "") for k in cols[1:]])

# 2. Atlas drop items with no item template (by name)
norm = {n(x) for x in names}
GENERIC = {"boots", "sleeves", "helm", "leggings", "gloves", "gauntlets", "vest", "pants", "shield", "necklace", "arms",
           "legs", "hauberk", "jerkin", "cap", "coif", "robe", "tunic", "breeches", "hood", "belt", "cloak", "bracer",
           "ring", "bracelet", "jewel", "mantle", "greaves", "vambraces", "chausses", "coat", "shirt", "skirt"}
def has_prefix(prefix):
    p = n(prefix)
    return bool(p) and any(x.startswith(p) for x in norm)
def exists(item, band=()):
    i = re.sub(r"\s*\(x\d+\)\s*$", "", item.strip())
    il = i.lower()
    if n(i) in norm or n(re.sub(r"^(an?|the)\s+", "", il)) in norm:
        return True
    if il.startswith("apoa:"):                    # "a piece of armor" of a set: any piece of that set
        i = i[5:].strip(); il = i.lower()
        parts = [x.strip() for x in re.split(r"\s+(?:and|or)\s+|,", i) if x.strip()]
        first = parts[0].split()
        # "Tattered Leather and Quilted": the second set shares the first one's leading word
        sets = [parts[0]] + [(x if len(x.split()) > 1 else " ".join(first[:-1] + [x])) for x in parts[1:]]
        return all(has_prefix(s) for s in sets)
    if " and " in il and len(il.split()) <= 5:    # an APOA pair written without "APOA:"
        a, b = [x.strip() for x in il.split(" and ", 1)]
        bb = b if len(b.split()) > 1 else " ".join(a.split()[:-1] + [b])
        if has_prefix(a) and has_prefix(bb):
            return True
    # a color stands for its dyes and enamels
    if any(x.startswith(il + " ") and ("dye" in x or "enamel" in x) for x in names):
        return True
    # "Stonecrush Leggings or Helm": a bare piece takes the set name of the band's first item
    if il in GENERIC:
        for other in band:
            w = (other or "").split()
            if len(w) > 1 and w[-1].lower() in GENERIC:
                return n(" ".join(w[:-1]) + " " + i) in norm or has_prefix(" ".join(w[:-1]) + " " + i)
        return True                               # a bare piece with no set name to go by: not an item name
    return False
SKIP = re.compile(r"^(<|jewels\s*\d|colors?\b)|\bcolors$", re.I)
loot = json.load(open(os.path.join(PT, "atlas_loot.json"), encoding="utf-8"))["entries"]
items = collections.defaultdict(lambda: {"chance": 0.0, "rows": [], "for_new": False, "for_live": False})
groups = collections.defaultdict(list)
for e in loot:
    if e["section"] in ("BATTLEGROUND LOOT", "MIDGARD FRONTIER TIPS"):
        continue
    is_new = (n(re.sub(r"^Dungeon:\s*", "", e["section"])), n(e["name"])) in missing or (n(e["section"]), n(e["name"])) in missing
    for roll in e["rolls"]:
        ch = max(roll.get("chances") or [0])
        for band in roll.get("items_by_band") or []:
            for it in band:
                it = (it or "").strip()
                if not it:
                    continue
                if it.startswith("<"):
                    groups[it].append(f'{e["name"]} ({e["section"]})')
                    continue
                if SKIP.search(it) or exists(it, band):
                    continue
                d = items[it]
                d["chance"] = max(d["chance"], ch)
                d["rows"].append(f'{e["name"]} [{e.get("levels")}] {e["section"]} {ch:g}%')
                d["for_new"] |= is_new
                d["for_live"] |= not is_new
with open(os.path.join(OUT, "items_to_research.csv"), "w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["item", "max_chance_pct", "monsters", "needed_by", "dropped_by (monster [levels] Atlas zone chance)"])
    for it, d in sorted(items.items(), key=lambda kv: (-len(kv[1]["rows"]), kv[0].lower())):
        need = "both" if d["for_new"] and d["for_live"] else ("monsters to add" if d["for_new"] else "live monsters")
        w.writerow([it, f'{d["chance"]:g}', len(d["rows"]), need, " | ".join(d["rows"])])
# 3. the Prima loot groups (<...>): their items from the Atlas cache lists
caches = {x["name"].lower(): x for x in json.load(open(os.path.join(PT, "atlas_extra.json"), encoding="utf-8"))["caches"]}
with open(os.path.join(OUT, "atlas_loot_groups.csv"), "w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f); w.writerow(["group", "monsters", "group_items", "items_without_template"])
    for g, mons in sorted(groups.items(), key=lambda kv: -len(kv[1])):
        cache = caches.get(g.strip("<>").strip().lower())
        its = cache["items"] if cache else []
        w.writerow([g, len(mons), len(its), "; ".join(i for i in its if not exists(i, its))])
print("monsters missing", sum(1 for _ in missing))
print("items without template", len(items), "| for monsters to add only", sum(1 for d in items.values() if d["for_new"] and not d["for_live"]),
      "| live monsters only", sum(1 for d in items.values() if d["for_live"] and not d["for_new"]), "| both", sum(1 for d in items.values() if d["for_new"] and d["for_live"]))
print("loot groups", len(groups), "with a cache list", sum(1 for g in groups if g.strip('<>').strip().lower() in caches))
print(OUT)
