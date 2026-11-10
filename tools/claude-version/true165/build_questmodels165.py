"""Quest monsters that appeared with no body (owner 2026-11-10, player report: "Anklebiter is invisible and he attacks
you when you can't see him").

17 quest steps in runtime/server/classic-quests.json spawn their monster with Model 0 and no template: the quest builder
found nothing to copy its look from, and model 0 draws as nothing in the client. This gives each one the look of its
own kind, taken from the period bestiaries (Illia, CapnBry, Uthgard) and the monsters around its spawn point:

  Anklebiter       hobgoblin raider (Smyga's camp between Mularn and Haggerfel)  -> 249, the camp's hobgoblins
  Neophyte Spirit  "Drakoran Neophyte Spirit", undead (Illia)                    -> 880, the drakoran neophytes of
                   the southern fort it is bound from (no ghost look of them exists; improvised, marked)
  Fasius Previlus  undead (Illia), the Roman at the temple ruins on Hadrian's Wall -> 106, the skeletal centurions
                   (Centurion Favius has it)
  Dash             undead (Illia), night spawn with the gytrashes south of Caer Boldiam tower 3 -> 450, gytrash
  Cadoc            no period record of its look; it comes after its enchanted horses -> 463, a faerie steed
                   (improvised, marked)
  Spicket          spraggon (the quest's own words, Illia "Magical")              -> 112, the spraggons
  Gashir           the demon the Elves of Gashir summon (Illia "Magical")        -> 606, Midgard's demons (grumoz,
                   manes demon)

Run: python -I build_questmodels165.py   (rewrites runtime/server/classic-quests.json in its own format)
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
PATH = os.path.join(HERE, '..', '..', '..', 'runtime', 'server', 'classic-quests.json')

MODELS = {
    'Anklebiter': 249,
    'Neophyte Spirit': 880,
    'Fasius Previlus': 106,
    'Dash': 450,
    'Cadoc': 463,
    'Spicket': 112,
    'Gashir': 606,
}


def save_server_json(obj, p):
    """The server's quest files keep their own format (CRLF, no final newline) so a change shows as only its lines."""
    text = json.dumps(obj, ensure_ascii=True, indent=1).replace('\n', '\r\n')
    with open(p, 'wb') as f:
        f.write(text.encode('utf-8'))


def main():
    with open(PATH, encoding='utf-8') as f:
        config = json.load(f)
    changed, left = 0, []
    for quest_id, quest in config['Quests'].items():
        for step in quest.get('Steps') or []:
            spawn = (step or {}).get('Spawn')
            if not spawn or spawn.get('Model') or spawn.get('TemplateId'):
                continue
            model = MODELS.get(spawn['Name'])
            if model is None:
                left.append((quest_id, spawn['Name']))
                continue
            spawn['Model'] = model
            changed += 1
    save_server_json(config, PATH)
    print('quest spawns given a model:', changed)
    print('still with no model and no template:', left or 'none')


if __name__ == '__main__':
    main()
