"""Writes 'ADDED MONSTERS AND ITEMS (Prima batch 2026-11-07).txt' from out/ (the builder's files). python -I write_added_list.py"""
import collections, json, os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
rep = json.load(open(os.path.join(OUT, 'report.json'), encoding='utf-8'))
items = json.load(open(os.path.join(OUT, 'add-primaitems165.json'), encoding='utf-8'))['rows']
spawns = json.load(open(os.path.join(OUT, 'add-primaspawns165.json'), encoding='utf-8'))['rows']
OT = {0: 'loot', 32: 'cloth armor', 33: 'leather armor', 34: 'studded armor', 35: 'chain armor', 36: 'plate armor',
      37: 'reinforced armor', 38: 'scale armor', 41: 'jewelry', 42: 'shield', 45: 'instrument'}
REALM = {0: 'any', 1: 'Albion', 2: 'Midgard', 3: 'Hibernia'}


def coin(c):
    c = int(c or 0); g, c = divmod(c, 10000); s, c = divmod(c, 100)
    return ' '.join(x for x in (f'{g}g' if g else '', f'{s}s' if s else '', f'{c}c' if c or not (g or s) else '') if x)


src = {}
for e in rep['items']:
    if e.get('Id_nb'):
        src.setdefault(e['Id_nb'], e)
L = []
w = L.append
w('TRUE 1.65 PRIMA BATCH (2026-11-07) - EVERYTHING ADDED')
w('=' * 60)
w('Monsters the Prima guides list that the world lacked, the Atlas drop items it lacked, their Atlas drop tables,')
w('and the wolf drop stack fix. Applied by the launcher at the next START SERVER (True 1.65 content files).')
w('')
w(f'Monsters added: {len({s["Name"] for s in spawns})} kinds, {len(spawns)} spawns '
  f'({sum(1 for s in spawns if s["PackageID"] == "true165-prima-new")} new at period positions, '
  f'{sum(1 for s in spawns if s["PackageID"] == "true165-restored")} restored archived rows)')
w(f'Items added: {len(items)} ({sum(1 for i in items if i["Object_Type"] == 0)} plain loot, '
  f'{sum(1 for i in items if i["Object_Type"] != 0)} equipment)')
w(f'Drop tables: {rep["counts"]["drop_rows_added"]} Atlas drop rows added on {rep["counts"]["monsters_with_new_drops"]} monsters')
w('Drops never come from a monster 15+ levels below the item (owner rule; exceptions: Darkness Falls seals, and the two')
w('very rare Atlas drops Spirit Crafted Shield 0.1% / Ether Ring 0.02% the owner kept).')
w('Wolf fix: wolf fang / torn wolf pelt / chunk of wolf meat drop one at a time; the "(20)/(100)" ones in bags become 1.')
w('')
w('MONSTERS ADDED (by zone)')
w('-' * 60)
byzone = collections.defaultdict(list)
for e in rep['spawns']:
    if e['action'] in ('RESTORE', 'PLACE') and e['rows']:
        byzone[e['zone']].append(e)
lv = collections.defaultdict(list)
for s in spawns:
    lv[s['Name'].lower()].append(s['Level'])
for z in sorted(byzone):
    w(z.split(': ', 1)[1] + ('  (Shrouded Isles)' if z.startswith('SI') else ''))
    for e in sorted(byzone[z], key=lambda x: (x['final_name'] or x['prima_name']).lower()):
        name = e['final_name'] or e['prima_name']
        ls = sorted(set(lv.get(name.lower(), [])))
        lvs = f'{ls[0]}-{ls[-1]}' if len(ls) > 1 else (str(ls[0]) if ls else '?')
        how = 'restored' if e['action'] == 'RESTORE' else 'new'
        ps = (e.get('position_source') or '').split(';')[0][:70]
        w(f'  {name:34} lvl {lvs:7} x{e["rows"]:<3} {how:8} {ps}')
    w('')
w('SCRIPTED PRIMA ENCOUNTERS (server scripts PrimaEncounters165/165b/165c.cs; details in ENCOUNTER SPECS.md)')
w('-' * 60)
for line in [
    'Cuinn (Connla): at 10% health calls the town guards and 6 garran treant seedlings (seeds drop 8% each)',
    'Talvor (Aegir\'s Landing): his spirit (25) rises where he dies and attacks his killers (staff 20%)',
    'Emmisary of Logi (Caldera): fireballs at range, calls the caldera lords, 3 obsidian doomwards at 75/50/25% (ring 20%)',
    'Forgemaster Brungar: one Spirit of the Forge (70)',
    'Weaponmaster Zargin: calls his students, up to 3 mastered weapons (48)',
    'panicked syhr\'phint: flees into Sys\'sro\'s lair and wakes him',
    'ixthiar creeper (Vale of Balor): wanders the vale seeding ixthiar decimaters (up to 24)',
    'Celain Eirich: back at 09:00 game time; a lieutenant and up to 5 raised undead',
    'forest grazer calf / lesser sylvanshade: lures; a greater anglator (33-34) jumps out',
    'Star Lake procession (Domnann): 3 priestesses plant treant sprouts at the lake; botonids sometimes attack',
    'Unseelie Envoy convoy (Lough Gur): the envoy, 2 stallions and 2 siabra escorts walk a route and back',
    'botonid seedlings x13 (Domnann): a scourgin (6-7) jumps out',
    'Arr (Aldland): flies high, swoops on a player and carries the fight to its nest (Griffon Talon Sword 15%)',
    'Thergus the Inspirer (Aldland): every few hours a speech, then 4 ogre squads march to the orc coast camp and fight;',
    '  a standby squad (squad leader + 9 threshers) stands on the coast (Squad-leader\'s Shield 20%)',
    'Gormror (Aegir\'s Landing): random raid on Dyrfjell with 4 lieutenants and 30 morvalt; disease clouds in town;',
    '  they leave when the leaders die or after 25 minutes (Kargil\'s / Rulod\'s Leggings 25%)',
    'Hamorik (Caldey broken tower): once a night a light show, then Hamorik (61) emerges; leaves at daybreak',
]:
    w('  ' + line)
w('Drop rates on these are owner-adjustable choices (no period figure).')
w('')
w('ITEMS ADDED')
w('-' * 60)
w(f'  {"item":40} {"kind":16} {"lvl":>3}  {"realm":8} {"value":>9}  price from')
for i in sorted(items, key=lambda r: (r['Object_Type'] != 0, r['Name'].lower())):
    e = src.get(i['Id_nb'], {})
    ps = (e.get('price_source') or ('period page / sibling' if i['Object_Type'] else '')).lower()
    ps = 'estimated' if ps.startswith('invented') else ('Uthgard' if ps.startswith('uthgard') else
                                                        ('derived (same type/slot/level)' if ps.startswith('derived') else ps[:22]))
    w(f'  {i["Name"][:40]:40} {OT.get(i["Object_Type"], "weapon"):16} {i["Level"]:>3}  {REALM.get(i["Realm"], "?"):8} '
      f'{coin(i["Price"]):>9}  {ps}')
w('')
w('Price note: "value" is the item price; merchants pay half. "estimated" = vendor trash with no period price (owner')
w('allowed); equipment stats are only from period pages (Allakhazam <= 2003-11 or Uthgard), never invented.')
w('')
w('NOT ADDED (for reference)')
w('-' * 60)
sk = [e for e in rep['spawns'] if e['action'].startswith('SKIP')]
c = collections.Counter(e['action'] for e in sk)
w('Monsters skipped: ' + ', '.join(f'{k.replace("SKIP_", "").lower().replace("_", " ")} {v}' for k, v in c.most_common()))
for k in ('SKIP_NO_POSITION', 'SKIP_NO_MODEL', 'SKIP_BOSS_OR_SCRIPTED', 'SKIP_OTHER'):
    names = sorted({(e['final_name'] or e['prima_name']) + ' (' + e['zone'].split(': ', 1)[1] + ')' for e in sk if e['action'] == k})
    if names:
        w(f'  {k.replace("SKIP_", "").lower().replace("_", " ")}: ' + '; '.join(names))
gear = sorted({e.get('item') or e['atlas_name'] for e in rep['items'] if e['verdict'] == 'SKIP_NO_STATS'})
w(f'Equipment with no period stat source (not built, no invented stats): {len(gear)}')
w('  ' + '; '.join(gear))
gap = sorted({(g['monster'], g['item']) for g in rep.get('level_gap_skipped', [])})
if gap:
    w('Drops left out by the 15-level rule (a low-level monster of the same name elsewhere): ' + '; '.join(f'{m} -> {i}' for m, i in gap))
open(os.path.join(HERE, 'ADDED MONSTERS AND ITEMS (Prima batch 2026-11-07).txt'), 'w', encoding='utf-8', newline='\r\n').write('\n'.join(L) + '\n')
print(len(L), 'lines')
