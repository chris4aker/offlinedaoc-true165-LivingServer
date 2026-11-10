"""The Midgard inn adventurers' road into Huginfell, around two buildings (owner 2026-11-10: they walked straight through
a house at loc 53415 47151, and the corner of a walled yard after it).

build_inns165.py has the same detours (ROUTE_DETOURS) for its next full run; it reads the old normal-mode world
database, which no longer exists, so this applies them to its last output instead: only the three Midgard adventurers'
paths in add-pathpoints165.json change, every other row stays as it was. Each adventurer keeps its own lane (25 units
either side of the middle one), worked out the way the builder does. Writes both copies (tools and runtime).

    python -I fix_huginfell_route165.py
"""
import json
import math
import os
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
RUNTIME = os.path.normpath(os.path.join(HERE, '..', '..', '..', 'runtime', 'true165'))
NAME = 'add-pathpoints165.json'
NAMESPACE = uuid.UUID("6b1d5c2e-1650-4a6e-9a1e-165165165165")   # build_inns165.py
NOW = "2026-10-30 00:00:00"
# the same as build_inns165.ROUTE_DETOURS["mid"]: last road point kept, the points walked around, first point kept
DETOURS = [((708552, 784760), [(708716, 784176, 4672)], (709272, 784040)),
           ((709416, 783896), [(709718, 783785, 4672), (709806, 783662, 4672)], (709848, 783464))]


def path_id(i):
    return "true165_adv_" + str(uuid.uuid5(NAMESPACE, f"mid/adventurer/{i}"))


def main():
    with open(os.path.join(HERE, NAME), encoding='utf-8') as f:
        data = json.load(f)
    rows = data['rows']
    paths = {i: sorted((r for r in rows if r['PathID'] == path_id(i)), key=lambda r: r['Step']) for i in range(3)}
    assert all(len(p) > 10 for p in paths.values()), 'Midgard adventurer paths not found'
    middle = [(r['X'], r['Y'], r['Z']) for r in paths[1]]          # lane 0: the road itself
    if any((x, y) == (w[0], w[1]) for _, around, _ in DETOURS for w in around for x, y, _ in middle):
        print('already applied')
        return
    lanes = {i: [(r['X'], r['Y'], r['Z']) for r in paths[i]] for i in range(3)}
    for before, around, after in DETOURS:
        def nearest(xy):
            return min(range(len(middle)), key=lambda k: math.hypot(middle[k][0] - xy[0], middle[k][1] - xy[1]))
        a, b = nearest(before), nearest(after)
        assert a < b
        road = middle[:a + 1] + list(around) + middle[b:]
        for i in range(3):
            lane = (i - 1) * 25
            new = []
            for k in range(a + 1, a + 1 + len(around)):
                x, y, z = road[k]
                nx_, ny_ = road[k + 1][:2]
                px_, py_ = road[k - 1][:2]
                dx_, dy_ = nx_ - px_, ny_ - py_
                dl = math.hypot(dx_, dy_) or 1.0
                new.append((int(x - dy_ / dl * lane), int(y + dx_ / dl * lane), int(z)))
            lanes[i] = lanes[i][:a + 1] + new + lanes[i][b:]
        middle = road
    ours = {path_id(i): i for i in range(3)}
    keep, written = [], set()
    for r in rows:                                   # each path's rows where they stood, the rest untouched
        i = ours.get(r['PathID'])
        if i is None:
            keep.append(r)
            continue
        if i in written:
            continue
        written.add(i)
        template = paths[i][0]
        for step, (x, y, z) in enumerate(lanes[i], 1):
            row = dict(template)
            row.update(PathPoints_ID=f"{path_id(i)}-{step}", Step=step, X=x, Y=y, Z=z, WaitTime=0,
                       LastTimeRowUpdated=NOW)
            keep.append(row)
    data['rows'] = keep
    for folder in (HERE, RUNTIME):
        with open(os.path.join(folder, NAME), 'w', encoding='utf-8') as f:
            json.dump(data, f, indent=1)
    print('Midgard adventurer route points:', {i: len(lanes[i]) for i in range(3)})


if __name__ == '__main__':
    main()
