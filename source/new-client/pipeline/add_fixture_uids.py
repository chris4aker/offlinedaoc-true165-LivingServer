"""Each zone fixture's Unique Id ("uid" in zone.json) where it differs from the fixture's row ID ("id").

A door's id is zone x 1,000,000 + fixture x 100 + leaf, and the fixture number the client sends is fixtures.csv's
Unique Id column, not its row ID (the server's door table is built that way: Forest Sauvage's relic keep is row 1782,
Unique Id 1811, and its doors are 11181101..; OpenDAoC-BuildNav Zone2Obj.cs: uniqueid == 0 ? row id : uniqueid).
ZoneLoader built door ids from the row ID, so the keep, house and battleground doors of every zone whose two columns
differ asked the server about doors it doesn't have (owner 2026-11-06, the battleground keeps' doors).

"id" stays the row ID (the inn layouts' removed_fixtures use it); ZoneLoader takes "uid" for door ids when it is there.
build_zone.py writes "uid" itself from now on; this adds it to the zones already built. Reads each zone's own
dat<id>.mpk (the table build_zone.py placed the fixtures from), checks every fixture's name against its row, and
rewrites zone.json only when something changes. Re-running changes nothing.
    python pipeline/add_fixture_uids.py [zone ids...]      (none: every built zone)
"""
import csv, io, json, re, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths as _paths
from daoc_mpk import read_mpk

ZONES = HERE.parent / 'godot/content/zones'


def unique_id(row):
    """fixtures.csv's Unique Id (column 15), the row ID where it is 0 or missing."""
    u = row[14].strip() if len(row) > 14 else ''
    return int(u) if u.isdigit() and int(u) != 0 else int(row[0])


def add(zone_id):
    path = ZONES / f'zone{zone_id:03d}/zone.json'
    raw = path.read_bytes().decode('utf-8')
    zone = json.loads(raw)
    if zone.get('city') or zone.get('dungeon'):
        return 0, 0                                     # (their fixture ids are placement indexes, not table rows)
    dat = read_mpk(str(_paths.zone_dir(zone_id) / f'dat{zone_id:03d}.mpk'))
    key = next(k for k in dat if k.lower() == 'fixtures.csv')
    rows = {int(r[0]): r for r in csv.reader(io.StringIO(dat[key].decode('latin1')))
            if r and r[0].strip().isdigit() and len(r) > 11 and r[1].strip().isdigit()}
    changed = bad = 0
    for f in zone['fixtures']:
        fid = int(f.get('id', -1))
        if fid <= 0 or f.get('source') is not None:
            continue
        r = rows.get(fid)
        if r is None or r[2].strip() != f['name']:
            bad += 1
            continue
        uid = unique_id(r)
        if uid != fid and f.get('uid') != uid:
            f['uid'] = uid
            changed += 1
        elif uid == fid and 'uid' in f:
            del f['uid']
            changed += 1
    out = json.dumps(zone, indent=1).replace('\n', '\r\n')
    if out != raw:
        path.write_bytes(out.encode('utf-8'))
    return changed, bad


def main():
    ids = [int(a) for a in sys.argv[1:]] or sorted(int(re.search(r'(\d+)$', p.parent.name).group(1))
                                                     for p in ZONES.glob('zone*/zone.json'))
    for zone_id in ids:
        changed, bad = add(zone_id)
        if changed or bad:
            print(f'zone {zone_id}: {changed} fixtures given their Unique Id' + (f', {bad} not matched to a row' if bad else ''))


if __name__ == '__main__':
    main()
