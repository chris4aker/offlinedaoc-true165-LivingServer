"""True 1.65 Darkness Falls spawn trim (GitHub issue #39, owner 2026-11-11: "keep the trimming for Darkness Falls only for
now"). Issue #39: roaming monsters have too many spawns, because several spawn sources were stacked (Public_DB / Atlas,
the true165-restored rows from add-restoredspawns165.json, BestiaryG9 = one spawn per CapnBry radar sighting).

Areas: the Darkness Falls dungeon (region 249, all of it) and the three home-realm DF entrances outside (the
DFEnterJumpPoint ZonePoints: Midgard - Vale of Mularn 760600,700475; Albion - Camelot Hills 599716,536189; Hibernia -
Connacht 325269,433985), AREA_RADIUS around each entrance.

Per area and monster name (realm-0 rows only):
 1. collapse: two rows of the same name from DIFFERENT spawn sources within COLLAPSE units (|dZ| < COLLAPSE) are one
    spawn: the lower source goes (Public_DB > old base rows (no package) > true165-restored > BestiaryG9);
 2. density cap against the CapnBry radar (period-data\\archive\\capnbry\\sightings_index.json, zone-local -> region via
    the Zones table, zones 154/158 skipped, Snowdonia shift as build_keepguards165.py):
      S      = radar spots (sightings clustered at SPOT units),
      near   = our rows of that name where the radar looked (within NEAR units, |dZ| < 800, of a radar sighting of
               ANY monster), radar = the radar's sightings of that name;
      f      = min(1, FACTOR * radar / near) when radar >= MIN_SIGHTINGS, else 1 (no radar evidence: left alone);
               the radar saw only ~25% of the DF dungeon, so the density is measured where it looked, not as a count;
      target = max(S, ceil(N * f), ceil(FACTOR * S)), never more than N: a name is trimmed only when it is denser
               than FACTOR x the radar where the radar looked AND more than FACTOR x the radar's spot count;
    the cut is spread over the camps (rows linked within CAMP units): every camp keeps at least one monster (gamebot
    camps stay where they are, only stacked camps get thinner); inside a camp the lower source goes first, then the
    most crowded row.
 Never trimmed: named monsters (capital first letter), any class other than DOL.GS.GameNPC (epics, bosses, merchants,
 guards), rows with a brain or a path (encounters, patrols), quest monsters (classic-quests.json QuestMonsterIds,
 kill/drop targets of classic-quests.json, DataQuest and add-primaquests165.json target names), any other package
 (ClassicQuest, Yggdrasil, Prima, period-new ...).

Removal: rows of add-restoredspawns165.json leave that file (both copies; the launcher deletes a row its add-file no
longer lists). Base rows (Public_DB, BestiaryG9, no package) need a remove.where rule in true165-rules.json: written to
pending-dfspawntrim165-rules.json (merge its "where" entry into true165-rules.json "remove" -> "where", replacing an
older entry whose reason starts with TAG; --merge-rules does that in both copies).

Rerunnable: rows this pass removed earlier are read back (offline_true165_removed rows with this TAG, and the
add-file rows kept in dfspawntrim165/removed-addrows.json), so a rerun after a refresh gives the same files.
Reads the world database read-only.
python -I build_dfspawntrim165.py [--db path] [--report-only] [--merge-rules]
"""
import argparse, collections, json, math, os, sqlite3, statistics

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
DB = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
CAPNBRY = os.path.join(CV, 'development-source', 'research', 'period-data', 'archive', 'capnbry', 'sightings_index.json')
QUEST_CONFIG = os.path.join(CV, 'runtime', 'server', 'classic-quests.json')
ADDFILE = 'add-restoredspawns165.json'
WORK = os.path.join(HERE, 'dfspawntrim')
SIDECAR = os.path.join(WORK, 'removed-addrows.json')
REPORT = os.path.join(WORK, 'REPORT.txt')
PENDING = os.path.join(HERE, 'pending-dfspawntrim165-rules.json')
TAG = 'True 1.65 Darkness Falls spawn trim'

AREA_RADIUS = 3500
AREAS = {   # name: (region, centre x, y) - centre None = the whole region
    'Darkness Falls (region 249)': (249, None, None),
    'Midgard DF entrance (Vale of Mularn)': (100, 760600, 700475),
    'Albion DF entrance (Camelot Hills)': (1, 599716, 536189),
    'Hibernia DF entrance (Connacht)': (200, 325269, 433985),
}
COLLAPSE, SPOT, NEAR, CAMP = 300, 500, 1000, 300
FACTOR, MIN_SIGHTINGS = 1.5, 4
ZONE_SHIFT = {12: (16384, 0)}
SKIP_ZONES = {154, 158}
TRIMMABLE = {'Public_DB': 4, None: 3, '': 3, 'true165-restored': 2, 'BestiaryG9': 1}   # source rank (higher stays)


def load_json(path):
    with open(path, 'rb') as f:
        raw = f.read()
    return json.loads(raw), (b'\r\n' in raw[:200])


def save_json(path, data, crlf):
    text = json.dumps(data, ensure_ascii=False, indent=1) + '\n'
    if crlf:
        text = text.replace('\n', '\r\n')
    with open(path, 'wb') as f:
        f.write(text.encode('utf-8'))


def quest_names(con):
    names = set()
    cq = json.load(open(QUEST_CONFIG, encoding='utf-8'))

    def walk(o):
        if isinstance(o, dict):
            for k, v in o.items():
                if k in ('Mob', 'Name', 'Target', 'Kill', 'MobName', 'Monster') and isinstance(v, str):
                    names.add(v.lower())
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)
    walk(cq['Quests'])
    targets = [r[0] for r in con.execute('SELECT TargetName FROM DataQuest')]
    targets += [r.get('TargetName') for r in json.load(open(os.path.join(RUNTIME, 'add-primaquests165.json'),
                                                            encoding='utf-8'))['rows']]
    for t in targets:
        for part in (t or '').split('|'):
            n = part.split(';')[0].strip()
            if n:
                names.add(n.lower())
    return names, set(cq['QuestMonsterIds'])


def clusters(points, radius, dz=None):
    out = []
    for p in sorted(points):
        for cl in out:
            q = cl[0]
            if math.hypot(q[0] - p[0], q[1] - p[1]) < radius and (dz is None or abs(q[2] - p[2]) < dz):
                cl.append(p)
                break
        else:
            out.append([p])
    return out


def camps(rows):
    """Single-link groups of rows within CAMP units (|dZ| < 400)."""
    parent = list(range(len(rows)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for i, a in enumerate(rows):
        for j in range(i + 1, len(rows)):
            b = rows[j]
            if abs(a['X'] - b['X']) < CAMP and abs(a['Y'] - b['Y']) < CAMP and abs(a['Z'] - b['Z']) < 400 \
                    and math.hypot(a['X'] - b['X'], a['Y'] - b['Y']) < CAMP:
                parent[find(i)] = find(j)
    groups = collections.defaultdict(list)
    for i, r in enumerate(rows):
        groups[find(i)].append(r)
    return sorted(groups.values(), key=lambda g: min(r['Mob_ID'] for r in g))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--db', default=DB)
    ap.add_argument('--report-only', action='store_true')
    ap.add_argument('--merge-rules', action='store_true', help='also write the rule into both true165-rules.json')
    a = ap.parse_args()
    con = sqlite3.connect('file:' + a.db.replace('\\', '/') + '?mode=ro', uri=True)
    con.row_factory = sqlite3.Row
    cols = [r[1] for r in con.execute('PRAGMA table_info("Mob")')]
    sel = 'Mob_ID,Name,X,Y,Z,Region,Realm,PackageID,ClassType,Brain,PathID'

    # the world as it was before this pass: live rows + rows this pass archived + add-file rows it took out
    world = {r['Mob_ID']: dict(r) for r in con.execute(f'SELECT {sel} FROM Mob WHERE Region IN (1,100,200,249)')}
    has_removed = con.execute("SELECT 1 FROM sqlite_master WHERE name='offline_true165_removed'").fetchone()
    if has_removed:
        for (rj,) in con.execute("SELECT RowJson FROM offline_true165_removed WHERE SourceTable='Mob' AND Reason LIKE ?",
                                 (TAG + '%',)):
            row = json.loads(rj)
            world.setdefault(row['Mob_ID'], {k: row.get(k) for k in sel.split(',')})
    sidecar = json.load(open(SIDECAR, encoding='utf-8')) if os.path.exists(SIDECAR) else {'rows': []}
    for item in sidecar['rows']:
        row = item['row']
        world.setdefault(row['Mob_ID'], {k: row.get(k) for k in sel.split(',')})
    add_data, add_crlf = load_json(os.path.join(HERE, ADDFILE))
    add_ids = {r['Mob_ID'] for r in add_data['rows']} | {it['row']['Mob_ID'] for it in sidecar['rows']}

    qnames, qids = quest_names(con)
    zones = {r['ZoneID']: (r['RegionID'], r['OffsetX'] * 8192, r['OffsetY'] * 8192) for r in con.execute('SELECT * FROM Zones')}
    sight = collections.defaultdict(list)
    for mob in json.load(open(CAPNBRY, encoding='utf-8')).values():
        seen = set()
        for zid, x, y, z, lv in mob['seen']:
            if zid in SKIP_ZONES or zid not in zones:
                continue
            dx, dy = ZONE_SHIFT.get(zid, (0, 0))
            reg, ox, oy = zones[zid]
            p = (ox + x + dx, oy + y + dy, z)
            if p not in seen:
                seen.add(p)
                sight[(reg, mob['name'].lower())].append(p)

    def protected(r):
        return (r['Name'][:1].isupper() or r['ClassType'] != 'DOL.GS.GameNPC' or (r['Brain'] or '') or (r['PathID'] or '')
                or r['Mob_ID'] in qids or r['Name'].lower() in qnames or r['PackageID'] not in TRIMMABLE)

    rank = lambda r: TRIMMABLE.get(r['PackageID'], 9)
    removed, report = {}, []
    for area, (reg, cx, cy) in AREAS.items():
        inside = lambda x, y: cx is None or math.hypot(x - cx, y - cy) <= AREA_RADIUS
        rows = [r for r in world.values() if r['Region'] == reg and (r['Realm'] or 0) == 0 and inside(r['X'], r['Y'])]
        by = collections.defaultdict(list)
        for r in rows:
            by[r['Name'].lower()].append(r)
        # where the radar looked: our rows within NEAR of a sighting of ANY monster there (the radar user was close)
        seen_all = [p for (r_, n_), ps in sight.items() if r_ == reg for p in ps if inside(p[0], p[1])]
        looked = {r['Mob_ID'] for r in rows if any(abs(r['X'] - p[0]) < NEAR and abs(r['Y'] - p[1]) < NEAR
                                                   and abs(r['Z'] - p[2]) < 800 for p in seen_all)}
        report.append(f'== {area}: {len(rows)} monsters, {len(seen_all)} radar sightings, '
                      f'{len(looked)} of our monsters where the radar looked')
        area_before = area_after = 0
        for name, ms in sorted(by.items(), key=lambda kv: (-len(kv[1]), kv[0])):
            ms = sorted(ms, key=lambda r: r['Mob_ID'])
            n0 = len(ms)
            gone = {}
            # 1. collapse rows of different sources standing on the same spot
            pairs = []
            for i, p in enumerate(ms):
                for q in ms[i + 1:]:
                    if (p['PackageID'] or '') != (q['PackageID'] or '') and abs(p['Z'] - q['Z']) < COLLAPSE:
                        d = math.hypot(p['X'] - q['X'], p['Y'] - q['Y'])
                        if d < COLLAPSE:
                            pairs.append((d, p['Mob_ID'], q['Mob_ID'], p, q))
            for d, _, _, p, q in sorted(pairs, key=lambda t: t[:3]):
                if p['Mob_ID'] in gone or q['Mob_ID'] in gone:
                    continue
                cand = [x for x in (p, q) if not protected(x)]
                if not cand:
                    continue
                lose = cand[0] if len(cand) == 1 else (p if rank(p) < rank(q) else q)   # equal rank: the later id
                gone[lose['Mob_ID']] = f'duplicate of {(p if lose is q else q)["Mob_ID"]} ({int(d)} units, other source)'
            left = [r for r in ms if r['Mob_ID'] not in gone]
            # 2. density cap against the radar
            s = [p for p in sight.get((reg, name), []) if inside(p[0], p[1])]
            spots = len(clusters(s, SPOT)) if s else 0
            near = [r for r in left if r['Mob_ID'] in looked]
            f = min(1.0, FACTOR * len(s) / len(near)) if len(s) >= MIN_SIGHTINGS and near else 1.0
            n1 = len(left)
            target = min(n1, max(spots, math.ceil(n1 * f - 1e-9), math.ceil(FACTOR * spots)))
            free = [r for r in left if not protected(r)]
            cut = min(n1 - target, len(free))
            if cut > 0:
                groups = camps(left)
                keep_share = target / n1
                # each camp keeps round(len * share), at least 1 and its protected rows; largest remainder to hit target
                quota = []
                for g in groups:
                    prot = sum(1 for r in g if protected(r))
                    want = len(g) * keep_share
                    quota.append([max(1, prot, math.floor(want)), want - math.floor(want), g])
                total = sum(q[0] for q in quota)
                for q in sorted(quota, key=lambda q: (-q[1], min(r['Mob_ID'] for r in q[2]))):
                    if total >= target:
                        break
                    if q[0] < len(q[2]):
                        q[0] += 1
                        total += 1
                for keep, _, g in quota:
                    drop = len(g) - keep
                    if drop <= 0:
                        continue

                    def crowd(r, g=g):
                        return min((math.hypot(r['X'] - o['X'], r['Y'] - o['Y']) for o in g if o is not r), default=1e9)
                    order = sorted((r for r in g if not protected(r)), key=lambda r: (rank(r), crowd(r), r['Mob_ID']))
                    for r in order[:drop]:
                        gone[r['Mob_ID']] = (f'density: {n1} rows, radar {len(s)} sightings / {spots} spots, '
                                             f'{len(near)} of ours where it looked; kept {target}')
            n2 = n0 - len(gone)
            area_before += n0
            area_after += n2
            if gone or n0 >= 5:
                pk = dict(collections.Counter(r['PackageID'] or 'base' for r in ms))
                report.append(f'  {name:32} {n0:4} -> {n2:4}  radar {len(s):3} sightings {spots:3} spots, '
                              f'ours where radar looked {len(near):3}, f {f:.2f}  {pk}')
            for mid, why in gone.items():
                removed[mid] = (area, name, why)
        report.append(f'  TOTAL {area_before} -> {area_after}')

    # outputs: add-file rows leave the file, base rows get a remove.where rule
    from_add = sorted(m for m in removed if m in add_ids)
    from_base = sorted(m for m in removed if m not in add_ids)
    report.append(f'removed: {len(removed)} ({len(from_add)} from {ADDFILE}, {len(from_base)} base rows by rule)')
    os.makedirs(WORK, exist_ok=True)
    with open(REPORT, 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(report) + '\n\n')
        for mid in sorted(removed, key=lambda m: removed[m][:2] + (m,)):
            area, name, why = removed[mid]
            fh.write(f'{mid}\t{area}\t{name}\t{"add-file" if mid in add_ids else "rule"}\t{why}\n')
    print('\n'.join(report))
    if a.report_only:
        return

    old = {it['row']['Mob_ID']: it for it in sidecar['rows']}
    new_sidecar = []
    for folder in (HERE, RUNTIME):
        path = os.path.join(folder, ADDFILE)
        data, crlf = load_json(path)
        rows = data['rows']
        for mid, it in sorted(old.items(), key=lambda kv: kv[1]['index']):       # put back what an earlier run took
            if mid not in removed and all(r['Mob_ID'] != mid for r in rows):
                rows.insert(min(it['index'], len(rows)), it['row'])
        keep = []
        for i, r in enumerate(rows):
            if r['Mob_ID'] in removed:
                if folder == HERE:
                    new_sidecar.append({'index': i, 'row': r})
            else:
                keep.append(r)
        for mid, it in old.items():                               # removed earlier and still removed
            if mid in removed and folder == HERE and all(x['row']['Mob_ID'] != mid for x in new_sidecar):
                new_sidecar.append(it)
        if [r['Mob_ID'] for r in keep] != [r['Mob_ID'] for r in load_json(path)[0]['rows']]:   # only when it changed
            data['rows'] = keep
            save_json(path, data, crlf)
        print(f'{path}: {len(rows) - len(keep)} rows taken out ({len(keep)} rows)')
    with open(SIDECAR, 'w', encoding='utf-8') as fh:
        json.dump({'about': f'{TAG}: rows build_dfspawntrim165.py took out of {ADDFILE} (index = place in the file)',
                   'rows': sorted(new_sidecar, key=lambda it: it['index'])}, fh, ensure_ascii=False, indent=1)
        fh.write('\n')

    rule = {'table': 'Mob', 'key': 'Mob_ID',
            'where': "Region IN (1, 100, 200, 249) AND Realm = 0 AND Mob_ID IN (" +
                     ','.join(f"'{m}'" for m in from_base) + ')',
            'reason': f'{TAG} (GitHub issue #39, owner 2026-11-11 "keep the trimming for Darkness Falls only for now"): '
                      f'{len(from_base)} stacked monster spawns in the Darkness Falls dungeon and at its three home-realm '
                      f'entrances - same-name rows of overlapping sources on one spot, and camps denser than '
                      f'{FACTOR}x the CapnBry radar where it looked; every camp keeps a monster; named, quest and '
                      f'encounter monsters untouched; build_dfspawntrim165.py, dfspawntrim\\REPORT.txt'}
    pending = {'_about': f'Written by build_dfspawntrim165.py. Merge: add "where" to true165-rules.json "remove" -> '
                         f'"where" (both copies), replacing any entry whose reason starts with "{TAG}".',
               'where': [rule] if from_base else []}
    with open(PENDING, 'w', encoding='utf-8') as fh:
        json.dump(pending, fh, ensure_ascii=False, indent=1)
        fh.write('\n')
    print(f'{PENDING}: rule for {len(from_base)} base rows')
    if a.merge_rules:
        for folder in (HERE, RUNTIME):
            path = os.path.join(folder, 'true165-rules.json')
            rules, crlf = load_json(path)
            rules['remove']['where'] = [w for w in rules['remove']['where'] if not w['reason'].startswith(TAG)] + pending['where']
            save_json(path, rules, crlf)
            print(f'{path}: rule merged')


if __name__ == '__main__':
    main()
