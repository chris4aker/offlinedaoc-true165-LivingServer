"""Helpers for the placement agents (read-only). Run with python -I; import from this folder:

    import sys; sys.path.insert(0, r'<this folder>'); import spawnkit as K
    K.zone(211)                      -> {'zone','region','name','ox','oy','w','h'}  (ox/oy in world units)
    K.radar('dryad', 9)              -> [(local_x, local_y, z, level), ...]  CapnBry sightings of that name in that zone
    K.radar_any('dryad')             -> {zone_id: count}  where the radar saw it at all
    K.uthgard('dryad')               -> [{'zone','zone_id','locs':[[x,y],...]}]  Uthgard map dots (Uthgard's own zone ids!)
    K.clusters(points, 300)          -> [{'x','y','z','n','levels'}]  greedy clusters (a patrolling monster seen many
                                        times along its path is ONE cluster chain: see merge_chain)
    K.merge_chain(clusters, 900)     -> clusters whose centres lie within 900 of each other joined (a patrol path)
    K.world(zone_id, lx, ly)         -> (X, Y) world coords
    K.ground(region, X, Y, near_z)   -> navmesh floor Z nearest near_z, or None (no navmesh / no floor)
    K.live_same_name(name)           -> live Mob rows of that name (any zone): to copy the look and stats from
    K.archived_same_name(name)       -> archived rows (offline_classic165_removed_mobs) of that name
    K.templates(name)                -> NpcTemplate rows of that name
CapnBry caution (owner): the radar is good for WHERE, not HOW MANY - it counts a patrolling monster several times along
its path. Use one spawn per real camp, never one per sighting.
"""
import functools, json, math, os, re, sqlite3, sys

CV = r"C:\Users\thedo\Desktop\Hiatus\DAOC HIATUS\new class test - CLAUDE VERSION"
PD = os.path.join(CV, r"development-source\research\period-data")
_DB = os.path.join(CV, "runtime", "data", "opendaoc-165.sqlite3.db").replace(chr(92), "/")


def _con():
    c = sqlite3.connect("file:" + _DB + "?mode=ro", uri=True)
    c.row_factory = sqlite3.Row
    return c


@functools.lru_cache(maxsize=None)
def _zones():
    return {r["ZoneID"]: dict(zone=r["ZoneID"], region=r["RegionID"], name=r["Name"], ox=r["OffsetX"] * 8192,
                              oy=r["OffsetY"] * 8192, w=r["Width"] * 8192, h=r["Height"] * 8192)
            for r in _con().execute("select * from Zones")}


def zone(zid):
    return _zones()[zid]


def world(zid, lx, ly):
    z = zone(zid)
    return z["ox"] + lx, z["oy"] + ly


def _n(s):
    return re.sub(r"[^a-z]", "", (s or "").lower())


@functools.lru_cache(maxsize=None)
def _radar():
    d = json.load(open(os.path.join(PD, r"archive\capnbry\sightings_index.json"), encoding="utf-8"))
    out = {}
    for v in d.values():
        out.setdefault(_n(v["name"]), []).extend(v["seen"])
    return out


def radar(name, zid):
    return [(x, y, z, lv) for (zz, x, y, z, lv) in _radar().get(_n(name), []) if zz == zid]


def radar_any(name):
    c = {}
    for s in _radar().get(_n(name), []):
        c[s[0]] = c.get(s[0], 0) + 1
    return c


@functools.lru_cache(maxsize=None)
def _uth():
    return {_n(k): v for k, v in json.load(open(os.path.join(PD, r"archive\uthgard\locations.json"), encoding="utf-8")).items()}


def uthgard(name):
    return _uth().get(_n(name), [])


def clusters(points, radius=300):
    pts = list(points)
    out = []
    while pts:
        x0, y0 = pts[0][0], pts[0][1]
        grp = [p for p in pts if math.hypot(p[0] - x0, p[1] - y0) <= radius]
        pts = [p for p in pts if p not in grp]
        cx = sum(p[0] for p in grp) / len(grp); cy = sum(p[1] for p in grp) / len(grp)
        best = min(grp, key=lambda p: math.hypot(p[0] - cx, p[1] - cy))
        out.append(dict(x=best[0], y=best[1], z=best[2] if len(best) > 2 else None, n=len(grp),
                        levels=sorted({p[3] for p in grp if len(p) > 3})))
    return out


def merge_chain(cl, gap=900):
    groups = []
    for c in cl:
        for g in groups:
            if any(math.hypot(c["x"] - o["x"], c["y"] - o["y"]) <= gap for o in g):
                g.append(c); break
        else:
            groups.append([c])
    return groups


_gz = None


def ground(region, x, y, near_z):
    global _gz
    if _gz is None:
        sys.path.insert(0, os.path.join(CV, r"tools\claude-version\true165"))
        sys.dont_write_bytecode = True
        import groundz165 as g
        _gz = g
    try:
        return _gz.ground_z(region, x, y, near_z)
    except Exception:
        return None


def live_same_name(name):
    return [dict(r) for r in _con().execute("select * from Mob where lower(Name)=lower(?)", (name,))]


def archived_same_name(name):
    return [dict(r) for r in _con().execute("select * from offline_classic165_removed_mobs where lower(Name)=lower(?)", (name,))]


def templates(name):
    return [dict(r) for r in _con().execute("select * from NpcTemplate where lower(Name)=lower(?)", (name,))]
