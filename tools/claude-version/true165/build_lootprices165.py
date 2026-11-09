"""True 1.65: loot that sold for a flat placeholder 50c gets its period value, or an estimate from the level of the monsters
that drop it (owner 2026-11-07: "i want period values for those 34 if they can be found, and estimated based on what lvl of
mobs drop the items not a generic amount").

    python -I build_lootprices165.py <world db after a refresh> [--dry]     (rerunnable; a second run changes nothing)
Run it after any builder that rewrites an item add-file (build_primaquests165.py writes its quest items at price 0).

Candidates: vendor trash a monster drops whose price is the placeholder 100 copper (sells for 50c), plus every item already in
loot_prices165.json. For each:
- the period value: the Uthgard item page's sell value (archive raw_pages) x 2 (merchants pay half);
- else an estimate: the median Uthgard sell value of period trash dropped by monsters within 2 levels of this item's droppers
  (gems against period gems only), widening until 9 samples; x 2.
Items whose Uthgard value really is 50c keep 100. Results (with their source) go to loot_prices165.json; the owning add-files
are edited in place (both copies), other rows get one rules update (also written to prima_batch_2026-11-07/
lootprices165_generated.json for the Prima batch builder). The batch builder and the drops builder read loot_prices165.json.

Unsellable loot (owner 2026-11-09: "make all 104 items have a price even if you have to give an estimated price based on
the level of mob that it drops from relative to similar other items without being op"): every item a monster drops whose
price is 0 (a merchant "isn't interested") is a candidate too, whatever its kind:
- a no-slot item with an Uthgard page: that sell value x 2, as above;
- other no-slot items (trash, quest pieces, XP turn-ins): the same estimate by the level of their droppers (no dropper
  level known: the item's own level);
- equipment (it has an object type): half the median price of the priced equipment of its object type that monsters
  drop, within 2 item levels (widening until 9 samples) - what similar drops of its level sell for;
- the three Darkness Falls seals are the dungeon's currency and drop by the handful from every monster there: a small
  fixed value (FIXED_SELL: 1s / 2s 50c / 5s; no period value found), far under the trash of those monsters;
- the "Ancient ..." crafting ingredients and Enriched Quicksilver: about 35 monsters of level 40-55 carry the whole set
  at 10% each, up to three at a time. At the trash estimate (2g) or Uthgard's values (2g and 5g, from far rarer drops)
  the set would add about 2g 80s to every kill there, two or three times what those monsters drop today; at a fixed
  10s each it adds about 12s.
A later run never changes an entry whose price is still in the world.
Once priced, a zero-price entry is kept as it is on later runs ('zero': true in loot_prices165.json).
"""
import gzip, html, json, os, re, sqlite3, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
UTHGARD = os.path.join(CV, 'development-source', 'research', 'period-data', 'archive', 'uthgard', 'raw_pages.repaired.jsonl.gz')
MAP_PATH = os.path.join(HERE, 'loot_prices165.json')
BATCH_GEN = os.path.join(HERE, 'prima_batch_2026-11-07', 'lootprices165_generated.json')
TAG = 'True 1.65 loot prices (owner 2026-11-07)'
PLACEHOLDER = 100
SEALS = 'Darkness Falls currency, dropped by the handful: a small fixed value (no period value found)'
ANCIENT = ('a crafting ingredient about 35 monsters drop as a set, up to three at a time: a small fixed value, so the set '
           'adds about 12s a kill (the trash estimate would add 2g 80s)')
FIXED_SELL = {'emeraldseal': (100, SEALS), 'sapphireseal': (250, SEALS), 'diamondseal': (500, SEALS),   # copper for one
              'ancient_crushed_focus_stone': (1000, ANCIENT), 'ancient_giant_blood': (1000, ANCIENT),
              'ancient_lich_tooth': (1000, ANCIENT), 'ancient_mirror': (1000, ANCIENT),
              'ancient_necrotic_brain_fluid': (1000, ANCIENT), 'ancient_pure_mercury': (1000, ANCIENT),
              'ancient_troll_blood': (1000, ANCIENT), 'enriched_quicksilver': (1000, ANCIENT)}
DRY = '--dry' in sys.argv
U_ALL = {}                                          # every no-slot Uthgard item: lower name -> sell copper
GEM_WORDS = ('opal', 'spinel', 'tourmaline', 'diopside', 'diamond', 'ruby', 'emerald', 'sapphire', 'garnet', 'topaz', 'jade',
             'onyx', 'amethyst', 'beryl', 'jasper', 'lapis', 'sphene', 'zircon', 'agate', 'moonstone', 'pearl', 'carnelian',
             'bloodstone', 'quartz', 'citrine', 'peridot', 'aquamarine', 'chrysoberyl', 'tanzanite', 'malachite', 'obsidian')
sys.dont_write_bytecode = True
sys.path.insert(0, HERE)
import trashlevel165 as TL   # noqa: E402


def is_gem(name):
    n = (name or '').lower()
    return any(w in n.split() or n.endswith(w) for w in GEM_WORDS) and 'staff' not in n and 'sword' not in n


def coin(t):
    g = re.search(r'(\d+)g', t); s = re.search(r'(\d+)s', t); c = re.search(r'(\d+)c', t)
    return (int(g.group(1)) if g else 0) * 10000 + (int(s.group(1)) if s else 0) * 100 + (int(c.group(1)) if c else 0)


def uthgard_trash():
    """{lower name: (sell copper, [dropping monster names])} for Uthgard items with no slot or item type."""
    out = {}
    for line in gzip.open(UTHGARD, 'rt', encoding='utf-8'):
        d = json.loads(line)
        if 'item.php?load=' not in d.get('url', ''):
            continue
        b = re.sub(r'<script.*?</script>', '', d.get('body') or d.get('html') or d.get('text') or '', flags=re.S)
        t = html.unescape(re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', ' ', b)))
        m = re.search(r'Item Name Information Stats (.*?)\. Level \d+ Slot ?(.*?) ?Item type ?(.*?) ?Damage type.*?Sell value '
                      r'([0-9gsc ]+?) Salvages', t)
        if not m or m.group(2).strip():
            continue
        name = m.group(1).strip(); h = len(name) // 2
        if len(name) % 2 == 1 and name[:h] == name[h + 1:]:
            name = name[:h]
        U_ALL.setdefault(name.lower(), coin(m.group(4)))      # crafting pieces have an item type and no slot
        if m.group(3).strip():
            continue
        drops = []
        dm = re.search(r'Item can drop from these monsters Name Total dropped Drop rate (.*)', t)
        if dm:
            drops = [x.strip() for x in re.findall(r"([A-Za-z][A-Za-z' -]*?) \d+ [\d.]+%", dm.group(1))[:12]]
        out.setdefault(name.lower(), (coin(m.group(4)), drops))
    return out


con = sqlite3.connect('file:' + sys.argv[1].replace(chr(92), '/') + '?mode=ro', uri=True)
con.row_factory = sqlite3.Row
mob_levels = {}
tmpl_levels = {r['TemplateId']: [int(x) for x in re.findall(r'\d+', str(r['Level'] or ''))] for r in con.execute(
    'select TemplateId, Level from NpcTemplate')}
for r in con.execute('select Name, Level, NPCTemplateID from Mob where Realm = 0'):
    lv = r['Level'] or min(tmpl_levels.get(r['NPCTemplateID']) or [0])
    if lv > 0:
        mob_levels.setdefault(r['Name'].lower(), []).append(lv)
level_of = lambda name: min(mob_levels.get((name or '').lower()) or [0])

U = uthgard_trash()
samples = {'gem': [], 'trash': []}
for name, (sell, drops) in U.items():
    lv = [level_of(m) for m in drops if level_of(m)]
    if sell > 0 and lv:
        samples['gem' if is_gem(name) else 'trash'].append((int(statistics.median(lv)), sell))


def estimate(level, gem):
    pool = samples['gem'] if gem and len(samples['gem']) >= 9 else samples['trash']
    for spread in range(2, 30):
        near = [s for lv, s in pool if abs(lv - level) <= spread]
        if len(near) >= 9:
            return int(statistics.median(near)), spread, len(near)
    return max(1, level * 20), None, 0


dropped = {}
for r in con.execute('select lower(TemplateName) t, lower(ItemTemplateID) i from LootTemplate'):
    dropped.setdefault(r['i'], set()).add(r['t'])
templ_mobs = {}
for r in con.execute('select lower(MobName) m, lower(LootTemplateName) t from MobXLootTemplate'):
    templ_mobs.setdefault(r['t'], set()).add(r['m'])
old = json.load(open(MAP_PATH, encoding='utf-8')) if os.path.exists(MAP_PATH) else {}
items = {r['Id_nb'].lower(): dict(r) for r in con.execute(
    'select Id_nb, Name, Price, PackageID, Object_Type, Level from ItemTemplate')}
all_mobs = {r[0].lower() for r in con.execute('select Name from Mob')}
gear = {}                                           # object type -> [(item level, price)] of priced dropped equipment
for iid, it in items.items():
    if it['Object_Type'] and it['Price'] > PLACEHOLDER and (it['Level'] or 0) > 0 and iid in dropped:
        gear.setdefault(it['Object_Type'], []).append((it['Level'], it['Price']))


def estimate_gear(object_type, level):
    pool = gear.get(object_type) or [x for v in gear.values() for x in v]
    for spread in range(2, 51):
        near = [p for lv, p in pool if abs(lv - level) <= spread]
        if len(near) >= 9:
            return int(statistics.median(near)) // 2, spread, len(near)
    return max(1, level * 20), None, 0


result = {}
for iid, it in sorted(items.items()):
    mobs = set()
    for t in dropped.get(iid, ()):
        mobs |= templ_mobs.get(t, {t})
    zero = it['Price'] == 0 and any(m in all_mobs for m in mobs)       # a monster drops it and no merchant takes it
    if iid in old and old[iid].get('zero') and it['Price'] in (0, old[iid]['price']):
        result[iid] = old[iid]                      # priced on an earlier run: stays as it is
        continue
    if iid not in old and not (it['Object_Type'] == 0 and it['Price'] == PLACEHOLDER and iid in dropped) and not zero:
        continue
    if iid in old and it['Price'] not in (PLACEHOLDER, old[iid]['price']):
        continue                                    # someone set a real price since: leave it
    levels = sorted(l for l in (level_of(m) for m in mobs) if l)
    level = int(statistics.median(levels)) if levels else 0
    if zero:
        droppers = f"droppers: level {levels[0]}-{levels[-1]}" if levels else 'no dropper level known'
        if iid in FIXED_SELL:
            sell, src = FIXED_SELL[iid]
        elif it['Object_Type']:
            ilv = it['Level'] or level or 1
            sell, spread, n = estimate_gear(it['Object_Type'], ilv)
            src = (f"estimated: half the median price of {n} dropped items of its kind (object type {it['Object_Type']}), "
                   f"item level {max(1, ilv - (spread or 0))}-{ilv + (spread or 0)} ({droppers})")
        elif U_ALL.get(it['Name'].lower(), 0) > 0:
            sell = U_ALL[it['Name'].lower()]; src = f'Uthgard sell value {sell} copper'
        else:
            lv = level or it['Level'] or 1
            sell, spread, n = estimate(lv, is_gem(it['Name']))
            src = (f"estimated: median Uthgard sell of {n} period {'gems' if is_gem(it['Name']) and len(samples['gem']) >= 9 else 'trash items'} "
                   f"dropped by level {max(1, lv - (spread or 0))}-{lv + (spread or 0)} monsters ({droppers})")
        result[iid] = {'name': it['Name'], 'price': max(1, sell) * 2, 'sell': max(1, sell), 'source': src, 'drop_level': level,
                       'zero': True}
        continue
    u = U.get(it['Name'].lower())
    if u and u[0] > 0:
        sell, src = u[0], f'Uthgard sell value {u[0]} copper'
    elif level:
        sell, spread, n = estimate(level, is_gem(it['Name']))
        src = (f"estimated: median Uthgard sell of {n} period {'gems' if is_gem(it['Name']) and len(samples['gem']) >= 9 else 'trash items'} "
               f"dropped by level {level - (spread or 0)}-{level + (spread or 0)} monsters (droppers: level {levels[0]}-{levels[-1]})")
    else:
        continue
    result[iid] = {'name': it['Name'], 'price': sell * 2, 'sell': sell, 'source': src, 'drop_level': level}

for iid in result:                                  # a later run changes no price that still stands
    if iid in old and old[iid]['price'] == result[iid]['price']:
        result[iid] = old[iid]

# write the prices where each row lives
owned = {}
for name in sorted(os.listdir(RUNTIME)):
    if not (name.startswith('add-') and name.endswith('.json')):
        continue
    for folder in (RUNTIME, HERE):
        p = os.path.join(folder, name)
        if not os.path.exists(p):
            continue
        raw = open(p, 'rb').read().decode('utf-8'); d = json.loads(raw)
        if d.get('table') != 'ItemTemplate':
            break
        n = 0
        for r in d['rows']:
            k = (r.get('Id_nb') or '').lower()
            owned[k] = name
            if k in result and r.get('Price') != result[k]['price']:
                r['Price'] = result[k]['price']; n += 1
        if n and name != 'add-primaitems165.json' and not DRY:      # the batch builder writes that one (it reads loot_prices165.json)
            nl = '\r\n' if '\r\n' in raw else '\n'
            open(p, 'w', encoding='utf-8', newline=nl).write(json.dumps(d, ensure_ascii=False, indent=1) + ('\n' if raw.endswith('\n') else ''))
rules = [{'sql': f"UPDATE ItemTemplate SET Price = {v['price']} WHERE lower(Id_nb) = '{k.replace(chr(39), chr(39) * 2)}' AND Price <> {v['price']}",
          'reason': f"{TAG}: {v['name']} - {'no merchant took it (price 0, owner 2026-11-09); ' if v.get('zero') else ''}{v['source']} "
                    f"(sells for {v['sell']} copper)"}
         for k, v in sorted(result.items()) if k not in owned]
if DRY:
    for k, v in sorted(result.items(), key=lambda kv: (not kv[1].get('zero'), kv[1]['name'].lower())):
        print(f"  {'NEW ' if k not in old else '    '}{v['name'][:34]:34} lvl {v['drop_level']:>2}  {v['sell']:>6}c  {owned.get(k, 'rules')[:28]:28} {v['source'][:110]}")
    changed = [k for k in old if k in result and result[k] != old[k]] + [k for k in old if k not in result]
    print(f'DRY RUN: {len(result)} priced ({sum(1 for k in result if k not in old)} new), earlier entries changed or dropped: {changed}')
    sys.exit(0)
json.dump({'rules': rules}, open(BATCH_GEN, 'w', encoding='utf-8'), indent=1)
for folder in (RUNTIME, HERE):
    p = os.path.join(folder, 'true165-rules.json')
    raw = open(p, 'rb').read().decode('utf-8'); d = json.loads(raw)
    d['updates'] = [u for u in d['updates'] if not str(u.get('reason', '')).startswith(TAG)] + rules
    nl = '\r\n' if '\r\n' in raw else '\n'
    open(p, 'w', encoding='utf-8', newline=nl).write(json.dumps(d, ensure_ascii=False, indent=1) + ('\n' if raw.endswith('\n') else ''))
json.dump(dict(sorted(result.items())), open(MAP_PATH, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
print(f"samples: {len(samples['trash'])} trash, {len(samples['gem'])} gems | priced {len(result)} "
      f"(period {sum(1 for v in result.values() if v['source'].startswith('Uthgard'))}, estimated "
      f"{sum(1 for v in result.values() if v['source'].startswith('estimated'))}) | rules rows {len(rules)}")
for k, v in sorted(result.items(), key=lambda kv: kv[1]['name'].lower()):
    print(f"  {v['name'][:34]:34} lvl {v['drop_level']:>2}  {v['sell']:>6}c  {v['source'][:95]}")
