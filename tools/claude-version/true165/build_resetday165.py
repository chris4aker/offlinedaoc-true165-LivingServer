"""True 1.65: world content fixes from the owner's reset-day list (2026-11-05), written into true165-rules.json.

S3  "Guard II" twice in the train preview: the skill table holds identical SpecXAbility rows (Shields Guard 2 at 10,
    ThaneCareer Shield 2 at 12). Duplicates are deleted, keeping the lowest id of each identical row.
S4  Cruella de Vil, the freeshard pet merchant in the three capitals (and region 73), is removed.
S5  The freeshard starter guilds (Clan Cotswold, Mularn Protectors, Tir na Nog Adventurers): new characters no longer
    join a guild (starting_guild off), the three guilds and their ranks are deleted and their members leave them.
S7  Adam Glaze's "Cloak" (quest Shades and Shadows) is a quest item, renamed Martley's cloak (period patch notes).
S10 Items that could not be dropped or sold get unlocked / a merchant value (see stuck_items).
S29 Instruments with no instrument type (Melodic Lute...) get theirs from their model or name.
S8  Quest journal counts: a step reads (0/2) before the first kill, not (1/2) (263 quest rows, 67 quests).
S12 Cronker (freeshard scripted monster, invisible outside hours 8-14) is removed; quest spawn doubling is fixed in
    the server (ClassicQuests.AlreadyThere).
S6  Camelot's second Lord Urqhart (the freeshard spot beside the account vault keeper) is removed; the regular one
    upstairs at the vault stays. The account vault keepers move: see build_accountvaults165.py.

python -I build_resetday165.py      (rerunnable: its own entries are replaced, never doubled)
Out (both copies): true165-rules.json.
"""
import json, os, re, sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
WORLD = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
TAG = 'True 1.65 reset day'
STARTER_GUILDS = ('62d4d73a-bb6b-4c5d-9d5e-2664c3fe0284',   # Clan Cotswold
                  '1631acab-4626-496f-a96e-48da87d75a89',   # Mularn Protectors
                  'c9b97783-7041-4fd5-9919-52f73189c0a8')   # Tir na Nog Adventurers
DUPLICATE_URQHART = '5a652f64-2f24-42db-897e-3f2e74ef32f2'


def updates():
    ids = ', '.join(f"'{g}'" for g in STARTER_GUILDS)
    return [
        {'sql': 'DELETE FROM SpecXAbility WHERE SpecXabilityID NOT IN (SELECT MIN(SpecXabilityID) FROM SpecXAbility '
                'GROUP BY Spec, SpecLevel, AbilityKey, AbilityLevel, ClassId)',
         'reason': f'{TAG} (S3): identical skill rows listed twice in the train preview ("Guard II" at Shields 10, '
                   f'Thane "Shield 2" at 12); the lowest id of each is kept'},
        {'sql': "UPDATE ItemTemplate SET Name = 'Martley''s cloak' WHERE Id_nb = 'cq_cloak'",
         'reason': f'{TAG} (S7): the "Cloak" Adam Glaze hands out in Shades and Shadows is a quest item, not a '
                   f'wearable cloak; period name from the patch notes ("Shades and Shadows - Martley\'s cloak has '
                   f'an icon", version 1.79, Warcry news 24309)'},
        {'sql': f"UPDATE DOLCharacters SET GuildID = '', GuildRank = 0 WHERE GuildID IN ({ids})",
         'reason': f'{TAG} (S5): members leave the freeshard starter guilds'},
        {'sql': f'DELETE FROM GuildRank WHERE GuildID IN ({ids})',
         'reason': f'{TAG} (S5): the freeshard starter guilds\' ranks'},
        {'sql': f'DELETE FROM Guild WHERE GuildID IN ({ids})',
         'reason': f'{TAG} (S5): the freeshard starter guilds (Clan Cotswold, Mularn Protectors, Tir na Nog '
                   f'Adventurers); players no longer start in a guild'},
    ]


COUNT = re.compile(r'\((\d+)/(\d+)\)(\s*)$')


def journal_counts():
    """S8: a "collect n" goal is split into n one-kill steps whose texts say (1/n) .. (n/n), so the journal shows 1/2
    before the first kill. Each step's count becomes what is done so far: (0/n) .. (n-1/n). Read from the world (read
    only); a world that already has the counts is recognised by its (0/n) and keeps them, so a rerun writes the same."""
    c = sqlite3.connect(f'file:{WORLD}?mode=ro', uri=True)
    out = []
    for qid, name, text in c.execute("SELECT ID, Name, StepText FROM DataQuest WHERE StepText LIKE '%(1/%' "
                                     "OR StepText LIKE '%(0/%' ORDER BY ID"):
        steps = text.split('|')
        if not any(COUNT.search(s) for s in steps):
            continue
        if not any((m := COUNT.search(s)) and m.group(1) == '0' for s in steps):
            steps = [COUNT.sub(lambda m: f'({int(m.group(1)) - 1}/{m.group(2)}){m.group(3)}', s)
                     if (m := COUNT.search(s)) and 1 <= int(m.group(1)) <= int(m.group(2)) else s for s in steps]
        new = '|'.join(steps).replace("'", "''")
        out.append({'sql': f"UPDATE DataQuest SET StepText = '{new}' WHERE ID = {qid}",
                    'reason': f'{TAG} (S8): journal counts show what is done so far ({name})'})
    c.close()
    return out


SNAPSHOT = os.path.join(HERE, 'resetday165_items.json')


def stuck_items():
    """S10 (owner 2026-11-05: "add vendor sell prices to keep bots and players from being stuck with items forever"):
    - loot that could not be dropped (and so not sold: merchants buy droppable items only) is unlocked - Melodic Lute,
      Lightbringer, Nightbled Cloak, the Mithra items...; never a quest's own step/collect item or a broken template;
    - the starter bows that could not be dropped are unlocked;
    - items with no merchant value (price 0: merchants "aren't interested") get one: starter and training weapons,
      quest rewards and gear drops. Each takes the median price of the world's priced gear of its level (within 2
      levels), so nothing is invented beyond the world's own prices. Items a merchant sells
      are left alone (a price there would change what they cost).
    The list is worked out once from the world and kept in resetday165_items.json, so a rerun after the refresh
    (when the prices are already set) writes the same rules."""
    if os.path.exists(SNAPSHOT):
        return json.load(open(SNAPSHOT, encoding='utf-8'))
    c = sqlite3.connect(f'file:{WORLD}?mode=ro', uri=True)
    low = lambda rows: {r[0].lower() for r in rows if r[0]}
    loot = low(c.execute('SELECT ItemTemplateID FROM LootTemplate'))
    merch = low(c.execute('SELECT ItemTemplateID FROM MerchantItem'))
    starter = low(c.execute('SELECT TemplateID FROM StarterEquipment'))
    quest_items, rewards = set(), set()
    for step, collect, final, optional in c.execute('SELECT StepItemTemplates, CollectItemTemplate, '
                                                    'FinalRewardItemTemplates, OptionalRewardItemTemplates FROM DataQuest'):
        quest_items |= {x.lower() for x in f'{step or ""}|{collect or ""}'.split('|') if x}
        rewards |= {x.split(',')[0].lower() for x in f'{final or ""}|{optional or ""}'.replace(';', '|').split('|') if x}
    items = {r[0].lower(): r for r in c.execute('SELECT Id_nb, Name, Item_Type, Object_Type, Level, Price, IsDropable '
                                                'FROM ItemTemplate')}
    c.close()
    real = lambda r: r[2] != 0 or r[3] != 0                     # not a broken / blank template
    unlock = sorted(r[0] for k, r in items.items() if r[6] == 0 and real(r) and k not in quest_items and
                    (k in loot or k in starter))
    wanted = {k for k, r in items.items() if r[5] == 0 and real(r) and k not in merch and k not in quest_items and
              (k in starter or k in rewards or 'training' in r[1].lower() or (k in loot and r[3] != 0))}
    # The world's own price for gear of a level: the median over every priced weapon, armour piece and jewel that drops
    # or is sold (hundreds per level), within 2 levels. A median of that many shrugs off the odd freeshard or artifact
    # price (some carry 2,000 gold), which a same-type match did not.
    priced = [r for k, r in items.items() if r[5] > 0 and r[3] != 0 and 10 <= r[2] <= 37 and k not in wanted and
              (k in loot or k in merch)]

    def median(values):
        values = sorted(values)
        return values[len(values) // 2] if values else None

    prices = {}
    for k in sorted(wanted):
        level = items[k][4]
        price = None
        for spread in (2, 3, 5, 10, 50):
            if price is None:
                price = median([r[5] for r in priced if abs(r[4] - level) <= spread])
        prices[items[k][0]] = int(price or 1)
    snap = {'unlock': unlock, 'prices': prices}
    with open(SNAPSHOT, 'w', encoding='utf-8') as f:
        json.dump(snap, f, ensure_ascii=False, indent=1)
        f.write('\n')
    return snap


def instrument_types(snap):
    """S29: 40 instruments (Melodic Lute among them) have no instrument type (DPS_AF 0); in True 1.65 Mode a song
    needs the right type, so nobody could play them. The type comes from the model (lute 227, drum 228, flute 325,
    harp 2116) or else the name. Kept in the snapshot like the rest (the world loses the zeros after the refresh)."""
    if 'instruments' in snap:
        return snap['instruments']
    models = {227: 2, 228: 1, 325: 3, 2116: 4}
    def by_name(text):
        text = text.lower()
        for word, kind in (('flute', 3), ('drum', 1), ('harp', 4), ('lute', 2)):
            if word in text:
                return kind
        return None
    c = sqlite3.connect(f'file:{WORLD}?mode=ro', uri=True)
    fixes = {}
    for idnb, name, model in c.execute('SELECT Id_nb, Name, Model FROM ItemTemplate WHERE Object_Type = 45 AND DPS_AF = 0'):
        kind = models.get(model) or by_name(name) or by_name(idnb)
        if kind:
            fixes[idnb] = kind
    c.close()
    snap['instruments'] = fixes
    with open(SNAPSHOT, 'w', encoding='utf-8') as f:
        json.dump(snap, f, ensure_ascii=False, indent=1)
        f.write(chr(10))
    return fixes


def stuck_item_updates():
    snap = stuck_items()
    out = []
    q = lambda s: "'" + s.replace("'", "''") + "'"
    if snap['unlock']:
        out.append({'sql': f"UPDATE ItemTemplate SET IsDropable = 1 WHERE Id_nb IN ({', '.join(map(q, snap['unlock']))})",
                    'reason': f"{TAG} (S10): {len(snap['unlock'])} loot items and starter bows that could not be "
                              f"dropped or sold (Melodic Lute, Lightbringer, Nightbled Cloak...)"})
    for idnb, kind in sorted(instrument_types(snap).items()):
        out.append({'sql': f"UPDATE ItemTemplate SET DPS_AF = {kind} WHERE Id_nb = {q(idnb)} AND DPS_AF = 0",
                    'reason': f'{TAG} (S29): an instrument with no instrument type could not play songs'})
    for idnb, price in sorted(snap['prices'].items()):
        out.append({'sql': f"UPDATE ItemTemplate SET Price = {price} WHERE Id_nb = {q(idnb)} AND Price = 0",
                    'reason': f'{TAG} (S10): a merchant value (median of priced items of its type and level)'})
    return out


def main():
    counts = journal_counts() + stuck_item_updates()
    assert len(counts) > 200, f'expected the ~263 quest rows with step counts, found {len(counts)}'
    for folder in (HERE, RUNTIME):
        p = os.path.join(folder, 'true165-rules.json')
        rules = json.load(open(p, encoding='utf-8'))
        # S5: no starter guild for new characters, and the world builder keeps none of them
        rules['settings']['starting_guild'] = 'False'
        rules['starter_guilds'] = []
        # S4: Cruella de Vil
        rules['remove']['mob_names']['Cruella de Vil'] = f'{TAG} (S4): freeshard pet merchant (launch reward items)'
        # S12: Cronker, a freeshard (Atlas) scripted monster by the Sephucoth river: invisible and untargetable outside
        # game hours 8-14 yet still fighting, and it saved itself to the database on every spawn
        rules['remove']['mob_classes']['DOL.GS.Cronker'] = f'{TAG} (S12): freeshard scripted monster (turns invisible but keeps fighting)'
        # S6: the duplicate Lord Urqhart
        where = [w for w in rules['remove']['where'] if not w['reason'].startswith(TAG)]
        where.append({'table': 'Mob', 'key': 'Mob_ID', 'where': f"Mob_ID = '{DUPLICATE_URQHART}'",
                      'reason': f'{TAG} (S6): the second Lord Urqhart at the freeshard spot in Camelot; the vault '
                                f'keeper upstairs at the vault stays'})
        rules['remove']['where'] = where
        rules['updates'] = [u for u in rules['updates'] if not u['reason'].startswith(TAG)] + updates() + counts
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(rules, f, ensure_ascii=False, indent=1)
            f.write('\n')
    print('rules written (both copies)')


if __name__ == '__main__':
    main()
