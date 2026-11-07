"""Evidence per Atlas item we lack: period Allakhazam pages, Uthgard item pages, live Allakhazam (dated), near-name
existing ItemTemplates. Writes items_work/batch_NN.json (50 items each). Read-only on everything."""
import csv, json, os, re, sys, gzip, sqlite3, difflib, collections
CV = r"C:\Users\thedo\Desktop\Hiatus\DAOC HIATUS\new class test - CLAUDE VERSION"
PD = os.path.join(CV, r"development-source\research\period-data")
sys.path.insert(0, PD)
from read_archive import records
W = os.path.dirname(os.path.abspath(__file__))
def n(s): return re.sub(r"[^a-z0-9]", "", (s or "").lower())
items = list(csv.DictReader(open(os.path.join(CV, r"tools\claude-version\true165\prima_sweep_2026-11-06\items_to_research.csv"), encoding="utf-8-sig")))
want = {n(re.sub(r"^APOA:\s*", "", x["item"])) for x in items}
dk = [k for k in items[0] if k.startswith("dropped")][0]
# period Allakhazam pages (parsed)
period = collections.defaultdict(list)
for l in open(os.path.join(CV, r"tools\claude-version\true165\vendor_work\period_item_pages.jsonl"), encoding="utf-8"):
    r = json.loads(l)
    k = n(re.sub(r"\s*\((?:hib|mid|alb|df)\)", "", r["name"], flags=re.I))
    if k in want:
        period[k].append({"key": r["key"], "capture": r["capture"], "name": r["name"], "realm": r["realm"],
                          "prices": r["prices"], "merchants": r["merchants"], "found_in": r["found_in"], "raw": r["raw"][:1500]})
print("period hits", sum(1 for k in want if k in period))
# Uthgard item pages
uth = collections.defaultdict(list)
TAG = re.compile(r"<[^>]+>")
for r in records(os.path.join(PD, r"archive\uthgard\raw_pages.jsonl.gz")):
    u = r.get("url", "")
    if "item.php" not in u:
        continue
    t = re.sub(r"(\|\s*)+", "|", TAG.sub("|", r.get("body", "")))
    i = t.find("|Item|Name|Information|Stats|")
    seg = t[i + 29:t.find("(adsbygoogle", i)] if i >= 0 else ""
    name = seg.split("|", 1)[0].strip()
    if n(name) in want:
        uth[n(name)].append({"load": u.rsplit("=", 1)[-1], "name": name, "text": seg[:1200]})
print("uthgard hits", sum(1 for k in want if k in uth))
# live Allakhazam item pages (current site; usable only with a pre-2003-11 date)
live = collections.defaultdict(list)
for r in records(os.path.join(PD, r"archive\allakhazam-live\item.jsonl.gz")):
    title = (r.get("title") or "").split(" :: ")[0].strip()
    if n(title) in want:
        t = re.sub(r"\s+", " ", TAG.sub(" ", r.get("html", "")))
        m = re.search(r"Last Updated:?\s*([A-Za-z]{3}\s+\w+\s+\d+[^A-Za-z]*\d{4})", t)
        i = t.find(title, t.find("<title") + 1 if "<title" in t else 0)
        live[n(title)].append({"url": r.get("url"), "last_updated": m.group(1) if m else None,
                               "text": t[max(0, t.find(title, 200)):][:1200]})
print("live hits", sum(1 for k in want if k in live))
# existing items with near names
DB = os.path.join(CV, "runtime", "data", "opendaoc-165.sqlite3.db").replace(chr(92), "/")
c = sqlite3.connect("file:" + DB + "?mode=ro", uri=True)
ex = c.execute("select Id_nb,Name,Realm,Object_Type,Item_Type,Level,Price,PackageID from ItemTemplate").fetchall()
byn = collections.defaultdict(list)
for row in ex:
    byn[n(row[1])].append(row)
keys = list(byn)
batches, cur = [], []
for x in items:
    nm = re.sub(r"^APOA:\s*", "", x["item"]); k = n(nm)
    near = difflib.get_close_matches(k, keys, n=4, cutoff=0.86)
    cur.append({"item": x["item"], "priority": x["priority"], "max_chance_pct": x["max_chance_pct"], "check": x["check"],
                "dropped_by": x[dk].split(" | ")[:12], "period_pages": period.get(k, [])[:4], "uthgard_pages": uth.get(k, [])[:3],
                "live_zam": live.get(k, [])[:2],
                "near_existing_items": [dict(zip(["Id_nb", "Name", "Realm", "Object_Type", "Item_Type", "Level", "Price", "PackageID"], r))
                                        for kk in near for r in byn[kk][:2]]})
    if len(cur) == 50:
        batches.append(cur); cur = []
if cur:
    batches.append(cur)
for i, b in enumerate(batches):
    json.dump(b, open(os.path.join(W, f"batch_{i:02d}.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
print("batches", len(batches))
