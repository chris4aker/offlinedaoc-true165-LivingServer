"""True 1.65: put back the Cornwall monsters the Prima Atlas lists there (owner 2026-11-06), from the rows the 2026-08-29
classic165 pass archived. Levels set to the Prima Atlas range. Read-only on the DB; writes add-cornwallprima165.json."""
import sqlite3, json, math, sys, collections
CV = r"C:\Users\thedo\Desktop\Hiatus\DAOC HIATUS\new class test - CLAUDE VERSION"
DB = CV + r"\runtime\data\opendaoc-165.sqlite3.db"
c = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
B = "Region=1 and X between 352256 and 417791 and Y between 630784 and 696319"
PRIMA = {  # Prima Atlas Cornwall loot list levels (Atlas txt ~7248-7460)
    'moor boogey': (25, 30), 'cornish giant': (40, 40), 'cliff spiderling': (14, 14),
    'muryan': (18, 20), 'giant rooter': (30, 34), 'forest cat': (16, 16), 'cliff spider': (18, 18), 'moor wolf': (14, 14),
    'black bear': (16, 21), 'brown bear': (15, 15), 'death stalker': (16, 27)}
ALL = {'moor boogey', 'cornish giant', 'cliff spiderling'}   # RESTORE: every archived row not in the world
expect = {'moor boogey': 87, 'cornish giant': 9, 'cliff spiderling': 10, 'muryan': 6, 'giant rooter': 22, 'forest cat': 10,
          'cliff spider': 2, 'moor wolf': 12, 'black bear': 3, 'brown bear': 4, 'death stalker': 3}
mobcols = [r[1] for r in c.execute("pragma table_info(Mob)")]
remcols = [r[1] for r in c.execute("pragma table_info(offline_classic165_removed_mobs)")]
live_ids = {r[0] for r in c.execute("select Mob_ID from Mob")}
live = collections.defaultdict(list)
spelling = collections.defaultdict(collections.Counter)   # the live monsters' spelling of each name
for name, x, y in c.execute(f"select Name,X,Y from Mob where {B} and Realm=0"):
    live[name.lower()].append((x, y))
    spelling[name.lower()][name] += 1
rows, counts, relevel = [], collections.Counter(), collections.Counter()
for r in c.execute(f"select * from offline_classic165_removed_mobs where {B}"):
    d = dict(zip(remcols, r))
    k = d['Name'].lower()
    if k not in PRIMA or d['Mob_ID'] in live_ids:
        continue
    if k not in ALL and any(math.hypot(d['X'] - x, d['Y'] - y) <= 1500 for x, y in live[k]):
        continue
    lo, hi = PRIMA[k]
    if d['Level'] and not lo <= d['Level'] <= hi:
        relevel[k] += 1
        d['Level'] = min(max(d['Level'], lo), hi)
    row = {col: d[col] for col in mobcols if col in d}
    row['PackageID'] = 'true165-restored'
    if spelling[k]:
        row['Name'] = spelling[k].most_common(1)[0][0]
    rows.append(row); counts[k] += 1
for k in expect:
    print(f"{k:18} {counts[k]:3} (agent {expect[k]})  releveled {relevel[k]}")
print('total', len(rows))
# drops: every kind must already have a loot list by name
for k in PRIMA:
    n = c.execute("select count(*) from MobXLootTemplate where lower(MobName)=?", (k,)).fetchone()[0]
    print(f"  loot lists for {k}: {n}")
if '--write' in sys.argv:
    doc = {"table": "Mob", "key": "Mob_ID",
           "reason": "True 1.65 Prima Cornwall spawns (owner 2026-11-06): the Atlas Cornwall monsters the 2026-08-29 classic165 pass archived - moor boogeys incl. Boogey Fort, Cornish giants, cliff spiderlings, and empty camps of 8 more; levels set to the Prima Atlas range",
           "rows": rows}
    for p in (CV + r"\tools\claude-version\true165\add-cornwallprima165.json", CV + r"\runtime\true165\add-cornwallprima165.json"):
        json.dump(doc, open(p, 'w', encoding='utf-8', newline='\n'), indent=1, ensure_ascii=False)
    print('written')
