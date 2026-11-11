"""True 1.65 issue batch #31/#35/#36 world-content fixes (owner 2026-11-11).

1. #36 Kalf: the second "Kalf" (Mob d2ff99a4..., a MidLargeWeapons merchant on Jordheim Kalf's template 60162740,
   realm 0, standing in Spindelhalla at 31946,28525 - CapnBry's Kalph spot) is a stray row. Gamebots that needed a
   weapon shop picked it as the nearest merchant, so their goal read "Grind Kalf in Spindelhalla" (the service trip
   borrows the camp line for the region crossing). The row is removed; the real Kalf in Jordheim (region 101) stays.
2. #36 Te'Bui (Mob 100005865, level 35) stood ~660 units from the Spindelhalla arrival point (32152,31841), so low-level
   bots arriving landed next to it. It goes back to its CapnBry period spot (zone 125 local 26031,23149 z 16015; the
   zone's offset is 1,1 x 8192 -> region 34223,31341), on the floor the hallaratta (34202,31361 z 16021) and svartalf
   thrall (33986,31311 z 16010) stand on. Position only: level, stats, aggression and heading unchanged.
3. Tattered Rawhide Brea Jerkin: the period item is the leather ld_tattered_rawhide_brea_jerkin. The Prima batch mapped
   the Atlas "Tattered Brea" to the cloth Tattered_Rawhide_Brea_Jerkin, so 11 monsters got a cloth row (7 beside their
   leather one). The cloth rows go; feccan, grass spirit, lugradan whelp and Weldo get the leather one at the same
   chance. The cloth template stays (owned copies keep working). A Prima batch rerun maps to the leather item
   (agent_results.json existing_id) and never adds the cloth one (exclude_drop_items.json).
5. #35: seven Atlas realm-ability descriptions were stored double-encoded ("spellâ€™s",
   "Attack Â bonus"); they get plain ASCII text (the server sends codepage 1252).
6. #31: no consignment merchant in 1.65 housing (owner 2026-11-02): the housing_consignment_deed MerchantItem rows (the
   housing_*_lotmarker lists, the old *_lotmarker lists and the deed merchants' lists) are removed. Other deeds stay.
7. Race 2209 (no such race; the server warned and used 0 resists): the 13 "innocent" Mob rows in Marfach Caverns and
   NpcTemplates 7701, 55500, 9902 get Race 0 like the other 46 innocents - numbers unchanged.

Writes both copies of true165-rules.json and drops165.json (and the Prima batch's out/ copy of drops165.json),
plus prima_batch_2026-11-07/agent_results.json and exclude_drop_items.json. Rerunnable.

    python -I fix_batch36_165.py
"""
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
PRIMA = os.path.join(HERE, 'prima_batch_2026-11-07')
DB = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
TAG = 'True 1.65 batch 36'

STRAY_KALF = 'd2ff99a4-3a90-4910-8348-30631f316950'
TEBUI = ('100005865', 34223, 31341, 16015)
CLOTH, LEATHER = 'Tattered_Rawhide_Brea_Jerkin', 'ld_tattered_rawhide_brea_jerkin'
ABILITIES = ['AtlasOF_Ichor', 'AtlasOF_SeveringTheTether', 'AtlasOF_StyleDoombringer', 'AtlasOF_StyleRavager',
             'AtlasOF_StyleRazorback', 'AtlasOF_SpeedOfSound', 'AtlasOF_ThornweedField']


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


def q(s):
    return "'" + s.replace("'", "''") + "'"


def clean(text):
    """Undo the double encoding (UTF-8 read as cp1252) and keep the text plain ASCII."""
    t = text.replace('â€™', "'").replace('’', "'").replace('Â ', ' ').replace(' ', ' ')
    return re.sub(r' {2,}', ' ', t)


def ability_texts():
    import sqlite3
    c = sqlite3.connect('file:' + DB.replace('\\', '/') + '?mode=ro', uri=True)
    out = {}
    for key in ABILITIES:
        row = c.execute('SELECT Description FROM Ability WHERE KeyName = ?', (key,)).fetchone()
        if row is None:
            raise SystemExit(f'Ability {key} not in the world database')
        fixed = clean(row[0])
        if any(ord(ch) > 126 for ch in fixed):
            raise SystemExit(f'{key}: non-ASCII left after the fix: {fixed!r}')
        out[key] = fixed
    return out


def rules_edit(texts):
    where = [
        {'table': 'Mob', 'key': 'Mob_ID', 'where': f'Mob_ID = {q(STRAY_KALF)}',
         'reason': f'{TAG} (#36, owner 2026-11-11): the stray second Kalf - a weapon merchant on Jordheim Kalf\'s template '
                   'standing in Spindelhalla (31946,28525); gamebots shopped there ("Grind Kalf in Spindelhalla"). '
                   'The Jordheim Kalf stays'},
        {'table': 'MerchantItem', 'key': 'MerchantItem_ID', 'where': "lower(ItemTemplateID) = 'housing_consignment_deed'",
         'reason': f'{TAG} (#31, owner 2026-11-11): no consignment merchant in 1.65 housing - the consignment deed comes '
                   'off the lot markers and deed merchants (housing_*_lotmarker, *_lotmarker and the deed lists)'},
    ]
    mob, x, y, z = TEBUI
    updates = [
        {'sql': f'UPDATE Mob SET X = {x}, Y = {y}, Z = {z} WHERE Mob_ID = {q(mob)} AND Region = 125',
         'reason': f"{TAG} (#36, owner 2026-11-11): Te'Bui back at its CapnBry spot (zone 125 local 26031,23149 z 16015 "
                   '-> region 34223,31341) instead of ~660 units from the Spindelhalla arrival point, where low-level '
                   'bots landed beside it; position only (level, stats, aggression unchanged)'},
    ] + [
        {'sql': f'UPDATE Ability SET Description = {q(text)} WHERE KeyName = {q(key)}',
         'reason': f'{TAG} (#35, owner 2026-11-11): {key} description stored double-encoded (UTF-8 read as cp1252); '
                   'plain ASCII text'}
        for key, text in texts.items()] + [
        {'sql': 'UPDATE Mob SET Race = 0 WHERE Race = 2209',
         'reason': f'{TAG} (owner 2026-11-11, "fix race 2209 if it\'s an issue"): the 13 "innocent" rows in Marfach '
                   'Caverns (region 276) had Race 2209, which does not exist (the server warned and fell back to 0 '
                   'resists); Race 0 like the other 46 innocents - numbers unchanged'},
        {'sql': 'UPDATE NpcTemplate SET Race = 0 WHERE TemplateId IN (7701, 55500, 9902) AND Race = 2209',
         'reason': f'{TAG} (owner 2026-11-11): the three "innocent" templates with the non-existent Race 2209 -> 0 '
                   '(same 0 resists as before, no warning)'}]
    for folder in (RUNTIME, HERE):
        p = os.path.join(folder, 'true165-rules.json')
        raw, d = load(p)
        d['remove']['where'] = [w for w in d['remove']['where'] if not w['reason'].startswith(TAG)] + where
        d['updates'] = [u for u in d['updates'] if not str(u.get('reason', '')).startswith(TAG)] + updates
        print(p, 'written' if save(p, raw, d) else 'unchanged')


def drops_edit():
    report = {}
    for p in (os.path.join(RUNTIME, 'drops165.json'), os.path.join(HERE, 'drops165.json'),
              os.path.join(PRIMA, 'out', 'drops165.json')):
        raw, d = load(p)
        for mob, v in d['monsters'].items():
            items = v['items']
            cloth = [it for it in items if it[0].lower() == CLOTH.lower()]
            if not cloth:
                continue
            has_leather = any(it[0].lower() == LEATHER for it in items)
            if has_leather:
                v['items'] = [it for it in items if it[0].lower() != CLOTH.lower()]
                report[mob] = 'cloth row removed (has the leather jerkin)'
            else:
                v['items'] = [[LEATHER, it[1]] if it[0].lower() == CLOTH.lower() else it for it in items]
                report[mob] = f'cloth row -> leather at {cloth[0][1]}%'
        print(p, 'written' if save(p, raw, d) else 'unchanged')
    for mob, what in sorted(report.items()):
        print('  ', mob, ':', what)

    p = os.path.join(PRIMA, 'agent_results.json')
    raw, d = load(p)
    n = 0

    def walk(o):
        nonlocal n
        if isinstance(o, dict):
            if o.get('existing_id') == CLOTH:
                o['existing_id'] = LEATHER
                o['notes'] = (o.get('notes') or '') + (' Mapped to the period leather ld_tattered_rawhide_brea_jerkin '
                                                       '(owner 2026-11-11: the cloth jerkin is not the period drop).')
                n += 1
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)
    walk(d)
    print(p, f'{n} existing_id changed,', 'written' if save(p, raw, d) else 'unchanged')

    p = os.path.join(PRIMA, 'exclude_drop_items.json')
    raw = open(p, 'rb').read().decode('utf-8')
    lst = json.loads(raw)
    if CLOTH.lower() not in [x.lower() for x in lst]:
        lst.append(CLOTH.lower())
        nl = '\r\n' if '\r\n' in raw else '\n'
        open(p, 'wb').write((json.dumps(lst, ensure_ascii=False) + (nl if raw.endswith(('\n', '\r\n')) else '')).encode('utf-8'))
        print(p, 'written')


if __name__ == '__main__':
    rules_edit(ability_texts())
    drops_edit()
