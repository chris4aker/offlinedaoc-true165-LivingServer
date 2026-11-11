class_name NamePlates
extends Node
## Overhead names, measured from the owner's recordings of the real client (2026-10-09, LOUGH DERG LEDGER.md
## "Name plates"):
## - font: the client's Minion Pro Bold (ui/fonts/minionpro-bold.ttf) with a dark outline
## - the same range for friendly NPCs and monsters: about 3.3x the camera-to-player distance (NAME_RANGE from the
##   player at the default camera); a name simply appears / disappears there (owner: no fading)
## - a SET size at every distance (owner: names never grow or shrink as you walk up to them)
## - the target's name (left click) is highlighted: bright white
## - friendly NPCs light aqua (sampled 175, 223, 208), monsters in their con colour to you
## - drawn IN the world and depth-tested: a wall or corner in front clips the name pixel by pixel, while a name above
##   a wall top stays visible (owner's video: "watch how this npcs name disappears as I leave around the corner")
## Each name is a Label3D on the NPC's view (fixed_size: one font pixel = one screen pixel), the guild line a second
## Label3D under it, offset in screen pixels.

# The name range, same for friend and foe: the owner's recordings showed names appearing at about 3.3x the camera's
# distance - and the owner's camera sits at its farthest, 500 units (the old client's saved [Camera] distance=500.00;
# game.dll clamps it to 50..500), so ~40 m (~1,600 units). The first reading took our own 3.6 m camera instead and gave
# 12 m - about a monster's aggro range (owner 2026-10-14: "I don't see an enemy's nameplate until it's in aggro range").
const NAME_RANGE := 40.0
const EM := 0.018                           # font size, a set fraction of the view height (~19 px at 1080)
const GUILD_SCALE := 0.8
const FRIENDLY := Color8(175, 223, 208)
const OUTLINE := Color(0.02, 0.04, 0.02, 0.9)

## Toggle Names / /hidenames: every overhead name hidden but your target's (the 1.64 client's key and commands).
static var hidden := false

## NAME COLOURS (owner 2026-11-10), client side only: the 1.64 client's Game Options "Names: Classic / Colored" and its
## /colornames (game.dll &colorname) and /oldnamecolors. Colored is the 1.54 scheme (1.54c test notes, 8 Oct 2002):
## monsters by con, NPCs cyan, realm enemies red, guild members dark green, players the normal light blue, dead people
## brown, the target white; 1.54e added group members light orange/brown. Saved for the whole install (settings.cfg),
## not per character as the 1.60e notes say the client kept it (owner 2026-11-10 review). game.dll has
## the RGB values in code with no table: players, group and enemies are the colours sampled from the owner's old-client
## screenshots (BotView's), cyan / dark green / brown are improvised close shades (owner 2026-11-10).
const CN_PLAYER := Color8(158, 154, 220)    # light blue (BotView.FRIENDLY, sampled)
const CN_GROUP := Color8(240, 168, 80)      # light orange/brown (BotView.GROUP, sampled)
const CN_ENEMY := Color8(215, 64, 56)       # red (BotView.ENEMY, sampled)
const CN_NPC := Color8(64, 232, 255)        # cyan (improvised)
const CN_GUILD := Color8(40, 150, 50)       # dark green (improvised)
const CN_DEAD := Color8(165, 110, 60)       # brown (improvised)
static var _colored := -1                   # -1: not read from the settings yet


static func colored() -> bool:
	if _colored < 0:
		var cfg := ConfigFile.new()
		cfg.load(UiArt.settings_path)
		_colored = 1 if bool(cfg.get_value("interface", "name_colours", false)) else 0
	return _colored == 1


static func set_colored(on: bool) -> void:
	_colored = 1 if on else 0
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)                  # (re-read: other parts of the UI write the file too)
	cfg.set_value("interface", "name_colours", on)
	cfg.save(UiArt.settings_path)

var manager: NpcManager
var _font: Font
var _size := -1                             # font px the labels were built for
var _pixel := 0.0


func _ready() -> void:
	_font = UiArt.font(true)


func _process(_delta: float) -> void:
	var t0 := Time.get_ticks_usec()
	_process_inner(_delta)
	Perf.proc_add("name plates", Time.get_ticks_usec() - t0)   # (the live log: process by part)


var _companion_names := true
var _group_oids := {}


func _process_inner(_delta: float) -> void:
	_companion_names = BotView.option("names_companions")
	_group_oids.clear()
	if Server.active:
		for m in Server.group_members:
			_group_oids[int(m.get("oid", 0))] = true
	var cam := get_viewport().get_camera_3d()
	if manager == null or cam == null or manager.player == null:
		return
	var vp_h := get_viewport().get_visible_rect().size.y
	var size := maxi(8, roundi(EM * vp_h))
	var pixel := 2.0 * tan(deg_to_rad(cam.fov * 0.5)) / vp_h
	var resized := size != _size or not is_equal_approx(pixel, _pixel)
	_size = size
	_pixel = pixel
	var pp := manager.player.global_position
	var use_colours := colored()
	if use_colours:
		_colored_frame()
	for i in manager.active:
		var n: Npc = manager.active[i]
		if not is_instance_valid(n):
			continue
		var targeted := n == manager.target
		var d := Vector2(n.global_position.x - pp.x, n.global_position.z - pp.z).length()
		# Colored names: a dead player or gamebot keeps its name, in brown (owner 2026-11-10)
		var alive_ok := n.state.alive or (use_colours and n.rez_target() and _player_like(n))
		var show := n.visible and alive_ok and (targeted or (d <= NAME_RANGE
				and not int(n.spawn.flags) & (Npc.FLAG_DONTSHOWNAME | Npc.FLAG_CANTTARGET)))
		# Options > COMPANION NAMES in the server's game: your companions are your group's other members (owner
		# 2026-10-15 UI audit - the toggle only reached the offline bots)
		if show and not targeted and hidden:
			show = false
		if show and not targeted and not _companion_names and _group_oids.has(int(n.spawn.get("oid", 0))):
			show = false
		if show and _behind_wall(n, cam):
			show = false
		var labels: Array = n.get_meta("plate", [])
		var fresh := false
		if labels.is_empty():
			if not show:
				continue
			labels = _build(n)
			n.set_meta("plate", labels)
			fresh = true
		# only on change: a Label3D rebuilds its text mesh when its colour is set, and 265 names re-coloured every
		# frame cost a raid crowd a large share of its frame time (tests/crowd_probe.gd)
		if bool(n.get_meta("plate_shown", false)) != show:
			n.set_meta("plate_shown", show)
			for l in labels:
				(l as Label3D).visible = show
		if not show:
			continue
		if resized or fresh:
			_fit(labels)
		var col: Color = Color.WHITE if targeted else (_colored_col(n) if use_colours
				else (FRIENDLY if n.is_friendly() else n.con_color(PlayerData.level)))
		if fresh or n.get_meta("plate_col", Color(0, 0, 0, 0)) != col:
			n.set_meta("plate_col", col)
			for l in labels:
				(l as Label3D).modulate = col


## Colored names (owner 2026-11-10): a gamebot or hired companion is an NPC on the server but is drawn as a player.
## The server sends no gamebot mark, so the client takes what it knows: your group and your hired companions (their
## object ids), a race figure that sits to rest or is shown to you hidden (only gamebots and companions do), one in
## your guild, or one that one of your realm's bot map markers follows (Server.map_markers, checked once a second).
## Once found it stays a player while in view (spawn "player_like"). Guards and lords of other realms are not
## player-like: red alive, no brown name dead (owner 2026-11-10 review).
const MATCH_M := 1.0                        # a bot marker this close (metres across) to a figure's path is on it
const MATCH_CELL := 2.0
## A marker marks a figure only once it was on that SAME figure at two spots this far apart. The server re-sends a
## bot's marker only on its 4 s turn and only after 4.8 m of movement, so a bot that stops beside a merchant leaves a
## stale marker on the merchant; a figure that stands still can never pass this (owner 2026-11-10 review: a missed
## gamebot drawn as an NPC beats a merchant drawn as a player).
const CONFIRM_M := 3.0
const PATH_MAX_M := 12.0                    # a longer step since the last check (a jump, a gap) counts as no path
var _my_realm := 0
var _my_guild := ""
var _roster_oids := {}
var _markers_at := 0
var _cand := {}                             # marker id -> [figure instance id, the marker's spot then]
var _last_pos := {}                         # figure instance id -> its spot at the last check
var _last_check := 0


func _colored_frame() -> void:
	_my_realm = PlayerData.realm_id()
	_my_guild = String(PlayerData.character.get("guild", "")).strip_edges()
	var now := Time.get_ticks_msec()
	if now < _markers_at:
		return
	_markers_at = now + 1000
	_roster_oids.clear()
	if manager is ServerWorld:
		var v = (manager as ServerWorld).viewer
		if v and v.get("ui") and v.ui.get("companion_net"):
			for r in v.ui.companion_net.roster.values():
				if r is Dictionary and int(r.get("oid", 0)) != 0:
					_roster_oids[int(r.get("oid", 0))] = true
	if Server.active:
		_match_markers()


## Your realm's bot markers onto the figures in view (see CONFIRM_M). Each marker goes to the nearest of your realm's
## race figures whose path since the last check (a still figure: its spot) passes within MATCH_M; it marks that figure
## when it lands on it again at least CONFIRM_M from where it first did.
func _match_markers() -> void:
	var now := Time.get_ticks_msec()
	var fresh_path := now - _last_check < 2000             # (colours switched off a while: no old paths)
	_last_check = now
	var grid := {}
	var pos_now := {}
	var prev := _last_pos if fresh_path else {}
	for k in manager.active:
		var n: Npc = manager.active[k]
		if not is_instance_valid(n) or not n.info.has("race") or int(n.spawn.get("realm", 0)) != _my_realm:
			continue
		var p := n.global_position
		var iid := n.get_instance_id()
		pos_now[iid] = p
		var b := Vector2(p.x, p.z)
		var a: Vector3 = prev.get(iid, p)
		var a2 := Vector2(a.x, a.z)
		if a2.distance_to(b) > PATH_MAX_M:
			a2 = b
		# The figure goes into every cell its path a2 -> b crosses, not only its cell now (owner 2026-11-10 round-2
		# review: a bot that ran past its marker spot was never tried). Samples 1 m apart: the path's closest point
		# to a marker (within MATCH_M) then has a sample within 1.5 m of it, inside the marker's 3x3 cells.
		var fig := [n, a2, b, p.y]
		var steps := ceili(a2.distance_to(b))
		var last := Vector2i(0x7fffffff, 0)
		for s in steps + 1:
			var at := a2.lerp(b, float(s) / float(maxi(steps, 1)))
			var key := Vector2i(floori(at.x / MATCH_CELL), floori(at.y / MATCH_CELL))
			if key == last:
				continue
			last = key
			if grid.has(key):
				(grid[key] as Array).append(fig)
			else:
				grid[key] = [fig]
	_last_pos = pos_now
	var zid := Server.current_zone_id()
	var box := Server.zone_box(zid)
	if box.is_empty() or grid.is_empty():
		_cand.clear()
		return
	var seen := {}
	for mid in Server.map_markers:
		if int(mid) >= Server.GIVER_MARKERS:              # (bot markers only, as Server.bot_dots)
			continue
		var v: Vector4 = Server.map_markers[mid]
		if not Server.in_box(box, v):
			continue
		seen[mid] = true
		var q := Server.to_local(zid, v.y, v.z, v.w)
		var q2 := Vector2(q.x, q.z)
		var c := Vector2i(floori(q.x / MATCH_CELL), floori(q.z / MATCH_CELL))
		var best: Npc = null
		var best_d := MATCH_M
		for dx in [-1, 0, 1]:
			for dz in [-1, 0, 1]:
				for fig in grid.get(c + Vector2i(dx, dz), []):
					if absf(q.y - float(fig[3])) >= 3.0:
						continue
					var d := q2.distance_to(Geometry2D.get_closest_point_to_segment(q2, fig[1], fig[2]))
					if d < best_d or (d == best_d and best == null):
						best_d = d
						best = fig[0]
		if best == null:
			continue
		var bid := best.get_instance_id()
		var was: Array = _cand.get(mid, [])
		if was.is_empty():
			_cand[mid] = [bid, q2]
		elif int(was[0]) == bid:
			if q2.distance_to(was[1]) >= CONFIRM_M:          # the same figure at a second spot: it is that bot
				best.spawn["player_like"] = true
				_cand.erase(mid)
		elif q2 != was[1]:                                   # the marker moved on to another figure
			_cand[mid] = [bid, q2]
	for mid in _cand.keys():
		if not seen.has(mid):
			_cand.erase(mid)


func _player_like(n: Npc) -> bool:
	if bool(n.spawn.get("player_like", false)):
		return true
	var oid := int(n.spawn.get("oid", 0))
	var yes := false
	if oid != 0 and (_group_oids.has(oid) or _roster_oids.has(oid)):
		yes = true
	elif n.info.has("race"):
		var realm := int(n.spawn.get("realm", 0))
		if realm == 0:
			return false
		# another realm's guards and lords are figures too: only sitting or hidden counts there (owner 2026-11-10)
		yes = n.seated or (n.state != null and n.state.stealth_look) \
				or (realm == _my_realm and _my_guild != "" and String(n.spawn.get("guild", "")) == _my_guild)
	if yes:
		n.spawn["player_like"] = true
	return yes


## A name's colour in the colored scheme (the target's white is set before). Group before guild: my order.
func _colored_col(n: Npc) -> Color:
	var person := _player_like(n)
	if not n.state.alive:
		return CN_DEAD                          # (only a dead person's name is shown)
	var realm := int(n.spawn.get("realm", 0))
	if realm == _my_realm:
		if person and _group_oids.has(int(n.spawn.get("oid", 0))):
			return CN_GROUP
		if person and _my_guild != "" and String(n.spawn.get("guild", "")) == _my_guild:
			return CN_GUILD
		return CN_PLAYER if person else CN_NPC
	if realm != 0:
		return CN_ENEMY
	return n.con_color(PlayerData.level)


## A building wall between the camera and the name: the whole name hides (owner 2026-10-20: "names are still clipping
## thru building walls i can see edge of nameplates" - a name is a flat card much wider than its NPC; standing just
## behind a wall seen at a slant, one end of the card came out in front of it). Walls only (ZoneLoader.LAYER_WALL):
## hills and the rest still clip the name pixel by pixel. Each NPC's ray is renewed every OCC_MS, at most
## OCC_PER_FRAME rays a frame (a raid's crowd).
const OCC_MS := 150
const OCC_PER_FRAME := 24
var _occ_rays := 0
var _occ_frame := -1


func _behind_wall(n: Npc, cam: Camera3D) -> bool:
	var now := Time.get_ticks_msec()
	if Engine.get_process_frames() != _occ_frame:
		_occ_frame = Engine.get_process_frames()
		_occ_rays = 0
	if now >= int(n.get_meta("plate_occ_t", 0)) and _occ_rays < OCC_PER_FRAME:
		_occ_rays += 1
		n.set_meta("plate_occ_t", now + OCC_MS)
		var to := n.global_position + Vector3(0, n.height + 0.2, 0)
		var q := PhysicsRayQueryParameters3D.create(cam.global_position, to, ZoneLoader.LAYER_WALL)
		n.set_meta("plate_occ", not cam.get_world_3d().direct_space_state.intersect_ray(q).is_empty())
	return bool(n.get_meta("plate_occ", false))


func _build(n: Npc) -> Array:
	var out := []
	var guild := String(n.spawn.get("guild", ""))
	for text in ([String(n.spawn.name)] + (["<%s>" % guild] if guild != "" else [])):
		var l := Label3D.new()
		l.text = text
		l.font = _font
		l.billboard = BaseMaterial3D.BILLBOARD_ENABLED
		l.fixed_size = true
		l.no_depth_test = false                          # walls in front clip it, like the client
		l.shaded = false
		l.double_sided = true
		l.outline_modulate = OUTLINE
		l.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
		l.position = Vector3(0, n.height + 0.2, 0)
		l.visible = false
		n.add_child(l)
		out.append(l)
	return out


## Font size and screen-pixel offsets for the current window: the name sits above the guild line.
func _fit(labels: Array) -> void:
	var guild_px := roundi(_size * GUILD_SCALE) if labels.size() > 1 else 0
	for k in labels.size():
		var l: Label3D = labels[k]
		l.pixel_size = _pixel
		l.font_size = _size if k == 0 else guild_px
		l.outline_size = maxi(3, roundi(l.font_size * 0.18))
		l.offset = Vector2(0, _font.get_height(guild_px) if k == 0 and guild_px > 0 else 0.0)
