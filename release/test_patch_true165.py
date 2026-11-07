"""End-to-end test of the True 1.65 update path on a small fake game (no network, nothing outside --work).

    python -I test_patch_true165.py --work <empty scratch folder> --world <a clean opendaoc-165.sqlite3.db>
                                    --repo <the True165 repository>

It builds a fake v0.1 and v0.2 (changed, added and removed files in the game and the client), installs v0.1 with the
downloader, adds a player's save (world, login, client settings), and checks the update: a changed file stops it (and
the fresh-install-with-save path works), a failure halfway puts everything back, a good run gives exactly v0.2 with
the save untouched and a backup of what it replaced, a second run says "already up to date", a half-applied update
finishes on the next run, and a v0.3 patch built from both baselines brings v0.1 and v0.2 installs to exactly v0.3.
"""
from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PS1 = HERE / "Get-OfflineDAoC-True165.ps1"
SAVE_FILES = ["runtime/data/opendaoc-165.sqlite3.db", "runtime/account.txt", "new-client/save/settings.cfg"]


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def tree(root: Path, skip=("patch-backups/", ".patch/", "UPDATE-CHANGED-FILES.txt")) -> dict[str, str]:
    out = {}
    for path in root.rglob("*"):
        if path.is_file():
            rel = path.relative_to(root).as_posix()
            if not rel.startswith(skip):
                out[rel] = sha(path)
    return out


def run(cmd, env=None, expect=0):
    r = subprocess.run(cmd, capture_output=True, text=True, env=env, stdin=subprocess.DEVNULL)
    if r.returncode != expect:
        print(r.stdout[-3000:], r.stderr[-2000:])
        raise SystemExit(f"exit {r.returncode} (expected {expect}): {' '.join(map(str, cmd))[:200]}")
    return r.stdout


def downloader(version, assets, *args, env=None, expect=0):
    return run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(PS1), "-ReleaseVersion", version,
                "-LocalAssets", str(assets), "-Yes", "-NoShortcut", *map(str, args)], env=env, expect=expect)


def make_game(root: Path, world: Path, version: str):
    for rel, text in {
        "START OFFLINE DAOC TRUE 1.65.cmd": "@echo off\r\n",
        "runtime/OfflineDAoC.exe": "launcher", "runtime/server/CoreServer.exe": "server",
        "runtime/server/config/serverconfig-165.xml": "<AutoAccountCreation>True</AutoAccountCreation>",
        "runtime/true165/true165-rules.json": "{}", "runtime/true165/drops165.json": '{"drops": 1}',
        "runtime/true165/old165.json": "removed in 0.2", "tools/dotnet/dotnet.exe": "dotnet",
    }.items():
        (root / rel).parent.mkdir(parents=True, exist_ok=True)
        (root / rel).write_text(text, encoding="utf-8")
    (root / "runtime/data").mkdir(parents=True, exist_ok=True)
    shutil.copyfile(world, root / "runtime/data/opendaoc-165.sqlite3.db")


def make_client(root: Path, version: str):
    c = root / "new-client"
    for rel, data in {"VERSION.txt": f"Offline DAoC True 1.65 v{version} new client\r\n".encode(),
                      "godot/project.godot": b"x", "godot/content/.gdignore": b"",
                      "godot/.godot/mono/temp/bin/Debug/MMOEngine.dll": b"dll",
                      "godot/content/a.png": os.urandom(3_000_000), "godot/content/b.png": os.urandom(3_000_000),
                      "tools/godot_net/Godot_v4.7.2-stable_mono_win64.exe": b"godot"}.items():
        (c / rel).parent.mkdir(parents=True, exist_ok=True)
        (c / rel).write_bytes(data)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--world", type=Path, required=True)
    parser.add_argument("--repo", type=Path, required=True)
    args = parser.parse_args()
    w = args.work.resolve()
    if w.exists() and any(w.iterdir()):
        raise SystemExit(f"{w} must be empty")
    w.mkdir(parents=True, exist_ok=True)
    py = [sys.executable, "-I"]

    # ---- v0.1 and v0.2
    make_game(w / "game1", args.world, "0.1")
    make_client(w / "client1", "0.1")
    shutil.copytree(w / "game1", w / "game2")
    shutil.copytree(w / "client1", w / "client2")
    (w / "game2/runtime/true165/drops165.json").write_text('{"drops": 2}', encoding="utf-8")       # changed
    (w / "game2/runtime/true165/new165.json").write_text("added in 0.2", encoding="utf-8")         # added
    (w / "game2/runtime/true165/old165.json").unlink()                                             # removed
    (w / "game2/VERSION.txt").write_text("Offline DAoC True 1.65 v0.2\r\n", encoding="utf-8")       # added
    (w / "client2/new-client/VERSION.txt").write_text("Offline DAoC True 1.65 v0.2 new client\r\n", encoding="utf-8")
    (w / "client2/new-client/godot/content/a.png").write_bytes(os.urandom(3_000_000))             # changed (big)
    for v, n in (("0.1", "1"), ("0.2", "2")):
        run(py + [str(HERE / "make_release_true165.py"), "--game", w / f"game{n}", "--client", w / f"client{n}",
                  "--repo", args.repo, "--out", w / "assets", "--version", v, "--part-budget", "4000000"])
    run(py + [str(HERE / "make_patch_true165.py"), "--base", f"0.1={w / 'assets/baseline-0.1.sha256'}",
              "--game", w / "game2", "--client", w / "client2", "--repo", args.repo, "--out", w / "assets",
              "--version", "0.2", "--part-budget", "4000000"])
    print("built v0.1, v0.2 and the 0.1 -> 0.2 patch")

    def install(name):
        target = w / name
        downloader("0.1", w / "assets", "-Mode", "install", "-Destination", target)
        (target / "runtime/account.txt").write_text("Account: player123456\nPassword: x\n", encoding="utf-8")
        (target / "new-client/save").mkdir(parents=True, exist_ok=True)
        (target / "new-client/save/settings.cfg").write_text("player settings", encoding="utf-8")
        with open(target / "runtime/data/opendaoc-165.sqlite3.db", "ab") as db:
            db.write(b"played")   # stands in for a played world
        return target

    v02 = {}
    for line in (w / "assets/baseline-0.2.sha256").read_text(encoding="utf-8").splitlines():
        h, rel = line.split("  ", 1)
        v02[rel] = h

    def check_is_v02(game: Path, save: dict):
        have = tree(game)
        for rel, h in v02.items():
            if rel in SAVE_FILES or rel.startswith("runtime/data/"):
                continue
            assert have.get(rel) == h, f"{rel} is not the v0.2 file"
        assert "runtime/true165/old165.json" not in have, "the removed file is still there"
        for rel in SAVE_FILES:
            assert have.get(rel) == save[rel], f"the save file {rel} was changed"

    # ---- 1. a file changed on this PC: nothing is touched; the fresh install brings the save over
    g = install("g1")
    (g / "runtime/true165/drops165.json").write_text("my own edit", encoding="utf-8")
    before = tree(g)
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g)
    assert "were changed on this PC" in out, out[-800:]
    after = tree(g)
    assert before == {k: v for k, v in after.items() if k != "UPDATE-CHANGED-FILES.txt"} or before == after, "the old folder was changed"
    fresh = Path(str(g) + "-v0.2")
    assert (fresh / "VERSION.txt").is_file(), "no fresh install"
    for rel in SAVE_FILES:
        assert sha(fresh / rel) == before[rel], f"{rel} was not brought over"
    print("1 ok: a changed file stops the update; the fresh install has the save")

    # ---- 2. a failure halfway puts everything back
    g = install("g2")
    before = tree(g)
    env = dict(os.environ, OFFLINEDAOC_TEST_FAIL_AFTER="2")
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g, env=env, expect=1)
    assert "back as it was" in out, out[-800:]
    assert tree(g) == before, "the failed update was not undone"
    assert not (g / "patch-backups").exists() and not (g / ".patch").exists(), "the failed update left files behind"
    print("2 ok: a failure halfway is undone")

    # ---- 3. a good update, and 4. a second run
    save = {rel: before[rel] for rel in SAVE_FILES}
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g)
    assert "updated to" in out, out[-800:]
    check_is_v02(g, save)
    backups = list((g / "patch-backups").iterdir())
    assert len(backups) == 1 and (backups[0] / "runtime/true165/drops165.json").read_text() == '{"drops": 1}'
    assert (backups[0] / "runtime/true165/old165.json").is_file(), "the removed file was not backed up"
    print("3 ok: the update gives exactly v0.2, the save is untouched, the replaced files are backed up")
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g)
    assert "already up to date" in out, out[-800:]
    print("4 ok: a second run says it's up to date")

    # ---- 5. a half-applied update (a crash after some files) finishes on the next run
    g = install("g5")
    save = {rel: sha(g / rel) for rel in SAVE_FILES}
    shutil.copyfile(w / "game2/runtime/true165/drops165.json", g / "runtime/true165/drops165.json")
    shutil.copyfile(w / "client2/new-client/VERSION.txt", g / "new-client/VERSION.txt")
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g)
    assert "updated to" in out, out[-800:]
    check_is_v02(g, save)
    print("5 ok: a half-applied update finishes")

    # ---- 6. v0.3: one cumulative patch brings both v0.1 and v0.2 up to v0.3
    shutil.copytree(w / "game2", w / "game3")
    shutil.copytree(w / "client2", w / "client3")
    (w / "game3/runtime/true165/drops165.json").write_text('{"drops": 3}', encoding="utf-8")       # changed again
    (w / "game3/runtime/true165/new165.json").unlink()                                             # added in 0.2, removed
    (w / "game3/runtime/true165/v03.json").write_text("added in 0.3", encoding="utf-8")            # added
    (w / "game3/VERSION.txt").write_text("Offline DAoC True 1.65 v0.3\r\n", encoding="utf-8")
    (w / "client3/new-client/VERSION.txt").write_text("Offline DAoC True 1.65 v0.3 new client\r\n", encoding="utf-8")
    run(py + [str(HERE / "make_release_true165.py"), "--game", w / "game3", "--client", w / "client3",
              "--repo", args.repo, "--out", w / "assets", "--version", "0.3", "--part-budget", "4000000"])
    run(py + [str(HERE / "make_patch_true165.py"), "--base", f"0.1={w / 'assets/baseline-0.1.sha256'}",
              "--base", f"0.2={w / 'assets/baseline-0.2.sha256'}",
              "--game", w / "game3", "--client", w / "client3", "--repo", args.repo, "--out", w / "assets",
              "--version", "0.3", "--part-budget", "4000000"])
    import json
    patch = json.loads((w / "assets/v0.3/download-manifest.json").read_text(encoding="utf-8"))["Patch"]
    assert patch["From"] == ["0.1", "0.2"], patch["From"]
    v03 = dict(reversed(line.split("  ", 1)) for line in
               (w / "assets/baseline-0.3.sha256").read_text(encoding="utf-8").splitlines() if line.strip())

    def check_is_v03(game: Path, save: dict):
        have = tree(game)
        for rel, h in v03.items():
            if rel in SAVE_FILES or rel.startswith("runtime/data/"):
                continue
            assert have.get(rel) == h, f"{game.name}: {rel} is not the v0.3 file"
        for gone in ("runtime/true165/old165.json", "runtime/true165/new165.json"):
            assert gone not in have, f"{game.name}: the removed file {gone} is still there"
        for rel in SAVE_FILES:
            assert have.get(rel) == save[rel], f"{game.name}: the save file {rel} was changed"

    g01 = install("g6-v01")
    g02 = install("g6-v02")
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g02)
    assert "updated to" in out, out[-800:]
    for g in (g01, g02):
        save = {rel: sha(g / rel) for rel in SAVE_FILES}
        out = downloader("0.3", w / "assets", "-Mode", "update", "-Update", g)
        assert "updated to" in out, f"{g.name}: {out[-800:]}"
        check_is_v03(g, save)
    print("6 ok: v0.1 and v0.2 both update to exactly v0.3 with one patch (files added in 0.2 and removed in 0.3 go)")

    # ---- 7. v0.4: one cumulative patch for v0.1, v0.2 and v0.3, whichever way a game got to its version
    shutil.copytree(w / "game3", w / "game4")
    shutil.copytree(w / "client3", w / "client4")
    (w / "game4/runtime/true165/v03.json").write_text("changed in 0.4", encoding="utf-8")         # added in 0.3, changed
    (w / "game4/runtime/true165/drops165.json").unlink()                                          # in every version, removed
    (w / "game4/runtime/true165/v04.json").write_text("added in 0.4", encoding="utf-8")            # added
    (w / "game4/VERSION.txt").write_text("Offline DAoC True 1.65 v0.4\r\n", encoding="utf-8")
    (w / "client4/new-client/VERSION.txt").write_text("Offline DAoC True 1.65 v0.4 new client\r\n", encoding="utf-8")
    run(py + [str(HERE / "make_release_true165.py"), "--game", w / "game4", "--client", w / "client4",
              "--repo", args.repo, "--out", w / "assets", "--version", "0.4", "--part-budget", "4000000"])
    run(py + [str(HERE / "make_patch_true165.py"), "--base", f"0.1={w / 'assets/baseline-0.1.sha256'}",
              "--base", f"0.2={w / 'assets/baseline-0.2.sha256'}", "--base", f"0.3={w / 'assets/baseline-0.3.sha256'}",
              "--game", w / "game4", "--client", w / "client4", "--repo", args.repo, "--out", w / "assets",
              "--version", "0.4", "--part-budget", "4000000"])
    patch = json.loads((w / "assets/v0.4/download-manifest.json").read_text(encoding="utf-8"))["Patch"]
    assert patch["From"] == ["0.1", "0.2", "0.3"], patch["From"]
    v04 = dict(reversed(line.split("  ", 1)) for line in
               (w / "assets/baseline-0.4.sha256").read_text(encoding="utf-8").splitlines() if line.strip())

    def check_is_v04(game: Path, save: dict):
        have = tree(game)
        for rel, h in v04.items():
            if rel in SAVE_FILES or rel.startswith("runtime/data/"):
                continue
            assert have.get(rel) == h, f"{game.name}: {rel} is not the v0.4 file"
        for gone in ("runtime/true165/old165.json", "runtime/true165/new165.json", "runtime/true165/drops165.json"):
            assert gone not in have, f"{game.name}: the removed file {gone} is still there"
        for rel in SAVE_FILES:
            assert have.get(rel) == save[rel], f"{game.name}: the save file {rel} was changed"

    games = {"v0.1": install("g7-v01"), "v0.2": install("g7-v02"), "v0.3 (0.1>0.2>0.3)": install("g7-v03-chain"),
             "v0.3 (0.1>0.3)": install("g7-v03-skip")}
    for g, steps in ((games["v0.2"], ["0.2"]), (games["v0.3 (0.1>0.2>0.3)"], ["0.2", "0.3"]), (games["v0.3 (0.1>0.3)"], ["0.3"])):
        for v in steps:
            out = downloader(v, w / "assets", "-Mode", "update", "-Update", g)
            assert "updated to" in out, f"{g.name} to {v}: {out[-800:]}"
    for name, g in games.items():
        save = {rel: sha(g / rel) for rel in SAVE_FILES}
        out = downloader("0.4", w / "assets", "-Mode", "update", "-Update", g)
        assert "updated to" in out, f"{name}: {out[-800:]}"
        check_is_v04(g, save)
        out = downloader("0.4", w / "assets", "-Mode", "update", "-Update", g)
        assert "already up to date" in out, f"{name}: {out[-800:]}"
    print("7 ok: v0.1, v0.2 and v0.3 (reached step by step or skipping 0.2) all update to exactly v0.4 with one patch")
    print("ALL PATCH TESTS PASSED")


if __name__ == "__main__":
    main()
