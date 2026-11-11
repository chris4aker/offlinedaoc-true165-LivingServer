"""Horse routes over the Innis Carthaig bridge ride on its deck (owner 2026-11-10: "the stablemaster route from connla to
innis carthaig my character goes straight to the riverbed when going over the bridge").

build_routefloor165.py lifted three points of each route that crosses the Innis Carthaig bridge (Shannon Estuary,
zone 202) onto the bridge's top structure (Z 5146-5408) instead of its deck (about 4870; Criofan the Fisherman stands on
it at 314192, 638129, 4870), and fix_routebridges165.py left them because the period leg ran just under the deck. The
client looks for a floor only a little above and below the height the server sends, misses the deck from up there and
draws the horse and rider on the riverbed. These points go back to the deck (+8, as the other bridge fixes).

Also reports (does not change) every other route point that sits far above both neighbours - the same kind of spike -
so they can be looked at one by one.

    python -I fix_innisbridge165.py
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
RUNTIME = os.path.normpath(os.path.join(HERE, '..', '..', '..', 'runtime', 'true165'))
NAME = 'add-routefloor165.json'
DECK_Z = 4878            # deck (about 4870) + 8
# the bridge's span: points above the deck inside it are on the top structure
BOX = (313600, 636900, 314800, 640300)
ROUTES = {'HR_Connla_InnisCarthaig': (13, 14, 15),
          'ticket_to_ConnlaMulgraighy': (130, 131, 132),
          'HR_InnisCarthaig_ParthananFarm': (154, 155, 156)}
SPIKE = 200              # report: a point this far above both neighbours


def main():
    with open(os.path.join(HERE, NAME), encoding='utf-8') as f:
        data = json.load(f)
    fixed = []
    for row in data['rows']:
        steps = ROUTES.get(row['PathID'])
        if not steps or row['Step'] not in steps:
            continue
        x0, y0, x1, y1 = BOX
        assert x0 <= row['X'] <= x1 and y0 <= row['Y'] <= y1, row
        if row['Z'] != DECK_Z:
            fixed.append((row['PathID'], row['Step'], row['Z']))
            row['Z'] = DECK_Z
    for folder in (HERE, RUNTIME):
        with open(os.path.join(folder, NAME), 'w', encoding='utf-8', newline='\n') as f:   # the file's own LF
            json.dump(data, f, indent=1)
    print('points put on the deck:', len(fixed), fixed)

    by_path = {}
    for row in data['rows']:
        by_path.setdefault(row['PathID'], []).append(row)
    spikes = []
    for pid, rows in by_path.items():
        rows.sort(key=lambda r: r['Step'])
        for i in range(1, len(rows) - 1):
            z, a, b = rows[i]['Z'], rows[i - 1]['Z'], rows[i + 1]['Z']
            if z - max(a, b) >= SPIKE:
                spikes.append((pid, rows[i]['Step'], rows[i]['X'], rows[i]['Y'], z, a, b))
    print('other spikes (report only):', len(spikes))
    for s in spikes[:40]:
        print('   ', s)


if __name__ == '__main__':
    main()
