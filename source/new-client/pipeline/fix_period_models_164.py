"""Zone models from the real 1.64 client where the later client's copy builds differently (build_zone.PERIOD_164_NIFS).

Owner 2026-11-06: "check frontier models used in the 1.64 client vs ours" (period first). Every keep / gate / tower /
wall / bridge model the built zones place was converted from both clients and compared (triangles, collision, door
groups, textures); the ones that differ are in build_zone.PERIOD_164_NIFS - among them the gate forts whose gates the
later copy has without doors (Emain Macha's h-gatefort, Albion's b-milefort, Midgard's n-gatefort-s), the relic keeps
missing their door002, and later remodels (hruintower, bhouse2).

Extracts each from paths.ZIP_164 (zones/Nifs/<name>.npk) into paths.WORK_164/zones/Nifs (build_zone.py takes them from
there), converts it into every built zone that places it - unless the zone has its own copy (zones/zoneNNN/nifs, which
build_zone.py prefers) - and updates that zone's model entry; textures a zone lacks come from another built zone, else
from the client's textures (original, and x4plus by Real-ESRGAN as build_zone does). Re-running converts again (same
output). Afterwards repack the changed zones' scenes:
    Godot --headless --path godot -s res://tools/pack_scenes.gd -- res://content/zones/zoneNNN/models ...
"""
import json, shutil, sys, zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths as _paths
from daoc_mpk import read_mpk
import nif_static
import build_zone
from texture_index import TextureIndex

ZONES = HERE.parent / 'godot/content/zones'


def extract(base):
    """The 1.64 client's <base>.npk in WORK_164/zones/Nifs (extracted once), keeping the zip's own file name."""
    d = _paths.WORK_164 / 'zones/Nifs'
    have = {p.name.lower(): p for p in d.iterdir()} if d.is_dir() else {}
    if f'{base}.npk' in have:
        return have[f'{base}.npk']
    with zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164')) as z:
        name = next(n for n in z.namelist() if n.lower() == f'zones/nifs/{base}.npk')
        d.mkdir(parents=True, exist_ok=True)
        dst = d / name.split('/')[-1]
        dst.write_bytes(z.read(name))
    return dst


def texture_into(tex, zdir, idx, upscale):
    """textures/original + x4plus/<tex>.png for this zone: another built zone's copy (the same file everywhere), else
    the client's texture (x4plus made by build_zone's upscaler afterwards)."""
    for sub in ('original', 'x4plus'):
        dst = zdir / 'textures' / sub / f'{tex}.png'
        if dst.exists():
            continue
        src = next((p for p in sorted(ZONES.glob(f'zone*/textures/{sub}/{tex}.png')) if p.parent != dst.parent), None)
        if src is not None:
            shutil.copyfile(src, dst)
            print('   copied', src.relative_to(ZONES), '->', dst.relative_to(ZONES))
            continue
        if sub == 'original':
            doubled = tex.endswith(('__d2', '__x2'))
            img = idx.image(tex[:-4] if doubled else tex)
            if img is None:
                print('   MISSING texture', tex)
                continue
            build_zone.save_if_changed(img, dst)
            print('   from the client:', tex)
        upscale.add(zdir)


def main():
    idx = TextureIndex()
    sources = {b: extract(b) for b in build_zone.PERIOD_164_NIFS}
    changed, upscale = set(), set()
    for path in sorted(ZONES.glob('zone*/zone.json')):
        raw = path.read_bytes().decode('utf-8')
        zone = json.loads(raw)
        if zone.get('city') or zone.get('dungeon') or zone['id'] in _paths.HOUSING_164:
            continue
        local = build_zone.zone_local_npks(zone['id'])
        zdir = path.parent
        for base, npk in sources.items():
            entry = next((mo for mo in zone['models'] if mo['file'] == f'models/{base}.gltf'), None)
            if entry is None:
                continue
            if f'{base}.npk' in local:
                print(f'zone {zone["id"]}: {base} - the zone has its own copy (kept)')
                continue
            nif = next(v for k, v in read_mpk(str(npk)).items() if k.endswith('.nif'))
            info = nif_static.convert(nif, str(zdir / 'models' / f'{base}.gltf'), '../textures/original')
            for tex in sorted(info['textures']):
                texture_into(tex.lower(), zdir, idx, upscale)
            entry.update({'triangles': info['triangles'], 'collision_triangles': info['collision_triangles'],
                          'warnings': [f'{base}: {w}' for w in info['warnings']]})
            g = json.loads((zdir / 'models' / f'{base}.gltf').read_text(encoding='utf-8'))
            print(f'zone {zone["id"]}: {base} {info["triangles"]} triangles, {info["collision_triangles"]} collision,'
                  f' door groups {sorted({d["group"] for d in g.get("extras", {}).get("nif_doors", [])})}')
            changed.add(zdir.name)
        out = json.dumps(zone, indent=1).replace('\n', '\r\n')
        if out != raw:
            path.write_bytes(out.encode('utf-8'))
    for zdir in sorted(upscale):
        build_zone.upscale_dir(zdir / 'textures/original', zdir / 'textures/x4plus')
    print('zones changed:', ' '.join(f'res://content/zones/{z}/models' for z in sorted(changed)))


if __name__ == '__main__':
    main()
