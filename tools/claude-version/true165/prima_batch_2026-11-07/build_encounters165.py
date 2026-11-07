"""True 1.65 Prima encounters, set 2 data (owner 2026-11-07): the Unseelie Envoy convoy route and rows, the Star Lake
procession controller row, and Celain Eirich's row. Read-only on the world; writes into out/ and edits the Celain row of
add-restoredspawns165.json (both copies). Run after build_prima_batch165.py:

    python -I build_encounters165.py <world snapshot db>
"""
import json, math, os, sqlite3, sys, uuid

HERE = os.path.dirname(os.path.abspath(__file__))
T165 = os.path.normpath(os.path.join(HERE, '..'))
CV = os.path.normpath(os.path.join(T165, '..', '..', '..'))
OUT = os.path.join(HERE, 'out')
sys.dont_write_bytecode = True
sys.path.insert(0, T165)
import groundz165 as G   # noqa: E402

NOW = '2026-11-07 12:00:00'
NS = uuid.UUID('6f1d2c58-7a51-4d7e-9c43-1d5b2f0e9a65')
con = sqlite3.connect('file:' + sys.argv[1].replace(chr(92), '/') + '?mode=ro', uri=True)
con.row_factory = sqlite3.Row
mob_cols = [r[1] for r in con.execute('pragma table_info(Mob)')]


def ground(region, x, y, z):
    f = G.floors(region, x, y)
    return int(min(f, key=lambda v: abs(v - z))) if f else None


# --- the convoy route (Freddyshouse 2002-05-05 / 2003-02-04, patch 1.42; ENCOUNTER SPECS.md 'Unseelie Envoy escort')
PATH_ID = 'true165_unseelie_envoy_convoy'
A = (388637, 752506, 5040)     # the base of the Siabra queen's tower, Bog of Cullen (home, rest)
B = (361569, 745050, 4959)     # the land bridge near Siopa
C = (334799, 742184, 6857)     # Lough Gur south-east
D = (313750, 704250, 4400)     # the turnaround by the pooka spawn west of Innis (never through Innis)
INNIS = (333800, 335600, 718900, 721100)
points, dropped = [], 0
for (x0, y0, z0), (x1, y1, z1) in ((A, B), (B, C), (C, D)):
    n = max(1, int(math.hypot(x1 - x0, y1 - y0) // 1500))
    for i in range(n):
        x, y = int(x0 + (x1 - x0) * i / n), int(y0 + (y1 - y0) * i / n)
        assert not (INNIS[0] <= x <= INNIS[1] and INNIS[2] <= y <= INNIS[3]), 'the route crosses Innis'
        z = ground(200, x, y, z0 + (z1 - z0) * i / n)
        if z is None:
            dropped += 1
            continue
        points.append((x, y, z))
zD = ground(200, *D[:2], D[2])
points.append((D[0], D[1], zD if zD is not None else D[2]))
path_rows = [{'PathID': PATH_ID, 'PathType': 2, 'RegionID': 200, 'LastTimeRowUpdated': NOW,
              'Path_ID': str(uuid.uuid5(NS, PATH_ID))}]
pp_rows = []
for step, (x, y, z) in enumerate(points, 1):
    wait = 900 if step == 1 else (60 if step == len(points) else 0)     # CHOICE: rest at home, a short stop at the end
    pp_rows.append({'PathID': PATH_ID, 'Step': step, 'X': x, 'Y': y, 'Z': z, 'MaxSpeed': 180, 'WaitTime': wait,
                    'LastTimeRowUpdated': NOW, 'PathPoints_ID': str(uuid.uuid5(NS, f'{PATH_ID}|{step}'))})
hx, hy, hz = points[0]


def near_home(i):
    a = 2 * math.pi * i / 5
    x, y = int(hx + 180 * math.cos(a)), int(hy + 180 * math.sin(a))
    z = ground(200, x, y, hz)
    return x, y, z if z is not None else hz


rules = []
ENVOY, STALLIONS = '96a684aa-74ce-44d7-9f7c-188451ca22e3', ['d91bd52f-4837-4ae1-b1d4-17ed24eee52a', '470ade65-3d8c-4266-bd71-07e7d58623d6']
rules.append({'sql': f"UPDATE Mob SET Brain='DOL.AI.Brain.UnseelieEnvoyBrain', PathID='{PATH_ID}', PackageID='UnseelieEnvoyConvoy', "
                     f"Level=55, RoamingRange=0, Speed=180, X={hx}, Y={hy}, Z={hz} WHERE Mob_ID='{ENVOY}'",
              'reason': 'True 1.65 Prima encounter (owner 2026-11-07): the Unseelie Envoy walks his convoy route from the Siabra city into '
                        'Lough Gur and back (Freddyshouse 2002-05-05; patch 1.42); Prima level 55'})
for i, sid in enumerate(STALLIONS):
    x, y, z = near_home(i)
    rules.append({'sql': f"UPDATE Mob SET Brain='DOL.AI.Brain.UnseelieConvoyFollowerBrain', PackageID='UnseelieEnvoyConvoy', Level=48, "
                         f"Model=414, RoamingRange=0, Speed=180, X={x}, Y={y}, Z={z} WHERE Mob_ID='{sid}'",
                  'reason': 'True 1.65 Prima encounter (owner 2026-11-07): the supernatural stallions follow the envoy and fight with him; '
                            'Prima level 48; model 414 (the 1.64 client has no 2132; Allakhazam: looks like a pookha)'})
# the 13 botonid seedlings by the two Prima SCOURGIN labels become the scourgin lure (scripts/mobs/BotonidSeedling.cs)
LURES = ['100027648', '100023803', '100026139', '100027977', '100026023', '100025572', '100027727', '100023379', '100024847',
         '100025444', '100024407', '100024326', '100026130']
rules.append({'sql': "UPDATE Mob SET ClassType='DOL.GS.Scripts.BotonidSeedling' WHERE Mob_ID IN (" + ','.join(f"'{m}'" for m in LURES) + ')',
              'reason': 'True 1.65 Prima encounter (owner 2026-11-07): the scourgin hide under these botonid seedlings by the two Prima '
                        'SCOURGIN labels and jump out (Prima SI 2002 Domnann AI; Allakhazam 2003-01-03)'})

# --- new rows: the two siabra escorts (at home with the envoy) and the procession controller
base = dict(con.execute("select * from Mob where Mob_ID=?", (STALLIONS[0],)).fetchone())
mob_rows = []
for i in range(2):
    x, y, z = near_home(i + 2)
    r = {c: base.get(c) for c in mob_cols}
    r.update({'Name': 'siabra escort', 'Model': 337, 'Size': 46, 'Level': 48, 'NPCTemplateID': 60165982, 'FactionID': 107, 'BodyType': 6,
              'MeleeDamageType': 2, 'Speed': 180, 'AggroLevel': 80, 'AggroRange': 500, 'Brain': 'DOL.AI.Brain.UnseelieConvoyFollowerBrain',
              'PackageID': 'UnseelieEnvoyConvoy', 'RoamingRange': 0, 'RespawnInterval': 300, 'X': x, 'Y': y, 'Z': z, 'Heading': 0,
              'EquipmentTemplateID': 'da0dca15-0ae0-4341-9292-24c0f46e837c', 'PathID': '', 'LastTimeRowUpdated': NOW,
              'Mob_ID': str(uuid.uuid5(NS, f'siabra escort|{i}'))})
    mob_rows.append(r)
cbase = dict(con.execute("select * from Mob where Mob_ID='100027804'").fetchone())
ctl = {c: cbase.get(c) for c in mob_cols}
ctl.update({'ClassType': 'DOL.GS.Scripts.StarLakeProcession165', 'Name': 'Star Lake procession', 'Model': 1, 'Size': 1, 'Level': 1, 'Flags': 28,
            'NPCTemplateID': -1, 'FactionID': 0, 'Brain': '', 'AggroLevel': 0, 'AggroRange': 0, 'RoamingRange': 0, 'RespawnInterval': -1,
            'X': 427068, 'Y': 405631, 'Z': 4441, 'Heading': 0, 'Region': 181, 'EquipmentTemplateID': '', 'PackageID': 'true165-prima-encounter',
            'PathID': '', 'LastTimeRowUpdated': NOW, 'Mob_ID': str(uuid.uuid5(NS, 'Star Lake procession'))})
mob_rows.append(ctl)

# --- set 3 (PrimaEncounters165c.cs): Thergus the Inspirer, the standby ogre squad, Gormror's and Hamorik's controllers
tbase = dict(con.execute("select * from Mob where NPCTemplateID=60167050 and Region=51 order by Mob_ID limit 1").fetchone())


def mob(base_row, key, **kw):
    r = {c: base_row.get(c) for c in mob_cols}
    r.update({'RoamingRange': 0, 'PathID': '', 'LastTimeRowUpdated': NOW, 'Mob_ID': str(uuid.uuid5(NS, key))})
    r.update(kw)
    return r


# Thergus: Prima SI 2002 (Outerward of Krondon, spot 5); a peaceful speaker (no period level or stats; level 50 does not
# matter while PEACE); heading 1024 faces west, toward the march route
mob_rows.append(mob(tbase, 'Thergus the Inspirer', ClassType='DOL.GS.Scripts.ThergusOgreWar165', Name='Thergus the Inspirer',
                    NPCTemplateID=60167043, Model=919, Size=104, Level=50, Flags=16, FactionID=8, Brain='', AggroLevel=0,
                    AggroRange=0, RespawnInterval=300, X=386650, Y=483150, Z=3119, Heading=1024, Region=51,
                    PackageID='true165-prima-encounter'))
# the standby squad Prima drew on the Aldland map: 1 squad leader + 9 threshers in a 3x3 block behind it (north), heading
# 0 toward the orcs; squad-linked aggro (Freddyshouse 2003-02-26); live thrawn values: speed 191, aggro 200/500, 80 s
squad = dict(ClassType='DOL.GS.GameNPC', Brain='DOL.AI.Brain.PrimaSquadBrain165', PackageID='ThrawnOgreStandby', FactionID=8,
             Flags=0, AggroLevel=200, AggroRange=500, Speed=191, RespawnInterval=80, Heading=0, Region=51, Z=3280, BodyType=6)
mob_rows.append(mob(tbase, 'thrawn ogre squad leader|standby', Name='thrawn ogre squad leader', NPCTemplateID=60167043,
                    Model=919, Size=104, Level=47, X=367050, Y=503640, **squad))
for i, lvl in enumerate([44, 45, 46, 47, 48, 44, 45, 46, 48]):
    mob_rows.append(mob(tbase, f'thrawn ogre thresher|standby|{i}', Name='thrawn ogre thresher', NPCTemplateID=60167050,
                        Model=919, Size=103 + i % 6, Level=lvl, X=367050 + 120 * (i % 3 - 1), Y=503640 - 120 * (i // 3 + 1), **squad))
# the hidden event controllers (invisible, untargetable, peaceful; their brains run the events)
hidden = dict(Model=1, Size=1, Level=1, Flags=28, NPCTemplateID=-1, FactionID=0, Brain='', AggroLevel=0, AggroRange=0,
              RespawnInterval=-1, EquipmentTemplateID='', PackageID='true165-prima-encounter')
mob_rows.append(mob(cbase, 'Gormror raid', ClassType='DOL.GS.Scripts.GormrorRaid165', Name='Gormror raid',
                    X=277037, Y=330107, Z=5214, Heading=2048, Region=151, **hidden))
mob_rows.append(mob(cbase, 'Tower Spirit', ClassType='DOL.GS.Scripts.HamorikRitual165', Name='Tower Spirit',
                    X=371520, Y=411072, Z=3486, Heading=831, Region=51, **hidden))


sys.path.insert(0, HERE)
import template_split165 as TSPLIT   # noqa: E402
enc_templates = TSPLIT.split(con, mob_rows, NOW, 'primaenc', 916700000)

# existing rows the encounter rules change (the rules run before the server loads them, then a ReplaceMobValues template
# put its own values back): the envoy's level and pace, the stallions' look and pace, Celain's and Zargin's factions.
# Each gets its own template copy with the rule's values; a rules update points the row at it (Celain's row lives in
# add-restoredspawns165.json, edited below).
CELAIN, ZARGIN = 'c866c1b9-f221-4a2b-9ace-8de25a36e80e', '0ce52dd6-27c5-49a6-8a56-e5b869fcf60d'
RULE_VALUES = {ENVOY: {'Level': 55, 'Speed': 180}, STALLIONS[0]: {'Model': 414, 'Level': 48, 'Speed': 180},
               STALLIONS[1]: {'Model': 414, 'Level': 48, 'Speed': 180}, CELAIN: {'FactionID': 55}, ZARGIN: {'FactionID': 155}}
rule_rows = []
for mid, values in RULE_VALUES.items():
    row = con.execute('select * from Mob where Mob_ID=?', (mid,)).fetchone()
    assert row is not None, mid
    r = dict(row)
    r.update(values)
    rule_rows.append(r)
enc_templates += TSPLIT.split(con, rule_rows, NOW, 'primaencrule', 916800000)
template_of = {r['Mob_ID']: r['NPCTemplateID'] for r in rule_rows}
for r in rule_rows:
    if r['Mob_ID'] != CELAIN:
        rules.append({'sql': f"UPDATE Mob SET NPCTemplateID={r['NPCTemplateID']} WHERE Mob_ID='{r['Mob_ID']}'",
                      'reason': 'True 1.65 Prima encounter (fixed for 0.5): its own NPC template, so the shared template no '
                                'longer puts back the level, look, pace or faction the encounter rules give it'})


def addfile(name, table, key, reason, rows):
    json.dump({'table': table, 'key': key, 'reason': reason, 'rows': rows},
              open(os.path.join(OUT, name), 'w', encoding='utf-8', newline='\n'), indent=1, ensure_ascii=False)


addfile('add-primapaths165.json', 'Path', 'Path_ID', 'True 1.65 Prima encounters (2026-11-07): the Unseelie Envoy convoy route', path_rows)
addfile('add-primapathpoints165.json', 'PathPoints', 'PathPoints_ID',
        'True 1.65 Prima encounters (2026-11-07): the Unseelie Envoy convoy route points (navmesh floor every ~1500 units, never through Innis)',
        pp_rows)
addfile('add-primaenctemplates165.json', 'NpcTemplate', 'NpcTemplate_ID',
        "True 1.65 Prima encounters (2026-11-07, fixed for 0.5): own NPC templates for encounter rows on another monster's "
        'template (Thergus the Inspirer and the squad leader came up as "thrawn ogre guardsman")', enc_templates)
addfile('add-primaencounters165.json', 'Mob', 'Mob_ID',
        'True 1.65 Prima encounters (2026-11-07): the siabra escorts of the Unseelie Envoy convoy and the Star Lake procession controller',
        mob_rows)
json.dump({'rules': rules}, open(os.path.join(HERE, 'encounters165_generated.json'), 'w', encoding='utf-8'), indent=1)

# --- Celain Eirich's row lives in add-restoredspawns165.json (the add-file overwrites a rules update every refresh)
CELAIN = 'c866c1b9-f221-4a2b-9ace-8de25a36e80e'
for folder in (os.path.join(CV, 'runtime', 'true165'), T165):
    p = os.path.join(folder, 'add-restoredspawns165.json')
    raw = open(p, 'rb').read().decode('utf-8')
    d = json.loads(raw)
    row = next(r for r in d['rows'] if r['Mob_ID'] == CELAIN)
    row.update({'ClassType': 'DOL.GS.Scripts.CelainEirich165', 'Brain': 'DOL.AI.Brain.CelainEirichBrain', 'FactionID': 55,
                'NPCTemplateID': template_of[CELAIN]})
    nl = '\r\n' if '\r\n' in raw else '\n'
    open(p, 'w', encoding='utf-8', newline=nl).write(json.dumps(d, indent=1, ensure_ascii=False) + ('\n' if raw.endswith(('\n',)) else ''))
print('path points', len(pp_rows), 'dropped (no floor)', dropped, '| home', (hx, hy, hz), '| rows', len(mob_rows), '| rules', len(rules))
