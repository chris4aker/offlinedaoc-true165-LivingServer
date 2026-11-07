"""True 1.65: the period loot gem "Moonstone" (owner 2026-11-03, via the new client chat's Realm Exchange check).

The drop builders matched loot by name, and the only item called Moonstone on the server is the Hibernian epic
quest item (Id_nb 'Moonstone', The Moonstone Twin: no merchant value, cannot be dropped), so 46 monsters dropped the
quest item and bots listed hundreds of them on the Realm Exchange. The period moonstone is a loot gem, rolled with
onyx, jade and obsidian (Prima Atlas 2002: mist sprite "(35%) Moonstone", swamp slime "(45%) Onyx (35%) Moonstone").

No period item page records its merchant value, so it copies the server's own onyx gem (onyx_gem: 30 copper,
quality 35), the gem the Atlas rolls beside it on the same monsters. Nothing is invented beyond that.

python -I build_moonstone165.py      (rerunnable)
Out (both copies): add-gems165.json (the moonstone_gem template); drops165.json ('Moonstone' -> 'moonstone_gem');
true165-rules.json: the quest Moonstone can be dropped, destroyed and sold for 30 copper (owner 2026-11-03).
build_drops165.py and build_prima165.py carry the same alias, so a rebuild keeps it.
"""
import json, os, sqlite3

import sys as _sys
_sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import trashlevel165 as T   # vendor trash level 0 (owner 2026-11-07)

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
DB = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
GEM = 'moonstone_gem'

def main():
    c = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
    c.row_factory = sqlite3.Row
    onyx = c.execute("select * from ItemTemplate where Id_nb = 'onyx_gem'").fetchone()
    assert onyx is not None, 'onyx_gem missing'
    row = {k: onyx[k] for k in onyx.keys()}
    row.update(Id_nb=GEM, Name='moonstone', Model=115, PackageID='True165_gem',
               Description='Loot gem (period Prima Atlas 2002); value as onyx')
    row.pop('ItemTemplate_ID', None)
    T.apply_display_level(row)        # vendor trash shows level 0 (owner 2026-11-07)
    for k in ('LastTimeRowUpdated',):
        row[k] = '2026-11-03 04:00:00'
    doc = {'table': 'ItemTemplate', 'key': 'Id_nb',
           'reason': 'True 1.65 loot gem: moonstone (the period gem; the drops had used the epic quest item of the same name)',
           'rows': [row]}
    T.save_originals()
    changed = 0
    for folder in (HERE, RUNTIME):
        with open(os.path.join(folder, 'add-gems165.json'), 'w', encoding='utf-8') as f:
            json.dump(doc, f, ensure_ascii=False, indent=1)
            f.write('\n')
        p = os.path.join(folder, 'drops165.json')
        d = json.load(open(p, encoding='utf-8'))
        for mob in d['monsters'].values():
            for entry in mob['items']:
                if entry[0] == 'Moonstone':
                    entry[0] = GEM
                    changed += 1
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(d, f, ensure_ascii=False, indent=1)
            f.write('\n')
    # The quest Moonstone itself can be dropped, destroyed and vendored for a little, by players and bots (owner 2026-11-03):
    # bots on worlds that still hold the wrong drops sell them like vendor trash, no inventory is touched
    tag = 'True 1.65 moonstone'
    for folder in (HERE, RUNTIME):
        p = os.path.join(folder, 'true165-rules.json')
        rules = json.load(open(p, encoding='utf-8'))
        rules['updates'] = [u for u in rules['updates'] if not u['reason'].startswith(tag)]
        rules['updates'].append({
            'sql': "UPDATE ItemTemplate SET IsDropable = 1, IsIndestructible = 0, Price = 30 WHERE Id_nb = 'Moonstone'",
            'reason': f'{tag}: the epic quest Moonstone can be dropped, destroyed and sold to a merchant for 30 copper '
                      f'(the value of the moonstone gem), by players and bots: bots of worlds that still carry the wrong drops '
                      f'sell them off (owner 2026-11-03)'})
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(rules, f, ensure_ascii=False, indent=1)
            f.write('\n')
    # the Prima build's record of its drops says moonstone_gem too, so check_prima165.py checks the drop that is there
    rp = os.path.join(HERE, 'prima165_report.json')
    if os.path.exists(rp):
        rep = json.load(open(rp, encoding='utf-8'))
        n = 0
        for drop in rep.get('drops', []):
            if 'Moonstone' in drop.get('added', []):
                drop['added'] = [GEM if i == 'Moonstone' else i for i in drop['added']]
                n += 1
            if 'Moonstone' in drop.get('chances', {}):
                drop['chances'][GEM] = drop['chances'].pop('Moonstone')
        if n:
            with open(rp, 'w', encoding='utf-8') as f:
                json.dump(rep, f, ensure_ascii=False, indent=1)   # its own format: no final newline
        print(f'prima165_report drops remapped: {n}')
    print(f'moonstone drops remapped: {changed} (both copies together)')

if __name__ == '__main__':
    main()
