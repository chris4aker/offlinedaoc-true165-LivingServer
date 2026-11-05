"""True 1.65: account vault keepers, a quality-of-life exception to 1.65 (owner 2026-11-03: "it would be an exception to
1.65 for qol to trade items between alts for each realm (separate per realm) there were already account vault keepers
and their locations in old server").

The server's AccountVaultKeeper keeps one vault per account AND realm (owner id "<account>_<realm>"), so alts of one
realm share it and the realms stay apart. The old (normal-mode) server had 13 keepers; the ones in True 1.65 regions come
back at their old spots in the three capitals only (owner: "just do the 3 capital cities for each realm"): Virsha in
Camelot City, Janash in Jordheim, Giorny in Tir na Nog. The others (Castle Sauvage, Atlantis, New Frontiers, the event
zones, the tutorial) stay out.

python -I build_accountvaults165.py      (rerunnable)
In: the normal-mode world (normal-mode-archive/data/opendaoc.sqlite3.db).
Out (both copies): add-accountvaults165.json (the three keepers); true165-rules.json: the class-wide removal of account
vault keepers becomes a removal of the ones outside the capitals (a fresh 1.65 world keeps the three from the base).
"""
import json, math, os, sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
NORMAL = os.path.join(CV, 'normal-mode-archive', 'data', 'opendaoc.sqlite3.db')
CLASS = 'DOL.GS.AccountVaultKeeper'
REGIONS = (10, 101, 201)             # Camelot City, Jordheim, Tir na Nog
REASON = 'True 1.65 account vault'
# Owner 2026-11-05 (reset-day list S6): each account vault keeper stands beside its capital's regular vault keeper (in
# Camelot upstairs at the vault), not at the old freeshard market spot. Regular keepers (1.65 world): x, y, z, heading.
REGULAR = {10: ('Lord Urqhart', 35500, 23857, 8751, 603),
           101: ('Jarl Yuliwyf', 31948, 28294, 8819, 2015),
           201: ('Bhreagar Hylvian', 33197, 31132, 8000, 4065)}
BESIDE = 90   # units to the keeper's right, facing the same way (the freeshard pair stood ~100 apart the same way)
POSITION_REASON = 'True 1.65 account vault position'
# Measured by the client chat's wall-clearance probe (godot/tests/spot_clearance_probe.gd, 2026-11-05): on the right
# side Virsha stood 26 units and Giorny 15 units from a wall (Giorny clipped it), so both stand 90 units on the regular
# keeper's other side (75 / 60 units of room). Janash keeps the right side (57 units of room).
MEASURED = {10: (35410, 23857, 8751, 603),
            201: (33107, 31132, 8000, 4065)}


def beside(region):
    if region in MEASURED:
        return MEASURED[region]
    _, x, y, z, heading = REGULAR[region]
    a = heading * 2 * math.pi / 4096   # DAoC heading: facing (-sin a, cos a); its right side is (cos a, sin a)
    return round(x + math.cos(a) * BESIDE), round(y + math.sin(a) * BESIDE), z, heading


def main():
    c = sqlite3.connect(f'file:{NORMAL}?mode=ro', uri=True)
    c.row_factory = sqlite3.Row
    rows = [dict(r) for r in c.execute(f"select * from Mob where ClassType = ? and Region in ({','.join('?' * len(REGIONS))}) "
                                       "order by Region, Name", (CLASS, *REGIONS))]
    assert len(rows) == 3, f'expected 3 keepers, found {len(rows)}'
    moves = []
    for r in rows:
        r['X'], r['Y'], r['Z'], r['Heading'] = beside(r['Region'])
        moves.append({'sql': f"UPDATE Mob SET X = {r['X']}, Y = {r['Y']}, Z = {r['Z']}, Heading = {r['Heading']} "
                             f"WHERE Mob_ID = '{r['Mob_ID']}'",
                      'reason': f"{POSITION_REASON}: {r['Name']} beside {REGULAR[r['Region']][0]}, the regular vault "
                                f"keeper (owner 2026-11-05)"})
    doc = {'table': 'Mob', 'key': 'Mob_ID',
           'reason': f'{REASON}: account vault keepers at their old spots (quality-of-life exception, owner 2026-11-03)',
           'rows': rows}
    for folder in (HERE, RUNTIME):
        with open(os.path.join(folder, 'add-accountvaults165.json'), 'w', encoding='utf-8') as f:
            json.dump(doc, f, ensure_ascii=False, indent=1)
            f.write('\n')
        p = os.path.join(folder, 'true165-rules.json')
        rules = json.load(open(p, encoding='utf-8'))
        rules['remove']['mob_classes'].pop(CLASS, None)
        where = [w for w in rules['remove']['where'] if not w['reason'].startswith(REASON)]
        where.append({'table': 'Mob', 'key': 'Mob_ID',
                      'where': f"ClassType = '{CLASS}' AND Region NOT IN ({', '.join(map(str, REGIONS))})",
                      'reason': f'{REASON}: only the three capitals have one (owner 2026-11-03); the others (Castle Sauvage, '
                                f'Atlantis, New Frontiers, event zones, tutorial) stay out'})
        rules['remove']['where'] = where
        rules['updates'] = [u for u in rules['updates'] if not u['reason'].startswith(POSITION_REASON)] + moves
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(rules, f, ensure_ascii=False, indent=1)
            f.write('\n')
    print('keepers:', [(r['Name'], r['Region'], r['X'], r['Y']) for r in rows])

if __name__ == '__main__':
    main()
