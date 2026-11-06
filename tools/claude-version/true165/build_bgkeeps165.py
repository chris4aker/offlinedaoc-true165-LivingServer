"""True 1.65 battleground keeps (owner 2026-11-06: "fix the floating lord and add the guards back", "check that the other
bg's still have guards and lords", "fix the doors", "stop the floating").

Reference = the ORIGINAL 1.65 client (test-client-165): its zone fixtures (zones\\zone25x\\csv25x.mpk fixtures.csv) and
the server navmeshes / door export OpenDAoC-BuildNav builds from them (runtime\\server\\navmesh\\zone25x.nav,
OpenDAoC-BuildNav ...\\base\\zones\\zone25x.doors). The base world's keep spawns and doors in Caledon (250), Murdaigean
(251) and Abermenai (253) stand on that geometry (navmesh check 2026-11-06: 15/16, 27/27, 26/26 on the floor), so they
are NOT moved. (The Godot client draws those three keeps turned and, for Caledon and Thidranki, with the wrong keep
model: a client fix, reported separately; first version of this pass had moved the spawns to the Godot drawing.)

1. Thidranki: the lord row added 2026-11-05 (add-thidranki165.json) had copied Abermenai's spot and floated 1,217
   over the ground; it goes to Thidranki's own lord spot (the archived Atlas lord, on the keep tower top, with the three
   archers): add-file row for new worlds, an update for worlds that already have it.
2. Thidranki: the 12 keep guards archived as "Atlas placeholder spawn" ("new mob" rows) come back at their archived
   spots (6 GuardFighters at the gate and in the tower, 6 GuardStaticCasters on the wall-walk): add-bgkeeps165.json.
   Row values copied from the other battlegrounds' guards of the same class (Caledon's Renegade Huscarl / Renegade
   Runemaster rows), level 27 = bracket top + 3 like Abermenai 22 (15-19), Murdaigean 32 (25-29), Caledon 38 (30-35),
   stats as Thidranki's own keep guards (the server sets keep guards' level, realm, name and stats from the keep anyway:
   GameKeepGuard.RefreshTemplate).
3. Doors: superseded 2026-11-06 by pending-navmesh164-updates.json (navmesh164 agent). The reference is now the REAL 1.64
   client ("1.64 real client\DAoC 1.64.zip"): Caledon keeps the 1.64 bfrontkeep1.nif and Thidranki nfrontkeep1.nif (the
   later client's bfrontkeep2/nfrontkeep2 are "New Frontier Keep" models with posterns). Their door 02 rows move to the
   1.64 door02 and the later posterns 250000303/304, 252000303-306 are archived there; the old 250000303 X/Y-swap update
   of this script is dropped. The battleground meshes were rebuilt from the 1.64 client (staged for the lead; the lord and
   all 87 battleground keep NPC spots stand on the new floors).

python -I build_bgkeeps165.py      (rerunnable; checks every Thidranki keep spot on the server navmesh, tolerance 64)
Out: pending-bgkeeps165-updates.json (the lord's rules update for the lead to merge, tagged "True 1.65 BG keeps"),
     add-bgkeeps165.json (the 12 Thidranki guards), add-thidranki165.json (the lord row's spot).
"""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
# the 1.64 battleground mesh once installed (True 1.65 override folder), else the base mesh (spots check on both)
NAVMESH = next(p for p in (os.path.join(CV, 'runtime', 'server', 'navmesh-165', 'zone252.nav'),
                           os.path.join(CV, 'runtime', 'server', 'navmesh', 'zone252.nav')) if os.path.exists(p))
PENDING = os.path.join(HERE, 'pending-bgkeeps165-updates.json')
ADDFILE = os.path.join(HERE, 'add-bgkeeps165.json')
THIDRANKI = os.path.join(HERE, 'add-thidranki165.json')
TAG = 'True 1.65 BG keeps'

THIDRANKI_LORD = ('true165-thidranki-lord', 32296, 38267, 4592, 1068)   # archived Atlas lord 863582fc-...
# Thidranki keep guards archived as "Atlas placeholder spawn" (offline_true165_removed RowKey, class, X, Y, Z, heading)
ATLAS_GUARDS = [
    ('379b93a9-7796-4bc4-a28c-e6af1dd2dd10', 'GuardFighter', 33485, 38252, 3720, 3096),
    ('680ff0fe-56c8-4d63-a959-f9db227f8a0f', 'GuardFighter', 34247, 37179, 3720, 2363),
    ('734ca5f0-2792-42fd-971a-77a666b09df9', 'GuardFighter', 33353, 38186, 3720, 3096),
    ('9145eac4-4c5e-4ddb-94f3-ab301c14d82d', 'GuardFighter', 32308, 38560, 4272, 2070),
    ('c61b3bde-cc63-4bc6-be09-a643f0eb3ddd', 'GuardFighter', 34024, 39282, 3720, 3885),
    ('fbf6d8ec-19ab-4fb5-8a68-e24ef57fe143', 'GuardFighter', 34720, 38637, 3715, 3271),
    ('10abd579-b36f-413b-8c61-0e0b8964ab92', 'GuardStaticCaster', 33679, 39074, 3976, 3791),
    ('2b5ff25d-c932-4407-90f1-1823c38814c5', 'GuardStaticCaster', 33185, 37386, 3975, 2005),
    ('5b850048-b2b8-4884-8cc1-3bb297fbc533', 'GuardStaticCaster', 33971, 38815, 3976, 3470),
    ('8b778475-5d5b-4a0d-b52c-093b10931bc3', 'GuardStaticCaster', 32478, 39130, 3976, 355),
    ('c1192c17-7970-4900-84ae-06aa84405ec8', 'GuardStaticCaster', 33976, 37722, 3976, 2693),
    ('c8187c3f-a92c-4117-8a4b-a6c3545ff8a8', 'GuardStaticCaster', 32441, 37328, 3991, 1549),
]
# the Thidranki keep spawns already in the world (checked on the navmesh too; none needs moving)
THIDRANKI_EXISTING = [
    ('606b14b5-4d63-4185-a0d4-fdf82a28ab8d', 34687, 38050, 3716), ('f00c1b81-bf0f-419e-9b43-80f26db9f5d3', 34396, 37556, 3730),
    ('0849e575-78af-4d6e-9b3b-fc63409e87c3', 32218, 37987, 4592), ('1d1c1d2f-8496-4f4b-9081-15c8dd0dcb43', 32213, 38573, 4592),
    ('a227aada-f492-4111-a487-86375d666b09', 32526, 38271, 4592),
]
THIDRANKI_LEVEL = 27
THIDRANKI_STATS = dict(Strength=71, Constitution=20, Dexterity=29, Quickness=40, Intelligence=58, Piety=57, Empathy=57, Charisma=57)
TEMPLATE = {   # caledon-guard-5 / caledon-guard-23
    'GuardFighter': dict(ClassType='DOL.GS.Keeps.GuardFighter', Name='Renegade Huscarl', Speed=250, Model=185,
                         AggroLevel=90, AggroRange=1000, RespawnInterval=307, Brain='DOL.AI.Brain.KeepGuardBrain',
                         Gender=1, VisibleWeaponSlots=16),
    'GuardStaticCaster': dict(ClassType='DOL.GS.Keeps.GuardStaticCaster', Name='Renegade Runemaster', Speed=0, Model=507,
                              AggroLevel=99, AggroRange=1850, RespawnInterval=335, Brain='DOL.AI.Brain.CasterBrain',
                              Gender=2, VisibleWeaponSlots=34),
}


def navmesh():
    sys.path.insert(0, os.path.join(CV, 'development-source', 'server', 'tools', 'navmesh_splice'))
    from navcomp import NavAreas
    return NavAreas(NAVMESH)


def check_floor(nav, label, x, y, z):
    if nav.locate(x, y, z, tolerance=64) is None:
        raise SystemExit(f'{label} at {x},{y},{z} is not on the Thidranki navmesh floor')


def build():
    nav = navmesh()
    lid, lx, ly, lz, lh = THIDRANKI_LORD
    check_floor(nav, 'Thidranki lord', lx, ly, lz)
    for mid, x, y, z in THIDRANKI_EXISTING:
        check_floor(nav, mid, x, y, z)
    updates = [{'sql': f'UPDATE Mob SET X = {lx}, Y = {ly}, Z = {lz}, Heading = {lh} WHERE Mob_ID = {chr(39)}{lid}{chr(39)}',
                'reason': f"{TAG}: Thidranki lord to Thidranki's own lord spot on the keep tower top (the archived Atlas "
                          f"lord), not Abermenai's spot where it floated 1,217 over the ground"}]
    rows = []
    for n, (key, cls, x, y, z, h) in enumerate(ATLAS_GUARDS, 1):
        check_floor(nav, key, x, y, z)
        row = {'ClassType': '', 'TranslationId': '', 'Name': '', 'Suffix': '', 'Guild': '', 'ExamineArticle': '',
               'MessageArticle': '', 'X': x, 'Y': y, 'Z': z, 'Speed': 0, 'Heading': h, 'Region': 252,
               'Model': 0, 'Size': 50, **THIDRANKI_STATS, 'Level': THIDRANKI_LEVEL, 'Realm': 0, 'EquipmentTemplateID': '',
               'ItemsListTemplateID': None, 'NPCTemplateID': -1, 'Race': 0, 'Flags': 0, 'AggroLevel': 0, 'AggroRange': 0,
               'MeleeDamageType': 2, 'RespawnInterval': 0, 'FactionID': 0, 'BodyType': 0, 'HouseNumber': 0, 'Brain': '',
               'PathID': '', 'OwnerID': '', 'RoamingRange': 0, 'IsCloakHoodUp': 0, 'Gender': 1, 'PackageID': None,
               'VisibleWeaponSlots': 16, 'LastTimeRowUpdated': '2026-11-06 12:00:00',
               'Mob_ID': 'true165-thidranki-guard-%02d' % n}
        row.update(TEMPLATE[cls])
        rows.append(row)
    add = {'table': 'Mob', 'key': 'Mob_ID',
           'reason': f'{TAG}: the 12 Thidranki Faste keep guards (6 fighters at the gate and in the tower, 6 casters on '
                     f'the wall-walk) back at their archived spots (Atlas rows named "new mob"); values copied from the '
                     f'other battlegrounds\' guards of the same class, level {THIDRANKI_LEVEL} = bracket top + 3 like theirs',
           'rows': rows}
    return updates, add


def main():
    updates, add = build()
    with open(PENDING, 'w', encoding='utf-8') as f:
        json.dump({'updates': updates}, f, ensure_ascii=False, indent=1)
    with open(ADDFILE, 'w', encoding='utf-8') as f:
        json.dump(add, f, ensure_ascii=False, indent=1)
    th = json.load(open(THIDRANKI, encoding='utf-8'))
    lid, lx, ly, lz, lh = THIDRANKI_LORD
    for row in th['rows']:
        if row['Mob_ID'] == lid:
            row.update(X=lx, Y=ly, Z=lz, Heading=lh)
    with open(THIDRANKI, 'w', encoding='utf-8') as f:
        json.dump(th, f, ensure_ascii=False, indent=1)
        f.write('\n')
    print(f'{len(updates)} updates -> {os.path.basename(PENDING)}; {len(add["rows"])} rows -> {os.path.basename(ADDFILE)}; '
          f'lord row in {os.path.basename(THIDRANKI)} at {lx},{ly},{lz}')


if __name__ == '__main__':
    main()
