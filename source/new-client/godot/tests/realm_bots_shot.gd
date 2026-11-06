extends Node
## A check for the owner (2026-11-05): your realm's bots on the maps - the realm maps (the realm and its Shrouded Isles)
## at the very top of the map list, every bot as a small dot on them, another zone's bots and quest givers wherever you
## are, and how long the dots take with thousands of bots - offline, with made-up server markers in the server's own
## format (id range -> kind; Vector4 region, x, y, z).
##   --skip-login --zone=N --world-mode=1.65 --probe=realm_bots_shot --out=<dir>

const PER_ZONE := 200                  # bots in each zone of the realm map (10 zones: 2,000)
const PER_SI_ZONE := 50


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(6.0).timeout
	var m: MapWindow = v.ui.map_window
	m.show_window()
	var realm := PlayerData.realm_id()
	var maps: Array = MapWindow.REALM_MAPS.get(realm, [])
	var top := []
	for i in mini(5, m._pick.item_count):
		top.append(m._pick.get_item_text(i))
	print("[map] realm %d, the list starts: %s" % [realm, top])
	var list_ok := maps.size() == 2 and m._entries.size() > 3
	if list_ok:
		list_ok = m._entries[0] == {"kind": "region", "id": int(maps[0][0])} \
				and m._entries[1] == {"kind": "region", "id": int(maps[1][0])} \
				and String((m._entries[2] as Dictionary).get("kind", "")) == "zone"
	var idx = JSON.parse_string(FileAccess.get_file_as_string("res://content/zones/zones_index.json"))
	if Server.zone_info(0).is_empty() and Server.get("_index") is Dictionary:
		for k in idx:
			Server._index[k] = idx[k]
	var was_active := Server.active
	Server.active = true
	Server.map_markers.clear()
	var rng := RandomNumberGenerator.new()
	rng.seed = 165
	var n := 0
	var per_map := {}
	var other := -1                                          # a zone of the realm map you are not in
	for j in maps.size():
		var rid := int(maps[j][0])
		var count := 0
		for q in m._region(rid).get("zones", []):
			var zi: Dictionary = Server.zone_info(int(q.zone))
			if zi.is_empty():
				continue
			if j == 0 and other < 0 and int(q.zone) != m.current_zone:
				other = int(q.zone)
			for k in (PER_ZONE if j == 0 else PER_SI_ZONE):
				var gx := (float(zi.x) + rng.randf_range(0.02, 0.98) * float(zi.w)) * 8192.0
				var gy := (float(zi.y) + rng.randf_range(0.02, 0.98) * float(zi.h)) * 8192.0
				Server.map_markers[0x80000000 + n] = Vector4(float(zi.region), gx, gy, 0.0)
				n += 1
				count += 1
		per_map[rid] = count
	var oz: Dictionary = Server.zone_info(other)
	for k in 3:                                              # three quest givers in that zone
		Server.map_markers[Server.GIVER_MARKERS + 1 + k] = Vector4(float(oz.region),
				(float(oz.x) + 0.3 + 0.2 * k) * 8192.0 + 2000.0, (float(oz.y) + 4.0) * 8192.0, 0.0)
	print("[map] %d made-up bot markers, %d on %s, %d on %s" % [n, int(per_map.get(int(maps[0][0]), 0)), maps[0][1],
			int(per_map.get(int(maps[1][0]), 0)), maps[1][1]])

	# the timings: a fresh count of each map's dots (the map redraws every frame, the dots at most every 0.3 s)
	var t0 := Time.get_ticks_usec()
	var here := Server.bot_dots()
	var t_here := Time.get_ticks_usec() - t0
	t0 = Time.get_ticks_usec()
	var quests_here := Server.quest_map_marks()
	var t_quests := Time.get_ticks_usec() - t0

	var out := String(v.args.get("out", "user://"))
	m._show({"kind": "region", "id": int(maps[0][0])}, true)
	t0 = Time.get_ticks_usec()
	var realm_dots: int = m._view._bot_fracs().size()
	var t_realm := Time.get_ticks_usec() - t0
	await get_tree().create_timer(1.0).timeout
	var fps_realm := Engine.get_frames_per_second()
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(out + "/realm_map_bots.png")

	m._show({"kind": "region", "id": int(maps[1][0])}, true)
	var si_dots: int = m._view._bot_fracs().size()
	await get_tree().create_timer(0.6).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(out + "/si_map_bots.png")

	m._show({"kind": "zone", "id": other}, true)
	t0 = Time.get_ticks_usec()
	var zone_dots: int = m._view._bot_fracs().size()
	var t_zone := Time.get_ticks_usec() - t0
	var givers: int = m.zone_quest_marks(other).filter(func(x): return String(x.kind) == "!").size()
	await get_tree().create_timer(1.0).timeout
	var fps_zone := Engine.get_frames_per_second()
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(out + "/zone_map_bots.png")

	print("[map] realm map %d dots (want %d), Shrouded Isles map %d (want %d), zone %d (%s) %d dots (want %d), %d quest givers (want 3)"
			% [realm_dots, int(per_map.get(int(maps[0][0]), 0)), si_dots, int(per_map.get(int(maps[1][0]), 0)), other,
			String(m._zone(other).get("name", "")), zone_dots, PER_ZONE, givers])
	print("[map] times (ms): realm map dots %.2f, a zone's dots %.2f, your zone's dots %.2f (%d), your zone's quest marks %.2f (%d); fps realm map %d, zone map %d"
			% [t_realm / 1000.0, t_zone / 1000.0, t_here / 1000.0, here.size(), t_quests / 1000.0, quests_here.size(),
			fps_realm, fps_zone])
	var ok := list_ok and realm_dots == int(per_map.get(int(maps[0][0]), 0)) \
			and si_dots == int(per_map.get(int(maps[1][0]), 0)) and zone_dots == PER_ZONE and givers == 3
	print("[map] RESULT ", "PASS" if ok else "FAIL", " (list ", "ok" if list_ok else "WRONG", ")")
	Server.active = was_active
	Server.map_markers.clear()
	get_tree().quit()
