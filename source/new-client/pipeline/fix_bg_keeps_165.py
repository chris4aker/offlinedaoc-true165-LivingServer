"""The battlegrounds' central keeps as the period client places them (owner 2026-11-06, a player saw floating lords /
guards and door hitboxes inside the keeps).

The zones were built from dat<id>.mpk (build_zone.py), whose fixture 3 is the same keep spot for all four
battlegrounds: (25088, 30080), A=90. The real 1.64 client's patch table csv<id>.mpk (paths.ZIP_164) moves and turns
it - the only fixture that differs between its two tables in these zones:
    250 Caledon     bfrontkeep1  (24930, 29908)  A=230
    251 Murdaigean  Hfrontkeep   (25216, 30080)  A=320
    252 Thidranki   nfrontkeep1  (25088, 30080)  A=90   (unchanged)
    253 Abermenai   Hfrontkeep   (24960, 30208)  A=160
A later client's csv tables (the "test-client-165" copy, which also holds the Trials of Atlantis zones) give Caledon
and Thidranki second keep models, bfrontkeep2 / nfrontkeep2, which the period client does not have: period first
(owner 2026-11-06: "check the 1.64 client").

Extracts the four csv tables from ZIP_164 into paths.WORK_164 (read-only source), rewrites godot/content/zones/
zone25x/zone.json (fixture 3; a model no fixture uses any more leaves the model list), converting a model only when the
zone lacks it. Re-running changes nothing.
"""
import csv, io, json, sys, zipfile
from pathlib import Path
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths as _paths
from daoc_mpk import read_mpk
import nif_static
from assets_pipeline import CLIENT
from build_zone import height_at, ambient_only

M = nif_static.UNITS_TO_METRES
ZONES = HERE.parent / 'godot/content/zones'
KEEP_FIXTURE = 3


def period_table(zone_id):
    """The 1.64 client's csv<id>.mpk, extracted once into WORK_164/zones/zone<id>/."""
    dst = _paths.WORK_164 / f'zones/zone{zone_id:03d}/csv{zone_id:03d}.mpk'
    if not dst.exists():
        dst.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164')) as z:
            dst.write_bytes(z.read(f'zones/zone{zone_id:03d}/csv{zone_id:03d}.mpk'))
    return read_mpk(str(dst))


def table(mpk, name):
    key = next(k for k in mpk if k.lower() == name)
    return [[c.strip() for c in r] for r in csv.reader(io.StringIO(mpk[key].decode('latin1'))) if r and r[0].strip().isdigit()]


def fix(zone_id):
    zdir = ZONES / f'zone{zone_id:03d}'
    path = zdir / 'zone.json'
    raw = path.read_bytes().decode('utf-8')
    zone = json.loads(raw)
    mpk = period_table(zone_id)
    row = next(r for r in table(mpk, 'fixtures.csv') if int(r[0]) == KEEP_FIXTURE)
    nifs = {int(r[0]): r for r in table(mpk, 'nifs.csv')}
    nif_id = int(row[1])
    base = nifs[nif_id][2].rsplit('.', 1)[0].lower()

    m = next((i for i, mo in enumerate(zone['models']) if mo['file'] == f'models/{base}.gltf'), None)
    if m is None:
        npk = next(p for p in (CLIENT / 'zones/Nifs').iterdir() if p.name.lower() == base + '.npk')
        nif = next(v for k, v in read_mpk(str(npk)).items() if k.endswith('.nif'))
        info = nif_static.convert(nif, str(zdir / 'models' / f'{base}.gltf'), '../textures/original')
        m = len(zone['models'])
        zone['models'].append({'nif': nif_id, 'name': nifs[nif_id][1], 'file': f'models/{base}.gltf',
                               'triangles': info['triangles'], 'collision_triangles': info['collision_triangles'],
                               'warnings': [f'{base}: {w}' for w in info['warnings']],
                               'ambient_only': ambient_only(nifs[nif_id])})

    hi = np.fromfile(zdir / 'heights_2x.f32', dtype='<f4').reshape(513, 513).astype(np.float64) / M
    x, y, a = float(row[3]), float(row[4]), float(row[6])
    gy = height_at(hi, x, y, 128.0) if row[11] != '0' else float(row[5])
    f = next(f for f in zone['fixtures'] if f.get('id') == KEEP_FIXTURE and f.get('source') is None)
    before = (zone['models'][f['m']]['file'], list(f['pos']), f['yaw'])
    f.update({'m': m, 'name': row[2], 'pos': [x * M, round(float(gy) * M, 4), y * M], 'yaw': -a})
    # a model no fixture places any more (the later client's bfrontkeep2 / nfrontkeep2) leaves the list - only from
    # its end, so no other fixture's model index moves
    used = {int(fx['m']) for fx in zone['fixtures']}
    while zone['models'] and len(zone['models']) - 1 not in used:
        print('  model no longer placed:', zone['models'].pop()['file'])
    print(f'  zone {zone_id} fixture {KEEP_FIXTURE}: {before[0]} {before[1]} yaw {before[2]}'
          f' -> {zone["models"][m]["file"]} {f["pos"]} yaw {f["yaw"]}')
    out = json.dumps(zone, indent=1).replace('\n', '\r\n')
    if out != raw:
        path.write_bytes(out.encode('utf-8'))


def main():
    for zone_id in (sys.argv[1:] or (250, 251, 252, 253)):
        fix(int(zone_id))


if __name__ == '__main__':
    main()
