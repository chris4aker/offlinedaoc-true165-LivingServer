"""True 1.65 battleground PvE spawns (owner 2026-11-06: "put the bg mobs back even if there is no period sources").

The 2026-08-29 classic165 conversion archived the battleground monsters as "Post-legacy generic neutral spawn" in
offline_classic165_removed_mobs (Caledon 250: 326 rows, Thidranki 252: 313 rows; Murdaigean 251 and Abermenai 253 lost
none). 66 Thidranki ones (tomb raider, goblin crawler, goblin watcher) came back earlier via add-restoredspawns165.json.
This builder writes every remaining archived battleground monster to add-bgspawns165.json, exactly as archived (same
Mob_ID, level, template, stats, position, respawn; PackageID true165-restored like the earlier restore), skipping:
  - rows that are not monsters: GM jump markers (JumpCaledonia / JumpThid: peace-flagged level 1 model 408), anything
    with a guild title, a non-neutral realm, a class other than DOL.GS.GameNPC, or the peace flag;
  - rows already restored by another add-file (same Mob_ID: the 66 Thidranki ones in add-restoredspawns165.json);
  - rows that would duplicate a live spawn: same name in the same region within DUP_RADIUS units (a row already live
    under its own Mob_ID is this restore applied earlier and stays in the file, so a rebuild after a server start
    gives the same file).
Reads the database read-only. Usage: python -I build_bgspawns165.py [--db path]
"""
import argparse, json, math, os, sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
DEFAULT_DB = os.path.join(ROOT, "runtime", "data", "opendaoc-165.sqlite3.db")
OUT = os.path.join(HERE, "add-bgspawns165.json")
BG_REGIONS = {250: "Caledon", 251: "Murdaigean", 252: "Thidranki", 253: "Abermenai"}
ARCHIVE_REASON = "Post-legacy generic neutral spawn excluded from the server 1.65 authority"
DUP_RADIUS = 25          # a same-name live spawn this close = the same spawn already there
PEACE_FLAG = 0x10
REASON = ("True 1.65 battleground monsters restored (owner 2026-11-06: \"put the bg mobs back even if there is no period "
          "sources\"): archived by the 2026-08-29 classic165 post-legacy removal (offline_classic165_removed_mobs), "
          "restored exactly as archived; build_bgspawns165.py")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", default=DEFAULT_DB)
    ap.add_argument("--out", default=OUT)
    a = ap.parse_args()
    con = sqlite3.connect("file:" + a.db.replace("\\", "/") + "?mode=ro", uri=True)
    mob_cols = [r[1] for r in con.execute('PRAGMA table_info("Mob")')]
    arch_cols = [r[1] for r in con.execute('PRAGMA table_info("offline_classic165_removed_mobs")')]
    regions = ",".join(str(r) for r in BG_REGIONS)
    rows = [dict(zip(arch_cols, r)) for r in con.execute(
        f"SELECT * FROM offline_classic165_removed_mobs WHERE Region IN ({regions}) AND RemovalReason = ? "
        "ORDER BY Region, Name, Level, Mob_ID", (ARCHIVE_REASON,))]
    live_ids = {r[0] for r in con.execute(f"SELECT Mob_ID FROM Mob WHERE Region IN ({regions})")}
    live = [(r[0], r[1], r[2].lower(), r[3], r[4]) for r in con.execute(
        f"SELECT Mob_ID, Region, Name, X, Y FROM Mob WHERE Region IN ({regions})")]
    other_ids = set()
    for fn in os.listdir(HERE):
        if fn.startswith("add-") and fn.endswith(".json") and not fn.startswith("add-bgspawns165"):
            d = json.load(open(os.path.join(HERE, fn), encoding="utf-8"))
            if d.get("table") == "Mob":
                other_ids.update(str(r.get("Mob_ID")) for r in d["rows"])

    out, skipped = [], []
    for r in rows:
        why = None
        if r["Name"].startswith("Jump") or (r["Flags"] or 0) & PEACE_FLAG:
            why = "not a monster (GM jump marker / peace NPC)"
        elif (r["Guild"] or "") or (r["Realm"] or 0) != 0 or r["ClassType"] != "DOL.GS.GameNPC":
            why = "not a monster (guild title / realm NPC / special class)"
        elif r["Mob_ID"] in other_ids:
            why = "already restored by another add-file"
        elif r["Mob_ID"] not in live_ids:      # live with its own Mob_ID = this restore already applied: keep the row
            for mid, reg, nm, x, y in live:
                if reg == r["Region"] and nm == r["Name"].lower() and math.dist((x, y), (r["X"], r["Y"])) < DUP_RADIUS:
                    why = f"duplicates a live {r['Name']} within {DUP_RADIUS} units"
                    break
        if why:
            skipped.append((BG_REGIONS[r["Region"]], r["Name"], r["Level"], r["Mob_ID"], why))
            continue
        row = {c: r[c] for c in mob_cols if c in r}
        row["PackageID"] = "true165-restored"
        out.append(row)

    json.dump({"table": "Mob", "key": "Mob_ID", "reason": REASON, "rows": out},
              open(a.out, "w", encoding="utf-8"), indent=1)
    summary = {}
    for r in out:
        k = (BG_REGIONS[r["Region"]], r["Name"])
        summary[k] = summary.get(k, 0) + 1
    for k in sorted(summary):
        print(f"{k[0]:<11} {k[1]:<18} {summary[k]}")
    print(f"restored {len(out)} rows -> {a.out}")
    for s in skipped:
        if not s[4].startswith("already restored"):
            print("skipped", s)
    print("already restored by another add-file:", sum(1 for s in skipped if s[4].startswith("already restored")))


if __name__ == "__main__":
    main()
