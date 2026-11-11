"""Monster loops cut short, made whole in the built glTFs (owner 2026-11-10: the lupine snarler's trot and the zombies'
walk - the necromancer's minor zombie servant's model - restarted before the step was done).

build_monsters cut every clip at animnifs 'frames' / fps. For some loops that is short of the .kfa: wlf_walk.kfa is
10 of its 54 frames, zombie_walk.kfa 15 of 30, and the pose at the cut is 35-55 degrees off the first, so the loop
snapped back every cycle. Those files close on their first pose at their own stop time (nif_character.keeps_whole_loop;
build_monsters now keeps them whole). This rewrites just those clips' keys in the glTFs already built - the same
channels, the whole .kfa at the same playback rate - without rebuilding meshes or textures. monsters.json is left as
it is (clip_seconds stays the table length: the stride's time base).

    python pipeline/fix_monster_loops.py            # list what would change
    python pipeline/fix_monster_loops.py --write    # rewrite those clips
"""
import base64
import json
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build_monsters as bm
import nif_character as nc
from assets_pipeline import CLIENT

nc.WHOLE_LOOPS = True


def folder_sets(meta, mon, nifs):
    """{folder: anim set} - every model drawn from one folder shares its anim set (build_monsters' '__a<set>' rule)."""
    out = {}
    for m, e in sorted(meta.items(), key=lambda x: (int(x[0]) & 0x7FFF, int(x[0]))):
        if not e.get('gltf') or e.get('race') or e.get('missing'):
            continue
        mr = mon.get(str(int(m) & 0x7FFF))
        nr = nifs.get(mr[2].strip()) if mr else None
        if nr is None or not nr[3].strip().isdigit():
            continue
        out.setdefault(e['gltf'].rsplit('/', 2)[-2], int(nr[3]))
    return out


def set_clips(anim_set):
    """The clips build_monsters gives an anim set: {glTF name: (kfa, speed, length)}."""
    try:
        files = nc.clip_files(anim_set, tuple(bm.CLIPS))
    except KeyError:
        return {}
    clips = {bm.CLIPS[k]: v for k, v in files.items() if k in bm.CLIPS}
    try:
        combat, _ = nc.combat_clip_files(anim_set, tuple(bm.COMBAT_CLIPS))
    except KeyError:
        combat = {}
    clips.update({bm.COMBAT_CLIPS[k]: v for k, v in combat.items()})
    try:
        weapon, _ = nc.combat_clip_files(anim_set, tuple(bm.WEAPON_CLIPS))
    except KeyError:
        weapon = {}
    kfas = {v[0].lower() for v in clips.values()}
    for k, v in weapon.items():
        if v[0].lower() not in kfas:
            clips[bm.WEAPON_CLIPS[k]] = v
            kfas.add(v[0].lower())
    for c, v in list(clips.items()):
        if str(v[0]).lower() not in bm.anim_files():
            clips.pop(c)
    bm.cast_clips(anim_set, clips)
    return clips


class Gltf:
    def __init__(self, path):
        self.path = path
        self.g = json.loads(path.read_text(encoding='utf-8'))
        uri = self.g['buffers'][0]['uri']
        self.head, data = uri.split(',', 1)
        self.buf = bytearray(base64.b64decode(data))

    def read(self, i):
        a = self.g['accessors'][i]
        bv = self.g['bufferViews'][a['bufferView']]
        n = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[a['type']]
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
        return np.frombuffer(bytes(self.buf[off:off + a['count'] * n * 4]), np.float32).reshape(a['count'], n)

    def add(self, arr, kind, minmax=False):
        while len(self.buf) % 4:
            self.buf.append(0)
        raw = np.ascontiguousarray(arr, np.float32).tobytes()
        self.g['bufferViews'].append({'buffer': 0, 'byteOffset': len(self.buf), 'byteLength': len(raw)})
        self.buf += raw
        a = {'bufferView': len(self.g['bufferViews']) - 1, 'componentType': 5126, 'count': len(arr), 'type': kind}
        if minmax:
            flat = np.asarray(arr, np.float32).reshape(len(arr), -1)
            a['min'], a['max'] = flat.min(0).tolist(), flat.max(0).tolist()
        self.g['accessors'].append(a)
        return len(self.g['accessors']) - 1

    def save(self):
        self.g['buffers'][0]['uri'] = self.head + ',' + base64.b64encode(bytes(self.buf)).decode()
        self.g['buffers'][0]['byteLength'] = len(self.buf)
        self.path.write_text(json.dumps(self.g), encoding='utf-8')


def whole(tracks, duration):
    """Every track up to the file's stop time (keys past it dropped, as trim drops them past its cut)."""
    out = {}
    for bone, tr in tracks.items():
        out[bone] = {}
        for key, (times, vals) in tr.items():
            keep = times <= duration + 1e-4
            keep[0] = True
            out[bone][key] = (times[keep], vals[keep])
    return out


def _last_key(times, t):
    """The last key time trim keeps at a cut at t (it adds no key at the cut; the first key always stays)."""
    kept = times[times <= t + 1e-4]
    return float(kept[-1]) if len(kept) else float(times[0])


def built_cut(gl, anim, tracks, length, duration, stretch):
    """True if every channel ends where trim cut it at the table length and one of them stops short of the .kfa's own
    end. A cut between keys ends on the key before it (0.467 s for a 0.5 s cut), so the end is matched per channel, not
    against length (owner 2026-11-10 review: those clips were taken for whole and skipped)."""
    lower = {str(b).lower(): b for b in tracks}
    short = False
    for ch in anim['channels']:
        name = gl.g['nodes'][ch['target']['node']].get('name', '')
        bone = name if name in tracks else lower.get(str(name).lower())
        key = {'rotation': 'rot', 'translation': 'pos', 'scale': 'scale'}[ch['target']['path']]
        if bone is None or key not in tracks[bone]:
            return False
        times = np.asarray(tracks[bone][key][0], np.float64)
        end = float(gl.read(anim['samplers'][ch['sampler']]['input'])[-1, 0])
        cut, full = _last_key(times, length), _last_key(times, duration)
        if abs(end - cut * stretch) > 2e-3:              # not the table's cut (already whole, or something else)
            return False
        short = short or full > cut + 1e-4
    return short


def fix_clip(gl, anim, tracks, stretch):
    """The clip's channels given the whole .kfa; False (nothing changed) if they don't match it as built."""
    lower = {str(b).lower(): b for b in tracks}
    plan = []
    for ch in anim['channels']:
        name = gl.g['nodes'][ch['target']['node']].get('name', '')
        bone = name if name in tracks else lower.get(str(name).lower())
        key = {'rotation': 'rot', 'translation': 'pos', 'scale': 'scale'}[ch['target']['path']]
        if bone is None or key not in tracks[bone]:
            return False
        times, vals = tracks[bone][key]
        s = anim['samplers'][ch['sampler']]
        old_t, old_v = gl.read(s['input'])[:, 0], gl.read(s['output'])
        if key == 'scale':
            vals = np.repeat(np.asarray(vals)[:, None], 3, axis=1)
        vals = np.asarray(vals, np.float32).reshape(len(times), -1)
        # the built keys are the start of these (same bone, same rate): anything else is not this clip as built
        k = len(old_t)
        if k > len(times) or not np.allclose(old_t, times[:k] * stretch, atol=2e-3) \
                or not np.allclose(old_v, vals[:k], atol=1e-4):
            return False
        plan.append((s, times * stretch, vals, {'rot': 'VEC4'}.get(key, 'VEC3')))
    for s, times, vals, kind in plan:
        s['input'] = gl.add(np.asarray(times, np.float32), 'SCALAR', minmax=True)
        s['output'] = gl.add(vals, kind)
    return True


def main():
    write = '--write' in sys.argv
    mon, nifs = bm.table('monsters.csv'), bm.table('monnifs.csv')
    meta = json.loads((bm.OUT / 'monsters.json').read_text(encoding='utf-8'))
    bone_names = (CLIENT / 'figures/animnode.dat').read_bytes().decode('latin1').split('\r\n')
    read = {}
    changed, skipped = 0, []
    for folder, anim_set in sorted(folder_sets(meta, mon, nifs).items()):
        path = bm.OUT / folder / 'model.gltf'
        if not path.exists():
            continue
        clips = set_clips(anim_set)
        todo = []
        for name, (kfa, speed, *rest) in clips.items():
            length = rest[0] if rest else None
            if not nc.loops(name) or not length:
                continue
            if kfa.lower() not in read:
                try:
                    read[kfa.lower()] = nc.read_clip(kfa, bone_names)
                except Exception:
                    read[kfa.lower()] = None
            if read[kfa.lower()] is None:
                continue
            tracks, duration = read[kfa.lower()]
            if nc.keeps_whole_loop(tracks, duration, length):
                todo.append((name, kfa, speed, length, tracks, duration))
        if not todo:
            continue
        gl = Gltf(path)
        anims = {a.get('name'): a for a in gl.g.get('animations', [])}
        done = []
        for name, kfa, speed, length, tracks, duration in todo:
            anim = anims.get(name)
            if anim is None:
                continue
            stretch = 1.0 / speed if speed > 0 else 1.0
            if not built_cut(gl, anim, tracks, length, duration, stretch):   # already whole (or not the cut): left alone
                continue
            built_end = max(float(gl.read(s['input'])[-1, 0]) for s in anim['samplers'])
            if fix_clip(gl, anim, whole(tracks, duration), stretch):
                done.append(f'{name} ({kfa} {built_end:.3f} -> {duration * stretch:.3f} s, '
                            f'snap {nc.pose_gap(tracks, length):.0f} -> {nc.pose_gap(tracks, duration):.0f} deg)')
            else:
                skipped.append(f'{folder} {name}: keys do not match the built clip - left alone')
        if done:
            changed += 1
            print(folder, '|', '; '.join(done))
            if write:
                gl.save()
    for s in skipped:
        print('SKIPPED', s)
    print(changed, 'model folders', 'rewritten' if write else 'to rewrite (--write to do it)')


if __name__ == '__main__':
    main()
