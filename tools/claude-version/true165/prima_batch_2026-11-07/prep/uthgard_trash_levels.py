"""Vendor-trash levels on Uthgard (owner 2026-11-07: vendor trash is level 0, as on the period pages - 1,840 of 1,860
Uthgard trash items are level 0; the rest are seals and a few lich drops). Writes uthgard_trash_levels.json:
{lower name: level} for every Uthgard item with no slot and no item type. python -I prep/uthgard_trash_levels.py"""
import gzip, html, json, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, '..', '..', '..', '..', '..', 'development-source', 'research', 'period-data',
                                    'archive', 'uthgard', 'raw_pages.repaired.jsonl.gz'))
out = {}
for line in gzip.open(SRC, 'rt', encoding='utf-8'):
    d = json.loads(line)
    if 'item.php?load=' not in d.get('url', ''):
        continue
    b = re.sub(r'<script.*?</script>', '', d.get('body') or d.get('html') or d.get('text') or '', flags=re.S)
    t = html.unescape(re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', ' ', b)))
    m = re.search(r'Item Name Information Stats (.*?)\. Level (\d+) Slot ?(.*?) ?Item type ?(.*?) ?Damage type', t)
    if not m or m.group(3).strip() or m.group(4).strip():
        continue
    name = m.group(1).strip()
    half = len(name) // 2
    if len(name) % 2 == 1 and name[:half] == name[half + 1:]:
        name = name[:half]                      # the page prints the name twice
    out[name.lower()] = max(out.get(name.lower(), 0), int(m.group(2)))
json.dump(dict(sorted(out.items())), open(os.path.join(HERE, '..', 'uthgard_trash_levels.json'), 'w', encoding='utf-8'), indent=0)
print(len(out), 'trash items;', sum(1 for v in out.values() if v), 'with a level:', {k: v for k, v in out.items() if v})
