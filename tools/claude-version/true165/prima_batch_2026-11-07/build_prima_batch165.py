"""True 1.65 Prima batch (owner 2026-11-07): the monsters the Prima guides list that our world lacked, the Atlas drop
items we lacked, their Atlas drop tables, and the wolf drop stack fix. Read-only on the world; writes content files.

    python -I build_prima_batch165.py <world snapshot db> <results json> <out folder>

Inputs: the agents' results (items: one verdict per Atlas item; spawns: RESTORE / PLACE / SKIP per monster).
Outputs (in <out folder>, a copy of the content folder layout):
  add-primaitems165.json   ItemTemplate rows (period Allakhazam page / Uthgard page / plain loot), PackageID True165PrimaItem
  add-primaspawns165.json  Mob rows: restored archived rows (true165-restored) and new period spawns (true165-prima-new)
  drops165.json            the existing file plus Atlas drops (new monsters, and live monsters' Atlas items we now have)
  true165-rules.json       the existing file plus the wolf drop stack fix (2 SQL updates)
  report.json              every decision
Owner rules: no invented gear stats (stat items without a period source are not built); vendor trash with no period
price gets an estimated price (marked); CapnBry radar for positions only (the agents cluster patrols, never one spawn
per sighting); coin: the server's level formula with the body type's coin chance (humanoids, undead...)."""
import collections, datetime, json, math, os, re, shutil, sqlite3, statistics, sys, uuid

HERE = os.path.dirname(os.path.abspath(__file__))
T165 = os.path.normpath(os.path.join(HERE, '..'))
VW = os.path.join(T165, 'vendor_work')
CV = os.path.normpath(os.path.join(T165, '..', '..', '..'))
PT = os.path.join(CV, 'development-source', 'research', 'period-data', 'prima-tools', 'out')
sys.dont_write_bytecode = True
sys.path.insert(0, VW)
import build_vendoritems165 as V   # noqa: E402
import pageparse                  # noqa: E402

SNAP, RESULTS, OUT = sys.argv[1:4]
NOW = '2026-11-07 12:00:00'
NS = uuid.UUID('6f1d2c58-7a51-4d7e-9c43-1d5b2f0e9a65')
REALM_NAME = {0: 'All', 1: 'Albion', 2: 'Midgard', 3: 'Hibernia'}
ITEM_PKG, NEW_PKG, RESTORE_PKG = 'True165PrimaItem', 'true165-prima-new', 'true165-restored'


def n(s):
    return re.sub(r'[^a-z0-9]', '', (s or '').lower())


def slug(s):
    return re.sub(r'[^a-z0-9]+', '_', (s or '').lower()).strip('_')[:60]


res = json.load(open(RESULTS, encoding='utf-8'))
# the re-check (owner 2026-11-07: a second, more thorough pass over what the first skipped): its item verdicts come after
# the first pass's (a build wins over a skip), its looks fix the icons, its monsters and the script-free bosses are added
RECHECK = os.path.join(HERE, 'recheck_results.json')
rc = json.load(open(RECHECK, encoding='utf-8')) if os.path.exists(RECHECK) else {'items': [], 'icons': [], 'monsters': [], 'boss': []}
res['items'] = res['items'] + rc['items']
ZONE_REGION = json.load(open(os.path.join(HERE, 'zone_regions.json'), encoding='utf-8'))
for z in rc['monsters']:
    res['spawns'].append(dict(z, region=ZONE_REGION.get(z['label'])))
# owner 2026-11-07: the encounters with no period script go in now; the scripted ones come with their scripts
SCRIPT_FREE_BOSSES = {'yecats', 'aine', 'bodb', 'earl gerald'}
# scripted encounters whose scripts exist (scripts/true165/PrimaEncounters165.cs): their own spawn rows, with the brain
ENC = json.load(open(os.path.join(HERE, 'encounters165.json'), encoding='utf-8'))
SCRIPT_FREE_BOSSES |= set(ENC['spawn_overrides'])
for z in rc['boss']:
    keep = [m for m in z.get('monsters', []) if (m.get('final_name') or m['prima_name']).lower() in SCRIPT_FREE_BOSSES]
    if keep:
        res['spawns'].append({'label': 'boss: ' + z['label'], 'monsters': keep, 'region': keep[0].get('region')})
LOOK_FIX = {}     # Id_nb -> look_from (the icon re-check)
for b in rc['icons']:
    for it in b.get('items', []):
        m = re.search(r'Id_nb:\s*([A-Za-z0-9_]+)', it.get('notes') or '')
        if m and it.get('look_from'):
            LOOK_FIX[m.group(1)] = it['look_from']
os.makedirs(OUT, exist_ok=True)
con = sqlite3.connect('file:' + SNAP.replace(chr(92), '/') + '?mode=ro', uri=True)
con.row_factory = sqlite3.Row
report = {'items': [], 'spawns': [], 'drops': [], 'wolf_fix': None, 'counts': {}}

# ================================================================ 1. items
V.DB = SNAP
V.W = V.World()
W = V.W
pages = {}
for l in open(os.path.join(VW, 'period_item_pages.jsonl'), encoding='utf-8'):
    r = json.loads(l)
    pages[r['key']] = r
parsed = {k: pageparse.parse(r) for k, r in pages.items()}
item_cols = W.cols
existing_ids = {r['Id_nb'].lower() for r in W.rows}
by_name = collections.defaultdict(list)
for r in W.rows:
    by_name[n(r['Name'])].append(r)

# plain loot siblings for the look of a built plain item (never quest / token / untradable items)
plain = [r for r in W.rows if r['Object_Type'] == 0 and r['Item_Type'] == 40 and r['IsTradable'] and r['Model']
         and not r['Id_nb'].lower().startswith(('cq_', 'quest', 'token')) and 'quest' not in (r['PackageID'] or '').lower()]


def trash_sibling(name, price):
    words = n(name) and [w for w in re.findall(r'[a-z]+', name.lower()) if len(w) > 2]
    last = words[-1] if words else ''
    same_last = [r for r in plain if re.findall(r'[a-z]+', r['Name'].lower())[-1:] == [last]]
    if same_last:
        return min(same_last, key=lambda r: abs((r['Price'] or 0) - price)), f'same last word "{last}"'
    shared = [r for r in plain if set(words) & set(re.findall(r'[a-z]+', r['Name'].lower()))]
    if shared:
        return min(shared, key=lambda r: abs((r['Price'] or 0) - price)), 'shared name word'
    return min(plain, key=lambda r: abs((r['Price'] or 0) - price)), 'nearest price (look is a guess)'


def build_with_look(rec, p, st, bon, unknown, e2, sname, look_id):
    look = next((r for r in W.rows if r['Id_nb'].lower() == look_id.lower()), None)
    if look is None:
        return None, f'look_from {look_id} not in the world'
    keep = V.siblings
    V.siblings = lambda *a, **k: [look]
    try:
        return V.build(rec, p, st, bon, unknown, e2, sname, parsed, pages, NOW)
    finally:
        V.siblings = keep


_atlas = json.load(open(os.path.join(PT, 'atlas_loot.json'), encoding='utf-8'))['entries']


def atlas_low_level(item_name):
    k = n(item_name)
    lows = []
    for e in _atlas:
        if any(k == n(x) for ro in e['rolls'] for band in (ro.get('items_by_band') or []) for x in band):
            m = re.findall(r'\d+', e.get('levels') or '')
            if m:
                lows.append(min(int(v) for v in m))
    return min(lows) if lows else None


def apply_look(row, look_id, ent=None):
    src_row = next((r for r in W.rows if r['Id_nb'].lower() == (look_id or '').lower()), None)
    if src_row is None or not src_row['Model']:
        return False
    for col in ('Model', 'Extension', 'Color', 'Effect', 'Emblem'):
        row[col] = src_row[col]
    if ent is not None:
        ent['look_from'] = f'{src_row["Id_nb"]} (chosen by the re-check: {src_row["Name"]})'
    return True


def finish(row):
    row.update({'PackageID': ITEM_PKG, 'MaxCount': 1, 'PackSize': 1, 'IsPickable': 1, 'IsDropable': 1,
                'CanDropAsLoot': 1, 'IsTradable': 1, 'LastTimeRowUpdated': NOW})
    return row


fragment_names = {n(it.get('atlas_name')) for b in res['items'] for it in b.get('items', []) if it['verdict'] == 'FRAGMENT'}
built = {}           # Id_nb -> row
name_to_id = {}      # n(atlas name) and n(built name) -> Id_nb
ic = collections.Counter()
for b in res['items']:
    for it in b.get('items', []):
        v = it['verdict']
        ic[v] += 1
        ent = {'atlas_name': it.get('atlas_name'), 'item': it.get('item'), 'verdict': v, 'evidence': it.get('evidence'),
               'notes': it.get('notes')}
        report['items'].append(ent)
        keys = {n(it.get('item')) or n(it.get('atlas_name'))} | ({n(it.get('atlas_name'))} - fragment_names)
        keys -= {''}
        if v == 'EXISTS_AS':
            eid = (it.get('existing_id') or '').strip()
            if eid.lower() in existing_ids:
                for k in keys:
                    name_to_id.setdefault(k, next(r['Id_nb'] for r in W.rows if r['Id_nb'].lower() == eid.lower()))
            else:
                ent['problem'] = f'existing_id {eid} not in the world'
            continue
        if v not in ('BUILD_PAGE', 'BUILD_RAW', 'BUILD_TRASH'):
            continue
        realm = it.get('realm') or 0
        row = why = None
        if v == 'BUILD_PAGE' and it.get('page_key') in pages:
            rec = pages[it['page_key']]
            p = parsed[rec['key']]
            st = V.page_stats(p); st['left'] = p['left_hand_usable']
            bon, unknown = V.map_bonuses(p)
            e2 = {'derived': {}}
            row, why = V.build(rec, p, st, bon, unknown, e2, V.strip_suffix(rec['name']), parsed, pages, NOW)
            if row is None and (why or '').startswith('no existing sibling') and it.get('look_from'):
                row, why = build_with_look(rec, p, st, bon, unknown, e2, V.strip_suffix(rec['name']), it['look_from'])
            ent['derived'] = e2.get('derived')
        elif v == 'BUILD_RAW' and it.get('raw'):
            key = 'pa_' + slug(it.get('item') or it['atlas_name'])
            rec = {'key': key, 'name': (it.get('item') or it['atlas_name']), 'realm': REALM_NAME.get(realm, 'All'),
                   'raw': it['raw'], 'prices': [], 'merchants': [], 'found_in': [], 'url': '', 'capture': ''}
            try:
                p = pageparse.parse(rec)
                p.setdefault('prices', []); p.setdefault('levels_of_use', None)
                st = V.page_stats(p); st['left'] = p.get('left_hand_usable', False)
                bon, unknown = V.map_bonuses(p)
                e2 = {'derived': {}}
                row, why = V.build(rec, p, st, bon, unknown, e2, rec['name'], parsed, pages, NOW)
                if row is None and (why or '').startswith('no existing sibling') and it.get('look_from'):
                    row, why = build_with_look(rec, p, st, bon, unknown, e2, rec['name'], it['look_from'])
                ent['derived'] = e2.get('derived')
            except Exception as ex:          # a raw string the parser cannot read
                row, why = None, f'raw not readable: {ex!r}'
        elif v == 'BUILD_TRASH':
            name = (it.get('item') or it['atlas_name']).strip()
            sell = it.get('sell_value_copper')
            if not sell or sell <= 0:
                lvl = int(it.get('level') or 1)
                pool = sorted(r['Price'] // 2 for r in built.values() if r['Object_Type'] == 0 and r['Price'] and abs(r['Level'] - lvl) <= 3)
                sell = pool[len(pool) // 2] if pool else max(1, lvl * lvl // 2)
                it = dict(it, price_source=f'invented: builder estimate, median of {len(pool)} built trash items within 3 levels')
            if False:
                pass
            else:
                price = int(sell) * 2            # merchants pay half (ITEM_SELL_RATIO 50)
                sib, how = trash_sibling(name, price)
                iid = 'p165_prima_' + slug(name)
                row = {c: None for c in item_cols}
                row.update({c: sib[c] for c in item_cols if c in sib.keys()})
                row.update({'Id_nb': iid, 'ItemTemplate_ID': iid, 'Name': name, 'Level': int(it.get('level') or 1),
                            'Price': price, 'Realm': 0, 'Object_Type': 0, 'Item_Type': 40, 'Description': None,
                            'TranslationId': None, 'ClassType': None, 'SpellID': 0, 'Charges': 0, 'MaxCharges': 0,
                            'ProcSpellID': 0, 'Bonus': 0})
                for k in range(1, 11):
                    row[f'Bonus{k}'] = 0; row[f'Bonus{k}Type'] = 0
                ent['look_from'] = f'{sib["Id_nb"]} ({how})'
                ent['price_source'] = it.get('price_source')
        if row is None:
            ent['problem'] = why or 'not built'
            continue
        if v != 'BUILD_TRASH' and it.get('sell_value_copper') and not row.get('Price'):
            row['Price'] = int(it['sell_value_copper']) * 2
            ent['price_source'] = 'source sell value x 2'
        if it.get('look_from'):
            apply_look(row, it['look_from'], ent)
        # a level the page did not give was copied from the borrowed look's item (e.g. 51 for a ring from a level 14
        # monster): the level the research found, else the lowest Atlas level of the monsters that drop it
        if str((ent.get('derived') or {}).get('Level', '')).startswith('from '):
            lv = it.get('level') or atlas_low_level(it.get('item') or it['atlas_name'])
            if lv:
                row['Level'] = int(lv)
                ent['derived']['Level'] = f'research / dropping monster level ({lv}); the page gives none'
        row = finish(row)
        if row['Id_nb'].lower() in existing_ids:
            ent['problem'] = f'Id {row["Id_nb"]} already exists'
            continue
        dup = next((x for x in built.values() if n(x['Name']) == n(row['Name']) and x['Realm'] == row['Realm']), None)
        if dup:
            row = dup
        built[row['Id_nb']] = row
        ent['Id_nb'] = row['Id_nb']
        for k in keys | {n(row['Name'])}:
            name_to_id.setdefault(k, row['Id_nb'])
# gear the page priced at nothing (owner 2026-11-07: no 0-value items): the median price of existing items of the same
# type and slot within 2 levels (a derived value, never a stat; marked in the report)
priced = collections.defaultdict(list)
for r in W.rows:
    if r['Price'] and r['Price'] > 0 and r['Object_Type']:
        priced[(r['Object_Type'], r['Item_Type'])].append((r['Level'], r['Price']))
for row in built.values():
    if row['Price'] or not row['Object_Type']:
        continue
    near = sorted(pr for lv, pr in priced.get((row['Object_Type'], row['Item_Type']), []) if abs(lv - row['Level']) <= 2)
    if near:
        row['Price'] = int(statistics.median(near))
        e = next((x for x in report['items'] if x.get('Id_nb') == row['Id_nb']), None)
        if e is not None:
            e['price_source'] = f'derived: median of {len(near)} existing items of the same type and slot, level +-2'
for iid, look in LOOK_FIX.items():
    if iid in built and apply_look(built[iid], look):
        e = next((x for x in report['items'] if x.get('Id_nb') == iid), None)
        if e is not None:
            e['look_from'] = f'{look} (icon re-check)'
report['counts']['item_verdicts'] = dict(ic)
report['counts']['items_built'] = len(built)

# ================================================================ 2. spawns
mob_cols = [r[1] for r in con.execute('pragma table_info(Mob)')]
live_ids = {r[0] for r in con.execute('select Mob_ID from Mob')}
arch_cols = [r[1] for r in con.execute('pragma table_info(offline_classic165_removed_mobs)')]


def mob_row(kind, bid):
    own = next((dict(r) for r in spawn_rows if r['Mob_ID'] == bid), None) if bid else None
    if own and kind in ('live', 'family'):
        return own
    if kind == 'live':
        r = con.execute('select * from Mob where Mob_ID=?', (bid,)).fetchone()
        return dict(r) if r else None
    if kind == 'family':
        r = con.execute('select * from Mob where Mob_ID=?', (bid,)).fetchone()
        return dict(r) if r else None
    if kind == 'archived':
        r = con.execute('select * from offline_classic165_removed_mobs where Mob_ID=?', (bid,)).fetchone()
        return {c: dict(r).get(c) for c in mob_cols} if r else None
    if kind == 'template':
        t = con.execute('select * from NpcTemplate where TemplateId=?', (bid,)).fetchone()
        if not t:
            return None
        t = dict(t)
        first = lambda s, d: int(str(s).split(';')[0].split('-')[0]) if s not in (None, '') else d
        row = {c: None for c in mob_cols}
        row.update({'ClassType': t['ClassType'] or 'DOL.GS.GameNPC', 'TranslationId': '', 'Name': t['Name'], 'Suffix': '',
                    'Guild': t['GuildName'] or '', 'ExamineArticle': '', 'MessageArticle': '', 'Speed': t['MaxSpeed'] or 0,
                    'Model': first(t['Model'], 0), 'Size': first(t['Size'], 50), 'Strength': t['Strength'],
                    'Constitution': t['Constitution'], 'Dexterity': t['Dexterity'], 'Quickness': t['Quickness'],
                    'Intelligence': t['Intelligence'], 'Piety': t['Piety'], 'Empathy': t['Empathy'],
                    'Charisma': t['Charisma'], 'Level': first(t['Level'], 1), 'Realm': 0,
                    'EquipmentTemplateID': t['EquipmentTemplateID'], 'ItemsListTemplateID': None,
                    'NPCTemplateID': t['TemplateId'], 'Race': t['Race'], 'Flags': t['Flags'] or 0,
                    'AggroLevel': t['AggroLevel'], 'AggroRange': t['AggroRange'], 'MeleeDamageType': t['MeleeDamageType'],
                    'RespawnInterval': 80, 'FactionID': t['FactionID'] or 0, 'BodyType': t['BodyType'], 'HouseNumber': 0,
                    'Brain': '', 'PathID': '', 'OwnerID': '', 'RoamingRange': 0, 'IsCloakHoodUp': 0,
                    'Gender': t['Gender'] or 0, 'VisibleWeaponSlots': t['VisibleWeaponSlots'] or 0})
        return row
    return None


NAMED_ONE = {'baugi', 'urkart', 'caoranach', 'grakin'}
LOWER = {'tempered fetters', 'weathered rope', 'shredded fabrication'}   # ordinary constructs (Prima SI tables Title Case every name)
world_spelling = {}                           # a name the world already has keeps the world's spelling
for (nm,) in con.execute("select Name from Mob where Realm=0 and Name is not null"):
    world_spelling.setdefault(nm.lower(), nm)
named_done = set()                            # a named monster gets one spawn, whichever agents placed it
spawn_rows, sc = [], collections.Counter()
seen_ids = set()
for z in res['spawns']:
    for m in z.get('monsters', []):
        a = m['action']
        sc[a] += 1
        ent = {'zone': z['label'], 'prima_name': m['prima_name'], 'action': a, 'final_name': m.get('final_name'),
               'notes': m.get('notes'), 'position_source': m.get('position_source'), 'rows': 0}
        report['spawns'].append(ent)
        lo, hi = m.get('level_min'), m.get('level_max')
        name = (m.get('final_name') or m['prima_name']).strip()
        if name.lower() in LOWER:
            name = name.lower()
        name = world_spelling.get(name.lower(), name)
        if a == 'RESTORE':
            for mid in m.get('restore_mob_ids') or []:
                if mid in live_ids or mid in seen_ids:
                    continue
                r = con.execute('select * from offline_classic165_removed_mobs where Mob_ID=?', (mid,)).fetchone()
                if not r:
                    ent.setdefault('problems', []).append(f'{mid} not archived')
                    continue
                row = {c: dict(r).get(c) for c in mob_cols}
                row['Name'] = name
                if row['Level'] and lo and hi:
                    row['Level'] = min(max(row['Level'], lo), hi)
                if m.get('aggro_level') is not None:
                    row['AggroLevel'] = m['aggro_level']
                if m.get('speed'):
                    row['Speed'] = m['speed']
                row['PackageID'] = RESTORE_PKG
                spawn_rows.append(row); seen_ids.add(mid); ent['rows'] += 1
        elif a == 'PLACE':
            base = mob_row(m.get('base_kind'), m.get('base_id'))
            if base is None:
                ent['problem'] = f'base {m.get("base_kind")} {m.get("base_id")} not found'
                continue
            pts = m.get('spawns') or []
            lv = list(range(lo, hi + 1)) if lo and hi and hi >= lo else [base['Level'] or 1]
            one = m.get('named') or name.lower() in NAMED_ONE
            if one and name.lower() in named_done:
                ent['problem'] = 'named monster already placed by another agent'
                continue
            if one:
                named_done.add(name.lower())
            if m.get('model'):
                base['Model'] = m['model']
            if m.get('size'):
                base['Size'] = m['size']
            for i, s in enumerate(pts[:1] if one else pts):
                row = dict(base)
                mid = str(uuid.uuid5(NS, f'{name}|{m.get("region")}|{s["x"]}|{s["y"]}'))
                if mid in seen_ids:
                    continue
                row.update({'Name': name, 'X': int(s['x']), 'Y': int(s['y']), 'Z': int(s['z']),
                            'Heading': int(s.get('heading') or 0) % 4096, 'Region': int(m.get('region') or z.get('region') or base['Region']),
                            'Level': lv[i % len(lv)], 'Mob_ID': mid, 'PackageID': NEW_PKG, 'LastTimeRowUpdated': NOW,
                            'PathID': '', 'OwnerID': '', 'HouseNumber': 0, 'Realm': 0 if base['Realm'] is None else base['Realm'],
                            'RoamingRange': int(m.get('roaming_range') if m.get('roaming_range') is not None else (base['RoamingRange'] or 0))})
                if m.get('aggro_level') is not None:
                    row['AggroLevel'] = m['aggro_level']
                if m.get('speed'):
                    row['Speed'] = m['speed']
                if m.get('body_type') is not None:
                    row['BodyType'] = m['body_type']
                if m.get('respawn_seconds'):
                    row['RespawnInterval'] = m['respawn_seconds']
                spawn_rows.append(row); seen_ids.add(mid); ent['rows'] += 1
# every spawn on walkable ground (owner 2026-11-07: bots must be able to hunt them). groundz165 reads the 1.65 / classic
# navmeshes; it cannot read the Shrouded Isles ones (no floor even under monsters that stood there all along), so those
# are checked against the nearest existing monster instead
sys.path.insert(0, T165)
import groundz165 as GZ   # noqa: E402
report['ground'] = {'snapped': [], 'not_on_ground': [], 'si_far_from_others': []}
SI_REGIONS = {51, 151, 181, 50, 60, 62, 150, 160, 161, 180, 190, 191}   # SI zones and dungeons
pre = collections.defaultdict(list)
for rg, x, y, z in con.execute('select Region, X, Y, Z from Mob'):
    if rg in SI_REGIONS:
        pre[rg].append((x, y, z))
for r in spawn_rows:
    if r['Region'] in SI_REGIONS:
        near = min(((math.hypot(x - r['X'], y - r['Y']), abs(z - r['Z'])) for x, y, z in pre[r['Region']]
                    if abs(x - r['X']) < 2000 and abs(y - r['Y']) < 2000), default=None)
        if not near or near[0] > 1500 or near[1] > 400:
            report['ground']['si_far_from_others'].append({'name': r['Name'], 'region': r['Region'], 'x': r['X'], 'y': r['Y'], 'z': r['Z']})
        continue
    fl = GZ.floors(r['Region'], r['X'], r['Y'])
    if fl and min(abs(f - r['Z']) for f in fl) <= 64:
        continue
    moved = None
    for rad in (50, 100, 200, 300, 400):
        for ang in range(0, 360, 15):
            x = int(r['X'] + rad * math.cos(math.radians(ang))); y = int(r['Y'] + rad * math.sin(math.radians(ang)))
            f = GZ.floors(r['Region'], x, y)
            if f:
                fz = min(f, key=lambda v: abs(v - r['Z']))
                if abs(fz - r['Z']) <= 450:
                    moved = (x, y, int(fz)); break
        if moved:
            break
    if moved:
        report['ground']['snapped'].append({'name': r['Name'], 'from': (r['X'], r['Y'], r['Z']), 'to': moved})
        r['X'], r['Y'], r['Z'] = moved
    else:
        report['ground']['not_on_ground'].append({'name': r['Name'], 'region': r['Region'], 'x': r['X'], 'y': r['Y'], 'z': r['Z']})
RADAR = {}
for v in json.load(open(os.path.join(CV, 'development-source', 'research', 'period-data', 'archive', 'capnbry', 'sightings_index.json'),
                        encoding='utf-8')).values():
    RADAR.setdefault(v['name'].lower(), v['name'])
by_low = collections.defaultdict(collections.Counter)
for r in spawn_rows:
    by_low[r['Name'].lower()][r['Name']] += 1
for low, spellings in by_low.items():
    if len(spellings) > 1 or low in RADAR:
        want = RADAR.get(low) or world_spelling.get(low) or spellings.most_common(1)[0][0]
        if low in LOWER:
            want = low
        for r in spawn_rows:
            if r['Name'].lower() == low:
                r['Name'] = want
for r in spawn_rows:
    for col, val in ENC['spawn_overrides'].get(r['Name'].lower(), {}).items():
        r[col] = val
report['counts']['spawn_actions'] = dict(sc)
report['counts']['spawn_rows'] = len(spawn_rows)

# ================================================================ 3. Atlas drops
# the content before this batch (base/, a copy of the pre-batch files) so a rebuild is exact
drops = json.load(open(os.path.join(HERE, 'base', 'drops165.json'), encoding='utf-8'))
mons = drops['monsters']
mkeys = {k.lower(): k for k in mons}
loot = json.load(open(os.path.join(PT, 'atlas_loot.json'), encoding='utf-8'))['entries']
zones = {r['ZoneID']: dict(r) for r in con.execute('select * from Zones')}
zn = {}
for zid, zr in sorted(zones.items(), key=lambda kv: kv[1]['RegionID'] == 163, reverse=True):
    zn[n(zr['Name'])] = zid
ALIAS = {'blackmountainsnorth': 'blackmtnsnorth', 'blackmountainssouth': 'blackmtnssouth', 'mtcollory': 'mountcollory',
         'svealandeast': 'eastsvealand', 'svealandwest': 'westsvealand', 'silvermountains': 'silverminemts',
         'silverminemountains': 'silverminemts', 'keltoifogue': 'keltoifogou', 'coruscatingmines': 'coruscatingmine',
         'darknessfallsdungeon': 'darknessfalls'}


def zone_of_section(sec):
    t = n(re.sub(r'^Dungeon:\s*', '', sec))
    t = ALIAS.get(t, t)
    return zn.get(t)


def in_zone(zid, region, x, y):
    zr = zones[zid]
    return region == zr['RegionID'] and zr['OffsetX'] * 8192 <= x < (zr['OffsetX'] + zr['Width']) * 8192 and \
        zr['OffsetY'] * 8192 <= y < (zr['OffsetY'] + zr['Height']) * 8192


spawned = collections.defaultdict(set)     # zone id -> monster names (live + this batch)
for name, region, x, y in con.execute('select Name, Region, X, Y from Mob'):
    for zid, zr in zones.items():
        if zr['RegionID'] == region and in_zone(zid, region, x, y):
            spawned[zid].add(n(name)); break
for r in spawn_rows:
    for zid, zr in zones.items():
        if zr['RegionID'] == r['Region'] and in_zone(zid, r['Region'], r['X'], r['Y']):
            spawned[zid].add(n(r['Name'])); break
PROTECT_REGIONS = {60, 160, 191}
boss_like = {n(x) for x, in con.execute("select Name from Mob where Region in (60,160,191) or (ClassType not in "
                                         "('DOL.GS.GameNPC','DOL.GS.NightSpawn','DOL.GS.DaySpawn') and ClassType is not null and ClassType<>'')")}
ids_by_name = collections.defaultdict(list)
for r in W.rows:
    ids_by_name[n(r['Name'])].append(r)
for r in built.values():
    ids_by_name[n(r['Name'])].append(r)


GENERIC_PIECES = {"boots", "sleeves", "helm", "leggings", "gloves", "gauntlets", "vest", "pants", "shield", "necklace", "arms",
                  "legs", "hauberk", "jerkin", "cap", "coif", "robe", "tunic", "breeches", "hood", "belt", "cloak", "bracer",
                  "ring", "bracelet", "mantle", "greaves", "vambraces", "chausses", "coat", "shirt", "skirt", "gem"}


def roll_items(roll):
    ch = roll.get('chances') or []
    if not ch:
        return []
    p = statistics.median(ch)
    out = []
    for band in roll.get('items_by_band') or []:
        band = [b for b in band if b and not b.startswith('<')]
        share = p / len(band) if roll.get('mode') in ('single', 'pick') and len(band) > 1 else p
        if roll.get('mode') == 'pick' and roll.get('pick_size', 1) > 1:
            share = p / roll['pick_size']
        first = band[0].split() if band else []
        def full(b):
            if b.lower() in GENERIC_PIECES and len(first) > 1 and first[-1].lower() in GENERIC_PIECES:
                return ' '.join(first[:-1] + [b])
            return b
        out += [(full(b), max(1, min(100, int(round(share))))) for b in band]
    return out


FREESHARD_PKG = {'atlasxpitem', 'dratlasxpitem', 'atlas_orbs_item', 'drmed', 'atlas_trophys'}


def resolve(item, realm, mlevel=None):
    k = n(re.sub(r'\s*\(x\d+\)\s*$', '', item))
    if k == 'moonstone':
        return 'moonstone_gem'
    if k in {n(x) for x in GENERIC_PIECES}:
        return None
    if k in name_to_id:
        return name_to_id[k]
    c = ids_by_name.get(k, [])
    c = [r for r in c if r['Realm'] in (realm, 0) and not r['Id_nb'].lower().startswith('cq_')
         and (r['PackageID'] or '').lower() not in FREESHARD_PKG]
    # the copy nearest the monster's level (the drop fix pass made level copies, e.g. spectral_essence_165l15)
    lv = mlevel or 0
    return sorted(c, key=lambda r: (r['Realm'] != realm, abs((r['Level'] or 0) - lv) if lv else 0, r['Id_nb']))[0]['Id_nb'] if c else None


# items the drop audit graded freeshard-only: their loot rows are removed by the rules (owner 2026-10-31); never add them
EXCLUDE = {x.lower() for x in json.load(open(os.path.join(HERE, 'exclude_drop_items.json'), encoding='utf-8'))}
mob_levels = collections.defaultdict(list)          # lowest spawn level of each monster name (live + this batch)
for name, level in con.execute('select Name, Level from Mob where Level > 0'):
    mob_levels[n(name)].append(level)
for r in spawn_rows:
    if r['Level']:
        mob_levels[n(r['Name'])].append(r['Level'])
item_level = {r['Id_nb'].lower(): r['Level'] for r in W.rows}
item_level.update({k.lower(): r['Level'] for k, r in built.items()})
report['level_gap_skipped'] = []
# owner 2026-11-07: the frukta warg row at level 1 (of 93 in Varulvhamn, the rest 39-43) is a data error -> 39
LEVEL_FIX = {'0e7c959a-c258-485d-9c4d-5d7e884efcfa': 39}
for name, level in con.execute('select Name, Level from Mob where Mob_ID in (%s)' % ','.join('?' * len(LEVEL_FIX)), list(LEVEL_FIX)):
    mob_levels[n(name)].remove(level)
    mob_levels[n(name)].append(39)
# owner: Darkness Falls seals are currency (worth 0 to a merchant) - their item level means nothing; and the two very
# rare Atlas gear drops stay in as the Atlas lists them
GAP_EXEMPT_ITEMS = {'diamondseal', 'emeraldseal', 'sapphireseal'}
GAP_ALLOWED = {('tomb raider scout', 'p165_item_pa_spirit_crafted_shield'), ('gray spectre', 'p165_item_pa_ether_ring')}
wanted = collections.defaultdict(dict)
for e in loot:
    zid = zone_of_section(e['section'])
    if zid is None or n(e['name']) not in spawned.get(zid, set()) or n(e['name']) in boss_like:
        continue
    if zones[zid]['RegionID'] in PROTECT_REGIONS:
        continue
    realm = zones[zid]['Realm'] if zones[zid]['Realm'] in (1, 2, 3) else 0
    for roll in e['rolls']:
        for item, ch in roll_items(roll):
            iid = resolve(item, realm, min(mob_levels.get(n(e['name'])) or [0]) or None)
            if iid and iid.lower() not in EXCLUDE:
                low = min(mob_levels.get(n(e['name'])) or [0]) or None
                if low and (item_level.get(iid.lower()) or 0) - low >= 15 and iid.lower() not in GAP_EXEMPT_ITEMS \
                        and (e['name'].lower(), iid.lower()) not in GAP_ALLOWED:
                    report['level_gap_skipped'].append({'monster': e['name'], 'monster_lowest_level': low, 'item': iid,
                                                        'item_level': item_level.get(iid.lower())})
                    continue
                wanted[e['name']][iid] = max(ch, wanted[e['name']].get(iid, 0))
base_random = collections.Counter()
for tpl, ch in con.execute('select lower(TemplateName), Chance from LootTemplate'):
    if ch is not None and 0 < ch < 100:
        base_random[tpl] += 1
added_rows = 0
for mob, its in sorted(wanted.items()):
    key = mkeys.get(mob.lower())
    created = key is None
    if created:
        key = mob; mons[key] = {'drop_count': 1, 'items': []}; mkeys[mob.lower()] = key
    have = {x[0].lower() for x in mons[key]['items']}
    add = [(iid, ch) for iid, ch in sorted(its.items()) if iid.lower() not in have]
    if not add:
        if created:
            mons.pop(key); mkeys.pop(mob.lower())
        continue
    mons[key]['items'] += [[iid, ch] for iid, ch in add]
    old = mons[key]['drop_count']
    need = base_random[mob.lower()] + sum(1 for _, ch in add if ch < 100)
    mons[key]['drop_count'] = max(old, min(need, 12))
    added_rows += len(add)
    report['drops'].append({'monster': key, 'created': created, 'old_drop_count': old,
                            'drop_count': mons[key]['drop_count'], 'added': add})
for mob, d in ENC['drops'].items():
    key = mkeys.get(mob.lower()) or mob
    mons[key] = {'drop_count': d['drop_count'], 'items': d['items']}
    report['drops'].append({'monster': key, 'created': True, 'old_drop_count': None, 'drop_count': d['drop_count'],
                            'added': d['items'], 'note': d['note']})
drops['source'] += ' + Prima batch 2026-11-07 (Atlas drops of the new monsters and of the Atlas items built)'
report['counts']['drop_rows_added'] = added_rows
report['counts']['level_gap_skipped'] = len(report['level_gap_skipped'])
report['counts']['monsters_with_new_drops'] = len(report['drops'])

# ================================================================ 4. wolf drop stack fix
rules = json.load(open(os.path.join(HERE, 'base', 'true165-rules.json'), encoding='utf-8'))
WOLF = "('wolf_fang','torn_wolf_pelt','chunk_of_wolf_meat')"
fix = [
    {'sql': f"UPDATE Inventory SET Count=1 WHERE lower(ITemplate_Id) IN {WOLF} AND Count>1",
     'reason': 'wolf drop stack fix (owner 2026-11-07): the 3 wolf loot items had PackSize 20/100 with MaxCount 1, so a '
               'single drop came as one non-stacking "(20)" item; the ones already in bags become 1 (what dropped)'},
    {'sql': f"UPDATE ItemTemplate SET PackSize=1 WHERE lower(Id_nb) IN {WOLF} AND PackSize<>1",
     'reason': 'wolf drop stack fix (owner 2026-11-07): wolf fang / torn wolf pelt / chunk of wolf meat drop one at a time'}]
fix.append({'sql': "UPDATE Mob SET Level=39 WHERE Mob_ID='0e7c959a-c258-485d-9c4d-5d7e884efcfa' AND Level=1",
            'reason': 'True 1.65 level fix (owner 2026-11-07): the one frukta warg in Varulvhamn at level 1 (the other 92 are '
                      '39-43) is a data error'})
fix += ENC['rules']
GEN = os.path.join(HERE, 'encounters165_generated.json')
if os.path.exists(GEN):
    fix += json.load(open(GEN, encoding='utf-8'))['rules']
have_sql = {u.get('sql') for u in rules['updates']}
for f in fix:
    if f['sql'] not in have_sql:
        rules['updates'].append(f)
report['wolf_fix'] = fix

# ================================================================ write
def addfile(name, table, key, reason, rows):
    json.dump({'table': table, 'key': key, 'reason': reason, 'rows': rows},
              open(os.path.join(OUT, name), 'w', encoding='utf-8', newline='\n'), indent=1, ensure_ascii=False)


addfile('add-primaitems165.json', 'ItemTemplate', 'Id_nb',
        'True 1.65 Prima batch items (2026-11-07): Atlas drop items our world lacked, from period Allakhazam item pages '
        '(Wayback <= 2003-11) or Uthgard item pages (stats as the page gives them; look from the closest existing item); '
        'plain loot priced from the period sell value, estimated only where no period price exists (marked in the report)',
        sorted(built.values(), key=lambda r: r['Id_nb']))
addfile('add-primaspawns165.json', 'Mob', 'Mob_ID',
        'True 1.65 Prima batch spawns (2026-11-07): monsters the Prima guides list that our world lacked - archived rows '
        'restored (true165-restored) and new spawns at period positions (CapnBry radar camps, Uthgard map dots, Prima / '
        'Kirstena map labels; true165-prima-new); levels, aggression and speed from the Prima guides',
        spawn_rows)
# the two existing files keep their own layout (indent 1, CRLF; drops165.json ends with a newline)
with open(os.path.join(OUT, 'drops165.json'), 'w', encoding='utf-8', newline='\r\n') as f:
    f.write(json.dumps(drops, ensure_ascii=False, indent=1) + '\n')
with open(os.path.join(OUT, 'true165-rules.json'), 'w', encoding='utf-8', newline='\r\n') as f:
    f.write(json.dumps(rules, ensure_ascii=False, indent=1))
json.dump(report, open(os.path.join(OUT, 'report.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False, default=str)
print(json.dumps(report['counts'], indent=1))
