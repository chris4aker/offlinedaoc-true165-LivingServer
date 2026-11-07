"""Own NPC templates for batch spawns that borrowed another monster's template (owner 2026-11-07: "they are both called
Mapog"). A template with ReplaceMobValues=1 overwrites the Mob row's name, level, model, size, aggression, speed, faction,
flags, body type and gear when the server loads it (GameNPC.LoadTemplate / HandleNewMobProperties / HandleBrain), so Pog
Mapog on Mapog's template came up as "Mapog", level 62. Each borrowed look gets its own copy of the template (same model,
gear, spells, styles, abilities, stats) carrying the row's own values; the original monster keeps its template.
"""
import collections, re, uuid, zlib

NS = uuid.UUID('3b1f8a2e-6d4c-4f59-9a7e-2c5d8e0f4b71')


def _nums(spec):
    return [int(x) for x in re.findall(r'\d+', str(spec or ''))]


def _in_spec(value, spec):
    """value inside a template list ('45;47;50') or range ('29-32', '150-200')."""
    spec = str(spec or '').strip()
    if not spec:
        return True
    if '-' in spec and ';' not in spec:
        lo, hi = (_nums(spec) + [0, 0])[:2]
        return lo <= int(value or 0) <= hi
    return int(value or 0) in _nums(spec)


def overrides(t, r):
    """What the template would overwrite on this row (empty: nothing that matters)."""
    why = []
    if (t['Name'] or '').strip().lower() != (r.get('Name') or '').strip().lower():
        why.append('name')
    if r.get('Level') and not _in_spec(r['Level'], t['Level']):
        why.append('level')
    if r.get('Model') and not _in_spec(r['Model'], t['Model']):
        why.append('model')
    if r.get('Size') and not _in_spec(r['Size'], t['Size']):
        why.append('size')
    for col, tcol in (('AggroLevel', 'AggroLevel'), ('AggroRange', 'AggroRange'), ('Speed', 'MaxSpeed')):
        if r.get(col) is not None and int(r[col]) != int(t[tcol] or 0):
            why.append(col.lower())
    # 0 on the row means "the template's" for these (live rows carry FactionID 0 and take the template's faction)
    for col, tcol in (('FactionID', 'FactionID'), ('Flags', 'Flags'), ('BodyType', 'BodyType')):
        if r.get(col) and int(r[col]) != int(t[tcol] or 0):
            why.append(col.lower())
    return why


def split(con, rows, now, tag, id_base):
    """Points every row a ReplaceMobValues template would overwrite at its own copy; returns the new template rows.
    Rows sharing (template, name) share one copy (levels, models and sizes as lists). Ids are stable: id_base + crc32."""
    cur = con.cursor()
    cols = [c[1] for c in cur.execute('pragma table_info(NpcTemplate)')]
    templates = {row[cols.index('TemplateId')]: dict(zip(cols, row)) for row in cur.execute('select * from NpcTemplate')}
    used = set(templates)
    groups = collections.defaultdict(list)
    for r in rows:
        t = templates.get(r.get('NPCTemplateID'))
        if t and t['ReplaceMobValues'] and overrides(t, r):
            # a faction of its own (not the template's) gets its own copy: one template carries one faction
            faction = int(r['FactionID']) if r.get('FactionID') and int(r['FactionID']) != int(t['FactionID'] or 0) else 0
            groups[(t['TemplateId'], r['Name'].strip().lower(), faction)].append(r)
    out = []
    for key in sorted(groups):
        tid, low, faction = key
        rs = groups[key]
        ident = f'{tag}|{tid}|{low}' + (f'|f{faction}' if faction else '')
        nid = id_base + zlib.crc32(ident.encode()) % 90_000 + 1
        while nid in used:
            nid += 1
        used.add(nid)
        base = templates[tid]
        common = lambda col: collections.Counter(r[col] for r in rs if r.get(col) is not None).most_common(1)[0][0] \
            if any(r.get(col) is not None for r in rs) else None
        new = dict(base)
        new.update({
            'TemplateId': nid, 'Name': rs[0]['Name'],
            'Level': ';'.join(str(v) for v in sorted({int(r['Level']) for r in rs if r.get('Level')})) or base['Level'],
            'Model': ';'.join(str(v) for v in sorted({int(r['Model']) for r in rs if r.get('Model')})) or base['Model'],
            'Size': ';'.join(str(v) for v in sorted({int(r['Size']) for r in rs if r.get('Size')})) or base['Size'],
            'AggroLevel': common('AggroLevel') if common('AggroLevel') is not None else base['AggroLevel'],
            'AggroRange': common('AggroRange') if common('AggroRange') is not None else base['AggroRange'],
            'MaxSpeed': common('Speed') if common('Speed') is not None else base['MaxSpeed'],
            'FactionID': faction or base['FactionID'],
            'Flags': common('Flags') if any(r.get('Flags') for r in rs) else base['Flags'],
            'BodyType': common('BodyType') or base['BodyType'],
            'EquipmentTemplateID': common('EquipmentTemplateID') or base['EquipmentTemplateID'],
            'PackageID': f'true165-{tag}-template', 'LastTimeRowUpdated': now,
            'NpcTemplate_ID': str(uuid.uuid5(NS, ident)),
        })
        out.append(new)
        for r in rs:
            r['NPCTemplateID'] = nid
    return out
