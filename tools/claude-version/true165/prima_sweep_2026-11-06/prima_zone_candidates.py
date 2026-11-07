"""Read-only: per zone, the monsters a Prima guide lists there that have no live spawn of that name in that zone."""
import json, os, re, sqlite3, sys, collections, difflib

CV = r"C:\Users\thedo\Desktop\Hiatus\DAOC HIATUS\new class test - CLAUDE VERSION"
PT = os.path.join(CV, r"development-source\research\period-data\prima-tools\out")
DB = os.path.join(CV, r"runtime\data\opendaoc-165.sqlite3.db")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "prima_zones")
os.makedirs(OUT, exist_ok=True)

def n(s):
    return re.sub(r"[^a-z]", "", (s or "").lower())

c = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
zones = c.execute("select ZoneID, RegionID, Name, OffsetX, OffsetY, Width, Height from Zones").fetchall()
by_region = collections.defaultdict(list)
for z in zones:
    by_region[z[1]].append(z)

def zone_of(region, x, y):
    for zid, rid, name, ox, oy, w, h in by_region.get(region, []):
        if ox * 8192 <= x < (ox + w) * 8192 and oy * 8192 <= y < (oy + h) * 8192:
            return zid
    return None

live = collections.defaultdict(collections.Counter)      # zone -> name -> count
live_any = collections.defaultdict(collections.Counter)  # name -> zone -> count
display = {}
for name, region, x, y, realm in c.execute("select Name, Region, X, Y, Realm from Mob"):
    zid = zone_of(region, x, y)
    k = n(name)
    live[zid][k] += 1
    live_any[k][zid] += 1
    display.setdefault(k, name)

removed = collections.defaultdict(collections.Counter)
cols = [r[1] for r in c.execute("pragma table_info(offline_classic165_removed_mobs)")]
xs = {k: i for i, k in enumerate(cols)}
for row in c.execute("select * from offline_classic165_removed_mobs"):
    zid = zone_of(row[xs["Region"]], row[xs["X"]], row[xs["Y"]])
    removed[zid][n(row[xs["Name"]])] += 1

zname = {z[0]: f"{z[2]} (zone {z[0]}, region {z[1]})" for z in zones}
dbnames = {n(z[2]): z[0] for z in sorted(zones, key=lambda z: z[1] == 163, reverse=True)}  # classic regions win over New Frontiers (163)

ALIASES = {
    "blackmountainsnorth": "blackmtnsnorth", "blackmountainssouth": "blackmtnssouth",
    "mtcollory": "mountcollory", "svealandeast": "eastsvealand", "svealandwest": "westsvealand",
}

def match_zone(title):
    t = n(re.sub(r"^Dungeon:\s*", "", title))
    t = ALIASES.get(t, t)
    if t in dbnames:
        return [dbnames[t]]
    hits = [zid for k, zid in dbnames.items() if t and (t in k or k in t) and min(len(t), len(k)) >= 5]
    if hits:
        return hits
    close = difflib.get_close_matches(t, list(dbnames), n=1, cutoff=0.8)
    return [dbnames[close[0]]] if close else []

SKIP = {"BATTLEGROUND LOOT", "MIDGARD FRONTIER TIPS"}
prima = collections.defaultdict(dict)   # title -> name -> levels
for e in json.load(open(os.path.join(PT, "atlas_loot.json"), encoding="utf-8"))["entries"]:
    if e["section"] not in SKIP:
        prima[("Atlas", e["section"])].setdefault(e["name"], e.get("levels"))
for e in json.load(open(os.path.join(PT, "si_tables.json"), encoding="utf-8"))["si_zone_monsters"]:
    prima[("SI", e["zone"])].setdefault(e["name"], e.get("levels"))
for e in json.load(open(os.path.join(PT, "si_extra.json"), encoding="utf-8"))["dungeon_monsters"]:
    prima[("SI dungeon", e["dungeon"])].setdefault(e["name"], e.get("levels"))

summary = []
for (src, title), mobs in sorted(prima.items()):
    zids = match_zone(title)
    here = collections.Counter()
    rem = collections.Counter()
    for zid in zids:
        here.update(live[zid]); rem.update(removed[zid])
    cands = []
    for name, levels in sorted(mobs.items()):
        k = n(name)
        if not k or here[k] > 0:
            continue
        # a near name in the zone (plural, "the", spelling) - for the agent to judge
        near = difflib.get_close_matches(k, list(here), n=3, cutoff=0.82)
        elsewhere = {zname.get(z, str(z)): cnt for z, cnt in live_any[k].most_common(4)}
        cands.append({"prima_name": name, "prima_levels": levels, "removed_in_this_zone": rem[k],
                      "live_elsewhere": elsewhere, "near_names_in_zone": [display.get(x, x) for x in near]})
    rec = {"source": src, "prima_zone": title, "db_zones": [zname.get(z) for z in zids],
           "prima_count": len(mobs), "missing_count": len(cands), "missing": cands}
    fn = re.sub(r"[^A-Za-z0-9]+", "_", f"{src}_{title}").strip("_") + ".json"
    json.dump(rec, open(os.path.join(OUT, fn), "w", encoding="utf-8"), indent=1)
    summary.append((src, title, [zname.get(z) for z in zids], len(mobs), len(cands), fn))

for s in summary:
    print(f"{s[0]:10} | {s[1]:32} | {s[3]:3} prima | {s[4]:3} missing | {s[2]} | {s[5]}")
