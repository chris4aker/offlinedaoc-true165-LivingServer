"""True 1.65: merchants sell gear of their own realm (owner's reset-day list S11, 2026-11-05; player report: "bought a
Bronze War Axe in East Svealand ... it says I cannot equip weapons that belong to another realm").

The base world's merchant lists point at copies of items carrying another realm. build_merchantfix165.py fixed the
30-copper placeholder copies; this pass fixes the rest, row by row, for weapons, armour, shields, jewellery and
instruments sold by a realm's merchants:
1. Item realm by weapon/armour type: a type only one realm uses (Midgard axes, Albion plate, Hibernian blades...)
   tells the item's realm. A template sold by that realm's merchants but marked another realm gets its realm fixed
   (the Bronze War Axe is a Midgard axe marked Albion). A shared type (cloth, leather, shields, jewellery...) sold
   only by another single realm gets that realm.
2. Then each merchant row whose item still belongs to another realm points at the item's twin of the merchant's realm
   (same name and level; same type first) when there is one; a shared-type item with no twin gets no realm (any
   realm may use it).
3. A row left with another realm's weapon or armour type (a Midgard merchant selling Albion slashing swords nobody
   in Midgard can use) is removed (archived like every removal).
Poisons, arrows and other non-gear are not touched.

python -I build_merchantrealm165.py   (rerunnable: the decisions are worked out once from the world and kept in
                                        merchantrealm165.json, so a rerun after the refresh writes the same rules)
Out (both copies): true165-rules.json (updates and removals tagged "True 1.65 merchant realm"); add-*.json rows of
merchant lists the launcher adds are patched the same way; merchantrealm165.json (the decisions, for review).
"""
import collections, json, os, sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
WORLD = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
SNAPSHOT = os.path.join(HERE, 'merchantrealm165.json')
TAG = 'True 1.65 merchant realm'
GEAR = set(range(1, 29)) | set(range(31, 39)) | {41, 42, 45}
TYPE_REALM = {**{t: 1 for t in (2, 3, 4, 6, 7, 9, 10, 24, 36)},          # Albion weapon types, plate
              **{t: 2 for t in (11, 12, 13, 14, 15, 16, 17, 25)},         # Midgard
              **{t: 3 for t in (18, 19, 20, 21, 22, 23, 26, 37, 38)}}     # Hibernia, reinforced, scale


def q(v):
    return "'" + str(v).replace("'", "''") + "'"


def decide():
    c = sqlite3.connect(f'file:{WORLD}?mode=ro', uri=True)
    list_realms = collections.defaultdict(set)
    for realm, lst in c.execute("SELECT Realm, ItemsListTemplateID FROM Mob WHERE Realm IN (1, 2, 3) AND "
                                "ItemsListTemplateID IS NOT NULL AND ItemsListTemplateID <> ''"):
        list_realms[lst].add(realm)
    items = {r[0]: r for r in c.execute('SELECT Id_nb, Name, Object_Type, Level, Realm, Price FROM ItemTemplate')}
    rows = [r for r in c.execute('SELECT MerchantItem_ID, ItemListID, ItemTemplateID FROM MerchantItem')
            if len(list_realms.get(r[1], ())) == 1 and r[2] in items and items[r[2]][2] in GEAR]
    added_items = {r[0] for r in c.execute("SELECT RowKey FROM offline_true165_added WHERE SourceTable = 'ItemTemplate'")}
    by_name = collections.defaultdict(list)
    for t in items.values():
        by_name[(t[1].lower(), t[3], t[4])].append(t)
    c.close()

    # 1. item realms
    sold_by = collections.defaultdict(set)
    for _, lst, iid in rows:
        sold_by[iid].add(next(iter(list_realms[lst])))
    realm_fix = {}
    for iid, realms in sold_by.items():
        t = items[iid]
        if t[4] == 0 or iid in added_items:
            continue
        own = TYPE_REALM.get(t[2])
        if own is not None:
            # a one-realm type sold by that realm's merchants is that realm's item, even when another realm's merchant
            # also lists it (the Bronze War Axe: Midgard and Albion lists); that other listing is removed below
            if own in realms and own != t[4]:
                realm_fix[iid] = own
        elif t[4] not in realms and len(realms) == 1:
            realm_fix[iid] = next(iter(realms))
    final = lambda iid: realm_fix.get(iid, items[iid][4])

    # 2./3. merchant rows
    repoint, remove, left = {}, [], []
    for mid, lst, iid in rows:
        realm = next(iter(list_realms[lst]))
        if final(iid) in (0, realm):
            continue
        t = items[iid]
        twins = [w for w in by_name[(t[1].lower(), t[3], realm)] if TYPE_REALM.get(w[2], realm) == realm]
        twins.sort(key=lambda w: (w[2] != t[2], w[5] in (0, 30), len(w[0]), w[0]))
        if twins:
            repoint[mid] = twins[0][0]
        elif TYPE_REALM.get(t[2], realm) != realm:
            remove.append(mid)
        elif iid not in added_items:
            realm_fix[iid] = 0          # a shared type (cloth, leather, shield, jewel...) with no twin: any realm may use it
        else:
            left.append(mid)
    snap = {'realm_fix': realm_fix, 'repoint': repoint, 'remove': sorted(remove), 'left': sorted(left),
            'names': {iid: items[iid][1] for iid in set(realm_fix) | {r[2] for r in rows if r[0] in repoint or
                                                                     r[0] in remove}}}
    with open(SNAPSHOT, 'w', encoding='utf-8') as f:
        json.dump(snap, f, ensure_ascii=False, indent=1)
        f.write('\n')
    return snap


def main():
    snap = json.load(open(SNAPSHOT, encoding='utf-8')) if os.path.exists(SNAPSHOT) else decide()
    updates = [{'sql': f"UPDATE ItemTemplate SET Realm = {realm} WHERE Id_nb = {q(iid)}",
                'reason': f"{TAG}: {snap['names'].get(iid, iid)} is a "
                          f"{'any-realm' if realm == 0 else ['', 'Albion', 'Midgard', 'Hibernian'][realm]} item "
                          f"(its type / the merchants that sell it)"}
               for iid, realm in sorted(snap['realm_fix'].items())]
    updates += [{'sql': f"UPDATE MerchantItem SET ItemTemplateID = {q(twin)} WHERE MerchantItem_ID = {q(mid)}",
                 'reason': f"{TAG}: the merchant sells its own realm's {twin}"}
                for mid, twin in sorted(snap['repoint'].items())]
    removal = {'table': 'MerchantItem', 'key': 'MerchantItem_ID',
               'where': 'MerchantItem_ID IN (' + ', '.join(map(q, snap['remove'])) + ')',
               'reason': f"{TAG}: another realm's weapon/armour type nobody of the merchant's realm can use"}
    for folder in (HERE, RUNTIME):
        p = os.path.join(folder, 'true165-rules.json')
        rules = json.load(open(p, encoding='utf-8'))
        rules['updates'] = [u for u in rules['updates'] if not u['reason'].startswith(TAG)] + updates
        rules['remove']['where'] = [w for w in rules['remove']['where'] if not w['reason'].startswith(TAG)] + \
            ([removal] if snap['remove'] else [])
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(rules, f, ensure_ascii=False, indent=1)
            f.write('\n')
        # merchant rows the launcher adds itself are rewritten from their add-file after the updates: patch them there
        patched = 0
        for name in os.listdir(folder):
            if not (name.startswith('add-') and name.endswith('.json')):
                continue
            path = os.path.join(folder, name)
            doc = json.load(open(path, encoding='utf-8'))
            docs = doc if isinstance(doc, list) else [doc]
            changed = False
            for d in docs:
                if d.get('table') != 'MerchantItem':
                    continue
                keep = []
                for row in d['rows']:
                    mid = row.get('MerchantItem_ID')
                    if mid in snap['repoint'] and row.get('ItemTemplateID') != snap['repoint'][mid]:
                        row['ItemTemplateID'] = snap['repoint'][mid]
                        changed = True
                        patched += 1
                    if mid in snap['remove']:
                        changed = True
                        patched += 1
                        continue
                    keep.append(row)
                d['rows'] = keep
            if changed:
                with open(path, 'w', encoding='utf-8') as f:
                    json.dump(doc, f, ensure_ascii=False, indent=1)
                    f.write('\n')
        print(f'{folder}: add-file rows patched {patched}')
    print(f"item realms fixed {len(snap['realm_fix'])}, merchant rows repointed {len(snap['repoint'])}, "
          f"removed {len(snap['remove'])}, left {len(snap['left'])}")


if __name__ == '__main__':
    main()
