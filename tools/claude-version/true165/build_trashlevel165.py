"""True 1.65: every vendor-trash item at level 0 (owner 2026-11-07), by the rule in trashlevel165.py.

    python -I build_trashlevel165.py <world db after a refresh>      (rerunnable; a second run changes nothing)

- rows an add-file owns (it rewrites them every refresh) are changed in that add-file, both copies;
- the other rows get one rules update (true165-rules.json, both copies, and prima_batch_2026-11-07/
  trashlevel165_generated.json so the Prima batch builder keeps it when it rebuilds the rules);
- each item's level before the change is kept in trash_levels_original165.json (the drop builders compare that level).
add-primaitems165.json is left to the Prima batch builder (it applies the same rule).
"""
import json, os, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
BATCH_GEN = os.path.join(HERE, 'prima_batch_2026-11-07', 'trashlevel165_generated.json')
sys.dont_write_bytecode = True
sys.path.insert(0, HERE)
import trashlevel165 as T   # noqa: E402

TAG = 'True 1.65 vendor trash is level 0 (owner 2026-11-07)'
SKIP_FILES = {'add-primaitems165.json'}

con = sqlite3.connect('file:' + sys.argv[1].replace(chr(92), '/') + '?mode=ro', uri=True)
con.row_factory = sqlite3.Row
dropped = {r[0].lower() for r in con.execute('select ItemTemplateID from LootTemplate')}
sold = {r[0].lower() for r in con.execute('select ItemTemplateID from MerchantItem')}
original = dict(T.original_levels())
report = {'add_files': {}, 'rules': [], 'kept_uthgard_level': []}


def want(row):
    return T.display_level(row.get('Name')) if T.is_vendor_trash(row, dropped, sold) else None


def remember(iid, level):
    if level and iid.lower() not in original:
        original[iid.lower()] = int(level)


# 1. the add-files that own ItemTemplate rows
owned = set()
for name in sorted(os.listdir(RUNTIME)):
    if not (name.startswith('add-') and name.endswith('.json')) or name in SKIP_FILES:
        continue
    docs = {}
    for folder in (RUNTIME, HERE):
        p = os.path.join(folder, name)
        if os.path.exists(p):
            raw = open(p, 'rb').read().decode('utf-8')
            docs[p] = (raw, json.loads(raw))
    first = next(iter(docs.values()))[1]
    if first.get('table') != 'ItemTemplate':
        continue
    changed = []
    for p, (raw, d) in docs.items():
        n = 0
        for r in d['rows']:
            owned.add((r.get('Id_nb') or '').lower())
            lv = want(r)
            if lv is not None and (r.get('Level') or 0) != lv:
                remember(r['Id_nb'], r.get('Level'))
                if p.startswith(RUNTIME):
                    changed.append((r['Id_nb'], r.get('Level'), lv))
                r['Level'] = lv
                n += 1
        if n:
            nl = '\r\n' if '\r\n' in raw else '\n'
            text = json.dumps(d, ensure_ascii=False, indent=1)
            if raw.endswith('\n'):
                text += '\n'
            open(p, 'w', encoding='utf-8', newline=nl).write(text)
    if changed:
        report['add_files'][name] = changed

# 2. the rows no add-file owns: one rules update per target level
by_level = {}
for r in con.execute('select Id_nb, Name, Level, Object_Type, Price, PackageID from ItemTemplate'):
    r = dict(r)
    if r['Id_nb'].lower() in owned:
        continue
    lv = want(r)
    if lv is None:
        continue
    before = T.research_level(r['Id_nb'], r['Level'])
    if lv:
        report['kept_uthgard_level'].append((r['Id_nb'], r['Name'], lv))
    if (r['Level'] or 0) != lv or r['Id_nb'].lower() in original:
        remember(r['Id_nb'], r['Level'])
        by_level.setdefault(lv, []).append(r['Id_nb'])
        report['rules'].append((r['Id_nb'], r['Name'], before, lv))
rules = []
for lv, ids in sorted(by_level.items()):
    ids = sorted(set(ids), key=str.lower)
    rules.append({'sql': f"UPDATE ItemTemplate SET Level = {lv} WHERE lower(Id_nb) IN ("
                         + ', '.join("'" + i.lower().replace("'", "''") + "'" for i in ids) + f") AND Level <> {lv}",
                  'reason': f'{TAG}: {len(ids)} vendor-trash items that monsters drop show level {lv} '
                            + ('(the Uthgard page level)' if lv else '(cons grey), as on the period pages; drops unchanged')})
json.dump({'rules': rules}, open(BATCH_GEN, 'w', encoding='utf-8'), indent=1)
for folder in (RUNTIME, HERE):
    p = os.path.join(folder, 'true165-rules.json')
    raw = open(p, 'rb').read().decode('utf-8')
    d = json.loads(raw)
    d['updates'] = [u for u in d['updates'] if not str(u.get('reason', '')).startswith(TAG)] + rules
    nl = '\r\n' if '\r\n' in raw else '\n'
    text = json.dumps(d, ensure_ascii=False, indent=1) + ('\n' if raw.endswith('\n') else '')
    open(p, 'w', encoding='utf-8', newline=nl).write(text)

json.dump(dict(sorted(original.items())), open(T.ORIGINAL_PATH, 'w', encoding='utf-8'), indent=0)
json.dump(report, open(os.path.join(HERE, 'trashlevel165_report.json'), 'w', encoding='utf-8'), indent=1)
print('add-files:', {k: len(v) for k, v in report['add_files'].items()}, '| rules rows:', len(report['rules']),
      '| originals kept:', len(original))
