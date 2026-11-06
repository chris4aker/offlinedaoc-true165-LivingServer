"""True 1.65 merchant fixes, part 2 (owner 2026-11-06).

1. "Aren sword vendor has a random lvl 45 hammer": the merchant pages pass (vendor_work/make_goods_content.py) put
   Brungar's Might - the period item of Forgemaster Brungar (region 151; period merchant page 5818, 2003-09-14) - on
   his merchant list MidBlades, which 12 other Midgard sword merchants share (Aren, Vordn, Orm, Stein, Pater, Vifil,
   Gyda, Harald, Liv, Flosi, Hrapp, svartalf merchant). Brungar gets his own list (p165list_Brungar: the MidBlades
   stock plus Brungar's Might in the same slot) and the hammer comes off MidBlades. It was the only period "named
   merchant" item the pass added to a shared list (the other shared-list additions are ordinary stock: crafting
   components, dyes, the list's own armour series).
2. "Dye merchants show Albion ... change the tooltip": merchant goods that can't be equipped (dyes, enamels, poisons,
   crafting materials, fireworks...) carry Realm 1/2/3 and are sold by other realms' merchants (LeatherDye1 = Albion
   dyes sold by Cale in Mularn). They become realm-neutral (Realm 0, no realm shown):
   - an item no recipe makes: its own Realm is set to 0 (realm doesn't matter to buying, using, dyeing, poisoning or
     as a recipe ingredient);
   - an item a recipe makes (the alchemy dyes, enamels and poisons): its Realm stays (Normal rules with
     allow_craft_norealm_items=False let only its own realm craft it, and a Realm 0 product could be crafted by no one),
     and the merchant rows selling it to another realm point at a Realm 0 copy "<id>_anyrealm" instead (same stats,
     price, model and colour; added by add-merchantfix2_165_anyrealm.json). On lists shared by several realms (the dye
     lists) every row of that item on the list points at the copy.
   Gear (weapons, armour, shields, jewellery, instruments) and arrows/bolts are not touched: their realm decides who
   can equip them (build_merchantrealm165.py handles gear).

python -I build_merchantfix2_165.py [--world DB] [--decide]
   Decisions are worked out once from the True 1.65 world (read only) and kept in merchantfix2_165.json, so a rerun
   on a world that already has the fix writes the same content. --decide works them out again (from a world WITHOUT
   this fix), --world picks the world db to read.
Out (this folder only; the lead merges / copies): pending-merchants165-updates.json (SQL updates, reasons
   "True 1.65 merchant fix: ..."), add-merchantfix2_165_brungar.json (MerchantItem: Brungar's list),
   add-merchantfix2_165_anyrealm.json (ItemTemplate: the Realm 0 copies), add-merchantgoods165.json patched (hammer
   row out, rows of crafted items sold to another realm repointed). Rows of other add-files that need the same
   repoint are listed (the SQL update repoints them on the next start; patching them makes a fresh world right on its
   first start too): --patch-other-addfiles patches them as well.
Rerun after vendor_work/make_goods_content.py (it rewrites add-merchantgoods165.json).
"""
import collections, json, os, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
WORLD = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
SNAPSHOT = os.path.join(HERE, 'merchantfix2_165.json')
PENDING = os.path.join(HERE, 'pending-merchants165-updates.json')
OUT_BRUNGAR = 'add-merchantfix2_165_brungar.json'
OUT_ANYREALM = 'add-merchantfix2_165_anyrealm.json'
OWN_ADDFILES = ('add-merchantgoods165.json', 'add-merchantlists165.json')
TAG = 'True 1.65 merchant fix:'
STAMP = '2026-11-06 00:00:00'

BRUNGAR = {'name': 'Forgemaster Brungar', 'region': 151, 'shared_list': 'MidBlades', 'item': 'Brungars_Might',
           'own_list': 'p165list_Brungar', 'old_row': 'p165goods-MidBlades-0-25'}
GEAR_TYPES = set(range(1, 29)) | set(range(31, 39)) | {42, 45}   # weapons, armour, shields, instruments
AMMO = {43, 44}                                                  # arrows, bolts: equippable (quiver), not touched
EQUIP_SLOTS = range(7, 38)                                       # eInventorySlot HorseArmor .. Mythical
COPY_SUFFIX = '_anyrealm'
COPY_PACKAGE = 'True165MerchantAnyRealm'
REALM_NAME = {1: 'Albion', 2: 'Midgard', 3: 'Hibernia'}


def q(v):
    return "'" + str(v).replace("'", "''") + "'"


def equippable(t):
    return t['Object_Type'] in GEAR_TYPES or t['Object_Type'] in AMMO or t['Item_Type'] in EQUIP_SLOTS


def list_realms(c):
    """Realms of the merchants using each list (lower-case list id -> set): a Mob's own list and its NPC template's
    list (a merchant reloads its template's list on respawn); a Realm 0 merchant counts as its region's realm (the
    flight masters, Forgemaster Brungar); housing merchant lists: realm by name, the generic ones every realm."""
    region_realm = collections.defaultdict(collections.Counter)
    for reg, realm in c.execute('SELECT Region, Realm FROM Mob WHERE Realm IN (1, 2, 3)'):
        region_realm[reg][realm] += 1
    region_realm = {k: v.most_common(1)[0][0] for k, v in region_realm.items()}
    tmpl = {tid: lst for tid, lst in c.execute("SELECT TemplateId, ItemsListTemplateID FROM NpcTemplate "
                                               "WHERE COALESCE(ItemsListTemplateID, '') <> ''")}
    realms = collections.defaultdict(set)
    for realm, reg, lst, tid in c.execute('SELECT Realm, Region, ItemsListTemplateID, NPCTemplateID FROM Mob'):
        r = realm if realm in (1, 2, 3) else region_realm.get(reg, 0)
        for l in (lst, tmpl.get(tid)):
            if l and r:
                realms[l.lower()].add(r)
    for lst in tmpl.values():
        l = lst.lower()
        if l.startswith('housing_'):
            realms[l] |= {1} if '_alb_' in l else {2} if '_mid_' in l else {3} if '_hib_' in l else {1, 2, 3}
    return realms


def decide(world):
    c = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    c.row_factory = sqlite3.Row
    cols = [r[1] for r in c.execute('PRAGMA table_info(ItemTemplate)')]
    items = {r['Id_nb'].lower(): dict(r) for r in c.execute('SELECT * FROM ItemTemplate')}
    products = {r[0].lower() for r in c.execute('SELECT Id_nb FROM CraftedItem')}
    # a crafted item that is also a recipe ingredient keeps its own id on every merchant (ingredients match by id: a
    # Realm 0 copy would stop other realms' alchemists using lifebane and the greater poisons, 2026-11-06)
    ingredients = {r[0].lower() for r in c.execute('SELECT IngredientId_nb FROM CraftedXItem')}
    if os.path.exists(os.path.join(HERE, 'add-recipes165-craftedxitem.json')):
        ingredients |= {r['IngredientId_nb'].lower() for r in json.load(open(os.path.join(HERE, 'add-recipes165-craftedxitem.json'), encoding='utf-8'))['rows']}

    # 1. Brungar's own list: the shared list's stock (without the hammer) + the hammer in its slot
    b = BRUNGAR
    mob = c.execute('SELECT Mob_ID, ItemsListTemplateID FROM Mob WHERE Name = ? AND Region = ?',
                    (b['name'], b['region'])).fetchone()
    assert mob is not None, 'Forgemaster Brungar not in this world'
    assert (mob['ItemsListTemplateID'] or '').lower() == b['shared_list'].lower(), \
        f"Brungar's list is {mob['ItemsListTemplateID']} - decide from a world without this fix"
    stock = [dict(r) for r in c.execute('SELECT ItemTemplateID, PageNumber, SlotPosition FROM MerchantItem WHERE '
                                        'lower(ItemListID) = lower(?) ORDER BY PageNumber, SlotPosition',
                                        (b['shared_list'],))]
    assert any(r['ItemTemplateID'].lower() == b['item'].lower() for r in stock), 'the hammer is not on the shared list'
    brungar_rows = [{'MerchantItem_ID': f"p165fix2-{b['own_list']}-{r['PageNumber']}-{r['SlotPosition']}",
                     'ItemListID': b['own_list'], 'ItemTemplateID': r['ItemTemplateID'], 'PageNumber': r['PageNumber'],
                     'SlotPosition': r['SlotPosition'], 'LastTimeRowUpdated': STAMP} for r in stock]

    # 2. non-equippable goods sold to another realm
    realms = list_realms(c)
    to_zero, to_copy = {}, collections.defaultdict(set)
    arrows = collections.Counter()
    for mid, lst, iid in c.execute('SELECT MerchantItem_ID, ItemListID, ItemTemplateID FROM MerchantItem'):
        t = items.get(iid.lower())
        if t is None or t['Realm'] == 0 or lst.lower() == b['shared_list'].lower() and iid.lower() == b['item'].lower():
            continue
        other = realms.get(lst.lower(), set()) - {t['Realm']}
        if not other:
            continue
        if t['Object_Type'] in AMMO:
            arrows['rows'] += 1
            for r in other:
                arrows[REALM_NAME[r]] += 1
            continue
        if equippable(t):
            continue
        if t['Id_nb'].lower() in products:
            if t['Id_nb'].lower() not in ingredients:
                to_copy[t['Id_nb']].add(lst)
        else:
            to_zero[t['Id_nb']] = t['Name']
    copies = []
    for iid in sorted(to_copy, key=str.lower):
        new = iid + COPY_SUFFIX
        assert new.lower() not in items, f'{new} already exists'
        row = {k: items[iid.lower()][k] for k in cols}
        row.update({'Id_nb': new, 'ItemTemplate_ID': new, 'Realm': 0, 'PackageID': COPY_PACKAGE,
                    'LastTimeRowUpdated': STAMP})
        copies.append(row)
    snap = {'_about': 'build_merchantfix2_165.py decisions (owner 2026-11-06); rerun the builder to rewrite its outputs',
            'brungar': {'mob_id': mob['Mob_ID'], 'rows': brungar_rows},
            'realm_zero': dict(sorted(to_zero.items(), key=lambda kv: kv[0].lower())),
            'anyrealm': {iid: sorted(to_copy[iid], key=str.lower) for iid in sorted(to_copy, key=str.lower)},
            'copies': copies,
            'report': {'cross_realm_arrow_rows (rows; per merchant realm selling them)': dict(arrows)}}
    c.close()
    with open(SNAPSHOT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(snap, f, ensure_ascii=False, indent=1)
        f.write('\n')
    return snap


def write_json(path, doc):
    raw = open(path, 'rb').read() if os.path.exists(path) else b''
    nl = b'\r\n' if b'\r\n' in raw else b'\n'
    out = json.dumps(doc, ensure_ascii=False, indent=1).encode('utf-8').replace(b'\n', nl)
    with open(path, 'wb') as f:
        f.write(out + nl)


def main():
    world = sys.argv[sys.argv.index('--world') + 1] if '--world' in sys.argv else WORLD
    snap = decide(world) if '--decide' in sys.argv or not os.path.exists(SNAPSHOT) else \
        json.load(open(SNAPSHOT, encoding='utf-8'))
    b = BRUNGAR
    copy_of = {iid.lower(): iid + COPY_SUFFIX for iid in snap['anyrealm']}

    updates = [
        {'sql': f"UPDATE Mob SET ItemsListTemplateID = {q(b['own_list'])} WHERE Name = {q(b['name'])} AND "
                f"Region = {b['region']} AND lower(ItemsListTemplateID) = lower({q(b['shared_list'])})",
         'reason': f"{TAG} Forgemaster Brungar gets his own merchant list {b['own_list']} (MidBlades + his period "
                   f"item Brungar's Might); MidBlades is shared by 12 other Midgard sword merchants (owner 2026-11-06: "
                   f"\"Aren sword vendor has a random lvl 45 hammer\")"},
        {'sql': f"DELETE FROM MerchantItem WHERE lower(ItemListID) = lower({q(b['shared_list'])}) AND "
                f"lower(ItemTemplateID) = lower({q(b['item'])})",
         'reason': f"{TAG} Brungar's Might off the shared list MidBlades (Aren, Vordn, Orm... sold it); it stays on "
                   f"Forgemaster Brungar's own list {b['own_list']} (add-merchantfix2_165_brungar.json)"}]
    updates += [{'sql': f"UPDATE ItemTemplate SET Realm = 0 WHERE Id_nb = {q(iid)} AND Realm <> 0",
                 'reason': f"{TAG} {name} is merchant goods no one equips, sold by several realms' merchants: no realm "
                           f"(the tooltip showed another realm; owner 2026-11-06)"}
                for iid, name in snap['realm_zero'].items()]
    names = {r['Id_nb'][:-len(COPY_SUFFIX)]: r['Name'] for r in snap['copies']}
    updates += [{'sql': f"UPDATE MerchantItem SET ItemTemplateID = {q(iid + COPY_SUFFIX)} WHERE lower(ItemTemplateID) = "
                        f"lower({q(iid)}) AND lower(ItemListID) IN ({', '.join(q(l.lower()) for l in lists)})",
                 'reason': f"{TAG} merchants of another realm sell the no-realm copy {iid + COPY_SUFFIX} of {names[iid]} "
                           f"(the crafted item keeps its realm for its crafters; owner 2026-11-06: the tooltip showed "
                           f"another realm)"}
                for iid, lists in snap['anyrealm'].items()]
    write_json(PENDING, {'_about': 'Written by build_merchantfix2_165.py: append these to true165-rules.json "updates" '
                                   '(after the existing entries; they are idempotent). Also install '
                                   + OUT_BRUNGAR + ' and ' + OUT_ANYREALM + '.',
                         'updates': updates})
    write_json(os.path.join(HERE, OUT_BRUNGAR), {
        'table': 'MerchantItem', 'key': 'MerchantItem_ID',
        'reason': "True 1.65 merchant fix: Forgemaster Brungar's own merchant list (the MidBlades stock plus his period "
                  "item Brungar's Might; owner 2026-11-06)", 'rows': snap['brungar']['rows']})
    write_json(os.path.join(HERE, OUT_ANYREALM), {
        'table': 'ItemTemplate', 'key': 'Id_nb',
        'reason': 'True 1.65 merchant fix: no-realm copies of crafted dyes, enamels and poisons that merchants sell to '
                  'another realm (owner 2026-11-06)', 'rows': snap['copies']})

    # add-file merchant rows: the launcher inserts them after the updates, so they must carry the fix themselves
    patch_all = '--patch-other-addfiles' in sys.argv
    lists_of = {iid.lower(): {l.lower() for l in lists} for iid, lists in snap['anyrealm'].items()}
    for name in sorted(os.listdir(HERE)):
        if not (name.startswith('add-') and name.endswith('.json')) or name in (OUT_BRUNGAR, OUT_ANYREALM):
            continue
        path = os.path.join(HERE, name)
        doc = json.load(open(path, encoding='utf-8'))
        if not isinstance(doc, dict) or doc.get('table') != 'MerchantItem':
            continue
        keep, hits = [], []
        for row in doc['rows']:
            lst, iid = row['ItemListID'].lower(), row['ItemTemplateID'].lower()
            if lst == b['shared_list'].lower() and iid == b['item'].lower():
                hits.append(row['MerchantItem_ID'] + ' (removed)')
                continue
            if iid in copy_of and lst in lists_of[iid]:
                hits.append(row['MerchantItem_ID'])
                row['ItemTemplateID'] = copy_of[iid]
            keep.append(row)
        if not hits:
            continue
        if name in OWN_ADDFILES or patch_all:
            doc['rows'] = keep
            write_json(path, doc)
            print(f'{name}: patched {len(hits)} rows: {", ".join(hits[:6])}{" ..." if len(hits) > 6 else ""}')
        else:
            print(f'{name}: {len(hits)} rows need the same patch (the SQL update fixes them from the next start; '
                  f'--patch-other-addfiles patches them now)')
    print(f"updates {len(updates)}: Brungar list + hammer removal 2, Realm 0 items {len(snap['realm_zero'])}, "
          f"no-realm copies {len(snap['copies'])}; Brungar's list rows {len(snap['brungar']['rows'])}; "
          f"cross-realm arrow/bolt rows (not touched) {next(iter(snap['report'].values()))}")


if __name__ == '__main__':
    main()
