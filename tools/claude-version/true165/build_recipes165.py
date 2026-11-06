"""True 1.65 recipes = the real 1.64 client's recipe file (owner 2026-11-06: "fix these formulas with period data").

The new client builds its crafting window from the 1.64 client's tdl.irf and sends that file's recipe id in 0xED
(game/ui/craft_window.gd); the server looks the id up in CraftedItem (craft/Recipe.cs RecipeDB.FindBy) and takes the
ingredients from CraftedXItem by the product's Id_nb. Where the server's ids, products, levels or ingredients differ
from tdl.irf the craft fails silently or makes something else. This builder makes the server's recipe tables match
tdl.irf, id for id:

  CraftedItem  CraftedItemID = tdl id, Id_nb = the tdl product, CraftingLevel = tdl craft level,
               CraftingSkillType = tdl craft line
  CraftedXItem the tdl ingredients and counts for that product

Source: development-source/research/period-data/client-1.64/ifd/tdl.irf (Aug 2003; 8,574 recipes). Columns:
0 id, 1 craft line (eCraftingSkill), 2 category, 3 realm, 5 icon, 6 group, 7 base product, 8 material code,
9 item level, 10 craft level, then 5-field ingredients (name, material code, count, ingredient craft, level).
Material codes are turned into the item words (BRON = bronze ... ME10 = arcanium, ORE1 = copper ... OR10 = arcanite
for Hibernia, WOO1 = rowan ..., LEA1 = rawhide ..., CLO1 = woolen ..., NAT1 = leaf ..., GEM0 = raw ... GEM9 = perfect);
dye/enamel rows give only the colour (the section header says cloth dye / leather dye / enamel).
Names are matched to the server's ItemTemplates; where several templates share a name the one the server already uses
(same recipe, same realm) wins. Normal server rules only let a player craft a product of his own realm
(allow_craft_norealm_items is off), so a tdl product whose template has no realm / another realm gets a copy for the
recipe's realm (<id>_alb / _mid / _hib; a weapon copied across realms takes the item class of that realm's other tiers).

Period decisions (sources in the report):
  * AR10 / AR11 / AR12 broadhead arrows and bolts = keen / barbed / blunt footed flight broadheads ("Made AR10 arrows
    which turned out to be Keen Footed Flight Broadhead" / "the 3 types of footed flight arrows: Keen=Slash,
    Barbed=Thrust, Blunt=Crush", freddyshouse 2002-11-22). The 1.64 broadhead lists have no plain ARO9 "footed
    flight" entry: the three 1.52 variants replaced it. AR11 = barbed, AR12 = blunt follows the order of that answer.
  * Siegecraft: new ItemTemplates for the 1.64 engines (Object_Type 80 ballista / 81 catapult / 83 ram / 84
    trebuchet, models of the 1.64 client's objects.csv: 561 Ballista, 562 Ram, 563 Mangonel, 482 Trebutchet). Period
    siege engines were built on the spot ("build trebuchet and wait", freddyshouse 2002); SiegeCrafting.BuildCraftedItem
    already spawns the engine where the crafter stands, named and modelled from the template. Ammunition (Realm All,
    weight 0 on the period Allakhazam pages) and the parts ram beak / bucket (valued as their sibling part) are added.
  * Metalworking "tempered metal studs / wire / strapping / scales" (tdl 3491-3520, 10491-10510, 17491-17520) were
    live 1.65 recipes that turned the retired pre-1.65 components into sellable items (freddyshouse 'Hib
    Metalworking', Nov 2003); nothing makes those components in a 1.65 world, so they are hidden (owner 2026-11-06):
    no server rows, and pending-recipes165-updates.json "remove_where" archives any a world has.
  * Furniture ingredients are the house Store's furniture (list housing_indoor_shop, HouseTemplateMgr.IndoorShopItems:
    period /help "click on Store ... buy some furniture"), not the unsold raw templates.
  * The named market taxidermists (Elkyrd / Caerwent, Bruhdor / Erikstaad, Urlaun / Meath) cleanse a Darkness Falls
    Daemon Diamond Seal into a cleansed diamond seal (Allakhazam item 9417, Oct 2003); an AutoStart DataQuest with one
    CollectFinish step does the hand-in with the server's existing quest code.

Writes (rerunnable, deterministic):
  add-recipes165-crafteditem.json   CraftedItem rows for tdl ids the server lacks (key CraftedItemID)
  add-recipes165-craftedxitem.json  CraftedXItem rows for every product whose ingredients change (key CraftedXItem_ID,
                                    't165rcp-<product>-<ingredient>')
  pending-recipes165-updates.json   {"updates": [{sql, reason}]} for the lead to merge into true165-rules.json:
                                    CraftedItem rows whose product/level/line differ are set to the tdl values, and the
                                    old CraftedXItem rows of the changed products are removed (ours are kept), so an
                                    existing world and a fresh one end up the same. Idempotent.
  add-recipes165-items.json         ItemTemplate rows: siege engines, ammunition, ram beak, bucket, realm copies
                                    (key Id_nb)
  add-recipes165-taxidermists.json  Mob rows: the three market taxidermists (key Mob_ID)
  --report PATH (optional)          a JSON report (collisions, left-out recipes, items added, realm copies,
                                    ingredient availability).

    python -I build_recipes165.py [--db PATH] [--report PATH]

Reads the database read-only (default runtime/data/opendaoc-165.sqlite3.db).
"""
import argparse, collections, csv, json, os, re, sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
DB = os.path.join(CV, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
TDL = os.path.join(CV, 'development-source', 'research', 'period-data', 'client-1.64', 'ifd', 'tdl.irf')
STAMP = '2026-11-06 00:00:00'
XKEY = 't165rcp-'
SKILLS = {1: 'Weaponcraft', 2: 'Armorcraft', 3: 'Siegecraft', 4: 'Alchemy', 6: 'Metalworking', 7: 'Leathercrafting',
          8: 'Clothworking', 9: 'Gemcutting', 10: 'Herbcraft', 11: 'Tailoring', 12: 'Fletching', 13: 'Spellcrafting',
          14: 'Woodworking', 15: 'Basic Crafting'}
REALMS = {1: 'Albion', 2: 'Midgard', 3: 'Hibernia'}

WORDS = {}
for _codes, _names in [
        ('BRON IRON STEE ALLO FALL MITH ADAM ASTE MET9 ME10',
         'bronze|iron|steel|alloy|fine alloy|mithril|adamantium|asterite|netherium|arcanium'),
        ('ORE1 ORE2 ORE3 ORE4 ORE5 ORE6 ORE7 ORE8 ORE9 OR10',
         'copper|ferrite|quartz|dolomite|cobalt|carbide|sapphire|diamond|netherite|arcanite'),
        ('WOO1 WOO2 WOO3 WOO4 WOO5 WOO6 WOO7 WOO8 WOO9 WO10',
         'rowan|elm|oaken|ironwood|heartwood|runewood|stonewood|ebonwood|dyrwood|duskwood'),
        ('LEA1 LEA2 LEA3 LEA4 LEA5 LEA6 LEA7 LEA8 LEA9 LE10',
         'rawhide|tanned|cured|hard|rigid|embossed|imbued|runed|eldritch|tempered'),
        ('CLO1 CLO2 CLO3 CLO4 CLO5 CLO6 CLO7 CLO8 CLO9 CL10',
         'woolen|linen|brocade|silk|gossamer|sylvan|seamist|nightshade|wyvernskin|silksteel'),
        ('NAT1 NAT2 NAT3 NAT4 NAT5 NAT6 NAT7 NAT8 NAT9 NA10',
         'leaf|bone|vine|shell|fossil|amber|coral|chitin|petrified|crystalized'),
        ('GEM0 GEM1 GEM2 GEM3 GEM4 GEM5 GEM6 GEM7 GEM8 GEM9',
         'raw|uncut|rough|flawed|imperfect|polished|faceted|precious|flawless|perfect'),
        ('ARO1 ARO2 ARO3 ARO4 ARO5 ARO6 ARO7 ARO8 ARO9 AR10',
         'rough clout|rough|clout|rough flight|standard|footed clout|flight|footed|footed flight|footed flight')]:
    for _c, _n in zip(_codes.split(), _names.split('|')):
        WORDS[_c] = [_n]
WORDS['CLO1'] = ['woolen', 'woolen hooded']
# the three 1.52 footed flight broadheads (period: freddyshouse 2002-11-22, posts 1484919 / 1484930)
ARROW_VARIANT = {'AR10': 'keen footed flight', 'AR11': 'barbed footed flight', 'AR12': 'blunt footed flight'}
DYE = {'cloth dyes': 'cloth dye', 'leather dyes': 'leather dye', 'enamels': 'enamel'}
# tdl spellings / names -> the server's names (same item)
SPELLING = [('erroding', 'eroding'), ('eixir', 'elixir'), ('stilletto', 'stiletto'), ('woad', 'wode'),
            ('mirrior', 'mirror'), ('faeries boots', 'faerie boots'), ('lamellar gloves', 'lamellar gauntlets'),
            ('osnadurtha helm', 'osnadurtha coif'), ('superior pointed steeple hat', 'superior quilted steeple hat'),
            ('superior steeple hat', 'superior padded steeple hat'), ('superior steeple hat', 'superior woven steeple hat')]
ING_TAIL = {'metal bars': 'metal bars', 'wood boards': 'wooden boards', 'wooden boards': 'wooden boards',
            'wood': 'wooden boards', 'leather': 'leather square', 'thread': 'heavy thread', 'cloth': 'cloth square'}
ING_ALIAS = {'cladding': 'ram cladding', 'trigger': 'ballista trigger', 'cushion': 'arm cushion'}
REALM_COPY = {1: '_alb', 2: '_mid', 3: '_hib'}      # realm copies of realm-less / other-realm products
NEW_SUFFIX = {1: '', 2: '2', 3: '3'}                 # the server's own per-realm Id_nb convention
PKG = 'True165_recipes'
# 1.64 siege engines: built where the crafter stands (SiegeCrafting.BuildCraftedItem spawns the engine for
# Object_Type 80-84 and takes name + model from the template). Models are the 1.64 client's objects.csv
# siege objects (561 Ballista, 562 Ram, 563 Mangonel, 482 Trebutchet); tier = light / medium / heavy (elm / oak / ironwood)
SIEGE = {'small ram': (83, 562, 1), 'ram': (83, 562, 2), 'siege ram': (83, 562, 3),
         'scorpion': (80, 561, 1), 'ballista': (80, 561, 2), 'palintone': (80, 561, 3),
         'onager': (81, 563, 1), 'catapult': (81, 563, 2), 'trebuchet': (84, 482, 3)}
# ammunition (period: Realm All, weight 0, Siegecraft; objects.csv 514 Stone / 513 Bolts)
AMMO = {'catapult stone': 514, 'ballista bolt': 513, 'bullet': 514, 'grapeshot': 514, 'ball of ice': 514}
# siege parts the server lacks, priced as their same-craft sibling part
NEW_PARTS = {'ram beak': ('winding crank', 'ram cladding'), 'bucket': ('counterweight', 'counterweight')}
# Hidden (owner 2026-11-06: "if the recipes cant be made just hide them"): the 1.65 metalworking tempering of the
# retired pre-1.65 components (studs / wire / strapping / scales). Real 1.65 recipes ("tempering them brings them back
# to the same price as hinges", freddyshouse 'Hib Metalworking', Nov 2003), but nothing makes those components in a
# 1.65 world. The client hides these ids; the server has no rows for them. (The tempered saddlebags stay.)
def hidden(r):
    return r['skill'] == 6 and r['base'].startswith('tempered ')


HIDDEN_WHY = 'hidden: 1.65 tempering of retired pre-1.65 components that cannot be made (owner 2026-11-06)'
TAXIDERMISTS = {   # period: Allakhazam grab bag (Elkyrd / Bruhdor / Urlaun) + item 9417 page (Meath Market, Oct 2003)
    1: ('Elkyrd', 'Caerwent', 2), 2: ('Bruhdor', 'Erikstaad', 102), 3: ('Urlaun', 'Meath', 202)}


def key(s):
    return re.sub('[^a-z0-9]', '', (s or '').lower())


def spellings(name):
    out = [name]
    low = name.lower()
    for a, b in SPELLING:
        if a in low:
            out.append(low.replace(a, b))
    return out


def tdl_rows():
    rows, labels = [], {}
    with open(TDL, encoding='latin1', newline='') as f:
        for r in csv.reader(f):
            if not r or r[0].startswith(';') or not r[0].strip().lstrip('-').isdigit() or int(r[0]) == 0:
                continue
            if r[6].strip() == '99':
                labels[(int(r[3]), int(r[1]), int(r[2]))] = r[7].strip()
                continue
            ings = []
            for k in range(11, min(len(r), 61), 5):
                n = r[k].strip()
                if n not in ('', '0'):
                    ings.append((re.sub(r'\s+', ' ', n), r[k + 1].strip(), int(r[k + 2] or 0)))
            rows.append(dict(id=int(r[0]), skill=int(r[1]), cat=int(r[2]), realm=int(r[3]),
                             base=re.sub(r'\s+', ' ', r[7]).strip(), code=r[8].strip(), ilevel=int(r[9] or 0),
                             level=int(r[10] or 0), ings=ings))
    for r in rows:
        r['section'] = labels.get((r['realm'], r['skill'], r['cat']), '')
    return rows


class World:
    def __init__(self, path):
        self.db = sqlite3.connect(f'file:{path}?mode=ro', uri=True)
        self.items, self.bykey = {}, collections.defaultdict(list)
        for idnb, name, realm in self.db.execute('SELECT Id_nb, Name, Realm FROM ItemTemplate'):
            self.items[idnb.lower()] = (idnb, name, realm or 0)
            self.bykey[key(name)].append(idnb.lower())
        self.crafted, self.prod_rows = {}, collections.defaultdict(list)
        for cid, p, lvl, sk, mt in self.db.execute('SELECT CraftedItemID, Id_nb, CraftingLevel, CraftingSkillType, '
                                                    'MakeTemplated FROM CraftedItem'):
            self.crafted[str(cid)] = (p.lower(), int(lvl or 0), int(sk or 0), int(mt or 0))
            self.prod_rows[p.lower()].append((str(cid), int(sk or 0), int(mt or 0)))
        self.xi, self.ing_use = collections.defaultdict(list), collections.Counter()
        for p, i, c in self.db.execute('SELECT CraftedItemId_nb, IngredientId_nb, Count FROM CraftedXItem'):
            self.xi[p.lower()].append((i.lower(), int(c or 0)))
            self.ing_use[i.lower()] += 1
        # where an item can be had: a merchant list an NPC uses, any merchant list, a loot table
        self.listed = {r[0].lower() for r in self.db.execute('SELECT DISTINCT ItemTemplateID FROM MerchantItem')}
        self.sold = {r[0].lower() for r in self.db.execute(
            "SELECT DISTINCT ItemTemplateID FROM MerchantItem WHERE ItemListID IN "
            "(SELECT ItemsListTemplateID FROM Mob WHERE COALESCE(ItemsListTemplateID, '') <> '')")}
        # the housing lists are the house interface's own stores and house merchants (HouseTemplateMgr): sold too
        self.sold |= {r[0].lower() for r in self.db.execute(
            "SELECT DISTINCT ItemTemplateID FROM MerchantItem WHERE ItemListID LIKE 'housing\\_%' ESCAPE '\\'")}
        self.loot = {r[0].lower() for r in self.db.execute('SELECT DISTINCT ItemTemplateID FROM LootTemplate')}
        self.new_items = {}                           # Id_nb lower -> full ItemTemplate row this builder adds
        mt = collections.defaultdict(collections.Counter)
        for p, _, sk, m in self.crafted.values():
            mt[sk][m] += 1
        self.mt_default = {sk: c.most_common(1)[0][0] for sk, c in mt.items()}

    def name(self, i):
        return self.items[i][1]

    def realm_ok(self, i, realm):
        return self.items[i][2] in (0, realm)

    def row(self, i):
        if i in self.new_items:
            return dict(self.new_items[i])
        cur = self.db.execute('SELECT * FROM ItemTemplate WHERE Id_nb = ?', (self.items[i][0],))
        return dict(zip([d[0] for d in cur.description], cur.fetchone()))

    def free_id(self, idnb):
        out, n = idnb, 1
        while out.lower() in self.items:
            n += 1
            out = f'{idnb}_t165_{n}'
        return out

    def add_item(self, row):
        row.update(ItemTemplate_ID=row['Id_nb'], PackageID=PKG, LastTimeRowUpdated=STAMP)
        i = row['Id_nb'].lower()
        assert i not in self.items, i
        self.new_items[i] = row
        self.items[i] = (row['Id_nb'], row['Name'], row['Realm'])
        self.bykey[key(row['Name'])].append(i)
        return i

    def find(self, name, realm=None):
        c = [i for i in self.bykey.get(key(name), []) if realm is None or self.realm_ok(i, realm)]
        return sorted(c, key=lambda i: (self.items[i][2] != realm, len(i)))[0] if c else None


def product_names(r):
    base = r['base']
    if r['section'].lower() in DYE:
        return [base + ' ' + DYE[r['section'].lower()]]
    if r['skill'] == 12 and base in ('broadhead arrows', 'broadhead bolts') and r['code'] in ARROW_VARIANT:
        return [ARROW_VARIANT[r['code']] + ' ' + base]
    words = WORDS.get(r['code'], [])
    names = [w + ' ' + base for w in words] + ([base] if not words else [])
    return [s for n in names for s in spellings(n)]


def resolve_product(w, r):
    keys = {key(n) for n in product_names(r)}
    cur = w.crafted.get(str(r['id']))
    cands = list(dict.fromkeys(i for k in keys for i in w.bykey.get(k, [])))
    if cur and cur[0] in w.items and key(w.name(cur[0])) in keys:
        if w.items[cur[0]][2] == r['realm']:
            return cur[0]
        # the server's product has no realm / another realm: a same-name template of the recipe's realm wins
        mine = [i for i in cands if w.items[i][2] == r['realm']]
        if not mine:
            return cur[0]
        cands = mine
    if not cands:
        return None

    def score(i):
        skills = {sk for _, sk, _ in w.prod_rows.get(i, ())}
        return (w.realm_ok(i, r['realm']), r['skill'] in skills, bool(skills), w.items[i][2] == r['realm'], -len(i))
    return max(cands, key=score)


def ingredient_names(n, code):
    low = n.lower()
    if code in ('GENE', '0', ''):
        names = [n]
    else:
        ws = WORDS.get(code, [])
        tail = ING_TAIL.get(low, low)
        names = [w_ + ' ' + tail for w_ in ws]
        if low in ING_ALIAS:
            names += [w_ + ' ' + ING_ALIAS[low] for w_ in ws] + [ING_ALIAS[low]]
    return [s for x in names for s in spellings(x)]


def make_period_items(w, rows, report):
    """ItemTemplates the 1.64 recipes need that the server never had: the siege engines and ammunition, the siege
    parts ram beak / bucket."""
    generic = w.row(w.find('spring arms'))

    def exists(name, realm):
        return any(w.items[i][2] == realm for i in w.bykey.get(key(name), []))

    def new(base_row, name, realm, why, **kw):
        row = dict(base_row)
        idnb = name.replace(' ', '_') + NEW_SUFFIX.get(realm, '')
        if idnb.lower() in w.items:                      # the plain id is another (later) item: use the realm word
            idnb = name.replace(' ', '_') + REALM_COPY.get(realm, '_t165')
        row.update(Id_nb=w.free_id(idnb), Name=name, Realm=realm,
                   CanDropAsLoot=0, Description=why, **kw)
        i = w.add_item(row)
        report['items_added'].append(dict(id=row['Id_nb'], name=name, realm=realm, why=why))
        return i

    def ing_cost(r):
        total = 0
        for n, code, cnt in r['ings']:
            for cand in ingredient_names(n, code):
                prices = [w.row(i)['Price'] or 0 for i in w.bykey.get(key(cand), [])]
                if prices:
                    total += max(prices) * cnt
                    break
        return total

    for r in rows:
        base, realm, name = r['base'], r['realm'], product_names(r)[0]
        if r['skill'] == 3 and base in SIEGE and not exists(name, realm):
            ot, model, tier = SIEGE[base]
            new(generic, name, realm, f'1.64 siege engine (tdl.irf {r["id"]}): built where the siegecrafter stands',
                Object_Type=ot, Model=model, Level=tier, Price=0, Weight=0, MaxCount=1, PackSize=1, Quality=100)
        elif r['skill'] == 3 and base in AMMO and not exists(name, realm):
            new(generic, name, realm, f'1.64 siege ammunition (tdl.irf {r["id"]})', Model=AMMO[base], Weight=0,
                Price=ing_cost(r), MaxCount=20, PackSize=1, Quality=100)   # stacks of 20 from 1.60b (Freddyshouse 113142)
        elif base in NEW_PARTS and not exists(name, realm):
            word = WORDS[r['code']][0]
            sib = w.find(f'{word} {NEW_PARTS[base][0]}') or w.find(NEW_PARTS[base][0])
            look = w.find(f'{word} {NEW_PARTS[base][1]}') or w.find(NEW_PARTS[base][1])
            new(w.row(look), name, realm, f'1.64 siege part (tdl.irf {r["id"]}); value as the {w.name(sib)}',
                Price=w.row(sib)['Price'])


def realm_copies(w, rows, target, report):
    """Normal server rules craft only products of the crafter's realm (allow_craft_norealm_items is off): a tdl product
    whose template has no realm or another realm gets a copy for the recipe's realm. A weapon copied across realms
    takes the item class of that realm's other tiers of the same recipe (an Albion great axe is a two-hander)."""
    shapes = collections.defaultdict(collections.Counter)
    cols = ('Object_Type', 'Hand', 'Item_Type', 'Type_Damage', 'SPD_ABS')
    for r in rows:
        p = target.get(r['id'])
        if p and w.items[p][2] == r['realm'] and r['skill'] in (1, 12):
            row = w.row(p)
            shapes[(r['realm'], r['skill'], r['base'])][tuple(row[c] for c in cols)] += 1
    copies = {}
    for r in rows:
        p = target.get(r['id'])
        if p is None or w.items[p][2] == r['realm']:
            continue
        k = (p, r['realm'])
        if k not in copies:
            row = w.row(p)
            orig = row['Id_nb']
            row['Id_nb'] = w.free_id(orig + REALM_COPY[r['realm']])
            row['Realm'] = r['realm']
            shape = shapes.get((r['realm'], r['skill'], r['base']))
            if shape:
                want = dict(zip(cols, shape.most_common(1)[0][0]))
                if want['Object_Type'] != row['Object_Type']:
                    row.update(want)
            copies[k] = w.add_item(row)
            report['realm_copies'].append(dict(copy=row['Id_nb'], of=orig, of_realm=w.items[p][2], realm=r['realm'],
                                               object_type=row['Object_Type'], used_as_ingredient=w.ing_use[p]))
        target[r['id']] = copies[k]
    return copies


def build(w, rows):
    report = {'left_out': [], 'collisions': [], 'ingredient_conflicts': [], 'level_or_line_fixed': 0,
              'items_added': [], 'realm_copies': []}
    make_period_items(w, rows, report)
    # pass 1: products
    target = {}
    for r in rows:
        p = resolve_product(w, r)
        if p is None:
            report['left_out'].append(dict(id=r['id'], realm=r['realm'], line=r['skill'],
                                           product=' / '.join(product_names(r)), why='no ItemTemplate for the product'))
            continue
        target[r['id']] = p
    copies = realm_copies(w, rows, target, report)
    made = collections.defaultdict(list)          # (realm, name key) -> products made in that realm
    for r in rows:
        if r['id'] in target:
            made[(r['realm'], key(w.name(target[r['id']])))].append(target[r['id']])
    # how matched server recipes of each realm name their ingredients (votes)
    votes = collections.defaultdict(collections.Counter)
    for r in rows:
        p = target.get(r['id'])
        if p:
            for i, _ in w.xi.get(p, ()):
                votes[(r['realm'], key(w.name(i)))][i] += 1

    craftable = set(target.values()) | {q for q, *_ in w.crafted.values()} | set(w.new_items)

    def avail(i):
        return 3 if (i in w.sold or i in craftable) else 2 if i in w.listed else 1 if i in w.loot else 0

    def resolve_ing(r, p, n, code):
        i = first_choice(r, p, n, code)
        if i is not None and (i, r['realm']) in copies:
            return copies[(i, r['realm'])]
        if i is None or avail(i) > 0:
            return i
        # nobody sells, makes or drops that template: a same-name template on a merchant list is the period item
        # (the woodworking furniture takes the furniture the housing merchants sell, e.g. 'marble chair')
        alts = [j for j in w.bykey.get(key(w.name(i)), []) if avail(j) > 0]
        return max(alts, key=lambda j: (avail(j), w.realm_ok(j, r['realm']), w.ing_use[j])) if alts else i

    def first_choice(r, p, n, code):
        keys = [key(x) for x in ingredient_names(n, code)]
        mine = {key(w.name(i)): i for i, _ in w.xi.get(p, ()) if i in w.items}
        for k in keys:
            if k in mine:
                return mine[k]
        for k in keys:
            if made.get((r['realm'], k)):
                return made[(r['realm'], k)][0]
        for k in keys:
            if votes.get((r['realm'], k)):
                return votes[(r['realm'], k)].most_common(1)[0][0]
        cands = list(dict.fromkeys(i for k in keys for i in w.bykey.get(k, [])))
        if not cands:
            return None
        return max(cands, key=lambda i: (w.realm_ok(i, r['realm']), w.ing_use[i] > 0, w.items[i][2] == r['realm'],
                                         w.ing_use[i], -len(i)))

    # pass 2: ingredients
    recipes = {}
    for r in rows:
        p = target.get(r['id'])
        if not p:
            continue
        ings, missing = collections.OrderedDict(), []
        for n, code, cnt in r['ings']:
            i = resolve_ing(r, p, n, code)
            if i is None:
                missing.append(f'{n} ({code})')
            else:
                ings[i] = ings.get(i, 0) + cnt
        if missing:
            report['left_out'].append(dict(id=r['id'], realm=r['realm'], line=r['skill'], product=w.name(p),
                                           why='no ItemTemplate for ingredient(s): ' + ', '.join(missing)))
            continue
        recipes[r['id']] = dict(r=r, product=p, ings=list(ings.items()))

    # one ingredient list per product (CraftedXItem is keyed by the product)
    per_product = {}
    for rid in sorted(recipes):
        rec = recipes[rid]
        have = per_product.get(rec['product'])
        if have is None:
            per_product[rec['product']] = (rid, rec['ings'])
        elif sorted(have[1]) != sorted(rec['ings']):
            report['ingredient_conflicts'].append(dict(product=w.items[rec['product']][0], kept_id=have[0],
                                                       kept=have[1], other_id=rid, other=rec['ings']))
    return target, recipes, per_product, report


MARKETS = {2: (556400, 559000), 102: (553200, 566000), 202: (563500, 560000)}   # the 1.64 housing markets


def taxidermists(w):
    """The named taxidermist of each 1.64 housing market and his seal cleansing (server class Taxidermist165: hand him
    Daemon Diamond Seals, receive cleansed diamond seals; repeatable)."""
    cur = w.db.execute('SELECT * FROM Mob WHERE Name = ? AND Region IN (2, 102, 202)', ('Alchemist apprentice',))
    cols = [d[0] for d in cur.description]
    apprentices = [dict(zip(cols, r)) for r in cur.fetchall()]
    npcs, quests = [], []
    for realm, (name, market, region) in sorted(TAXIDERMISTS.items()):
        cx, cy = MARKETS[region]
        near = [m for m in apprentices if m['Region'] == region and abs(m['X'] - cx) < 4000 and abs(m['Y'] - cy) < 4000]
        assert near, (region, 'no market apprentice to stand beside')
        row = dict(near[0])
        row.update(Mob_ID=f'{XKEY}taxidermist-{market.lower()}', Name=name, Guild='Taxidermist',
                   ClassType='DOL.GS.Taxidermist165', ItemsListTemplateID=None, NPCTemplateID=-1, X=row['X'] + 160,
                   PackageID='true165_taxidermist', LastTimeRowUpdated=STAMP, Realm=realm)
        npcs.append(row)
        quests.append({
            'ID': 165100 + realm, 'Name': 'Cleansing a Diamond Seal', 'StartType': 2, 'StartName': name,
            'StartRegionID': region, 'AcceptText': '',
            'Description': f'I am the taxidermist of the {market} Market. I can cleanse a Daemon Diamond Seal from '
                           'Darkness Falls for you.',
            'SourceName': '', 'SourceText': 'Hand me a Daemon Diamond Seal from Darkness Falls, one seal at a time, '
                                            'and I will cleanse it for you.',
            'StepType': '11', 'StepText': f'Give {name} one Daemon Diamond Seal from Darkness Falls.',
            'StepItemTemplates': 'DiamondSeal', 'CollectItemTemplate': 'DiamondSeal', 'AdvanceText': '',
            'TargetName': f'{name};{region}', 'TargetText': '', 'MaxCount': 0, 'MinLevel': 1, 'MaxLevel': 50,
            'RewardMoney': '0', 'RewardXP': '0', 'RewardCLXP': '', 'RewardRP': '', 'RewardBP': '',
            'OptionalRewardItemTemplates': '', 'FinalRewardItemTemplates': 'cleansed_diamond_seal',
            'FinishText': 'There you are: a cleansed diamond seal.', 'QuestDependency': '', 'AllowedClasses': '',
            'ClassType': '', 'LastTimeRowUpdated': STAMP})
    return npcs, quests


def hide_rules(w, hidden_rows):
    """true165-rules.json "remove.where" entries ({table, key, where, reason}: archived, not just deleted) for the hidden
    recipe ids, should a world have them: the CraftedItem rows under those ids, and the ingredient rows / templates of
    their products when no other recipe makes or uses those products."""
    ids = sorted(r['id'] for r in hidden_rows)
    why = 'True 1.65 recipes: ' + HIDDEN_WHY + ' (build_recipes165.py)'
    out = [{'table': 'CraftedItem', 'key': 'CraftedItemID', 'reason': why,
            'where': f"CraftedItemID IN ({','.join(sql_str(str(i)) for i in ids)})"}]
    names = sorted({key(n) for r in hidden_rows for n in product_names(r)})
    prods = sorted({w.items[i][0] for k in names for i in w.bykey.get(k, [])
                    if not any(cid not in {str(x) for x in ids} for cid, _, _ in w.prod_rows.get(i, ()))
                    and not w.ing_use[i] and i not in w.new_items})
    if prods:
        out.append({'table': 'CraftedXItem', 'key': 'CraftedXItem_ID', 'reason': why,
                    'where': f"CraftedItemId_nb IN ({','.join(sql_str(p) for p in prods)})"})
    return out


def sql_str(s):
    return "'" + s.replace("'", "''") + "'"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--db', default=DB)
    ap.add_argument('--report')
    ap.add_argument('--out', default=HERE)
    a = ap.parse_args()
    w = World(a.db)
    rows = tdl_rows()
    hidden_rows = [r for r in rows if hidden(r)]
    rows = [r for r in rows if not hidden(r)]
    removed = set()
    rules_path = os.path.join(HERE, 'true165-rules.json')
    if os.path.exists(rules_path):
        for rule in json.load(open(rules_path, encoding='utf-8'))['remove'].get('where', []):
            if rule['table'] == 'CraftedItem':
                removed |= {m.lower() for m in re.findall(r"'([^']*)'", rule['where'])}
    target, recipes, per_product, report = build(w, rows)

    add_ci, upd = [], collections.defaultdict(list)          # upd[(line, realm)] -> (id, product, level, line, mt)
    for rid in sorted(recipes):
        rec = recipes[rid]
        r, p = rec['r'], rec['product']
        idnb = w.items[p][0]
        if p in removed:
            report['left_out'].append(dict(id=rid, realm=r['realm'], line=r['skill'], product=idnb,
                                           why='product is in the true165-rules.json CraftedItem removal list'))
            continue
        others = [m for cid, _, m in w.prod_rows.get(p, ()) if cid != str(rid)]
        mt = others[0] if others else w.mt_default.get(r['skill'], 0)
        cur = w.crafted.get(str(rid))
        if cur is None:
            add_ci.append({'CraftedItemID': str(rid), 'Id_nb': idnb, 'CraftingLevel': r['level'],
                           'CraftingSkillType': r['skill'], 'MakeTemplated': mt, 'LastTimeRowUpdated': STAMP,
                           'CraftedItem_ID': str(rid)})
        elif (cur[0], cur[1], cur[2]) != (p, r['level'], r['skill']):
            if cur[0] != p:
                old = cur[0]
                still = sorted(int(x) for x, q in target.items() if q == old and x in recipes)
                report['collisions'].append(dict(
                    id=rid, realm=r['realm'], line=r['skill'],
                    server_product=w.items[old][0] if old in w.items else old,
                    tdl_product=idnb,
                    server_product_still_made_by_tdl_id=still or None))
            else:
                report['level_or_line_fixed'] += 1
            upd[(r['skill'], r['realm'])].append((rid, idnb, r['level'], r['skill'], mt if cur[0] != p else cur[3]))

    # ingredient rows
    add_x, replace = [], collections.defaultdict(list)
    for p, (rid, ings) in sorted(per_product.items()):
        if p in removed or rid not in recipes:
            continue
        have = sorted((i, c) for i, c in w.xi.get(p, ()))
        if have == sorted(ings):
            continue
        r = recipes[rid]['r']
        if have:
            replace[(r['skill'], r['realm'])].append(w.items[p][0])
        for i, c in ings:
            add_x.append({'CraftedXItem_ID': f'{XKEY}{p}-{i}', 'CraftedItemId_nb': w.items[p][0],
                          'IngredientId_nb': w.items[i][0], 'Count': c, 'LastTimeRowUpdated': STAMP})

    updates = []
    for (line, realm), lst in sorted(upd.items()):
        ids = ','.join(sql_str(str(x[0])) for x in lst)

        def case(col, idx):
            return f'{col} = CASE CraftedItemID ' + ' '.join(
                f'WHEN {sql_str(str(x[0]))} THEN {sql_str(x[idx]) if isinstance(x[idx], str) else x[idx]}'
                for x in lst) + ' END'
        sql = (f'UPDATE CraftedItem SET {case("Id_nb", 1)}, {case("CraftingLevel", 2)}, '
               f'{case("CraftingSkillType", 3)}, {case("MakeTemplated", 4)} WHERE CraftedItemID IN ({ids})')
        updates.append({'sql': sql, 'reason': f'True 1.65 recipes: {REALMS[realm]} {SKILLS[line]} recipe ids set to the '
                                              f'1.64 client tdl.irf product / craft level ({len(lst)} recipes)'})
    for (line, realm), lst in sorted(replace.items()):
        names = ','.join(sql_str(x) for x in lst)
        updates.append({'sql': f"DELETE FROM CraftedXItem WHERE CraftedItemId_nb IN ({names}) "
                               f"AND CraftedXItem_ID NOT LIKE '{XKEY}%'",
                        'reason': f'True 1.65 recipes: {REALMS[realm]} {SKILLS[line]} ingredients replaced by the 1.64 '
                                  f'client tdl.irf ones ({len(lst)} products; the new rows are in '
                                  f'add-recipes165-craftedxitem.json)'})

    def dump(name, table, k, reason, rws):
        with open(os.path.join(a.out, name), 'w', encoding='utf-8') as f:
            json.dump({'table': table, 'key': k, 'reason': reason, 'rows': rws}, f, indent=1)
    dump('add-recipes165-crafteditem.json', 'CraftedItem', 'CraftedItemID',
         'True 1.65 recipes: 1.64 client tdl.irf recipe ids the server lacked (build_recipes165.py)', add_ci)
    dump('add-recipes165-craftedxitem.json', 'CraftedXItem', 'CraftedXItem_ID',
         'True 1.65 recipes: 1.64 client tdl.irf ingredients (build_recipes165.py)', add_x)
    with open(os.path.join(a.out, 'pending-recipes165-updates.json'), 'w', encoding='utf-8') as f:
        json.dump({'updates': updates, 'remove_where': hide_rules(w, hidden_rows)}, f, indent=1)
    dump('add-recipes165-items.json', 'ItemTemplate', 'Id_nb',
         'True 1.65 recipes: items the 1.64 recipes need (siege engines, ammunition, ram beak, bucket, realm '
         'copies) (build_recipes165.py)', sorted(w.new_items.values(), key=lambda x: x['Id_nb']))
    npcs, quests = taxidermists(w)
    dump('add-recipes165-taxidermists.json', 'Mob', 'Mob_ID',
         'True 1.65: the housing-market taxidermists who cleanse Darkness Falls diamond seals (build_recipes165.py)', npcs)
    # the hand-in is the Taxidermist165 NPC class (server), not a DataQuest: a quest hand-in took a whole stack for one
    # seal (2026-11-06); quests stay built only for the ingredient check below
    report['hidden'] = [dict(id=r['id'], realm=r['realm'], product=product_names(r)[0], why=HIDDEN_WHY)
                        for r in hidden_rows]
    report['taxidermists'] = [(n['Name'], n['Region'], n['X'], n['Y']) for n in npcs]

    # ingredient availability (merchant / crafted / loot)
    sold, loot = w.sold, w.loot
    craftable = {rec['product'] for rec in recipes.values()} | {p for p, *_ in w.crafted.values()}
    rewards = {x.strip().lower() for q in quests for x in q['FinalRewardItemTemplates'].split('|') if x.strip()}
    for (txt,) in w.db.execute("SELECT FinalRewardItemTemplates FROM DataQuest WHERE COALESCE(FinalRewardItemTemplates,'')<>''"):
        rewards |= {x.strip().lower() for x in re.split(r'[|;]', txt) if x.strip()}
    unavailable = collections.defaultdict(set)
    for rec in recipes.values():
        for i, _ in rec['ings']:
            if i not in sold and i not in craftable and i not in rewards:
                unavailable[w.items[i][0]].add((rec['r']['realm'], 'loot' if i in loot else
                                                'merchant list no NPC uses' if i in w.listed else 'none'))
    report['ingredients_not_sold_or_crafted'] = {k: sorted(v) for k, v in sorted(unavailable.items())}
    report['counts'] = dict(tdl_recipes=len(rows), resolved=len(recipes), left_out=len(report['left_out']),
                            crafteditem_added=len(add_ci), crafteditem_updated=sum(len(v) for v in upd.values()),
                            products_with_new_ingredients=len({x['CraftedItemId_nb'] for x in add_x}),
                            craftedxitem_rows_added=len(add_x), collisions=len(report['collisions']),
                            sql_updates=len(updates))
    print(json.dumps(report['counts']))
    if a.report:
        with open(a.report, 'w', encoding='utf-8') as f:
            json.dump(report, f, indent=1, default=list)


if __name__ == '__main__':
    main()
