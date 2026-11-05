"""True 1.65: drops far above their monster's level (owner 2026-11-05: "fix the drop builder and review the list").

build_drops165.py matched Uthgard's measured drops to our items by NAME and took the template nearest the monster's
level - but when the only template of that name is a high-level item, a low monster drops it. Level-3 phantom hounds
dropped "Gnawed Bone" (35%), and the only Gnawed Bone here is the level-49 Shimmering Gnawed Bone hammer (vendor value
~3.8 gold): low gamebots sold them by the dozen (one Shadowblade went from 65 copper to 34 gold). The period phantom hound
dropped a plain gnawed bone (Allakhazam zone page: "phantom hound lev 3-3 undead drop gnawed bone").

For every period-drop row (drops165.json) whose item is GAP or more levels above the monster:
- loot junk (a generic item - essences, sacks, vials, gems - or a junk name in JUNK_NAMES) becomes a junk twin at the
  monster's level ('<id>_165l<N>', a generic item priced as the classic vendor trash of monsters of that level);
- gear (weapons, armor, jewelry) is removed: so far above its monster it is no high-likelihood period drop (owner's drop
  policy 2026-10-31); the period evidence check below is recorded for the report only.
Evidence recorded (report only): the period pages (Allakhazam zone pages' "monster lev x-y ... drop item" lines, Wayback pre-ToA monster captures)
  name the item for that monster (the live Allakhazam monster pages are not used: they list later-expansion drops): the drop is the period loot item of that name, so it becomes a junk twin at the monster's level ('<id>_165l<N>',
  a generic item with the median price of generic loot of that level);
- no period page names it: an Uthgard-only drop, not a high-likelihood period drop (owner's drop policy 2026-10-31), so the
  row is removed.
Worlds that already carry the old rows lose them through a rules update (refresh: updates run before the drops step).

python -I build_dropfix165.py      (rerunnable; decisions kept in dropfix165.json, the snapshot)
Out (both copies): add-droptwins165.json, drops165.json, true165-rules.json; dropfix165.json (this folder).
Run it again after build_drops165.py.
"""
import collections, gzip, json, os, re, sqlite3, statistics

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
DB = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
ARCHIVE = os.path.join(CV, 'development-source', 'research', 'period-data', 'archive')
SNAPSHOT = os.path.join(HERE, 'dropfix165.json')
GAP = 15
# period loot junk whose only template on the server is a high-level weapon of the same name (the period phantom hound
# dropped a plain gnawed bone: Allakhazam zone page "phantom hound lev 3-3 undead drop gnawed bone")
JUNK_NAMES = {'gnawed bone'}
TAG = 'True 1.65 dropfix'


def norm(s):
    s = (s or '').lower().replace('`', "'").replace('’', "'")
    return re.sub(r'[^a-z0-9]+', ' ', s).strip()


def period_texts():
    """(kind, subject, text) of the period pages: monster pages (pre-ToA Wayback captures, live Allakhazam) with their
    title as the subject, and zone pages; texts lower-cased and tag-free."""
    sources = [(os.path.join(ARCHIVE, 'wayback-allakhazam', 'period_mobs.jsonl.gz'), 'body', 'mob'),
               (os.path.join(ARCHIVE, 'allakhazam-live', 'zone.jsonl.gz'), 'html', 'zone')]
    for path, field, kind in sources:
        if not os.path.exists(path):
            continue
        with gzip.open(path, 'rt', encoding='utf-8', errors='ignore') as f:
            for line in f:
                try:
                    o = json.loads(line)
                except ValueError:
                    continue
                if o.get('era') not in (None, 'pre-ToA'):
                    continue
                raw = o.get(field) or ''
                title = o.get('title') or ''
                m = re.search(r'document\.title\s*=\s*"[^"]*?:\s*([^"]+)"', raw)   # Illia's bestiary: "...: Bear Cub"
                if m:
                    title = m.group(1)
                if not title:
                    m = re.search(r'<title>(.*?)</title>', raw, re.S | re.I) or re.search(r'<h1[^>]*>(.*?)</h1>', raw, re.S | re.I)
                    title = m.group(1) if m else ''
                yield kind, norm(re.sub(r'<[^>]+>', ' ', title)), norm(re.sub(r'<[^>]+>', ' ', raw))


def names_drop(kind, subject, text, mob, item):
    """The page says this monster drops this item: its own page names the item, or a zone line 'mob lev x-y ... drop item'."""
    if kind == 'mob':
        return subject.startswith(mob) and item in text
    return re.search(re.escape(mob) + r' lev \d+ \d+ .{0,80}?drop.{0,160}?' + re.escape(item), text) is not None


def main():
    con = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
    con.row_factory = sqlite3.Row
    q = lambda s, *a: con.execute(s, a).fetchall()

    tmpl_level = {}
    for r in q('SELECT TemplateId, Level FROM NpcTemplate'):
        nums = [int(x) for x in re.findall(r'\d+', str(r['Level'] or ''))]
        if nums:
            tmpl_level[str(r['TemplateId'])] = max(nums)
    mob_levels = collections.defaultdict(list)
    for r in q('SELECT Name, Level, NPCTemplateID FROM Mob WHERE Realm = 0'):
        L = r['Level'] or tmpl_level.get(str(r['NPCTemplateID']), 0)
        if L > 0:
            mob_levels[(r['Name'] or '').lower()].append(L)
    templates = {r['Id_nb'].lower(): r for r in q('SELECT * FROM ItemTemplate')}
    # Junk prices from the classic vendor trash (level-0 generic loot such as rotting carrion 60c, torn wolf pelt 30c, cliff
    # spider legs 540c, chunk of ghoul flesh 1,400c, fire giant tooth 2,400c): the median price of the trash dropped by
    # monsters of about the same level.
    trash = {key for key, t in templates.items()
             if (t['Object_Type'] or 0) == 0 and (t['Level'] or 0) == 0 and (t['Price'] or 0) > 0}
    template_items = collections.defaultdict(set)
    for r in q('SELECT TemplateName, ItemTemplateID FROM LootTemplate'):
        if (r[1] or '').lower() in trash:
            template_items[(r[0] or '').lower()].add(r[1].lower())
    junk_prices = collections.defaultdict(list)
    for r in q('SELECT MobName, LootTemplateName FROM MobXLootTemplate'):
        levels = mob_levels.get((r[0] or '').lower())
        if not levels:
            continue
        level = round(sum(levels) / len(levels))
        for key in template_items.get((r[1] or '').lower(), ()):
            junk_prices[level].append(templates[key]['Price'])

    def junk_price(level):
        for spread in range(2, 8):
            near = [p for L in range(level - spread, level + spread + 1) for p in junk_prices.get(L, [])]
            if len(near) >= 9:
                return int(statistics.median(near))
        return max(1, level * 20)

    snap = json.load(open(SNAPSHOT, encoding='utf-8')) if os.path.exists(SNAPSHOT) else {'decisions': []}
    decided = {(d['mob'].lower(), d['item'].lower()): d for d in snap['decisions']}

    drops = json.load(open(os.path.join(HERE, 'drops165.json'), encoding='utf-8'))
    todo = []
    for mob, entry in drops['monsters'].items():
        levels = mob_levels.get(mob.lower())
        if not levels:
            continue
        for idnb, chance in entry['items']:
            t = templates.get(idnb.lower())
            if t is not None and (t['Level'] or 0) >= max(levels) + GAP and (mob.lower(), idnb.lower()) not in decided:
                todo.append((mob, idnb, chance, t, levels))

    if todo:
        wanted = {(mob, norm(t['Name'])): (norm(mob), norm(t['Name'])) for mob, idnb, chance, t, levels in todo}
        found = set()
        for kind, subject, text in period_texts():
            for key, (m, item) in wanted.items():
                if key not in found and m in text and item in text and names_drop(kind, subject, text, m, item):
                    found.add(key)
        for mob, idnb, chance, t, levels in todo:
            level = round(sum(levels) / len(levels))
            evidence = (mob, norm(t['Name'])) in found
            d = {'mob': mob, 'item': idnb, 'item_name': t['Name'], 'item_level': t['Level'], 'item_price': t['Price'],
                 'mob_levels': [min(levels), max(levels)], 'chance': chance, 'period_page_names_it': evidence}
            # loot junk (a generic item, or a junk loot name whose only template here is a weapon) keeps dropping as a
            # junk item of the monster's level; gear 15+ levels above its monster is no high-likelihood period drop
            junk = (t['Object_Type'] or 0) == 0 or norm(t['Name']) in JUNK_NAMES
            if junk:
                twin = f'{idnb}_165l{level}'
                d.update(action='junk twin', twin=twin, twin_level=level, twin_price=junk_price(level))
            else:
                d.update(action='removed')
            snap['decisions'].append(d)
            decided[(mob.lower(), idnb.lower())] = d

    twins = {}
    for d in snap['decisions']:
        if d['action'] != 'junk twin' or d['twin'] in twins:
            continue
        src = templates.get(d['item'].lower())
        row = {k: src[k] for k in src.keys()} if src is not None else {}
        for k in list(row):
            if k.startswith('Bonus') or k in ('ExtraBonus', 'ExtraBonusType', 'SpellID', 'SpellID1', 'ProcSpellID', 'ProcSpellID1',
                                              'Charges', 'Charges1', 'MaxCharges', 'MaxCharges1', 'PoisonSpellID', 'PoisonCharges',
                                              'PoisonMaxCharges', 'ProcChance', 'Effect', 'Emblem'):
                row[k] = 0
        row.update(Id_nb=d['twin'], Name=(d['item_name'] or '').lower(), Level=d['twin_level'], Price=d['twin_price'],
                   Object_Type=0, Item_Type=0, DPS_AF=0, SPD_ABS=0, Hand=0, Type_Damage=0, Quality=100, Weight=1,
                   IsDropable=1, IsTradable=1, IsPickable=1, IsIndestructible=0, AllowedClasses='', AllowedRealms='',
                   Realm=0, PackageID='True165_dropfix', LastTimeRowUpdated='2026-11-05 22:00:00',
                   Description=f"Loot ({d['mob']}, period drop); the only '{d['item_name']}' template was level {d['item_level']}")
        row.pop('ItemTemplate_ID', None)
        twins[d['twin']] = row

    remap = {(d['mob'].lower(), d['item'].lower()): d for d in snap['decisions']}
    rules_updates = []
    for d in snap['decisions']:
        rules_updates.append({
            'sql': "DELETE FROM LootTemplate WHERE lower(TemplateName) = lower('%s') AND lower(ItemTemplateID) = lower('%s')"
                   % (d['mob'].replace("'", "''"), d['item'].replace("'", "''")),
            'reason': f"{TAG}: {d['mob']} (level {d['mob_levels'][0]}-{d['mob_levels'][1]}) no longer drops the level "
                      f"{d['item_level']} '{d['item_name']}' "
                      + (f"(period drop: now the level {d['twin_level']} loot item {d['twin']})" if d['action'] == 'junk twin'
                         else '(Uthgard-only, no period page names it)')})

    changed = 0
    for folder in (HERE, RUNTIME):
        p = os.path.join(folder, 'drops165.json')
        data = json.load(open(p, encoding='utf-8'))
        for mob, entry in data['monsters'].items():
            items = []
            for idnb, chance in entry['items']:
                d = remap.get((mob.lower(), idnb.lower()))
                if d is None:
                    items.append([idnb, chance])
                elif d['action'] == 'junk twin':
                    items.append([d['twin'], chance])
                    changed += 1
                else:
                    changed += 1
            entry['items'] = items
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
            f.write('\n')
        with open(os.path.join(folder, 'add-droptwins165.json'), 'w', encoding='utf-8') as f:
            json.dump({'table': 'ItemTemplate', 'key': 'Id_nb',
                       'reason': 'True 1.65 loot items at their monster level (period drops whose only template was far above it)',
                       'rows': list(twins.values())}, f, ensure_ascii=False, indent=1)
            f.write('\n')
        rp = os.path.join(folder, 'true165-rules.json')
        rules = json.load(open(rp, encoding='utf-8'))
        rules['updates'] = [u for u in rules['updates'] if not u['reason'].startswith(TAG)] + rules_updates
        with open(rp, 'w', encoding='utf-8') as f:
            json.dump(rules, f, ensure_ascii=False, indent=1)
            f.write('\n')
    with open(SNAPSHOT, 'w', encoding='utf-8') as f:
        json.dump(snap, f, ensure_ascii=False, indent=1)
        f.write('\n')
    acts = collections.Counter(d['action'] for d in snap['decisions'])
    print(f"decisions {len(snap['decisions'])} {dict(acts)}; twins {len(twins)}; drops165 entries changed {changed} (both copies)")


if __name__ == '__main__':
    main()
