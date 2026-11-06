"""True 1.65 milegates ("gate forts") and border keep doors owned by their realm (owner 2026-11-06: "make sure bot navmeshes
respect the gate fort gates and can go thru them but enemy factions have to attack the doors first if they want through";
scope: every milegate and keep door).

The server (GameServer/gameobjects/GameRealmGate.cs, DoorMgr.LoadDoor) loads a Door row OUTSIDE every keep whose Realm is
1-3 as a realm gate in True 1.65 Mode: its realm passes (players open it, bots walk through), enemies must break it (health
of a level 1 frontier keep door, 10,000), broken it stands open until repaired. Every other door stays realm 6 ("Door") and
unchanged. Keep doors (rows inside a keep's area) are GameKeepDoors and take their keep's realm already; nothing to set.

Reference = the ORIGINAL 1.65 client's doors as OpenDAoC-BuildNav exported them with the navmeshes (base\\zones\\zoneNNN.doors:
each door's spot from the fixture's NIF door nodes), cross-checked against the 1.64 client the Godot client draws (fixture
Unique Id x 100 + the NIF's door group number, pipeline add_fixture_uids.py): every door below is drawn by both, within
~60 units. Door ids are the ids the client sends (zone x 1,000,000 + fixture Unique Id x 100 + door number).

1. Realm for the existing milegate and border keep door rows (realm 6 today: anyone opened them):
   Hadrian's Wall (Albion): 15000401/02, 15000501/02; Odin's Gate (Midgard): 115000101/02; Emain Macha (Hibernia):
   214000101/02, 214000201/02; border keeps: Castle Sauvage 11020501/02 and Snowdonia Fortress 12000101/02 (Albion),
   Svasud Faste 111161301/02 and Vindsaul Faste 102093501/02 (Midgard), Druim Ligen 206016801/02 and Druim Cain
   207156901/02 (Hibernia).
2. The 1.64 navmesh/door rebuild (the lead's pending-navmesh164-updates.json + add-navmesh164-doors.json, 2026-11-06)
   renumbers Odin's duplicate 115000102 row to 115000101 and ADDS the gate forts' wall-walk doors 03/04 (15000403/04,
   15000503/04, 115000103/04, 214000103/04, 214000203/04), Odin's Gate's southern gate fort 115154301-04 and the relic
   inner doors 111068007-09 / 112179307-09. Those rows are not added here again. The realm update below names the gate
   rows too, so on a world that already has them it sets their realm (it does not depend on the renumbering: both of
   Odin's rows are in it). ON A FRESH WORLD the add-files run after every update, so the lead folds Realm (and Health
   10000) into add-navmesh164-doors.json for: 15000403/04 15000503/04 (1), 115000103/04 115154301-04 (2), 214000103/04
   214000203/04 (3). The relic doors stay realm 6 (keep doors take their keep's realm).
3. Rows the client draws that neither the server nor the rebuild has (added here): the border keep side doors 03/04 of
   Castle Sauvage and Snowdonia Fortress (borderkeep1: 11020503/04, 12000103/04 - the four-piece big gate
   DoorRequestHandler mentions; spots from the BuildNav door export, Albion).

python -I build_gates165.py     (rerunnable)
Out: pending-gates165-updates.json (rules updates for the lead to merge, tagged "True 1.65 gates"; order-independent of the
     navmesh164 updates), add-gates165.json (the four border keep side doors).
"""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
EXPORT = os.path.join(CV, 'development-source', 'tools', 'OpenDAoC-BuildNav', 'bin', 'Release', 'net10.0-windows7.0',
                      'base', 'zones')
PENDING = os.path.join(HERE, 'pending-gates165-updates.json')
ADDFILE = os.path.join(HERE, 'add-gates165.json')
TAG = 'True 1.65 gates'
GATE_HEALTH = 10_000          # GameRealmGate: keep_doors_base_health 200 x base level 50 (the server uses its own value)
RELIC_HEALTH = 180_000        # relic_doors_health (GameKeepDoor of a relic keep uses the server property)
ALB, MID, HIB = 1, 2, 3

# Existing rows: id -> (realm, where)
EXISTING = {
    15000401: (ALB, "Hadrian's Wall, Hibernia's milegate"), 15000402: (ALB, "Hadrian's Wall, Hibernia's milegate"),
    15000501: (ALB, "Hadrian's Wall, Midgard's milegate"), 15000502: (ALB, "Hadrian's Wall, Midgard's milegate"),
    115000101: (MID, "Odin's Gate, northern gate fort"), 115000102: (MID, "Odin's Gate, northern gate fort"),
    214000101: (HIB, "Emain Macha, northern gate fort"), 214000102: (HIB, "Emain Macha, northern gate fort"),
    214000201: (HIB, "Emain Macha, southern gate fort"), 214000202: (HIB, "Emain Macha, southern gate fort"),
    11020501: (ALB, 'Castle Sauvage'), 11020502: (ALB, 'Castle Sauvage'),
    12000101: (ALB, 'Snowdonia Fortress'), 12000102: (ALB, 'Snowdonia Fortress'),
    111161301: (MID, 'Svasud Faste'), 111161302: (MID, 'Svasud Faste'),
    102093501: (MID, 'Vindsaul Faste'), 102093502: (MID, 'Vindsaul Faste'),
    206016801: (HIB, 'Druim Ligen'), 206016802: (HIB, 'Druim Ligen'),
    207156901: (HIB, 'Druim Cain'), 207156902: (HIB, 'Druim Cain'),
    # added by the 1.64 navmesh/door rebuild (add-navmesh164-doors.json)
    15000403: (ALB, "Hadrian's Wall, Hibernia's milegate"), 15000404: (ALB, "Hadrian's Wall, Hibernia's milegate"),
    15000503: (ALB, "Hadrian's Wall, Midgard's milegate"), 15000504: (ALB, "Hadrian's Wall, Midgard's milegate"),
    115000103: (MID, "Odin's Gate, northern gate fort"), 115000104: (MID, "Odin's Gate, northern gate fort"),
    115154301: (MID, "Odin's Gate, southern gate fort"), 115154302: (MID, "Odin's Gate, southern gate fort"),
    115154303: (MID, "Odin's Gate, southern gate fort"), 115154304: (MID, "Odin's Gate, southern gate fort"),
    214000103: (HIB, "Emain Macha, northern gate fort"), 214000104: (HIB, "Emain Macha, northern gate fort"),
    214000203: (HIB, "Emain Macha, southern gate fort"), 214000204: (HIB, "Emain Macha, southern gate fort"),
}

# New rows: client id -> (realm, where, export id, export spot filter or None, health)
NEW = {}
for did, where in ((11020503, 'Castle Sauvage'), (11020504, 'Castle Sauvage'),
                   (12000103, 'Snowdonia Fortress'), (12000104, 'Snowdonia Fortress')):
    NEW[did] = (ALB, where, did, None, GATE_HEALTH)



def export_rows(zone):
    path = os.path.join(EXPORT, 'zone%03d.doors' % zone)
    rows = []
    for m in re.finditer(r'VALUES\((\d+), "([^"]+)", (\d+), (\d+), (\d+), (\d+), (\d+),', open(path, encoding='utf-8').read()):
        rows.append(tuple(int(v) if i != 1 else v for i, v in enumerate(m.groups())))
    return rows


def export_spot(export_id, which):
    zone = export_id // 1_000_000
    found = [r for r in export_rows(zone) if r[0] == export_id]
    if which == 'north':
        found = [r for r in found if r[3] >= 600_000]
    elif which == 'south':
        found = [r for r in found if r[3] < 600_000]
    if len(found) != 1:
        raise SystemExit(f'export {export_id} ({which}): {len(found)} rows')
    _, nif, x, y, z, heading, size = found[0]
    return nif, x, y, z, heading


def build():
    updates = []
    by_realm = {}
    for did, (realm, _) in EXISTING.items():
        by_realm.setdefault(realm, []).append(did)
    names = {ALB: 'Albion', MID: 'Midgard', HIB: 'Hibernia'}
    for realm in (ALB, MID, HIB):
        ids = sorted(by_realm[realm])
        places = sorted({EXISTING[i][1] for i in ids})
        updates.append({
            'sql': f'UPDATE Door SET Realm = {realm}, Health = {GATE_HEALTH} WHERE InternalID IN ({", ".join(map(str, ids))}) AND Realm <> {realm}',
            'reason': f"{TAG}: {names[realm]}'s milegates and border keep doors belong to {names[realm]} (GameRealmGate: its realm "
                      f"passes, enemies break them): {'; '.join(places)}"})
    rows = []
    for did, (realm, where, export_id, which, health) in sorted(NEW.items()):
        nif, x, y, z, heading = export_spot(export_id, which)
        rows.append({
            'Door_ID': f'true165-gate-{did}', 'Name': 'door', 'Type': did // 100_000_000, 'X': x, 'Y': y, 'Z': z,
            'Heading': heading, 'InternalID': did, 'Guild': None, 'Level': 20, 'Realm': realm if realm else 6,
            'Flags': 0, 'Locked': 0, 'Health': health, 'IsPostern': 0, 'State': 1,
            'LastTimeRowUpdated': '2026-11-06 00:00:00'})
        print(f'add {did:>10} {names.get(realm, "keep door"):9} {where:38} {nif:18} {x},{y},{z} heading {heading}')
    add = {'table': 'Door', 'key': 'Door_ID',
           'reason': f"{TAG}: Castle Sauvage / Snowdonia Fortress border keep side doors 03/04, drawn by the original client "
                     f"(borderkeep1) but missing from the server; Albion's; spots from the OpenDAoC-BuildNav door export",
           'rows': rows}
    return updates, add


def main():
    updates, add = build()
    with open(PENDING, 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'updates': updates}, f, ensure_ascii=False, indent=1)
    with open(ADDFILE, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(add, f, ensure_ascii=False, indent=1)
    print(f'{len(updates)} updates -> {PENDING}\n{len(add["rows"])} rows -> {ADDFILE}')


if __name__ == '__main__':
    sys.exit(main())
