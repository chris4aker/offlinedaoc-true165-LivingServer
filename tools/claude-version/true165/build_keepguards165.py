"""True 1.65 portal keep and border keep guards (owner 2026-11-06: add the missing portal/border keep guards, "period data
for guards for their appearances etc whenever possible").

Source of the rosters and spots = the CapnBry radar archive (period-data\\archive\\capnbry\\sightings_index.json, late
old-frontier era): every guard type seen at level 60+ within 2,500 of each portal keep / border keep, compared with our
Mob rows there. A guard type the period saw but our world lacks at that keep is added at the radar's spots:
  * sightings converted zone-local -> region with the Zones table offsets (OffsetX/Y * 8192); CapnBry's Snowdonia (zone
    12) is 16,384 west of ours (calibrated on 8 unmoved named NPCs there: Sir Defi +16390, Lieutenant Niane +16416,
    Captain Leri +16307, Haegan McLeary +16178 ...), so zone 12 gets +16,384; zones 154/158 are exact repeats of 111/115
    and are skipped; repeated identical spots are counted once (zone 15 also holds a copy 16,384 west of the Midgard
    portal keep, outside the 2,500 radius, so it is not used);
  * sightings within 150 of each other = one guard; its spot = the median of the cluster, Z = the radar's Z;
  * a spot with our guard of the same name within 400, or a guard renamed/placed by this pass within 150, is held;
  * RENAMES (owner 2026-11-06, "so the rosters match 1.65"): one of our realm guards of another type standing within
    100 of a missing type's spot (|dZ| < 300) takes that type's full identity (ClassType, brain, level, template,
    equipment, model ... exactly as this pass's new rows of the type; position and heading kept), e.g. the Hadrian's
    Wall Masters of Runes 1-43 from the radar's Viking Archer spots. Never below the period: a rename that would leave
    its old type under that type's (capped) radar count at the keep is not made (reported). Written as UPDATEs guarded
    by Mob_ID AND the old Name (they apply once) to pending-keepguards165-updates.json; a rerun reads those guards back
    under their old name and leaves this pass's add-file rows out, so it gives the same files after they are applied;
  * WALKER CAP (owner 2026-11-06, "they probably counted walking guards, keep it reasonable"): a type whose radar spot
    count at a keep is more than 1.5 x its count at the same realm's sibling keep (Odin's Gate / Emain portal keeps,
    Hadrian's Wall / Emain, Castle Sauvage / Snowdonia, Svasud / Vindsaul) is capped at the sibling's count (renames +
    new rows together; e.g. Snowdonia's 19 Man at Arms spots -> 7 = Castle Sauvage's). Renames come first; the new rows
    are farthest-point picks among the free spots (seeded with the renamed guards), so the kept ones spread round the
    keep.
Level: realm guards 255 like every other portal/border keep guard of ours (period "immortal", forum t86747 2002-04-30,
t89223 2002-06-03; their NpcTemplates 60157484/60167654 are 255 too); the gate guards "elite guard" (Albion) and
"Viking Eyfrod" (Midgard) at the radar's own level 65 (every sighting 65, two 69/71), they are a different guard: "That
pulls the two elite guards at the doors, not the realm guards. Realm guards don't attack mobs" (Freddyshouse t86819,
2002-05-04) - so they are GameGuard (GuardBrain fights monsters too).
Heading: the nearest existing realm guard's heading within 500, else facing out from the keep centre.
Appearance (the radar has none; no Allakhazam/Uthgard/Prima page gives a model):
  Bowman Commander  NpcTemplate 60157484 (its model list / size / PortalKeepArcherAlb gear, level 255); row values copied
                    from Castle Sauvage's own Bowman Commander row c3b9e34e (region 1).
  Viking Archer     NpcTemplate 60167654 (Vindsaul's Viking Archer row 087b868a copied), level 255.
  Viking Eyfrod     NpcTemplate 60167661 (Norse Male 6 model 158, size 51, Midgard chain + green cloak d7d1f9e6, level 65);
                    row values copied from our one Eyfrod row 7a4429f1 at Vindsaul.
  Bowman, Keep Sentinel (Albion)  no Albion row or template exists: Bowman Commander template 60157484's appearance
                    written into the row (models 32;35;41;43;51;52;716;724, size 50, PortalKeepArcherAlb); Keep Sentinel
                    as an archer like Midgard's Keep Sentinel template 8000 (MidVikingArcher gear).
  Man at Arms       no row/template: the same keep's Knight Commander appearance (template 60157485: models 716;724,
                    PortalKeepFighterAlb) on a copy of that keep's Knight Commander row.
  Viking Jarl       NpcTemplate 60167666 exists but is not used (level 99, aggro 0, no gear, no period source): the same
                    keep's Viking Huscarl appearance (template 60157473: models 160;161;192;193, SvasudGuard gear) on a
                    copy of that keep's Huscarl row.
  elite guard       no period appearance anywhere: Albion's portal keep fighter look (Knight Commander template 60157485:
                    models 716;724, PortalKeepFighterAlb) on a copy of the Eyfrod row (the Midgard gate guard).
Hibernian portal keeps (Odin's Gate, Hadrian's Wall) and Druim Ligen / Druim Cain: the radar has no sightings there, so
nothing is added. Every new spot is checked on the server navmesh (navmesh-165 first, then navmesh; tolerance 64); a spot
off the navmesh is reported and skipped.

python -I build_keepguards165.py [--db PATH] [--report PATH]
    (rerunnable; reads runtime\\data\\opendaoc-165.sqlite3.db read-only, or --db)
Out: add-keepguards165.json (new Mob rows, PackageID true165-keepguards), pending-keepguards165-updates.json (the renames,
rules updates for the lead to merge, tagged "True 1.65 keep guards"); --report PATH also writes the per-keep roster report
(period vs ours before/after, renamed, added, capped, held / blocked spots, navmesh) as JSON.
"""
import json, math, os, sqlite3, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
DB = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
CAPNBRY = os.path.join(CV, 'development-source', 'research', 'period-data', 'archive', 'capnbry', 'sightings_index.json')
NAVDIRS = [os.path.join(CV, 'runtime', 'server', 'navmesh-165'), os.path.join(CV, 'runtime', 'server', 'navmesh')]
NAVTOOLS = os.path.join(CV, 'development-source', 'server', 'tools', 'navmesh_splice')
ADDFILE = os.path.join(HERE, 'add-keepguards165.json')
PENDING = os.path.join(HERE, 'pending-keepguards165-updates.json')
TAG = 'True 1.65 keep guards'
PACKAGE = 'true165-keepguards'
STAMP = '2026-11-06 12:00:00'

RADIUS, CLUSTER, HELD_ANY, HELD_SAME, SNAP = 2500, 150, 100, 400, 160
ZONE_SHIFT = {12: (16384, 0)}          # CapnBry Snowdonia -> ours (see above)
SKIP_ZONES = {154, 158}                # repeats of 111 / 115

# keep: (label, region, centre x, y, realm)  - portal keeps = the Keep table rows, border keeps = the gate forts
KEEPS = {
    'albpk-odins': ("Albion portal keep, Odin's Gate", 100, 596372, 631373, 1),
    'albpk-emain': ('Albion portal keep, Emain Macha', 200, 475844, 343696, 1),
    'midpk-hw': ("Midgard portal keep, Hadrian's Wall", 1, 655269, 293142, 2),
    'midpk-emain': ('Midgard portal keep, Emain Macha', 200, 474129, 295207, 2),
    'castle-sauvage': ('Castle Sauvage (Albion border keep)', 1, 585085, 477504, 1),
    'snowdonia': ('Snowdonia Fortress (Albion border keep)', 1, 528451, 358290, 1),
    'svasud': ('Svasud Faste (Midgard border keep)', 100, 766235, 669173, 2),
    'vindsaul': ('Vindsaul Faste (Midgard border keep)', 100, 704022, 738009, 2),
    'hibpk-odins': ("Hibernia portal keep, Odin's Gate", 100, 596157, 581610, 3),
    'hibpk-hw': ("Hibernia portal keep, Hadrian's Wall", 1, 605589, 293789, 3),
    'druim-ligen': ('Druim Ligen (Hibernian border keep)', 200, 333229, 419539, 3),
    'druim-cain': ('Druim Cain (Hibernian border keep)', 200, 421166, 484998, 3),
}
# realm guard names (for the roster comparison and the "held" test)
GUARD_NAMES = {'Bowman', 'Bowman Commander', 'Man at Arms', 'Knight Commander', 'Master Wizard', 'Master Armsman',
               'elite guard', 'Keep Sentinel', 'Viking Archer', 'Viking Huscarl', 'Viking Jarl', 'Viking Eyfrod',
               'Master of Runes', 'Troll Archer', 'Master Eldritch', 'Master Guardian', 'Master Ranger', 'Champion',
               'Celtic Guardian', 'Sentinel', 'Guardian', 'Keep Sentinel'}
GATE_GUARDS = {'elite guard', 'Viking Eyfrod'}   # level-65 gate guards (any level counted)
# gate guards first (the radar's door spots win over a realm guard cluster next to them), then commanders, archers, fighters
ORDER = ['elite guard', 'Viking Eyfrod', 'Bowman Commander', 'Viking Jarl', 'Viking Archer', 'Keep Sentinel', 'Bowman',
         'Man at Arms']
# the same realm's other portal keep / border keep: the walker sanity check (owner 2026-11-06)
SIBLING = {'albpk-odins': 'albpk-emain', 'albpk-emain': 'albpk-odins', 'midpk-hw': 'midpk-emain',
           'midpk-emain': 'midpk-hw', 'castle-sauvage': 'snowdonia', 'snowdonia': 'castle-sauvage',
           'svasud': 'vindsaul', 'vindsaul': 'svasud', 'hibpk-odins': 'hibpk-hw', 'hibpk-hw': 'hibpk-odins',
           'druim-ligen': 'druim-cain', 'druim-cain': 'druim-ligen'}
CAP_RATIO = 1.5
# our guard rows that may take a period type's identity (realm guards of the portal / border keeps)
RENAMEABLE = {'Bowman Commander', 'Knight Commander', 'Master Wizard', 'Master Armsman', 'Viking Huscarl',
              'Master of Runes', 'Viking Archer', 'Troll Archer', 'Keep Sentinel'}
# the columns a renamed guard takes from its new type (everything but position, heading, region, key, package, path)
IDENTITY = ['ClassType', 'TranslationId', 'Name', 'Suffix', 'Guild', 'ExamineArticle', 'MessageArticle', 'Speed', 'Model',
            'Size', 'Strength', 'Constitution', 'Dexterity', 'Quickness', 'Intelligence', 'Piety', 'Empathy', 'Charisma',
            'Level', 'Realm', 'EquipmentTemplateID', 'ItemsListTemplateID', 'NPCTemplateID', 'Race', 'Flags',
            'AggroLevel', 'AggroRange', 'MeleeDamageType', 'RespawnInterval', 'FactionID', 'BodyType', 'Brain',
            'IsCloakHoodUp', 'Gender', 'VisibleWeaponSlots']

ALB_ARCHER_MODELS = [32, 35, 41, 43, 51, 52, 716, 724]     # NpcTemplate 60157484
ALB_FIGHTER_MODELS = [716, 724]                             # NpcTemplate 60157485
MID_HUSCARL_MODELS = [160, 161, 192, 193]                   # NpcTemplate 60157473
BASE_BC = 'c3b9e34e-ec85-41e4-be6a-2b89c1432030'            # Castle Sauvage Bowman Commander (region 1)
BASE_VA = '087b868a-e781-477a-8a0f-0331e7953901'            # Vindsaul Viking Archer (GameGuard)
BASE_EYFROD = '7a4429f1-521c-4d2e-84cf-243511cdbfb9'        # Vindsaul Viking Eyfrod (GameGuard)


def recipe(name, keep_rows):
    """(base row, overrides, appearance note) for a guard type; keep_rows = our Mob rows at that keep (for KC/Huscarl)."""
    def same_keep(n):
        rows = [r for r in keep_rows if r['Name'] == n]
        cls = max({r['ClassType'] for r in rows}, key=lambda c: sum(r['ClassType'] == c for r in rows))
        return sorted((r for r in rows if r['ClassType'] == cls), key=lambda r: r['Mob_ID'])[0]
    if name == 'Bowman Commander':
        return BASE_BC, dict(Level=255, NPCTemplateID=60157484), 'NpcTemplate 60157484'
    if name in ('Bowman', 'Keep Sentinel'):
        return BASE_BC, dict(Name=name, Level=255, NPCTemplateID=-1, Size=50, EquipmentTemplateID='PortalKeepArcherAlb',
                             models=ALB_ARCHER_MODELS), 'Bowman Commander template 60157484 look (no period appearance)'
    if name == 'Man at Arms':
        base = same_keep('Knight Commander')
        return base['Mob_ID'], dict(Name=name, Level=255, NPCTemplateID=-1, Size=50, EquipmentTemplateID='PortalKeepFighterAlb',
                                    models=ALB_FIGHTER_MODELS), 'Knight Commander template 60157485 look (no period appearance)'
    if name == 'elite guard':
        # weapon display and damage type as the Knight Commanders, whose look this is
        return (BASE_EYFROD, dict(Name=name, Realm=1, Level=65, NPCTemplateID=-1, Size=50,
                                  EquipmentTemplateID='PortalKeepFighterAlb', models=ALB_FIGHTER_MODELS,
                                  Gender=0, VisibleWeaponSlots=240, MeleeDamageType=3),
                'Albion portal keep fighter look, template 60157485 (no period appearance)')
    if name == 'Viking Archer':
        return BASE_VA, dict(Level=255, NPCTemplateID=60167654), 'NpcTemplate 60167654'
    if name == 'Viking Jarl':
        base = same_keep('Viking Huscarl')
        return base['Mob_ID'], dict(Name=name, Level=255, NPCTemplateID=-1, Size=50, EquipmentTemplateID='SvasudGuard',
                                    models=MID_HUSCARL_MODELS), 'Viking Huscarl template 60157473 look (no period appearance)'
    if name == 'Viking Eyfrod':
        return BASE_EYFROD, dict(Level=65, NPCTemplateID=60167661, Model=158, Size=51,
                                 EquipmentTemplateID='d7d1f9e6-91fd-419f-931b-a179c20e566e'), 'NpcTemplate 60167661'
    return None


def make_row(base, over, index):
    """A copy of the base row with the type's overrides; 'models' = the template's model list, taken in turn."""
    row, o = dict(base), dict(over)
    models = o.pop('models', None)
    row.update(o)
    if models:
        row['Model'] = models[index % len(models)]
    for k in ('_prior', '_renamed'):
        row.pop(k, None)
    return row


def sql(v):
    if v is None:
        return 'NULL'
    if isinstance(v, (int, float)):
        return str(v)
    return "'" + str(v).replace("'", "''") + "'"


def prior_renames():
    """{Mob_ID: old name} of the renames in the pending file this script wrote before (WHERE Mob_ID = .. AND Name = ..)."""
    import re
    if not os.path.exists(PENDING):
        return {}
    out = {}
    for u in json.load(open(PENDING, encoding='utf-8'))['updates']:
        m = re.search(r"WHERE Mob_ID = '((?:[^']|'')*)' AND Name = '((?:[^']|'')*)'$", u['sql'])
        if m:
            out[m.group(1).replace("''", "'")] = m.group(2).replace("''", "'")
    return out


def heading_to(x, y, tx, ty):
    return int(round(math.atan2(-(tx - x), ty - y) * 2048 / math.pi)) & 0xFFF


class Navs:
    def __init__(self):
        sys.path.insert(0, NAVTOOLS)
        from navcomp import NavAreas
        self._cls, self._cache = NavAreas, {}

    def get(self, zone):
        if zone not in self._cache:
            path = next((os.path.join(d, 'zone%03d.nav' % zone) for d in NAVDIRS
                         if os.path.exists(os.path.join(d, 'zone%03d.nav' % zone))), None)
            self._cache[zone] = (self._cls(path), path) if path else (None, None)
        return self._cache[zone]

    def floor(self, nav, x, y, z):
        """z when the spot stands on the navmesh (tolerance 64), else the nearest navmesh floor height under/over the
        spot within SNAP (the radar's Z is the player's view of the guard), else None."""
        if nav is None:
            return None
        if nav.locate(x, y, z, tolerance=64) is not None:
            return z
        from navcomp import _inside
        best = None
        for i in nav._grid().get((int(x // nav.CELL), int(y // nav.CELL)), ()):
            _, pts, bb, _, om = nav.polys[i]
            if om or not (bb[0] <= x <= bb[2] and bb[1] <= y <= bb[3]) or not _inside(pts, x, y):
                continue
            h = nav.height(i, x, y)
            if abs(h - z) <= SNAP and (best is None or abs(h - z) < abs(best - z)):
                best = h
        if best is None:
            return None
        zz = int(round(best))
        return zz if nav.locate(x, y, zz, tolerance=64) is not None else None


def build(db=DB):
    con = sqlite3.connect('file:' + db.replace('\\', '/') + '?mode=ro', uri=True)
    con.row_factory = sqlite3.Row
    zones = {r['ZoneID']: (r['RegionID'], r['OffsetX'] * 8192, r['OffsetY'] * 8192, r['Width'] * 8192, r['Height'] * 8192)
             for r in con.execute('SELECT ZoneID, RegionID, OffsetX, OffsetY, Width, Height FROM Zones')}

    def zone_of(region, x, y):
        for zid, (r, ox, oy, w, h) in zones.items():
            if r == region and ox <= x < ox + w and oy <= y < oy + h:
                return zid

    radar = json.load(open(CAPNBRY, encoding='utf-8'))
    # our rows as the world had them before this pass: this pass's own add-file rows left out, and the guards its
    # pending updates rename read back under their old name (so a rerun after the updates gives the same files)
    prior = prior_renames()
    mobs = []
    for r in con.execute('SELECT * FROM Mob WHERE Region IN (1, 100, 200)'):
        m = dict(r)
        if m['PackageID'] == PACKAGE:
            continue
        if m['Mob_ID'] in prior:
            m['Name'], m['_prior'] = prior[m['Mob_ID']], True
        mobs.append(m)
    by_id = {m['Mob_ID']: m for m in mobs}
    navs = Navs()
    reports = {}
    # pass 1: our guards and the radar's sightings per keep
    for key, (label, region, cx, cy, realm) in KEEPS.items():
        near = lambda x, y, r=RADIUS: [m for m in mobs if m['Region'] == region and math.hypot(m['X'] - x, m['Y'] - y) <= r]
        ours = [m for m in near(cx, cy) if m['Name'] in GUARD_NAMES and (m['Level'] or 0) >= 60]
        ours_wide = [m for m in near(cx, cy, RADIUS + 1000) if m['Name'] in GUARD_NAMES and (m['Level'] or 0) >= 60]
        period = {}
        for mob in radar.values():
            seen = set()
            for zid, x, y, z, lv in mob['seen']:
                if zid in SKIP_ZONES or zid not in zones or zones[zid][0] != region:
                    continue
                if lv < 60 and mob['name'] not in GATE_GUARDS:
                    continue
                dx, dy = ZONE_SHIFT.get(zid, (0, 0))
                X, Y = zones[zid][1] + x + dx, zones[zid][2] + y + dy
                if (X, Y, z) in seen or math.hypot(X - cx, Y - cy) > RADIUS:
                    continue
                seen.add((X, Y, z))
                period.setdefault(mob['name'], []).append((X, Y, z, lv))
        reports[key] = {'keep': label, 'region': region, 'centre': (cx, cy), 'realm': realm, 'ours': ours,
                        'ours_wide': ours_wide, 'period': period}

    def clusters_of(sights):
        out = []
        for s in sorted(sights):
            for cl in out:
                if math.hypot(cl[0][0] - s[0], cl[0][1] - s[1]) < CLUSTER:
                    cl.append(s)
                    break
            else:
                out.append([s])
        return [(int(statistics.median(s[0] for s in cl)), int(statistics.median(s[1] for s in cl)),
                 int(statistics.median(s[2] for s in cl))) for cl in out]

    spots = {key: {n: clusters_of(s) for n, s in k['period'].items() if n in GUARD_NAMES} for key, k in reports.items()}

    # pass 2: per keep and missing type: renames of our guards standing on the type's radar spots, then new rows
    rows, updates, report = [], [], {}
    for key, k in reports.items():
        label, region, (cx, cy), realm = k['keep'], k['region'], k['centre'], k['realm']
        ours, ours_wide = k['ours'], k['ours_wide']
        present = {m['Name'] for m in ours}                     # types the keep had: not touched
        rep = {'keep': label, 'period': {}, 'ours_before': {}, 'renamed': {}, 'added': {}, 'capped': {}, 'held': [],
               'rename_blocked': [], 'off_navmesh': []}
        for m in ours:
            rep['ours_before'][m['Name']] = rep['ours_before'].get(m['Name'], 0) + 1
        sibling = SIBLING.get(key)
        cap = {}
        for n, s in spots[key].items():
            rep['period'][n] = {'guards': len(s), 'levels': sorted({x[3] for x in k['period'][n]})}
            sib = len(spots[sibling].get(n, [])) if sibling else 0
            cap[n] = sib if sib and len(s) > CAP_RATIO * sib else len(s)
            if cap[n] != len(s):
                rep['capped'][n] = f"{len(s)} radar spots > {CAP_RATIO} x {KEEPS[sibling][0]}'s {sib}: capped at {sib}"
        count = lambda n: sum(m['Name'] == n for m in ours)
        new_here = []                                           # (x, y, z) of guards renamed / placed in this pass
        for name in [n for n in ORDER if n in spots[key]]:
            if name in present:
                continue
            rec = recipe(name, [m for m in ours if not m.get('_prior') and not m.get('_renamed')])
            if rec is None:
                continue
            base_id, over, look = rec
            renamed, free = [], []
            for (x, y, z) in spots[key][name]:
                if any(math.hypot(nx - x, ny - y) <= CLUSTER and abs(nz - z) < 300 for nx, ny, nz in new_here):
                    rep['held'].append(f'{name} {x},{y},{z}: next to a guard renamed/placed in this pass')
                    continue
                near = [m for m in ours_wide if abs(m['Z'] - z) < 300 and not m.get('_renamed') and
                        math.hypot(m['X'] - x, m['Y'] - y) <= (HELD_SAME if m['Name'] == name else HELD_ANY)]
                if near:
                    m = min(near, key=lambda m: math.hypot(m['X'] - x, m['Y'] - y))
                    d, old = int(math.hypot(m['X'] - x, m['Y'] - y)), m['Name']
                    why = None
                    if old == name:
                        rep['held'].append(f'{name} {x},{y},{z}: {old} {m["Mob_ID"]} {d} away')
                        continue
                    if old not in RENAMEABLE or m['Realm'] != realm:
                        why = 'not a realm guard of this keep'
                    elif count(old) - 1 < cap.get(old, 0):
                        why = f'would leave {count(old) - 1} {old}, the period roster has {cap[old]}'
                    elif len(renamed) >= cap[name]:
                        why = f'{name} cap {cap[name]} reached'
                    if why:
                        rep['rename_blocked'].append(f'{old} {m["Mob_ID"]} on a {name} spot ({d} away): {why}')
                        continue
                    renamed.append((m, old, d))
                    m['Name'], m['_renamed'] = name, True
                    new_here.append((m['X'], m['Y'], m['Z']))
                    continue
                zid = zone_of(region, x, y)
                nav, navpath = navs.get(zid)
                fz = navs.floor(nav, x, y, z)
                if fz is None:
                    rep['off_navmesh'].append(f'{name} {x},{y},{z} zone {zid}: no navmesh floor within {SNAP}')
                    continue
                if fz != z:
                    rep.setdefault('snapped', []).append(f'{name} {x},{y}: radar Z {z} -> navmesh floor {fz}')
                free.append((x, y, fz, os.path.basename(os.path.dirname(navpath)) + '/' + os.path.basename(navpath)))
            # renamed guards take this type's full identity (as its new rows below); position and heading are kept
            for i, (m, old, d) in enumerate(renamed):
                ident = make_row(by_id[base_id], over, i)
                sets = ', '.join(f'"{c}" = {sql(ident[c])}' for c in IDENTITY)
                updates.append({'sql': f"UPDATE Mob SET {sets} WHERE Mob_ID = {sql(m['Mob_ID'])} AND Name = {sql(old)}",
                                'reason': f'{TAG}: {label}: our {old} {m["Mob_ID"]} stands {d} from a CapnBry radar '
                                          f'"{name}" spot: it becomes that 1.65 guard ({look}), position and heading '
                                          f'kept'})
                rep['renamed'].setdefault(f'{old} -> {name}', []).append(m['Mob_ID'])
            # new rows at the free spots: farthest-point picks (seeded with the renamed guards) up to the cap
            room, picked, pool = max(0, cap[name] - len(renamed)), [], list(free)
            seeds = [(m['X'], m['Y']) for m, _, _ in renamed]
            while pool and len(picked) < room:
                ref = seeds + [(p[0], p[1]) for p in picked]
                if ref:
                    best = max(pool, key=lambda p: (min(math.hypot(p[0] - a, p[1] - b) for a, b in ref), p[:2]))
                else:                                           # first: the spot farthest from the spots' centre
                    mx, my = statistics.mean(p[0] for p in pool), statistics.mean(p[1] for p in pool)
                    best = max(pool, key=lambda p: (math.hypot(p[0] - mx, p[1] - my), p[:2]))
                picked.append(best)
                pool.remove(best)
            if pool:
                rep['capped'][name] = (rep['capped'].get(name, f'cap {cap[name]}') +
                                       f'; renamed {len(renamed)} + added {len(picked)}, {len(pool)} free spots unused')
            for n, (x, y, z, navname) in enumerate(sorted(picked), 1):
                guards = [m for m in ours if m['Realm'] == realm and math.hypot(m['X'] - x, m['Y'] - y) <= 500]
                heading = (min(guards, key=lambda m: math.hypot(m['X'] - x, m['Y'] - y))['Heading'] if guards
                           else heading_to(x, y, 2 * x - cx, 2 * y - cy))
                row = make_row(by_id[base_id], over, len(renamed) + n - 1)
                row.update(X=x, Y=y, Z=z, Heading=heading, Region=region, Guild='', PathID='', PackageID=PACKAGE,
                           RoamingRange=0, LastTimeRowUpdated=STAMP,
                           Mob_ID=f"true165-keepguard-{key}-{name.lower().replace(' ', '')}-{n:02d}")
                rows.append(row)
                new_here.append((x, y, z))
                a = rep['added'].setdefault(name, {'count': 0, 'level': row['Level'], 'appearance': look,
                                                   'copied_from': base_id, 'spots': []})
                a['count'] += 1
                a['spots'].append([x, y, z, heading, row['Mob_ID'], navname])
        rep['ours_after'] = {}
        for m in ours:
            rep['ours_after'][m['Name']] = rep['ours_after'].get(m['Name'], 0) + 1
        for name, a in rep['added'].items():
            rep['ours_after'][name] = rep['ours_after'].get(name, 0) + a['count']
        report[key] = rep
    add = {'table': 'Mob', 'key': 'Mob_ID',
           'reason': f'{TAG}: portal keep and border keep guard types the CapnBry radar saw (late old frontiers) and our '
                     f'world lacked (Bowman Commander, Bowman, Man at Arms, elite guard at the Albion portal keeps; Viking '
                     f'Archer, Viking Jarl, Viking Eyfrod at the Midgard portal keeps; Bowman, Man at Arms, Keep Sentinel, '
                     f'elite guard at Castle Sauvage and Snowdonia; Viking Eyfrod at Svasud and Vindsaul) at the radar\'s '
                     f'free spots (spots where one of our guards stands are renamed instead: pending-keepguards165-'
                     f'updates.json), counts capped at the sibling keep\'s where the radar re-counted walkers; realm '
                     f'guards level 255 like the other portal/border keep guards, the gate guards at the radar\'s level '
                     f'65; build_keepguards165.py',
           'rows': rows}
    return add, updates, report


def main():
    db = sys.argv[sys.argv.index('--db') + 1] if '--db' in sys.argv else DB
    add, updates, report = build(db)
    with open(ADDFILE, 'w', encoding='utf-8') as f:
        json.dump(add, f, ensure_ascii=False, indent=1)
        f.write('\n')
    with open(PENDING, 'w', encoding='utf-8') as f:
        json.dump({'updates': updates}, f, ensure_ascii=False, indent=1)
        f.write('\n')
    if '--report' in sys.argv:
        with open(sys.argv[sys.argv.index('--report') + 1], 'w', encoding='utf-8') as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
    for key, rep in report.items():
        print(f"{rep['keep']}: added { {k: a['count'] for k, a in rep['added'].items()} }; renamed "
              f"{ {k: len(v) for k, v in rep['renamed'].items()} }; capped {rep['capped']}; rename blocked "
              f"{len(rep['rename_blocked'])}; held {len(rep['held'])}; off navmesh {len(rep['off_navmesh'])}")
    print(f"{len(add['rows'])} rows -> {os.path.basename(ADDFILE)}; {len(updates)} renames -> {os.path.basename(PENDING)}")


if __name__ == '__main__':
    main()
