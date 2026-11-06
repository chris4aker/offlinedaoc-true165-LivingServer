extends Node
## Debug (owner 2026-11-06, battleground keeps: a player saw floating lords / guards and door hitboxes inside the
## keeps): the battleground's central keep against the server's own data, which follows the original 1.65 client -
##   doors: every server door row (door table: id, place) against the client's door leaves with the same id - found,
##          and how far its doorway is from the server's door spot
##   floors: every server monster near the keep - the floor the player's own floor pick finds under it, and the gap
## and shots of the keep and of its chieftain.
##   --skip-login --zone=250 --world-mode=1.65 --probe=bg_keep_probe --data=<bg_keep_data.json> --out=<dir> [--tag=x]
## (bg_keep_data.json: {zone: {doors: [[id, x, y, z]], mobs: [[name, x, y, z]]}}, zone-local units, from the server DB)
var v: Node


func _shot(cam: Camera3D, name: String) -> void:
	cam.current = true
	await get_tree().create_timer(0.8).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("%s/%s.png" % [String(v.args.get("out", "user://")), name])


func _m(x: float, y: float, z: float) -> Vector3:
	return Vector3(x * 0.025, z * 0.025, y * 0.025)


func _ready() -> void:
	v = get_tree().current_scene
	var zone_id := int(v.args.get("zone", "0"))
	var tag := String(v.args.get("tag", ""))
	var all = JSON.parse_string(FileAccess.get_file_as_string(String(v.args.get("data", ""))))
	var data: Dictionary = all.get(str(zone_id), {})
	await get_tree().create_timer(8.0).timeout
	var keep := _m(25088, 30080, 0)
	v.player.global_position = Vector3(keep.x, 120.0, keep.z + 40.0)
	await get_tree().create_timer(4.0).timeout
	var leaves := {}
	for d in get_tree().root.find_children("*", "ZoneDoor", true, false):
		var id: int = (d as ZoneDoor).door_id
		if id >= 0:
			if not leaves.has(id):
				leaves[id] = []
			leaves[id].append(d)
	var near := 0
	for row in data.get("doors", []):
		var id := int(row[0])
		var at := _m(float(row[1]), float(row[2]), float(row[3]))
		if not leaves.has(id):
			var near_id := -1
			var near_d := INF
			for d in get_tree().root.find_children("*", "ZoneDoor", true, false):
				var w: Vector3 = (d as ZoneDoor).doorway()
				if w == Vector3.INF:
					w = (d as Node3D).global_position
				var dd := Vector2(w.x - at.x, w.z - at.z).length()
				if dd < near_d:
					near_d = dd
					near_id = (d as ZoneDoor).door_id
			print("[keep] %d door %d: NO client door with this id (server spot %s) - nearest client door %d, %.1f m" % [
					zone_id, id, at, near_id, near_d])
			continue
		var best := INF
		var mid := Vector3.ZERO
		for d in leaves[id]:
			var w: Vector3 = (d as ZoneDoor).doorway()
			if w == Vector3.INF:
				w = (d as Node3D).global_position
			mid += w / float(leaves[id].size())
			best = minf(best, Vector2(w.x - at.x, w.z - at.z).length())
		if best > 4.0:
			print("[keep] %d door %d: the client's doorway is at zone units (%d, %d, %d)" % [zone_id, id, mid.x * 40.0,
					mid.z * 40.0, mid.y * 40.0])
		near += 1 if best <= 4.0 else 0
		print("[keep] %d door %d: client door %.1f m from the server's spot" % [zone_id, id, best])
	print("[keep] %d %s doors: %d of %d server doors have a client door within 4 m" % [zone_id, tag, near,
			data.get("doors", []).size()])
	var ok := 0
	var worst := 0.0
	var chief := Vector3.INF
	for row in data.get("mobs", []):
		var at := _m(float(row[1]), float(row[2]), float(row[3]))
		if String(row[0]).contains("Chief"):
			chief = at
		var floor_y: float = v.player._floor_below(at, at.y)
		var gap := at.y - floor_y
		worst = maxf(worst, absf(gap))
		if absf(gap) <= 1.0:
			ok += 1
		else:
			print("[keep] %d mob %s at %s: floor %.1f m %s it" % [zone_id, row[0], at, absf(gap),
					"below" if gap > 0 else "above"])
			# every face straight down through its spot (floors and walls, any slope) and the nearest floor face
			var space: PhysicsDirectSpaceState3D = (v.player as Node3D).get_world_3d().direct_space_state
			var from_y := at.y + 20.0
			for _i in 12:
				var q := PhysicsRayQueryParameters3D.create(Vector3(at.x, from_y, at.z), Vector3(at.x, at.y - 40.0, at.z),
						ZoneLoader.LAYER_FLOOR | ZoneLoader.LAYER_WALL)
				var hit := space.intersect_ray(q)
				if hit.is_empty():
					break
				print("[keep]     face at y %.2f normal %s" % [hit.position.y, hit.normal])
				from_y = float(hit.position.y) - 0.01
			for r in [1.0, 2.0, 4.0, 8.0]:
				for k in 8:
					var a := TAU * k / 8.0
					var p := at + Vector3(cos(a) * r, 0, sin(a) * r)
					var fy: float = v.player._floor_below(p, p.y)
					if absf(at.y - fy) <= 1.0:
						print("[keep]     a floor at its height %.1f m away (%s)" % [r, p])
						break
	print("[keep] %d %s floors: %d of %d monsters stand on a floor (within 1 m), worst gap %.1f m" % [zone_id, tag, ok,
			data.get("mobs", []).size(), worst])
	v.player.visible = false
	var cam := Camera3D.new()
	v.add_child(cam)
	cam.fov = 70
	cam.global_position = keep + Vector3(-45, 135, 55)
	cam.look_at(keep + Vector3(0, 95, 0))
	await _shot(cam, "keep%d%s_overview" % [zone_id, tag])
	if chief != Vector3.INF:
		cam.global_position = chief + Vector3(6, 4, 6)
		cam.look_at(chief + Vector3(0, 1, 0))
		await _shot(cam, "keep%d%s_chief" % [zone_id, tag])
	get_tree().quit()
