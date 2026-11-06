"""The battlegrounds' central keeps as the original 1.65 client places them (owner 2026-11-06, a player saw floating
lords / guards and door hitboxes inside the keeps; the server chat's spawns, doors and navmesh follow the 1.65 client).

The zones were built from dat<id>.mpk (build_zone.py), whose fixture 3 is the same keep spot for all four
battlegrounds: (25088, 30080), A=90, with bfrontkeep1 / hfrontkeep / nfrontkeep1. The 1.65 client's patch table
csv<id>.mpk moves and turns that keep, and gives Caledon and Thidranki their second keep model:
    250 Caledon     bfrontkeep2.nif  (24930, 29908)  A=230
    251 Murdaigean  Hfrontkeep.nif   (25216, 30080)  A=320
    252 Thidranki   nfrontkeep2.nif  (25088, 30080)  A=90   (its door 2 is the tower door)
    253 Abermenai   Hfrontkeep.nif   (24960, 30208)  A=160
Only fixture 3 differs between the two tables in these zones (checked column by column).

Reads the 1.65 client read-only (paths.TRUE165_WORK / test-client-165) and rewrites godot/content/zones/zone25x/
zone.json (fixture 3, the model list), converting the new keep model and copying a texture it needs from another zone
that has it. Re-running changes nothing. Afterwards re-export the zones' collision for the world host:
    Godot --headless --path godot -s res://tools/export_world_collision.gd -- 250 251 252 253
"""
import csv, io, json, shutil, sys
from pathlib import Path
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths as _paths
from daoc_mpk import read_mpk
import nif_static
from build_zone import height_at, ambient_only

M = nif_static.UNITS_TO_METRES
CLIENT_165 = _paths.TRUE165_WORK / 'test-client-165'
ZONES = HERE.parent / 'godot/content/zones'
KEEP_FIXTURE = 3


def table(mpk, name):
    key = next(k for k in mpk if k.lower() == name)
    return [[c.strip() for c in r] for r in csv.reader(io.StringIO(mpk[key].decode('latin1'))) if r and r[0].strip().isdigit()]


def texture_from_other_zone(tex, zdir):
    """Copy textures/<set>/<tex>.png from another built zone (the same file everywhere) where this zone lacks it."""
    for sub in ('original', 'x4plus'):
        dst = zdir / 'textures' / sub / f'{tex}.png'
        if dst.exists():
            continue
        src = next((p for p in sorted(ZONES.glob(f'zone*/textures/{sub}/{tex}.png')) if p.parent != dst.parent), None)
        if src is None:
            raise SystemExit(f'no zone has textures/{sub}/{tex}.png')
        shutil.copyfile(src, dst)
        print('  copied', src.relative_to(ZONES), '->', dst.relative_to(ZONES))


def fix(zone_id):
    zdir = ZONES / f'zone{zone_id:03d}'
    path = zdir / 'zone.json'
    raw = path.read_bytes().decode('utf-8')
    zone = json.loads(raw)
    mpk = read_mpk(str(CLIENT_165 / f'zones/zone{zone_id:03d}/csv{zone_id:03d}.mpk'))
    row = next(r for r in table(mpk, 'fixtures.csv') if int(r[0]) == KEEP_FIXTURE)
    nifs = {int(r[0]): r for r in table(mpk, 'nifs.csv')}
    nif_id = int(row[1])
    filename = nifs[nif_id][2]
    base = filename.rsplit('.', 1)[0].lower()

    m = next((i for i, mo in enumerate(zone['models']) if mo['file'] == f'models/{base}.gltf'), None)
    if m is None:
        npk = next(p for p in (CLIENT_165 / 'zones/Nifs').iterdir() if p.name.lower() == base + '.npk')
        nif = next(v for k, v in read_mpk(str(npk)).items() if k.endswith('.nif'))
        info = nif_static.convert(nif, str(zdir / 'models' / f'{base}.gltf'), '../textures/original')
        for tex in sorted(info['textures']):
            texture_from_other_zone(tex.lower(), zdir)
        m = len(zone['models'])
        zone['models'].append({'nif': nif_id, 'name': nifs[nif_id][1], 'file': f'models/{base}.gltf',
                               'triangles': info['triangles'], 'collision_triangles': info['collision_triangles'],
                               'warnings': [f'{base}: {w}' for w in info['warnings']],
                               'ambient_only': ambient_only(nifs[nif_id])})
        print('  converted', filename, info['triangles'], 'triangles')

    hi = np.fromfile(zdir / 'heights_2x.f32', dtype='<f4').reshape(513, 513).astype(np.float64) / M
    x, y, a = float(row[3]), float(row[4]), float(row[6])
    on_ground = row[11] != '0'
    gy = height_at(hi, x, y, 128.0) if on_ground else float(row[5])
    f = next(f for f in zone['fixtures'] if f.get('id') == KEEP_FIXTURE and f.get('source') is None)
    before = dict(f)
    f.update({'m': m, 'name': row[2], 'pos': [x * M, round(gy * M, 4), y * M], 'yaw': -a})
    print(f'  zone {zone_id} fixture {KEEP_FIXTURE}: {zone["models"][before["m"]]["file"]} {before["pos"]} yaw {before["yaw"]}'
          f' -> {zone["models"][m]["file"]} {f["pos"]} yaw {f["yaw"]}')
    out = json.dumps(zone, indent=1).replace('\n', '\r\n')
    if out != raw:
        path.write_bytes(out.encode('utf-8'))


def main():
    for zone_id in (sys.argv[1:] or (250, 251, 252, 253)):
        fix(int(zone_id))


if __name__ == '__main__':
    main()
