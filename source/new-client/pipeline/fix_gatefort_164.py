"""Emain Macha's gate forts with their big gates (owner 2026-11-06: the gate fort's big gates were not doors).

The later client's H-gatefort.npk keeps the two big gates' leaves (door001 gateL / gateR, door002 gateL01 / gateR01)
with their open animation but nothing in them - no panel, no collidee - so the converter made no door of them: the
gateways stood open, unclickable, and the server's DoorState for 214000101/02 and 214000201/02 moved nothing. The real
1.64 client's copy has them (each leaf a 'visible gate' panel and a 'collidee' piece).

Extracts zones/Nifs/H-gatefort.npk from paths.ZIP_164 into paths.WORK_164/zones/Nifs (build_zone.py takes it from
there: PERIOD_164_NIFS), converts it into every built zone that places h-gatefort and updates that zone's model entry;
textures the zone lacks are copied from another built zone. Afterwards repack the zone's scenes:
    Godot --headless --path godot -s res://tools/pack_scenes.gd -- res://content/zones/zone214/models
"""
import json, shutil, sys, zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths as _paths
from daoc_mpk import read_mpk
import nif_static

ZONES = HERE.parent / 'godot/content/zones'
BASE = 'h-gatefort'


def texture_from_other_zone(tex, zdir):
    for sub in ('original', 'x4plus'):
        dst = zdir / 'textures' / sub / f'{tex}.png'
        if dst.exists():
            continue
        src = next((p for p in sorted(ZONES.glob(f'zone*/textures/{sub}/{tex}.png')) if p.parent != dst.parent), None)
        if src is None:
            raise SystemExit(f'no zone has textures/{sub}/{tex}.png')
        shutil.copyfile(src, dst)
        print('  copied', src.relative_to(ZONES), '->', dst.relative_to(ZONES))


def main():
    dst = _paths.WORK_164 / 'zones/Nifs/H-gatefort.npk'
    if not dst.exists():
        dst.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164')) as z:
            dst.write_bytes(z.read('zones/Nifs/H-gatefort.npk'))
    nif = next(v for k, v in read_mpk(str(dst)).items() if k.endswith('.nif'))
    for path in sorted(ZONES.glob('zone*/zone.json')):
        raw = path.read_bytes().decode('utf-8')
        zone = json.loads(raw)
        entry = next((mo for mo in zone['models'] if mo['file'] == f'models/{BASE}.gltf'), None)
        if entry is None:
            continue
        zdir = path.parent
        info = nif_static.convert(nif, str(zdir / 'models' / f'{BASE}.gltf'), '../textures/original')
        for tex in sorted(info['textures']):
            texture_from_other_zone(tex.lower(), zdir)
        entry.update({'triangles': info['triangles'], 'collision_triangles': info['collision_triangles'],
                      'warnings': [f'{BASE}: {w}' for w in info['warnings']]})
        g = json.loads((zdir / 'models' / f'{BASE}.gltf').read_text(encoding='utf-8'))
        print(f'zone {zone["id"]}: {BASE} {info["triangles"]} triangles, doors',
              [(d['name'], d['group']) for d in g.get('extras', {}).get('nif_doors', [])])
        out = json.dumps(zone, indent=1).replace('\n', '\r\n')
        if out != raw:
            path.write_bytes(out.encode('utf-8'))


if __name__ == '__main__':
    main()
