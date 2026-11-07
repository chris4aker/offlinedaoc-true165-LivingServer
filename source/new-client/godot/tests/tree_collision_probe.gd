extends Node
## Offline check (owner 2026-11-07: "why do none of the trees have collision in avalon marsh" - walked into the willows'
## trunks): the zone's trees (fixtures the client doesn't collide with but gives a Radius - ZoneLoader.proxy_faces) stop
## you at their trunk. A wall-layer ray from 4 m beside each of the first 40 such trees, aimed at the trunk 1 m up,
## must hit within 4 m.
##   --skip-login --zone=N --world-mode=1.65 --probe=tree_collision_probe


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(14.0).timeout
	var zl: ZoneLoader = v.get("zone")
	if zl == null:
		print("[trees] no zone")
		get_tree().quit()
		return
	var space := (zl as Node3D).get_world_3d().direct_space_state
	var tried := 0
	var hit := 0
	var names := {}
	for f in zl.info.get("fixtures", []):
		if tried >= 40:
			break
		if int(f.get("radius", 0)) <= 0 or ZoneLoader.picked(f) or f.get("no_collision", false) or f.has("basis"):
			continue
		var at: Vector3 = zl.to_global(Vector3(f.pos[0], f.pos[1], f.pos[2]))
		var to := at + Vector3(0, 1.0, 0)
		var from := to + Vector3(4.0, 0, 0)
		var q := PhysicsRayQueryParameters3D.create(from, to, ZoneLoader.LAYER_WALL)
		var r := space.intersect_ray(q)
		tried += 1
		if not r.is_empty():
			hit += 1
		names[String(f.get("name", ""))] = int(names.get(String(f.get("name", "")), 0)) + (0 if r.is_empty() else 1)
	var ok := tried > 0 and hit == tried
	print("[trees] zone %s: %d trees tried, %d stop you at the trunk  %s  %s" % [String(zl.info.get("name", "?")), tried,
			hit, names, "OK" if ok else "WRONG"])
	print("[trees] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
