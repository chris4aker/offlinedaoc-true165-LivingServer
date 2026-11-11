"""True 1.65 loot that stacks like its kind (owner 2026-11-11, #36: "do another pass and find vendor trash items that
should stack and make them stack compared to other similar items").

Rule: a monster drop that is plain loot (Object_Type 0, Item_Type 0/40) and does not stack (MaxCount <= 1) is given
the stack size of its kind when at least two other monster drops of that kind already stack. The kind is the last
word of the name ("piece of X" / "chunk of X" ... use X), with synonyms merged (teeth/fang/tusk -> tooth; skin/hide/fur
-> pelt; ...) and every gemstone name (diopside, tourmaline, spinel, opal, jacinth, sphene, diamond, ...) as one "gem"
kind. The size is the most common MaxCount among those stacking drops (a tie goes to the smaller stack). PackSize stays
1 (the wolf lesson of 2026-11-07), so a kill still drops one; merchants pay Price x Count / PackSize for a stack.

Never stacked: quest items (cq_*, PackageID ClassicQuest), anything a DataQuest takes as a step / hand-in item (a
hand-in step takes the whole stack it is given), anything classic-quests.json or the server code names, usable items
(spell/charges) and items with a PackSize above 1.

Decisions are kept in stack165.json (an item decided once keeps its size on later runs, also after a refresh made it
stack). Items owned by an add-*.json file are edited there (both copies: a refresh writes the add-file values over
rules updates); the others get one rules update per size in true165-rules.json (both copies). The Prima batch builder
reads stack165.json, so a rebuild of add-primaitems165.json keeps the sizes.
Run after the item generators (build_prima_batch165.py, build_twins165.py, build_dropfix165.py, build_moonstone165.py).

    python -I build_stacks165.py           (rerunnable)
    python -I build_stacks165.py --dry     (report only)
"""
import collections
import glob
import json
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
DB = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
SERVER_SRC = os.path.join(CV, 'development-source', 'server', 'GameServer')
CLASSIC_QUESTS = os.path.join(CV, 'runtime', 'server', 'classic-quests.json')
MAP_PATH = os.path.join(HERE, 'stack165.json')
TAG = 'True 1.65 loot stacks like its kind'
DRY = '--dry' in sys.argv

SYN = {'teeth': 'tooth', 'fang': 'tooth', 'fangs': 'tooth', 'tusk': 'tooth', 'tusks': 'tooth',
       'skin': 'pelt', 'hide': 'pelt', 'fur': 'pelt', 'pelts': 'pelt', 'skins': 'pelt', 'hides': 'pelt',
       'legs': 'leg', 'bones': 'bone', 'wings': 'wing', 'claws': 'claw', 'talon': 'claw', 'talons': 'claw',
       'eyes': 'eye', 'ears': 'ear', 'scales': 'scale', 'feathers': 'feather', 'hooves': 'hoof'}
GEMS = {'gem', 'diamond', 'sapphire', 'ruby', 'emerald', 'topaz', 'tourmaline', 'opal', 'spinel', 'jacinth', 'sphene',
        'diopside', 'citrine', 'chryoprase', 'chrysoprase', 'malachite', 'azurite', 'lazuli', 'amethyst', 'kunzite',
        'beryl', 'aquamarine', 'alexandrite', 'chrysoberyl', 'kornerupine', 'jade', 'garnet', 'zircon', 'pearl',
        'onyx', 'obsidian', 'aventurine', 'bloodstone', 'jasper', 'agate', 'carnelian', 'peridot', 'quartz',
        'turquoise', 'hematite', 'iolite', 'amber', 'moonstone'}
OF = re.compile(r'^(?:a |an |the )?(?:small |large |polished |rough )?'
                r'(?:piece|chunk|shard|scrap|bit|sliver|handful|fragment)s? of (.+)$')


def kind(name):
    n = re.sub(r"[^a-z' -]", '', (name or '').lower()).strip()
    m = OF.match(n)
    if m:
        n = m.group(1)
    w = n.replace('-', ' ').split()
    if not w:
        return ''
    last = w[-1].strip("'")
    return 'gem' if last in GEMS else SYN.get(last, last)


def load(p):
    raw = open(p, 'rb').read().decode('utf-8')
    return raw, json.loads(raw)


def save(p, raw, d):
    nl = '\r\n' if '\r\n' in raw else '\n'
    text = json.dumps(d, ensure_ascii=False, indent=1).replace('\n', nl) + (nl if raw.endswith(nl) else '')
    if text != raw:
        open(p, 'wb').write(text.encode('utf-8'))
        return True
    return False


def decide(c):
    hand_ins = set()
    for step, collect in c.execute('SELECT StepItemTemplates, CollectItemTemplate FROM DataQuest'):
        for v in (step, collect):
            hand_ins.update(p.strip().lower() for p in re.split(r'[|;,]', v or '') if p.strip())
    classic = open(CLASSIC_QUESTS, encoding='utf-8').read().lower()
    named = set()
    for f in glob.glob(os.path.join(SERVER_SRC, '**', '*.cs'), recursive=True):
        named.update(m.lower() for m in re.findall(r'"([A-Za-z0-9_\' -]{3,80})"',
                                                   open(f, encoding='utf-8', errors='ignore').read()))
    drops = {r[0].lower() for r in c.execute('SELECT ItemTemplateID FROM LootTemplate')}
    c.row_factory = sqlite3.Row
    rows = [dict(r) for r in c.execute('SELECT Id_nb, Name, MaxCount, PackSize, Item_Type, PackageID, SpellID, SpellID1, '
                                       'Charges FROM ItemTemplate WHERE Object_Type = 0 AND Item_Type IN (0, 40)')]

    def excluded(r):
        i = r['Id_nb'].lower()
        if i.startswith('cq_') or (r['PackageID'] or '') == 'ClassicQuest':
            return 'quest item'
        if i in hand_ins:
            return 'DataQuest step / hand-in item'
        if '"' + i + '"' in classic:
            return 'named in classic-quests.json'
        if i in named:
            return 'named in the server code'
        if r['SpellID'] or r['SpellID1'] or r['Charges']:
            return 'usable item'
        if (r['PackSize'] or 1) > 1:
            return 'pack size above 1'
        return None

    refs = collections.defaultdict(list)
    for r in rows:
        if (r['MaxCount'] or 0) > 1 and (r['PackSize'] or 1) <= 1 and r['Item_Type'] == 40 and \
                r['Id_nb'].lower() in drops and not r['Id_nb'].lower().startswith('cq_'):
            refs[kind(r['Name'])].append(r)
    found, skipped = {}, collections.defaultdict(list)
    for r in rows:
        if r['Id_nb'].lower() not in drops or (r['MaxCount'] or 0) > 1:
            continue
        k = kind(r['Name'])
        ref = refs.get(k, [])
        if len(ref) < 2:
            continue
        why = excluded(r)
        if why:
            skipped[why].append(r['Id_nb'])
            continue
        sizes = collections.Counter(x['MaxCount'] for x in ref)
        size = max(sizes.items(), key=lambda kv: (kv[1], -kv[0]))[0]
        found[r['Id_nb']] = {'name': r['Name'], 'kind': k, 'max_count': size,
                             'source': f'{len(ref)} stacking {k} drops: ' +
                                       ', '.join(f'{n} x{s}' for s, n in sorted(sizes.items()))}
    return found, skipped


def main():
    c = sqlite3.connect('file:' + DB.replace('\\', '/') + '?mode=ro', uri=True)
    found, skipped = decide(c)
    kept = json.load(open(MAP_PATH, encoding='utf-8')) if os.path.exists(MAP_PATH) else {}
    new = {k: v for k, v in found.items() if k.lower() not in {x.lower() for x in kept}}
    decided = dict(kept, **new)
    by_kind = collections.Counter(v['kind'] for v in decided.values())
    print(f'{len(decided)} items stack ({len(new)} new this run); skipped: ' +
          ', '.join(f'{why} {len(v)}' for why, v in skipped.items()))
    for k, n in by_kind.most_common():
        print(f'  {k:10} {n:4} -> {next(v["max_count"] for v in decided.values() if v["kind"] == k)}')
    if DRY:
        return

    lower = {k.lower(): v['max_count'] for k, v in decided.items()}
    owned = set()
    for folder in (RUNTIME, HERE):
        for p in sorted(glob.glob(os.path.join(folder, 'add-*.json'))):
            raw, d = load(p)
            if d.get('table') != 'ItemTemplate':
                continue
            n = 0
            for r in d['rows']:
                k = (r.get('Id_nb') or '').lower()
                if k in lower:
                    owned.add(k)
                    if r.get('MaxCount') != lower[k]:
                        r['MaxCount'] = lower[k]
                        n += 1
            if save(p, raw, d):
                print(f'{p}: {n} rows')
    by_size = collections.defaultdict(list)
    for k, size in sorted(lower.items()):
        if k not in owned:
            by_size[size].append(k)
    updates = [{'sql': f"UPDATE ItemTemplate SET MaxCount = {size} WHERE lower(Id_nb) IN "
                       f"({', '.join(chr(39) + i.replace(chr(39), chr(39) * 2) + chr(39) for i in ids)}) AND MaxCount <> {size}",
                'reason': f'{TAG} (owner 2026-11-11, #36): {len(ids)} loot items stack to {size} like the other drops of '
                          f'their kind (build_stacks165.py, stack165.json)'}
               for size, ids in sorted(by_size.items())]
    for folder in (RUNTIME, HERE):
        p = os.path.join(folder, 'true165-rules.json')
        raw, d = load(p)
        d['updates'] = [u for u in d['updates'] if not str(u.get('reason', '')).startswith(TAG)] + updates
        print(p, 'written' if save(p, raw, d) else 'unchanged')
    json.dump(dict(sorted(decided.items(), key=lambda kv: kv[0].lower())), open(MAP_PATH, 'w', encoding='utf-8'),
              indent=1, ensure_ascii=False)
    print(f'{len(owned)} in add-files, {sum(len(v) for v in by_size.values())} by rules updates')


if __name__ == '__main__':
    main()
