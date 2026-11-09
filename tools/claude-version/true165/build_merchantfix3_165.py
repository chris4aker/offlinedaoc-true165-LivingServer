"""True 1.65 merchant fixes, part 3 (owner 2026-11-09): Daracha, the Hibernian bow merchant of Tir Urphost, sold four
Midgard jewels - Svartalf Crafted Belt, Necklace and Ring and the Svartalf Hair Collar - at no price.

The merchant pages pass (vendor_work/make_goods_content.py) took them from her period Allakhazam page (bestiary 1844,
captured 2003-09-04: "Goods for Sale: Svartalf Crafted Belt, Svartalf Crafted Necklace, Svartalf Crafted Ring,
Svartalf Hair Collar"). That page is a site error: the same four items are the drops of the arachites of Spindelhalla
on a dozen period bestiary pages, their item pages say "(Midgard)", and the Belt's item page lists "Daracha" among the
monsters that drop it. Owner: "Remove them". They stay in the game as the arachites' drops (drops165.json).

python -I build_merchantfix3_165.py      (rerunnable)
Rerun after vendor_work/make_goods_content.py (it rewrites add-merchantgoods165.json).
Out (both copies): add-merchantgoods165.json without the four rows (the launcher deletes a row its add-file no longer
lists).
"""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
RUNTIME = os.path.normpath(os.path.join(HERE, '..', '..', '..', 'runtime', 'true165'))
DARACHA_LIST = '6326d1cf-744d-475b-aa4f-5ccc9c664b09'
NOT_HERS = {'p165_item_1353': 'Svartalf Crafted Belt', 'p165_item_1216': 'Svartalf Crafted Necklace',
            'p165_item_1354': 'Svartalf Crafted Ring', 'p165_item_3504': 'Svartalf Hair Collar'}


def main():
    for folder in (HERE, RUNTIME):
        p = os.path.join(folder, 'add-merchantgoods165.json')
        raw = open(p, 'rb').read().decode('utf-8')
        data = json.loads(raw)
        keep = [r for r in data['rows']
                if not (r['ItemListID'] == DARACHA_LIST and r['ItemTemplateID'] in NOT_HERS)]
        print(f'{p}: {len(data["rows"]) - len(keep)} rows removed ({len(keep)} rows)')
        data['rows'] = keep
        nl = '\r\n' if '\r\n' in raw else '\n'
        with open(p, 'w', encoding='utf-8', newline=nl) as f:
            f.write(json.dumps(data, ensure_ascii=False, indent=1) + ('\n' if raw.endswith('\n') else ''))


if __name__ == '__main__':
    main()
