class_name MapWindow
extends ClassicWindow
## The map window (M): the client's own map system (owner 2026-10-09 - post-1.65, the in-game map came with test
## 1.71d), in the classic stone frame. pipeline/build_maps.py -> content/maps/maps.json + the client's painted maps:
##   - a zone map (z###) for every zone, area maps (z###_NN) for towns / dungeon wings inside a zone, and the region
##     maps (r###) with each zone's square on them - click a square to open that zone;
##   - the dropdown finds any map (realm overview, its zones, their areas);
##   - FOLLOW keeps the map on where you are (the zone, or the area map you stand in); picking a map turns it off.
## The player's arrow shows on any map that holds the spot; quest marks on zone / area maps; your realm's bots as green
## dots on every zone / area map and, small, on the realm maps (owner 2026-11-05).

const MAP_SIZE := 260.0
const TOP := 43.0                         # title + dropdown row above the picture
var player: Node3D
var zone_size := 1638.4                   # current zone's size (metres)
var current_zone := -1
var marks: Array = []                     # quest marks (QuestMarkers): [{at: Vector2 world x / z, kind: "!" / "?" / "step"}]
var follow := true
var shown := {}                           # {kind: "zone" / "area" / "region", id, area}
var _maps := {}
var _view: MapView
var _title: Label
var _pick: OptionButton
var _follow_btn: Button
var _entries: Array = []                  # dropdown index -> shown dict ({} = header)
var _tex := {}
var _legend: LegendStrip
## The key to the map's marks under the map (owner 2026-11-05: "move the legend below the actual map and add an option
## in settings to hide it" - Options > INTERFACE > MAP LEGEND, saved [interface] map_legend, on by default).
static var show_legend := true
## Quest marks on the maps at all (owner 2026-11-05: "add an option to disable quest markers" - the map ones only, the
## circles under NPCs in the world stay): Options > INTERFACE > QUEST MARKERS ON MAP, saved [interface] map_quest_marks.
## Off: no marks and no legend.
static var show_quest_marks := true
const LEGEND_H := 15.0


func build() -> void:
	closable = true
	var parsed = JSON.parse_string(FileAccess.get_file_as_string("res://content/maps/maps.json"))
	_maps = parsed if parsed is Dictionary else {}
	_title = UiArt.label("", 11, UiArt.GOLD_HI)
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_title)
	_pick = _dropdown()
	add_child(_pick)
	_follow_btn = UiArt.button("Follow", Vector2(58, 18), 9)
	_follow_btn.toggle_mode = true
	_follow_btn.button_pressed = follow
	_follow_btn.toggled.connect(func(on: bool):
		follow = on
		if on:
			_follow_now())
	add_child(_follow_btn)
	_view = MapView.new()
	_view.window = self
	add_child(_view)
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)
	show_legend = bool(cfg.get_value("interface", "map_legend", true))
	show_quest_marks = bool(cfg.get_value("interface", "map_quest_marks", true))
	_legend = LegendStrip.new()
	add_child(_legend)
	_fill_dropdown()
	_layout()
	resized.connect(_layout)
	PlayerData.quests_changed.connect(_on_quests_changed)


func _layout() -> void:
	if _view == null:
		return
	var w := size.x - 27.0
	var legend := show_legend and show_quest_marks
	var h := size.y - TOP - 8.0 - 33.0 + 21.0 - (LEGEND_H + 2.0 if legend else 0.0)
	var side := maxf(64.0, minf(w, h))
	_title.position = Vector2(12, 3)                       # in the frame's top band (was a gap above it)
	_title.size = Vector2(size.x - 24.0, 16)
	_pick.position = Vector2(12, 21)
	_pick.size = Vector2(size.x - 24.0 - 72.0, 18)
	_follow_btn.position = Vector2(size.x - 22.0 - 58.0, 21)  # clear of the frame's right edge
	_view.position = Vector2(12 + (w - side) * 0.5, TOP)
	_view.size = Vector2(side, side)
	_legend.visible = legend
	_legend.position = Vector2(_view.position.x, TOP + side + 2.0)
	_legend.size = Vector2(side, LEGEND_H)
	_legend.queue_redraw()


## Options > INTERFACE > MAP LEGEND: the key under the map shown or not (the map grows into its room).
func set_legend(on: bool) -> void:
	show_legend = on
	_layout()


## Options > INTERFACE > QUEST MARKERS ON MAP: the quest marks (and with them the legend) shown on the maps or not.
func set_quest_marks(on: bool) -> void:
	show_quest_marks = on
	_layout()
	if _view:
		_view.queue_redraw()


## The key to the map's marks, under the map in the windows' dark stone: each kind's medallion and its word.
class LegendStrip extends Control:
	const ITEMS := [["!", "QUEST"], ["?", "TURN IN"], ["step", "OBJECTIVE"], ["o", "IN PROGRESS"], ["task", "TASK"]]

	func _init() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE

	func _draw() -> void:
		draw_rect(Rect2(Vector2.ZERO, size), Color(0.06, 0.05, 0.04, 0.9))
		draw_rect(Rect2(Vector2.ZERO, size), Color(0.55, 0.42, 0.2, 0.9), false, 1.0)
		var f := UiArt.font(true)
		var fs := 8
		var widths := []
		var total := 0.0
		for it in ITEMS:
			var tw := f.get_string_size(String(it[1]), HORIZONTAL_ALIGNMENT_LEFT, -1, fs).x
			widths.append(tw)
			total += 9.0 + tw
		var gap := maxf(4.0, (size.x - 8.0 - total) / maxf(1.0, ITEMS.size() - 1.0))
		var x := 4.0 + 3.5
		for i in ITEMS.size():
			MapView.draw_mark(self, Vector2(x, size.y * 0.5), String(ITEMS[i][0]), 2.8)
			draw_string(f, Vector2(x + 5.5, size.y * 0.5 + 3.0), String(ITEMS[i][1]), HORIZONTAL_ALIGNMENT_LEFT, -1, fs,
					UiArt.CREAM)
			x += 9.0 + float(widths[i]) + gap


## The zone the player is in now (zone_viewer at every zone load).
func set_current_zone(zone_id: int, size_m: float) -> void:
	current_zone = zone_id
	zone_size = size_m
	_fill_dropdown()
	if follow or shown.is_empty():
		_follow_now()


## Old entry point (kept for the zone viewer's older call): the zone id from the zone folder's name.
func set_zone(map_path: String, size_m: float, _title_text: String) -> void:
	set_current_zone(int(map_path.get_base_dir().get_file().trim_prefix("zone")), size_m)


func _zone(z: int) -> Dictionary:
	return (_maps.get("zones", {}) as Dictionary).get(str(z), {})


## The square of a zone its map shows, in zone units: the whole zone, or - the capitals and dungeons - the square their
## region gives (regions.dat zoneK_offsetx / _offsety / _width / _height; game.dll's map takes a spot minus that offset
## over that size, and an area's rect inside it). Owner 2026-10-14: Jordheim's map put you, the bots and the quest
## marks way off - it was drawn over the whole zone.
func map_rect(z: int) -> Rect2:
	var r = _zone(z).get("map_rect")
	if r is Array and (r as Array).size() == 4:
		return Rect2(float(r[0]), float(r[1]), float(r[2]), float(r[3]))
	var units := _zone_size_of(z) / 0.025
	return Rect2(0.0, 0.0, units, units)


func _region(r: int) -> Dictionary:
	return (_maps.get("regions", {}) as Dictionary).get(str(r), {})


## The player's spot in zone units: x, y on the map, height.
func _player_units() -> Vector3:
	var p := player
	if p == null:
		return Vector3(-1, -1, 0)
	return Vector3(p.global_position.x, p.global_position.z, p.global_position.y) / 0.025


func _area_at(z: int, u: Vector3) -> int:
	var areas: Array = _zone(z).get("areas", [])
	for i in areas.size():
		var a: Dictionary = areas[i]
		var r: Array = a.rect
		var mr := map_rect(z)                                   # an area's rect is inside the zone map's square
		var x := u.x - mr.position.x
		var y := u.y - mr.position.y
		if x <= float(r[0]) or y <= float(r[1]) or x > float(r[0]) + float(r[2]) or y > float(r[1]) + float(r[3]):
			continue
		# a level of a dungeon: its floor (area z) and the height above it (depth) - game.dll 0x530a31: z > area z
		# and z <= area z + depth (the levels stack upward: Darkness Falls' 19126 + 1675 = the next one's 20801)
		if a.has("z") and a.has("depth") and int(a.z) != -1 and int(a.depth) != -1:
			if u.z <= float(a.z) or u.z > float(a.z) + float(a.depth):
				continue
		return i
	return -1


func _follow_now() -> void:
	if current_zone < 0:
		return
	var i := _area_at(current_zone, _player_units())
	_show({"kind": "area", "id": current_zone, "area": i} if i >= 0 else {"kind": "zone", "id": current_zone}, false)


func _process(_delta: float) -> void:
	if not visible:
		return
	if follow and current_zone >= 0:
		var i := _area_at(current_zone, _player_units())
		var want := {"kind": "area", "id": current_zone, "area": i} if i >= 0 else {"kind": "zone", "id": current_zone}
		if want != shown:
			_show(want, false)
	_view.queue_redraw()


func _show(what: Dictionary, picked: bool) -> void:
	shown = what
	var title := ""
	var pic := ""
	match String(what.get("kind", "")):
		"region":
			var r := _region(int(what.id))
			title = _realm_map_name(int(what.id), String(r.get("name", "")))
			pic = String(r.get("map", ""))
		"area":
			var a: Dictionary = (_zone(int(what.id)).get("areas", []) as Array)[int(what.area)]
			title = String(a.get("name", "")) if String(a.get("name", "")) != "" else String(_zone(int(what.id)).get("name", ""))
			pic = String(a.get("map", ""))
		_:
			title = String(_zone(int(what.id)).get("name", ""))
			pic = String(_zone(int(what.id)).get("map", ""))
	_title.text = title.to_upper()
	_view.texture = _texture(pic)
	var idx := _entries.find(what)
	if idx >= 0 and _pick.selected != idx:
		_pick.select(idx)
	if picked and follow:
		follow = false
		_follow_btn.set_pressed_no_signal(false)


func _texture(name: String) -> Texture2D:
	if name == "":
		return null
	if not _tex.has(name):
		var path := "res://content/maps/x4plus/" + name
		if not FileAccess.file_exists(path):
			path = "res://content/maps/original/" + name
		var img := Image.load_from_file(ProjectSettings.globalize_path(path)) if FileAccess.file_exists(path) else null
		if img:
			img.generate_mipmaps()
		_tex[name] = ImageTexture.create_from_image(img) if img else null
	return _tex[name]


func _dropdown() -> OptionButton:
	var o := OptionButton.new()
	o.focus_mode = Control.FOCUS_NONE
	o.clip_text = true
	o.fit_to_longest_item = false
	o.add_theme_font_override("font", UiArt.font(true))
	o.add_theme_font_size_override("font_size", 9)
	o.add_theme_color_override("font_color", UiArt.CREAM)
	o.add_theme_color_override("font_hover_color", UiArt.GOLD_HI)
	o.add_theme_stylebox_override("normal", UiArt.bar_style(0))
	o.add_theme_stylebox_override("hover", UiArt.bar_style(1))
	o.add_theme_stylebox_override("pressed", UiArt.bar_style(2))
	o.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	UiArt.themed(o)
	var pop := o.get_popup()
	pop.add_theme_font_override("font", UiArt.font(true))
	pop.add_theme_font_size_override("font_size", 13)
	pop.add_theme_color_override("font_color", UiArt.CREAM)
	pop.add_theme_color_override("font_hover_color", UiArt.GOLD_HI)
	pop.add_theme_color_override("font_separator_color", UiArt.GOLD_HI)
	pop.add_theme_color_override("font_disabled_color", UiArt.GOLD_HI)
	pop.max_size = Vector2i(0, 520)
	var panel := StyleBoxFlat.new()
	panel.bg_color = Color(0.07, 0.065, 0.06, 0.97)
	panel.border_color = UiArt.GOLD.darkened(0.3)
	panel.set_border_width_all(1)
	panel.set_content_margin_all(4)
	pop.add_theme_stylebox_override("panel", panel)
	var hov := StyleBoxFlat.new()
	hov.bg_color = Color(0.3, 0.22, 0.1, 0.9)
	pop.add_theme_stylebox_override("hover", hov)
	o.item_selected.connect(func(i: int):
		if i >= 0 and i < _entries.size() and not (_entries[i] as Dictionary).is_empty():
			_show(_entries[i], true))
	return o


## Your realm only (owner 2026-10-09): its realm maps at the very top (owner 2026-11-05: every zone as connected
## blocks, click one to open it - the realm and its Shrouded Isles: "is there not a shrouded isles equivalent too for each
## realm?"), then its capital city, then its classic and Shrouded Isles open world zones in alphabetical order. No
## dungeons, no other realms. Inside a dungeon: that dungeon's maps only.
const REALM_REGIONS := {1: [1, 51], 2: [100, 151], 3: [200, 181]}
const CAPITALS := {1: 26, 2: 120, 3: 209}
const REALM_MAPS := {1: [[1, "ALBION"], [51, "AVALON (SHROUDED ISLES)"]],
		2: [[100, "MIDGARD"], [151, "AEGIR (SHROUDED ISLES)"]],
		3: [[200, "HIBERNIA"], [181, "HY BRASIL (SHROUDED ISLES)"]]}


func _fill_dropdown() -> void:
	_pick.clear()
	_entries.clear()
	var realm := PlayerData.realm_id()
	var here := _zone(current_zone)
	if not here.is_empty() and int(here.get("type", 0)) == 2:
		_add_zone(current_zone)                              # in a dungeon: its own maps, nothing else
		return
	var top := false
	for rm in REALM_MAPS.get(realm, []):
		if String(_region(int(rm[0])).get("map", "")) != "":
			_add_entry(String(rm[1]), {"kind": "region", "id": int(rm[0])}, true)
			top = true
	var cap := int(CAPITALS.get(realm, -1))
	if String(_zone(cap).get("map", "")) != "":
		_add_entry(String(_zone(cap).name).to_upper(), {"kind": "zone", "id": cap}, false)
		top = true
	if top:
		_pick.add_separator("")
		_entries.append({})
	var open: Array = []
	for k in (_maps.get("zones", {}) as Dictionary).keys():
		var z := _zone(int(k))
		if int(z.get("type", 0)) == 0 and int(z.get("region", -1)) in REALM_REGIONS.get(realm, []) and String(z.get("map", "")) != "":
			open.append(int(k))
	open.sort_custom(func(a, b): return String(_zone(a).name) < String(_zone(b).name))
	for z in open:
		_add_entry(String(_zone(z).name).to_upper(), {"kind": "zone", "id": z}, false)


## A realm map's name in the list ("AVALON (SHROUDED ISLES)"), else the map's own.
func _realm_map_name(rid: int, fallback: String) -> String:
	for realm in REALM_MAPS:
		for rm in REALM_MAPS[realm]:
			if int(rm[0]) == rid:
				return String(rm[1])
	return fallback


func _add_zone(z: int) -> void:
	_add_entry(String(_zone(z).name).to_upper(), {"kind": "zone", "id": z}, false)
	var areas: Array = _zone(z).get("areas", [])
	for i in areas.size():
		var nm := String(areas[i].get("name", ""))
		_add_entry("   - " + (nm if nm != "" else "AREA %d" % (i + 1)).to_upper(), {"kind": "area", "id": z, "area": i}, false)


func _add_entry(text: String, what: Dictionary, header: bool) -> void:
	_pick.add_item(text)
	if header:
		_pick.set_item_tooltip(_pick.item_count - 1, "Realm map")
	_entries.append(what)


var _zone_npcs := {}                       # zone -> [[name, x, z], ...] its realm NPCs (from its npcs.json)
var _quest_cache := {}                     # zone -> [{at, kind}] (rebuilt when your quests / level change)
var _quest_key := []
var _zone_sizes := {}                      # zone -> its size in metres (its zone.json, read once)


## Quest marks for any zone (owner: see the quests you can take in a zone without being there). The current zone uses
## QuestMarkers' live marks; another zone reads its NPC list: yellow = a quest you can take now (your level / class /
## race / quests done), green = your step ends there, orange = its giver has your quest in progress.
func _zone_size_of(z: int) -> float:
	if z == current_zone:
		return zone_size
	# read once per zone (2026-11-05: the map asks for every mark and bot dot; parsing zone.json each time took a zone
	# map with 200 bots to 2.8 s a refresh)
	if not _zone_sizes.has(z):
		var info := "res://content/zones/zone%03d/zone.json" % z
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(info)) if FileAccess.file_exists(info) else null
		_zone_sizes[z] = float((parsed as Dictionary).get("size_m", 1638.4)) if parsed is Dictionary else 1638.4
	return float(_zone_sizes[z])


func zone_quest_marks(z: int) -> Array:
	if z == current_zone:
		return marks
	var key := [PlayerData.level, PlayerData.class_id()]   # a level / class change opens other quests
	if key != _quest_key:
		_quest_key = key
		_quest_cache.clear()
	if Server.active:                                   # (the server's own markers; the NPC lists are offline-only)
		return Server.quest_map_marks_in(z)
	if _quest_cache.has(z):
		return _quest_cache[z]
	var out := []
	var eng = get_tree().current_scene.get("quests") if get_tree() and get_tree().current_scene else null
	if eng == null:
		return out
	if not _zone_npcs.has(z):
		var list := []
		var path := "res://content/zones/zone%03d/npcs.json" % z
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(path)) if FileAccess.file_exists(path) else null
		if parsed is Dictionary:
			for sp in parsed.get("spawns", []):
				if int(sp.get("realm", 0)) != 0:
					list.append([String(sp.name), float(sp.pos[0]), float(sp.pos[2])])
		_zone_npcs[z] = list
	var targets := {}
	var givers := {}
	for id in PlayerData.quests:
		var st: Dictionary = eng.step_of(id)
		if String(st.get("target", "")) != "":
			targets[String(st.target).to_lower()] = true
		if String(st.get("custom", {}).get("target", "")) != "":
			targets[String(st.custom.target).to_lower()] = true
		givers[String(QuestDb.quest(id).get("giver", "")).to_lower()] = true
	var seen := {}
	for n in _zone_npcs[z]:
		var who := String(n[0])
		if seen.has(who):
			continue
		var kind := ""
		if targets.has(who.to_lower()):
			kind = "?"
		else:
			for q in QuestDb.by_giver(who):
				if eng.qualifies(q):
					kind = "!"
					break
			if kind == "" and givers.has(who.to_lower()):
				kind = "o"
		if kind != "":
			seen[who] = true
			out.append({"at": Vector2(n[1], n[2]), "kind": kind})
	_quest_cache[z] = out
	return out


func _on_quests_changed() -> void:
	_quest_cache.clear()


## Map space -> the region map square of zone z ([left, top, width, height] in the 256-px picture), or empty.
func region_square(r: int, z: int) -> Rect2:
	for p in _region(r).get("zones", []):
		if int(p.zone) == z:
			return Rect2(float(p.left), float(p.top), float(p.width), float(p.height))
	return Rect2()


class MapView extends Control:
	var window: MapWindow
	var texture: Texture2D
	var _hover := -1

	func _init() -> void:
		mouse_filter = Control.MOUSE_FILTER_STOP

	## The fraction (0..1 across the picture) of a spot given in the current zone's units, or (-1, -1) if not shown.
	func _frac(u: Vector2) -> Vector2:
		var w := window
		var s := w.shown
		var zunits := w.zone_size / 0.025
		match String(s.get("kind", "")):
			"zone":
				if int(s.id) != w.current_zone:
					return Vector2(-1, -1)
				var mr := w.map_rect(int(s.id))
				return (u - mr.position) / mr.size
			"area":
				if int(s.id) != w.current_zone:
					return Vector2(-1, -1)
				var r: Array = (w._zone(int(s.id)).areas as Array)[int(s.area)].rect
				var off := w.map_rect(int(s.id)).position
				return (u - off - Vector2(float(r[0]), float(r[1]))) / Vector2(float(r[2]), float(r[3]))
			"region":
				var sq := w.region_square(int(s.id), w.current_zone)
				if sq.size == Vector2.ZERO:
					return Vector2(-1, -1)
				return (sq.position + u / zunits * sq.size) / 256.0
		return Vector2(-1, -1)

	## A spot (in the SHOWN zone's units) on the shown zone / area map.
	func _mark_frac(u: Vector2) -> Vector2:
		var w := window
		var s := w.shown
		var mr := w.map_rect(int(s.id))
		if String(s.get("kind", "")) == "area":
			var r: Array = (w._zone(int(s.id)).areas as Array)[int(s.area)].rect
			return (u - mr.position - Vector2(float(r[0]), float(r[1]))) / Vector2(float(r[2]), float(r[3]))
		return (u - mr.position) / mr.size

	## The map's quest marks as small gold-rimmed medallions in the interface's own gold and stone (owner 2026-11-05:
	## "clarify the markers ... make them premium and fit the game's theme"; the old client could only borrow the red
	## relic marker): a dark rim, a gold ring, the face in its colour with a soft highlight, and its sign - a gold "!" a
	## quest you can take, a steel-silver "?" its turn-in, crimson with a gold heart an objective, bronze a quest of that NPC
	## you are on, violet your /task. No green: the realm's bots are the green dots.
	## kind -> [face colour, sign ("" none), the legend's words, sign colour]
	const MARK_STYLE := {
		"!": [Color(0.96, 0.76, 0.22), "!", "QUEST", Color(0.22, 0.12, 0.02)],
		"?": [Color(0.74, 0.77, 0.82), "?", "TURN IN", Color(0.12, 0.1, 0.08)],
		"step": [Color(0.72, 0.1, 0.09), "", "OBJECTIVE", Color(1.0, 0.82, 0.35)],
		"o": [Color(0.72, 0.44, 0.2), "", "IN PROGRESS", Color(0.3, 0.16, 0.04)],
		"task": [Color(0.55, 0.3, 0.8), "", "TASK", Color(0.98, 0.9, 1.0)],
	}
	const MARK_R := 2.8                         # (owner 2026-11-05: half the first size)
	var _drawn_marks: Array = []                # [map spot, kind] of the marks drawn now (their hover text)

	static func draw_mark(ci: CanvasItem, at: Vector2, kind: String, r := MARK_R) -> void:
		var st: Array = MARK_STYLE.get(kind, MARK_STYLE["step"])
		ci.draw_circle(at + Vector2(r * 0.11, r * 0.16), r * 1.29, Color(0, 0, 0, 0.45))   # its shadow on the map
		ci.draw_circle(at, r * 1.27, Color(0.1, 0.06, 0.02))                          # the dark rim
		ci.draw_circle(at, r * 1.15, Color(0.86, 0.67, 0.3))                          # the gold ring
		ci.draw_circle(at, r, Color(0.36, 0.25, 0.1))                                 # (its inner edge)
		ci.draw_circle(at, r * 0.89, st[0])                                           # the face
		ci.draw_circle(at + Vector2(-r * 0.28, -r * 0.32), r * 0.38, Color(1, 1, 1, 0.22))   # a soft highlight
		var sign: String = st[1]
		if sign != "":
			var f := UiArt.font(true)
			var fs := int(round(r * 2.3))
			var tw := f.get_string_size(sign, HORIZONTAL_ALIGNMENT_LEFT, -1, fs).x
			var base := at + Vector2(-tw * 0.5, r * 0.78)
			ci.draw_string_outline(f, base, sign, HORIZONTAL_ALIGNMENT_LEFT, -1, fs, 1, Color(1, 1, 1, 0.35))
			ci.draw_string(f, base, sign, HORIZONTAL_ALIGNMENT_LEFT, -1, fs, st[3])
		else:
			ci.draw_circle(at, r * 0.36, st[3])                                       # a heart in the sign's colour

	## Your realm's bots on the map shown, as fractions (0..1) across it: any zone of the list wherever you are, and on
	## the realm maps every bot on its zone's block (owner 2026-11-05). Worked out at most every 0.3 s: the map redraws
	## every frame, a realm can hold thousands of bots, and the server moves each at most every 4 s (your zone) or 12 s.
	var _bots_for := {}
	var _bots_at := -1000000
	var _bots := PackedVector2Array()

	func _bot_fracs() -> PackedVector2Array:
		var w := window
		var now := Time.get_ticks_msec()
		if w.shown == _bots_for and now - _bots_at < 300:
			return _bots
		_bots_for = w.shown.duplicate()
		_bots_at = now
		_bots = PackedVector2Array()
		var id := int(w.shown.get("id", -1))
		if String(w.shown.get("kind", "")) == "region":
			var blocks := []                                 # [zone box (region units), its square on the picture]
			for q in w._region(id).get("zones", []):
				var box: Array = Server.zone_box(int(q.zone))
				if not box.is_empty():
					blocks.append([box, Rect2(float(q.left), float(q.top), float(q.width), float(q.height))])
			for v in WorldLink.bot_markers():
				for b in blocks:
					var box: Array = b[0]
					if Server.in_box(box, v):
						var sq: Rect2 = b[1]
						var rel := Vector2((float(v.y) - float(box[1])) / (float(box[3]) - float(box[1])),
								(float(v.z) - float(box[2])) / (float(box[4]) - float(box[2])))
						_bots.append((sq.position + rel * sq.size) / 256.0)
						break
			return _bots
		var pts: PackedVector3Array = WorldLink.bot_dots() if id == w.current_zone else WorldLink.bot_dots_in(id)
		for b in pts:
			_bots.append(_mark_frac(Vector2(b.x, b.z) / 0.025))
		return _bots

	## The mark under the pointer, its words (the hover text), else "".
	func mark_words_at(p: Vector2) -> String:
		for d in _drawn_marks:
			if (d[0] as Vector2).distance_to(p) <= MARK_R + 2.0:     # (a little more than the mark: easy to hover)
				return {"!": "QUEST AVAILABLE", "?": "TURN IN HERE", "step": "QUEST OBJECTIVE", "o": "A QUEST YOU ARE ON",
						"task": "YOUR TASK"}.get(String(d[1]), "")
		return ""

	func _gui_input(event: InputEvent) -> void:
		var w := window
		if String(w.shown.get("kind", "")) != "region":
			if event is InputEventMouseMotion:                   # a mark's meaning on hover
				var words := mark_words_at(event.position)
				if words != tooltip_text:
					tooltip_text = words
			return
		if event is InputEventMouseMotion:
			var z := _zone_under(event.position)
			if z != _hover:
				_hover = z
				tooltip_text = String(w._zone(z).get("name", "")) if z >= 0 else ""
				queue_redraw()
		elif event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
			var z := _zone_under(event.position)
			if z >= 0 and String(w._zone(z).get("map", "")) != "":
				w._show({"kind": "zone", "id": z}, true)
				accept_event()

	func _zone_under(p: Vector2) -> int:
		var at := p / size * 256.0
		for q in window._region(int(window.shown.id)).get("zones", []):
			if Rect2(float(q.left), float(q.top), float(q.width), float(q.height)).has_point(at):
				return int(q.zone)
		return -1

	## The roadside inns (bot plan phase 6, owner: clearly labelled on their zone's map without covering what is
	## there): a gold marker, and the label on the quietest part of the painted map nearby (pipeline/place_inns.py
	## picked the spot), joined by a thin line.
	const SHOW_INNS := false
	static var _inns := {}

	func _draw_inns(zone_id: int) -> void:
		if _inns.is_empty():
			var parsed = JSON.parse_string(FileAccess.get_file_as_string("res://content/companions/inns.json"))
			_inns = (parsed as Dictionary).get("inns", {"-": {}}) if parsed is Dictionary else {"-": {}}
		for k in _inns:
			var inn: Dictionary = _inns[k]
			if inn.is_empty() or int(inn.get("zone", -1)) != zone_id:
				continue
			var lab: Dictionary = inn.get("map_label", {})
			var f := Vector2(float(inn.site[0]), float(inn.site[1])) / float(window._zone_size_of(zone_id))
			var at := f * size
			var off: Array = lab.get("offset", [0.02, -0.035])
			# owner 2026-10-11: like the map's own markers - small white text with a dark edge, no box, a small dot
			var font := UiArt.font(true)
			var fs := maxi(6, int(size.x / 44.0))
			var text := String(lab.get("text", "ROADSIDE INN"))
			var tsize := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, fs)
			# beside the dot, on the side the quiet-spot pick chose (left / right, or above / below)
			var base := at + Vector2(5, tsize.y * 0.35)
			if float(off[0]) < -0.1:
				base.x = at.x - 5 - tsize.x
			elif float(off[0]) < 0.0:
				base = at + Vector2(-tsize.x * 0.5, (-6.0 if float(off[1]) < 0 else tsize.y + 4.0))
			draw_string_outline(font, base, text, HORIZONTAL_ALIGNMENT_LEFT, -1, fs, 2, Color(0, 0, 0, 0.85))
			draw_string(font, base, text, HORIZONTAL_ALIGNMENT_LEFT, -1, fs, Color(1, 1, 1))
			draw_circle(at, 2.6, Color(0, 0, 0, 0.85))
			draw_circle(at, 1.8, Color(1, 1, 1))

	## True 1.65 Mode's traveler's inns (owner 2026-10-31: "map icons for the new travelers inn areas for avalon marsh,
	## shannon estuary and west svealand so people can find them easily"): a gold dot at the innkeeper with
	## "TRAVELER'S INN" on the zone / area map (not on the realm maps - owner). From each zone's
	## inns_165.json "map" (pipeline/build_inns_165.py).
	const TRAVELER_INN_ZONES := [9, 102, 202]
	static var _traveler_inns := {}                # zone -> [x, y] zone units, "label"

	func _traveler_inn(z: int) -> Dictionary:
		if not (z in TRAVELER_INN_ZONES) or Server.world_mode != "1.65":
			return {}
		if not _traveler_inns.has(z):
			var path := "res://content/zones/zone%03d/inns_165.json" % z
			var parsed = JSON.parse_string(FileAccess.get_file_as_string(path)) if FileAccess.file_exists(path) else null
			_traveler_inns[z] = (parsed as Dictionary).get("map", {}) if parsed is Dictionary else {}
		return _traveler_inns[z]

	func _inn_dot(at: Vector2, r: float) -> void:
		draw_circle(at, r + 1.2, Color(0.08, 0.05, 0.0))
		draw_circle(at, r, Color(1.0, 0.8, 0.25))
		draw_circle(at, r * 0.4, Color(1.0, 0.97, 0.8))

	func _draw_traveler_inns() -> void:
		var w := window
		var kind := String(w.shown.get("kind", ""))
		if kind == "region":                                          # (none on the realm maps: owner 2026-10-31)
			return
		var inn := _traveler_inn(int(w.shown.get("id", -1)))
		if inn.is_empty():
			return
		var fr := _mark_frac(Vector2(float(inn.units[0]), float(inn.units[1])))
		var at := fr * size
		if fr.x < 0 or not Rect2(Vector2.ZERO, size).has_point(at):
			return
		_inn_dot(at, 4.0)
		var font := UiArt.font(true)
		var fs := maxi(7, int(size.x / 40.0))
		var text := String(inn.get("label", "TRAVELER'S INN"))
		var tsize := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, fs)
		var base := at + Vector2(-tsize.x * 0.5, -8.0)               # centred above the dot (or below it)
		if String(inn.get("side", "")) == "below":
			base.y = at.y + tsize.y + 4.0
		base.x = clampf(base.x, 2.0, size.x - tsize.x - 2.0)
		draw_string_outline(font, base, text, HORIZONTAL_ALIGNMENT_LEFT, -1, fs, 3, Color(0, 0, 0, 0.9))
		draw_string(font, base, text, HORIZONTAL_ALIGNMENT_LEFT, -1, fs, Color(1.0, 0.85, 0.35))

	func _draw() -> void:
		draw_rect(Rect2(Vector2.ZERO, size), Color(0, 0, 0))
		if texture:
			draw_texture_rect(texture, Rect2(Vector2.ZERO, size), false)
		var w := window
		if String(w.shown.get("kind", "")) == "region" and _hover >= 0:
			var sq := w.region_square(int(w.shown.id), _hover)
			draw_rect(Rect2(sq.position / 256.0 * size, sq.size / 256.0 * size), Color(1.0, 0.85, 0.3), false, 1.5)
		draw_rect(Rect2(Vector2.ZERO, size), Color(0.45, 0.42, 0.35), false, 1.0)
		_drawn_marks.clear()
		# (the realm map shows no quest marks - owner 2026-11-05: "remove quest markers from the realm map"; click a
		# zone to see its quests and objectives, wherever you are)
		if String(w.shown.get("kind", "")) != "region":
			for m in (w.zone_quest_marks(int(w.shown.id)) if MapWindow.show_quest_marks else []):   # quest marks for the shown zone, wherever you are
				var f := _mark_frac((m.at as Vector2) / 0.025)
				var mp := f * size
				if f.x < 0 or not Rect2(Vector2.ZERO, size).has_point(mp):
					continue
				draw_mark(self, mp, String(m.kind))
				_drawn_marks.append([mp, String(m.kind)])
			# your realm's gamebots as green dots on any zone's map, wherever you are (bot plan phase 3, owner: also in
			# dungeons; enemy bots never; owner 2026-11-05: every zone of the list, not only yours; Options > BOTS >
			# REALM BOTS ON MAP)
			for bf in _bot_fracs():
				var bp: Vector2 = bf * size
				if bp.x < 0 or not Rect2(Vector2.ZERO, size).has_point(bp):
					continue
				draw_circle(bp, 2.8, Color(0.02, 0.08, 0.02))
				draw_circle(bp, 2.0, Color(0.3, 0.95, 0.3))
			if int(w.shown.get("id", -1)) == w.current_zone:
				# your companions in blue - their own colour (owner 2026-10-11: the orange looked like an in-progress quest
				# mark; blue is used for nothing else on the map)
				for c in WorldLink.companion_dots():
					var cf := _mark_frac(Vector2(c.x, c.z) / 0.025)
					var cpt := cf * size
					if cf.x < 0 or not Rect2(Vector2.ZERO, size).has_point(cpt):
						continue
					draw_circle(cpt, 3.0, Color(0.0, 0.03, 0.1))
					draw_circle(cpt, 2.2, Color8(70, 150, 255))
		else:
			# the realm maps: every bot of your realm as a small dot on its zone's block (owner 2026-11-05: "little green
			# dots all over the game world on that large realm map"); no quest marks here (owner 2026-11-05). Squares,
			# not circles: thousands of them a frame.
			var d := clampf(size.x / 200.0, 1.0, 2.0)
			for bf in _bot_fracs():
				var bp: Vector2 = bf * size
				draw_rect(Rect2(bp - Vector2(d, d), Vector2(d, d) * 2.0), Color(0.02, 0.08, 0.02))
				draw_rect(Rect2(bp - Vector2(d, d) * 0.55, Vector2(d, d) * 1.1), Color(0.3, 0.95, 0.3))
		# the roadside inns are shelved (owner 2026-10-11: 0.36c is the Claude version's world 1:1 - no inns on Avalon
		# Marsh / Myrkwood / Shannon Estuary); the drawing stays for if they come back ("INNS AND CUSTOM SYSTEMS (SHELVED).md")
		if SHOW_INNS and String(w.shown.get("kind", "")) == "zone":
			_draw_inns(int(w.shown.get("id", -1)))
		_draw_traveler_inns()
		var p := w.player as Player
		if p == null:
			return
		var u := w._player_units()
		var f := _frac(Vector2(u.x, u.y))
		var at := f * size
		if f.x < 0 or not Rect2(Vector2.ZERO, size).has_point(at):
			return
		var fwd := Vector2(-sin(p.char_yaw), -cos(p.char_yaw))   # north up: forward -Z is up on the map
		var side := Vector2(-fwd.y, fwd.x)
		var tip := at + fwd * 7.0
		var pts := PackedVector2Array([tip, at - fwd * 4.0 + side * 4.5, at - fwd * 2.0, at - fwd * 4.0 - side * 4.5])
		draw_colored_polygon(pts, Color(1.0, 0.85, 0.25))
		draw_polyline(PackedVector2Array([pts[0], pts[1], pts[2], pts[3], pts[0]]), Color(0.1, 0.05, 0.0), 1.0)
