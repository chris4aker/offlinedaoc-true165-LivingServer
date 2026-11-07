"""True 1.65 vendor trash is level 0 (owner 2026-11-07: "everything consistent with vendor trash being level 0 across the
board"). The period pages give vendor trash no level: 1,840 of the 1,860 vendor-trash items on Uthgard are level 0, the
rest being the Darkness Falls seals and a few odd ones (prima_batch_2026-11-07/uthgard_trash_levels.json).

The shared rule for every builder that writes or compares item levels:
- is_vendor_trash(row, ...): plain loot (Object_Type 0) with a sell price, that a monster drops (or one of the True 1.65
  loot packages), not sold by a merchant, and not a quest item, seal, token or Atlas XP item;
- display_level(name): the level the item shows - the Uthgard page's level, else 0;
- research_level(id_nb, level): the level the item had before it went to 0 (trash_levels_original165.json). Builders
  that match drops to monster levels (the drop builders, the drop fix's 15-level twins) compare THIS level, so the
  display change never moves a drop (a level 42 sphene stays off a level 3 monster).
"""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
ORIGINAL_PATH = os.path.join(HERE, 'trash_levels_original165.json')
UTHGARD_PATH = os.path.join(HERE, 'prima_batch_2026-11-07', 'uthgard_trash_levels.json')
EXCLUDED_PACKAGES = {'classicquest', 'atlasxpitem', 'dratlasxpitem', 'atlas_orbs_item', 'atlas_trophys', 'artifact credits',
                     'bufftokens', 'bpbufftokens', 'drmed'}
LOOT_PACKAGES = {'true165drop', 'true165_dropfix', 'true165uthgard', 'true165_gem', 'true165primaitem'}
_original = None
_uthgard = None


def original_levels():
    global _original
    if _original is None:
        _original = json.load(open(ORIGINAL_PATH, encoding='utf-8')) if os.path.exists(ORIGINAL_PATH) else {}
    return _original


def uthgard_levels():
    global _uthgard
    if _uthgard is None:
        _uthgard = json.load(open(UTHGARD_PATH, encoding='utf-8')) if os.path.exists(UTHGARD_PATH) else {}
    return _uthgard


def research_level(id_nb, level):
    """The level to compare with monster levels: the pre-zero level of a vendor-trash item, else the item's own."""
    return original_levels().get((id_nb or '').lower(), level or 0)


def display_level(name):
    return int(uthgard_levels().get((name or '').strip().lower(), 0))


def is_vendor_trash(row, dropped_ids=None, sold_ids=None):
    """row: an ItemTemplate row as a dict (Id_nb, Name, Object_Type, Price, PackageID)."""
    iid = (row.get('Id_nb') or '').lower()
    pkg = (row.get('PackageID') or '').lower()
    name = (row.get('Name') or '').lower()
    if (row.get('Object_Type') or 0) != 0 or not row.get('Price') or row['Price'] <= 0:
        return False
    if pkg in EXCLUDED_PACKAGES or iid.startswith('cq_') or any(w in name for w in ('seal', 'token', 'credit')):
        return False
    if sold_ids is not None and iid in sold_ids:
        return False
    return pkg in LOOT_PACKAGES or (dropped_ids is not None and iid in dropped_ids)


def keep_original(id_nb, level):
    """Remember a vendor-trash item's level before a builder writes it as its display level."""
    if level and (id_nb or '').lower() not in original_levels():
        original_levels()[(id_nb or '').lower()] = int(level)


def save_originals():
    json.dump(dict(sorted(original_levels().items())), open(ORIGINAL_PATH, 'w', encoding='utf-8'), indent=0)


def apply_display_level(row, dropped_ids=None, sold_ids=None):
    """A builder's ItemTemplate row: vendor trash gets its display level (its old level kept). Returns the row."""
    if is_vendor_trash(row, dropped_ids, sold_ids):
        lv = display_level(row.get('Name'))
        if (row.get('Level') or 0) != lv:
            keep_original(row.get('Id_nb'), row.get('Level'))
            row['Level'] = lv
    return row
