"""True 1.65 rules agent (2026-11-06): hasteners (item 56), the Market Explorer label (item 60) and arrow merchants (item 67).

Reads the live 1.65 database read-only and writes:
  add-rules165-hasteners.json   the period hasteners back from offline_true165_removed (an existing world lost them)
  pending-rules165-updates.json the arrow merchant updates and the exact remove-list changes for the lead to merge
Run: python -I build_rules165.py
"""
import collections, json, os, sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
DB = os.path.join(HERE, "..", "..", "..", "runtime", "data", "opendaoc-165.sqlite3.db")
con = sqlite3.connect("file:" + os.path.abspath(DB) + "?mode=ro", uri=True)

# ---- 56. Hasteners ------------------------------------------------------------------------------------------------
# 1.52b notes (Freddyshouse 92569, 2002-07-18): "Each frontier keep has now been outfitted with a new ally, a Hastener ...
# Further, each border keep now has a Hastener ally as well." 1.64 client help: the housing zones have "horse routes and
# hasteners". Town hasteners stay removed (no period source; players asked for "in realm hasteners" in Nov 2002).
HOUSING_REGIONS = (2, 102, 202)
# GameHastener rows standing at the border keeps (guards, the medallion teleporter or the keep trainers next to them) and
# at each realm's portal keep in an enemy frontier (Freddyshouse 113348, 2003-02: "bind just outside PK. With hastener ...").
BORDER_KEEP_HASTENERS = {
    "11777336-1b8b-": "Castle Sauvage (Minstrel of Albion)",
    "3ca81fd9-abdf-": "Castle Sauvage (Minstrel of Albion)",
    "436737f1-95bc-": "Snowdonia Fortress (Minstrel of Albion)",
    "1935cd41-8c50-": "Svasud Faste (Hastener)",
    "bacc22fa-385e-": "Svasud Faste (Skald of Midgard)",
    "9a2c73dc-65fe-": "Vindsaul Faste (Hastener)",
    "88c728d0-583d-": "Vindsaul Faste (Skald of Midgard)",
    "ee1145cc-ea2d-": "Druim Ligen (Hastener)",
    "ef9a0f40-d8be-": "Druim Ligen (Hastener)",
    "1d41a716-ca62-": "Druim Cain (Hastener)",
    "c4f1caae-9a27-": "Druim Cain (Hastener)",
    "7c14f6f0-4df1-": "Hibernia portal keep, Hadrian's Wall",
    "86945a6f-e302-": "Midgard portal keep, Hadrian's Wall",
}
FRONTIER_REGIONS = (1, 100, 200)   # the old frontiers; battleground keeps are left to the battleground work

archived = {}
for reason, row_json in con.execute("SELECT Reason, RowJson FROM offline_true165_removed WHERE SourceTable='Mob' "
                                    "AND (Reason LIKE 'town hastener%' OR Reason LIKE 'frontier hastener%') ORDER BY Id"):
    row = json.loads(row_json)
    archived[row["Mob_ID"]] = row          # the latest copy of each row wins

housing, border, keep = [], [], []
for row in archived.values():
    if row["ClassType"] == "DOL.GS.GameHastener" and row["Region"] in HOUSING_REGIONS:
        housing.append(row)
    elif row["ClassType"] == "DOL.GS.GameHastener" and any(row["Mob_ID"].startswith(p) for p in BORDER_KEEP_HASTENERS):
        border.append(row)
    elif row["ClassType"] == "DOL.GS.Keeps.FrontierHastener" and row["Region"] in FRONTIER_REGIONS:
        keep.append(row)
assert len(border) == len(BORDER_KEEP_HASTENERS), (len(border), "border keep hastener rows")

# every frontier hastener must stand by a keep of its region (outside the gate, under 2000 units from the keep centre)
keeps = con.execute("SELECT Name, Region, X, Y FROM Keep").fetchall()
for row in keep:
    near = min((((k[2] - row["X"]) ** 2 + (k[3] - row["Y"]) ** 2) ** 0.5, k[0]) for k in keeps if k[1] == row["Region"])
    assert near[0] < 2000, (row["Mob_ID"], near)

# a keep hastener archived under the placeholder name "new mob" gets the name the server gives it (FrontierHastener.SetName),
# or the "new mob" placeholder rule would archive it again at every start and this file add it back
for row in keep:
    if (row.get("Name") or "").strip().lower() == "new mob":
        row["Name"] = "Hastener"
rows = sorted(housing + border + keep, key=lambda r: (r["Region"], r["Mob_ID"]))
json.dump({
    "table": "Mob",
    "key": "Mob_ID",
    "reason": "True 1.65 hasteners: frontier keep (1.52b), border and portal keep hasteners, and the housing zone hasteners (1.64 help)",
    "rows": rows,
}, open(os.path.join(HERE, "add-rules165-hasteners.json"), "w", encoding="utf-8"), indent=1)

border_ids = sorted(r["Mob_ID"] for r in border)
keep_ids = sorted(r["Mob_ID"] for r in keep)
sql_list = lambda ids: ",".join(f"'{i}'" for i in ids)
where_rules = [
    {
        "table": "Mob",
        "key": "Mob_ID",
        "where": f"ClassType = 'DOL.GS.GameHastener' AND Region NOT IN ({','.join(map(str, HOUSING_REGIONS))}) "
                 f"AND Mob_ID NOT IN ({sql_list(border_ids)})",
        "reason": "town hastener (not in 1.65: hasteners stood only at frontier, border and portal keeps and in the housing zones)",
    },
    {
        "table": "Mob",
        "key": "Mob_ID",
        "where": f"ClassType = 'DOL.GS.Keeps.FrontierHastener' AND Mob_ID NOT IN ({sql_list(keep_ids)})",
        "reason": "frontier hastener away from the old frontier keeps (New Frontiers and battleground placements)",
    },
]

# ---- 67. Arrows ---------------------------------------------------------------------------------------------------
# Arrows (Object_Type 43) carry a realm and can't be put in another realm's quiver (CheckItemRealmRestriction). Several
# merchant lists sell another realm's copy (the same name with no suffix = Albion, 2 = Midgard, 3 = Hibernia). Each row
# is pointed at its own realm's copy of the same arrow; a list shared by merchants of several realms keeps the realm of
# most of its arrows and the other merchants move to their realm's own arrow list.
REGION_REALM = {1: 1, 10: 1, 51: 1, 100: 2, 101: 2, 151: 2, 200: 3, 201: 3, 181: 3}
PREFIX = {"alb": 1, "mid": 2, "hib": 3}
REALM_ARROW_LIST = {1: "AlbArrowsOnly", 2: "MidArrows", 3: "HibArrowMerchant"}   # 28 arrows each, nothing else

mobs = con.execute("SELECT Mob_ID, Name, Region, Realm, ItemsListTemplateID FROM Mob "
                   "WHERE ItemsListTemplateID IS NOT NULL AND ItemsListTemplateID <> ''").fetchall()
name_realm = collections.defaultdict(collections.Counter)
for m in mobs:
    if m[3] > 0:
        name_realm[m[1]][m[3]] += 1


def merchant_realm(m):
    if m[3] > 0:
        return m[3]
    if m[2] in REGION_REALM:
        return REGION_REALM[m[2]]
    c = name_realm.get(m[1])                # Takker, Allia Fletcher, Llala in the multi-realm event regions
    return c.most_common(1)[0][0] if c else None


arrows, by_name = {}, collections.defaultdict(dict)
for idnb, name, realm, spd, dmg in con.execute("SELECT Id_nb, Name, Realm, SPD_ABS, Type_Damage FROM ItemTemplate WHERE Object_Type = 43"):
    arrows[idnb.lower()] = (idnb, name, realm, spd, dmg)
    by_name[(name.lower(), realm)].setdefault((spd, dmg), idnb)
items = collections.defaultdict(list)
for mid, lst, tid in con.execute("SELECT MerchantItem_ID, ItemListID, ItemTemplateID FROM MerchantItem"):
    items[lst.lower()].append((mid, lst, tid))
users = collections.defaultdict(list)
for m in mobs:
    users[m[4].lower()].append(m)
for lst in items:                           # unused lists named for a realm are fixed too
    if lst[:3] in PREFIX and lst not in users:
        users[lst] = []

updates, duplicates = [], []
for lst, merchants in sorted(users.items()):
    rows_here = [r for r in items.get(lst, []) if r[2].lower() in arrows]
    if not rows_here:
        continue
    realms = collections.Counter(merchant_realm(m) for m in merchants)
    if not merchants:
        target = PREFIX[lst[:3]]
    elif len(realms) == 1:
        target = next(iter(realms))
    else:
        target = collections.Counter(arrows[r[2].lower()][2] for r in rows_here if arrows[r[2].lower()][2] > 0).most_common(1)[0][0]
    for m in merchants:
        r = merchant_realm(m)
        if r != target:
            updates.append({
                "sql": f"UPDATE Mob SET ItemsListTemplateID = '{REALM_ARROW_LIST[r]}' WHERE Mob_ID = '{m[0]}' "
                       f"AND ItemsListTemplateID = '{m[4]}'",
                "reason": f"True 1.65 arrows: {m[1]} (region {m[2]}) shared a {'Albion Midgard Hibernia'.split()[target - 1]} "
                          f"arrow list; now sells the {'Albion Midgard Hibernia'.split()[r - 1]} arrows ({REALM_ARROW_LIST[r]})",
            })
    have = {r[2].lower() for r in items.get(lst, [])}
    for mid, raw_list, tid in rows_here:
        idnb, name, realm, spd, dmg = arrows[tid.lower()]
        if realm in (0, target):
            continue
        cands = by_name.get((name.lower(), target), {})
        new = cands.get((spd, dmg)) or next(iter(cands.values()), None)
        assert new, (lst, name, target)
        if new.lower() in have:
            duplicates.append(mid)
            continue
        have.add(new.lower())
        updates.append({
            "sql": f"UPDATE MerchantItem SET ItemTemplateID = '{new}' WHERE MerchantItem_ID = '{mid}' AND ItemTemplateID = '{tid}'",
            "reason": f"True 1.65 arrows: {raw_list} sold another realm's {name}; now the {'Albion Midgard Hibernia'.split()[target - 1]} copy",
        })

if duplicates:
    where_rules.append({
        "table": "MerchantItem",
        "key": "MerchantItem_ID",
        "where": f"MerchantItem_ID IN ({sql_list(sorted(duplicates))})",
        "reason": "1.65 arrows: another realm's arrow in a list that already sells its own realm's copy",
    })

json.dump({
    "_about": "Written by build_rules165.py (rules agent, 2026-11-06). Append \"updates\" to true165-rules.json \"updates\" "
              "(idempotent); apply \"remove_changes\" to the remove lists; install add-rules165-hasteners.json.",
    "updates": updates,
    "remove_changes": (
        "1) remove.mob_classes: DELETE the two entries \"DOL.GS.GameHastener\" (\"town hastener (patch 1.70)\") and "
        "\"DOL.GS.Keeps.FrontierHastener\" (\"frontier hastener (New Frontiers)\"). "
        "2) remove.where: APPEND the rules listed in \"remove_where_add\" below (town hasteners and non-keep frontier "
        "hasteners stay removed; the period ones in add-rules165-hasteners.json are excluded, so a server start never "
        "archives and re-adds them). "
        "3) remove.mob_classes: change only the reason text of \"DOL.GS.MarketExplorer\" from \"market explorer (2004)\" "
        "to \"market explorer (1.64 housing; off: no consignment merchants, owner 2026-11-02)\"; it stays removed."
    ),
    "remove_where_add": where_rules,
}, open(os.path.join(HERE, "pending-rules165-updates.json"), "w", encoding="utf-8"), indent=1)

print(f"hasteners: {len(housing)} housing, {len(border)} border/portal keep, {len(keep)} frontier keep")
print(f"arrow updates: {len(updates)} ({sum(1 for u in updates if u['sql'].startswith('UPDATE Mob'))} merchants moved), "
      f"duplicate rows removed: {len(duplicates)}")
