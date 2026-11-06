"""True 1.65 merchant titles (2026-11-05): a merchant shows the title of what it sells.

Owner: "every vendor sells staves now" - in Adribard's Retreat Graeme Dalston showed <Shield Merchant> and Wylie Edyn
<Armor Merchant> while selling Theurgist and Sorcerer staves (Prima's Atlas: Graeme Dalston = Theurgist staves, Wylie
Edyn = Sorcerer staves). Their Mob rows say "Weapon Merchant", but their NPC templates have ReplaceMobValues = 1, and
GameNPC applies a template's Name, GuildName, flags... over the Mob row at load. The templates came from an older
database and carry other titles; the Mob rows (the vetted 1.65 spawns) match the stock.

Rule: for every NpcTemplate with ReplaceMobValues = 1 whose users are all merchants (merchant class, a "...Merchant"
title on the Mob row) with one and the same title, the template takes that title. Guards, trainers and templates
shared by NPCs with different titles are left alone. Rerunnable: the rules updates carry the TAG and are replaced.

Reads the world database read-only. Out (both copies): true165-rules.json updates; merchanttitles165.json (this
folder, the review list).
"""
import collections, json, os, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
DB = os.path.join(ROOT, "runtime", "data", "opendaoc-165.sqlite3.db")
COPIES = [HERE, os.path.join(ROOT, "runtime", "true165")]
TAG = "True 1.65 merchant titles:"


def sql_text(s):
    return "'" + str(s).replace("'", "''") + "'"


def main():
    con = sqlite3.connect("file:///" + DB.replace("\\", "/") + "?mode=ro", uri=True)
    q = lambda s, *a: con.execute(s, a).fetchall()
    templates = {r[0]: r for r in q("SELECT TemplateId, Name, GuildName FROM NpcTemplate WHERE ReplaceMobValues = 1")}
    users = collections.defaultdict(list)
    for name, guild, ct, tid, region in q("SELECT Name, Guild, ClassType, NPCTemplateID, Region FROM Mob WHERE NPCTemplateID > 0"):
        if tid in templates:
            users[tid].append((name, guild or "", ct or "", region))
    changes, kept = [], []
    for tid, mobs in sorted(users.items()):
        _, tname, tguild = templates[tid]
        titles = {g for _, g, _, _ in mobs}
        merchants = all("merchant" in ct.lower() and g.endswith("Merchant") for _, g, ct, _ in mobs)
        if not merchants or len(titles) != 1:
            continue
        title = titles.pop()
        if (tguild or "") == title:
            continue
        changes.append({"template": tid, "template_name": tname, "was": tguild, "now": title,
                        "mobs": [f"{n} (region {r})" for n, _, _, r in mobs]})
    updates = [{
        "sql": f"UPDATE NpcTemplate SET GuildName = {sql_text(c['now'])} WHERE TemplateId = {c['template']} "
               f"AND ifnull(GuildName, '') = {sql_text(c['was'] or '')}",
        "reason": f"{TAG} {', '.join(c['mobs'])} shows <{c['now']}> as its Mob row says (its template said "
                  f"<{c['was'] or ''}> and replaces the Mob's values)",
    } for c in changes]
    json.dump({"rule": __doc__.split("\n\n")[2].strip(), "changes": changes},
              open(os.path.join(HERE, "merchanttitles165.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    for folder in COPIES:
        rp = os.path.join(folder, "true165-rules.json")
        rules = json.load(open(rp, encoding="utf-8"))
        rules["updates"] = [u for u in rules["updates"] if not u["reason"].startswith(TAG)] + updates
        with open(rp, "w", encoding="utf-8") as f:
            json.dump(rules, f, ensure_ascii=False, indent=1)
            f.write("\n")
    print(f"merchant templates retitled: {len(changes)}")
    for c in changes:
        print(f"  {c['template']} {c['template_name']}: <{c['was']}> -> <{c['now']}>  ({'; '.join(c['mobs'])})")


if __name__ == "__main__":
    sys.exit(main())
