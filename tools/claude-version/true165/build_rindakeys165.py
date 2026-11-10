"""Rinda's Lost Keys takes back what it asks for (owner 2026-11-10: "the quest rinda's lost keys was completed by right
clicking and she never took the rinda's key or iron keychain from me").

Its last step was an interact-to-finish step ("Return the keys and keychain to Rinda"), so speaking to her ended the
quest and both quest items stayed in the backpack. A hand-in step takes one kind of item, so the last step becomes two:
give her the iron keychain (the snake-finder's), then her key (the mauler cub's), which finishes it with her recorded
thanks and the same reward (4 silver, 40 experience). A player standing on the old last step is on the keychain step.
Writes an update to true165-rules.json (both copies) and the new step's map marker / XP share to classic-quests.json.

    python -I build_rindakeys165.py
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
RUNTIME = os.path.join(CV, 'runtime', 'true165')
QUEST_CONFIG = os.path.join(CV, 'runtime', 'server', 'classic-quests.json')
TAG = "True 1.65 Rinda's keys"
QUEST = 20097
RINDA = 'Dwarven Guard Rinda;100'
SOURCE = ("Thank you! Thank you! I know there are some hobgoblins around that like to play jokes on people, viscious "
          "and mean ones. Why don't you check there while I check the merchant huts again?|||")
STEPS = ["Search for a hobgoblin snake-finder around Haggerfel and see if it has Rinda's keys.",
         "Slay the black mauler cub that ate Rinda's key.",
         "Give the iron keychain to Rinda in Haggerfel.",
         "Give Rinda her key."]


def sql_text(s):
    return "'" + s.replace("'", "''") + "'"


def main():
    sets = {
        'StepType': '0|0|10|11',
        'StepText': '|'.join(STEPS),
        'SourceText': SOURCE,
        'AdvanceText': '|||',
        'TargetText': '|||',
        'TargetName': f'hobgoblin snake-finder;100|black mauler cub;100|{RINDA}|{RINDA}',
        'StepItemTemplates': 'cq_iron_keychain|cq_rinda_s_key||',
        'CollectItemTemplate': '||cq_iron_keychain|cq_rinda_s_key',
        'RewardMoney': '0|0|0|400',
        'RewardXP': '0|0|0|40',
    }
    sql = 'UPDATE DataQuest SET ' + ', '.join(f'{k} = {sql_text(v)}' for k, v in sets.items()) + f' WHERE ID = {QUEST}'
    for folder in (HERE, RUNTIME):
        p = os.path.join(folder, 'true165-rules.json')
        rules = json.load(open(p, encoding='utf-8'))
        rules['updates'] = [u for u in rules['updates'] if not str(u.get('reason', '')).startswith(TAG)] + [
            {'sql': sql,
             'reason': f'{TAG} (owner 2026-11-10): the last step took nothing (right-click finished it); now the iron '
                       f'keychain is handed in, then the key finishes the quest - same text, reward and finish line'}]
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(rules, f, ensure_ascii=False, indent=1)
            f.write('\n')
    print('rules written (both copies)')

    # the new last step's map marker (Rinda, as the step before it) and its share of the quest's experience
    cq = json.load(open(QUEST_CONFIG, encoding='utf-8'))
    quest = cq['Quests'][str(QUEST)]
    steps = quest['Steps']
    if len(steps) == 4:                              # null + three steps: add the fourth
        steps.append(json.loads(json.dumps(steps[3])))
        quest['XpShares'] = [0.0, 0.0, 0.0, quest['XpShares'][-1]]
    with open(QUEST_CONFIG, 'wb') as f:
        f.write(json.dumps(cq, ensure_ascii=True, indent=1).replace('\n', '\r\n').encode('utf-8'))
    print(QUEST_CONFIG, 'steps', len(steps) - 1, 'xp shares', quest['XpShares'])


if __name__ == '__main__':
    main()
