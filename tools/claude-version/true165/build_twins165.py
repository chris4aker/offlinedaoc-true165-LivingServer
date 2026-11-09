"""True 1.65: quest NPC twins (owner 2026-11-09: "fix the twin spawn issues. if a mob is already there no need to
spawn a quest copy"). A tester met two Fressens side by side. Eleven quest NPCs stood beside a period spawn of the
same name; each pair becomes one.

A quest copy that is only a kill target, beside period spawns that count for the kill by name (the step counts kills
by name): the quest copy goes (rules, remove.where):
    Cyfwlch Difwych (Growler's Necklace, Growler Mace) - the period bandit stands 345 units off at the same camp;
    ellyll villager (Prelude to War) - an extra among the 13 period villagers of the small Ellyll village.

A quest NPC the player talks to or hands an item to (peaceful, at the quest's marker), beside a hostile period monster
of the same name: the two are one character, so the quest NPC takes the period spawn's place - the hostile row leaves
add-restoredspawns165.json (the launcher deletes a row its add-file no longer lists):
    Aessa, Chief Gaeth, Macnol, Terje, Tradande, Fressen - the only period spawn of that name;
    dverge smith, elder tidal sheerie - the nearest of the period group (2 smiths, 19 sheeries): the talking one
    stands among them instead of being one more.
Fressen is also the kill target of Visions of Darkness: with the hostile twin gone the step's own event spawn brings
him (classic-quests.json; ClassicQuests.AlreadyThere no longer counts an NPC the player cannot fight).

Left alone: the quest's fenrir prophet at the Fenrir fort is one of Fjall's fellows (classic-quests.json Fellows) and
walks with him.

python -I build_twins165.py      (rerunnable: rows already gone are skipped, its rules entry is replaced)
Rerun it after anything rebuilds add-restoredspawns165.json.
Out (both copies): add-restoredspawns165.json, true165-rules.json.
"""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
RUNTIME = os.path.normpath(os.path.join(HERE, '..', '..', '..', 'runtime', 'true165'))
TAG = 'True 1.65 quest twins'

QUEST_COPIES = {   # ClassicQuest kill targets beside period spawns of the same name
    '650595cd-5057-5d41-8e48-638729fb74a1': 'Cyfwlch Difwych',
    '43fcae3c-16ac-57f5-bba2-b949e1386fd8': 'ellyll villager',
}
PERIOD_TWINS = {   # hostile true165-restored rows whose place a talking quest NPC of the same name takes
    'd21bbf70-32e0-430c-880b-c2b94d5578bb': 'Aessa',
    'd272d27d-9ff9-4a08-b977-bc29ac349ac5': 'Chief Gaeth',
    '18f4d5e4-e799-460d-99b4-a8219187177f': 'Macnol',
    '43215d41-9cf5-4a76-94d4-6d4c89aafb53': 'Terje',
    'f0dc6ede-0341-4960-a463-c83bcda39f21': 'Tradande',
    '8ee1d1e5-0c6f-41a8-a557-ceedb6ff0314': 'Fressen',
    '6e4e18db-d676-4139-a595-ce5bbc559d4d': 'dverge smith',
    '366644d7-18f3-42d4-8a62-4c128a6cb599': 'elder tidal sheerie',
}


def main():
    for folder in (HERE, RUNTIME):
        p = os.path.join(folder, 'add-restoredspawns165.json')
        data = json.load(open(p, encoding='utf-8'))
        before = len(data['rows'])
        for row in data['rows']:
            if row['Mob_ID'] in PERIOD_TWINS:
                assert row['Name'].lower() == PERIOD_TWINS[row['Mob_ID']].lower(), row['Name']
        data['rows'] = [row for row in data['rows'] if row['Mob_ID'] not in PERIOD_TWINS]
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
            f.write('\n')
        print(f'{p}: {before - len(data["rows"])} period twins removed ({len(data["rows"])} rows)')

        p = os.path.join(folder, 'true165-rules.json')
        rules = json.load(open(p, encoding='utf-8'))
        where = [w for w in rules['remove']['where'] if not w['reason'].startswith(TAG)]
        ids = ', '.join(f"'{i}'" for i in sorted(QUEST_COPIES))
        where.append({'table': 'Mob', 'key': 'Mob_ID', 'where': f"Mob_ID IN ({ids}) AND PackageID = 'ClassicQuest'",
                      'reason': f'{TAG} (owner 2026-11-09): quest copies of kill targets that stood beside period '
                                f'spawns of the same name (Cyfwlch Difwych, ellyll villager); the period ones count '
                                f'for the quest'})
        rules['remove']['where'] = where
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(rules, f, ensure_ascii=False, indent=1)
            f.write('\n')
    print('rules written (both copies)')


if __name__ == '__main__':
    main()
