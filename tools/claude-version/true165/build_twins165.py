"""True 1.65: quest NPC twins (owner 2026-11-09: "fix the twin spawn issues. if a mob is already there no need to
spawn a quest copy"). A tester met two Fressens side by side. Ten quest NPCs stood beside a period spawn of the same
name; each pair becomes one.

1. A quest copy that is only a kill target, beside period spawns that count for the kill (the step counts kills by
   name): the quest copy goes (rules, remove.where).
     Cyfwlch Difwych (Growler's Necklace, Growler Mace) - the period bandit stands 345 units off at the same camp and
       becomes the quest monster gamebots leave alone (classic-quests.json QuestMonsterIds), as the copy was;
     ellyll villager (Prelude to War) - an extra among the 13 period villagers of the small Ellyll village.

2. A quest NPC the player talks to or hands an item to, beside a hostile period monster of the same name with no
   drops of its own (Chief Gaeth, Terje, Fressen) or one of a group (dverge smith: 2, elder tidal sheerie: 19): the
   talking NPC takes that monster's place - the hostile row leaves add-restoredspawns165.json (the launcher deletes a
   row its add-file no longer lists).
   Fressen is also the kill target of Visions of Darkness: the step's own event spawn brings him (classic-quests.json;
   ClassicQuests.AlreadyThere no longer counts an NPC the player cannot fight).

3. Aessa, Macnol and Tradande have period drops (Tradande: Spear of the Elements, Bark Edged Boots), so a peaceful
   NPC alone would take those out of the game. Owner 2026-11-09: "One neutral monster" - one character that talks for
   the quest, never attacks first and can be killed for its drops. The period bestiary lists all three as quest
   characters with no aggro range. The quest's row (it stands at the quest's marker) becomes that monster: no realm
   (rules update), and its own NPC template stops attacking on sight (AggroLevel 0; each template is used by these
   two rows only, and with ReplaceMobValues it decides the aggression, not the Mob row). The hostile duplicate leaves
   add-restoredspawns165.json. Its drops come by name, as before. Gamebots leave the three alone (QuestMonsterIds),
   so the quest's talker is not hunted. Talking and hand-ins to a monster with no realm are what DataQuest and
   QuestTalkTruce already do for some 60 other quest monsters.

Left alone: the quest's fenrir prophet at the Fenrir fort is one of Fjall's fellows (classic-quests.json Fellows) and
walks with him.

python -I build_twins165.py      (rerunnable: rows already gone are skipped, its own entries are replaced)
Rerun it after anything rebuilds add-restoredspawns165.json or classic-quests.json's QuestMonsterIds.
Out: add-restoredspawns165.json, true165-rules.json (both copies); runtime/server/classic-quests.json.
"""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
QUEST_CONFIG = os.path.join(CV, 'runtime', 'server', 'classic-quests.json')
TAG = 'True 1.65 quest twins'

QUEST_COPIES = {   # ClassicQuest kill targets beside period spawns of the same name
    '650595cd-5057-5d41-8e48-638729fb74a1': 'Cyfwlch Difwych',
    '43fcae3c-16ac-57f5-bba2-b949e1386fd8': 'ellyll villager',
}
PERIOD_TWINS = {   # hostile true165-restored rows whose place the quest's character of the same name takes
    'd21bbf70-32e0-430c-880b-c2b94d5578bb': 'Aessa',
    'd272d27d-9ff9-4a08-b977-bc29ac349ac5': 'Chief Gaeth',
    '18f4d5e4-e799-460d-99b4-a8219187177f': 'Macnol',
    '43215d41-9cf5-4a76-94d4-6d4c89aafb53': 'Terje',
    'f0dc6ede-0341-4960-a463-c83bcda39f21': 'Tradande',
    '8ee1d1e5-0c6f-41a8-a557-ceedb6ff0314': 'Fressen',
    '6e4e18db-d676-4139-a595-ce5bbc559d4d': 'dverge smith',
    '366644d7-18f3-42d4-8a62-4c128a6cb599': 'elder tidal sheerie',
}
NEUTRAL = {        # quest rows that become the one neutral monster: Mob_ID -> (name, its NPC template)
    '1f3d36b9-b842-5a00-9e05-ad0e704414a8': ('Aessa', 60157597),
    'f7fd965b-d778-5cc3-8b6d-3c36fd5cfd92': ('Macnol', 60163538),
    'bc13c771-45ea-5ef7-930c-0e65695e7157': ('Tradande', 60167202),
}
NEUTRAL_RESPAWN = 80                               # seconds, as the monster of that name had (the quest row: 30)
PROTECT = set(NEUTRAL) | {'9771db82-4675-43be-b497-f7ae8982dc31'}   # + the period Cyfwlch Difwych at the camp
UNPROTECT = {'650595cd-5057-5d41-8e48-638729fb74a1'}                 # the removed quest copy of Cyfwlch Difwych


def main():
    ids = lambda keys: ', '.join(f"'{i}'" for i in sorted(keys))
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
        where.append({'table': 'Mob', 'key': 'Mob_ID',
                      'where': f"Mob_ID IN ({ids(QUEST_COPIES)}) AND PackageID = 'ClassicQuest'",
                      'reason': f'{TAG} (owner 2026-11-09): quest copies of kill targets that stood beside period '
                                f'spawns of the same name (Cyfwlch Difwych, ellyll villager); the period ones count '
                                f'for the quest'})
        rules['remove']['where'] = where
        names = ', '.join(n for n, _ in NEUTRAL.values())
        rules['updates'] = [u for u in rules['updates'] if not str(u.get('reason', '')).startswith(TAG)] + [
            {'sql': f"UPDATE Mob SET Realm = 0, Flags = Flags & ~16, RespawnInterval = {NEUTRAL_RESPAWN} "
                    f"WHERE Mob_ID IN ({ids(NEUTRAL)}) AND PackageID = 'ClassicQuest'",
             'reason': f'{TAG} (owner 2026-11-09, "one neutral monster"): {names} - the quest\'s character is the '
                       f'monster of that name: no realm, so it can be killed for its period drops; it still talks '
                       f'and takes the quest items'},
            {'sql': f"UPDATE NpcTemplate SET AggroLevel = 0 WHERE TemplateId IN "
                    f"({', '.join(str(t) for _, t in sorted(NEUTRAL.values()))}) AND AggroLevel <> 0",
             'reason': f'{TAG} (owner 2026-11-09, "one neutral monster"): {names} never attack first (the period '
                       f'bestiary lists them as quest characters with no aggro range); each template is theirs alone'},
        ]
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(rules, f, ensure_ascii=False, indent=1)
            f.write('\n')
    print('rules written (both copies)')

    # gamebots leave quest monsters alone (ClassicQuests.IsQuestMonster); the file keeps its own format (ASCII, CRLF,
    # no final newline: build_primaquests165.save_server_json)
    cq = json.load(open(QUEST_CONFIG, encoding='utf-8'))
    have = set(cq['QuestMonsterIds'])
    cq['QuestMonsterIds'] = sorted((have | PROTECT) - UNPROTECT)
    with open(QUEST_CONFIG, 'wb') as f:
        f.write(json.dumps(cq, ensure_ascii=True, indent=1).replace('\n', '\r\n').encode('utf-8'))
    print(f'{QUEST_CONFIG}: quest monsters {len(have)} -> {len(cq["QuestMonsterIds"])}')


if __name__ == '__main__':
    main()
