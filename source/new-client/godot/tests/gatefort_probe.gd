extends Node
## Debug (owner 2026-11-06: Emain Macha's gate fort big gates were not doors - pipeline/fix_gatefort_164.py takes the
## gate fort from the 1.64 client): each door id given, its leaves - found, the straight line through the doorway
## blocked while shut and clear once open - and shots of it shut and open.
##   --skip-login --zone=214 --world-mode=1.65 --probe=gatefort_probe --doors=214000101,214000201,214000103 --out=<dir>
var v: Node


func _shot(cam: Camera3D, name: String) -> void:
	cam.current = true
	await get_tree().create_timer(0.8).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("%s/%s.png" % [String(v.args.get("out", "user://")), name])


func _blocked(a: Vector3, b: Vector3) -> bool:
	var q := PhysicsRayQueryParameters3D.create(a, b, ZoneLoader.LAYER_WALL)
	return not (v.player as Node3D).get_world_3d().direct_space_state.intersect_ray(q).is_empty()


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(8.0).timeout
	var ids: Array = Array(String(v.args.get("doors", "")).split(",")).map(func(s): return int(s))
	var by_id := {}
	for d in get_tree().root.find_children("*", "ZoneDoor", true, false):
		if (d as ZoneDoor).door_id in ids:
			if not by_id.has((d as ZoneDoor).door_id):
				by_id[(d as ZoneDoor).door_id] = []
			by_id[(d as ZoneDoor).door_id].append(d)
	var ok := true
	var cam := Camera3D.new()
	v.add_child(cam)
	cam.fov = 70
	for id in ids:
		if not by_id.has(id):
			print("[gate] %d: NO door leaves" % id)
			ok = false
			continue
		var leaves: Array = by_id[id]
		var mid := Vector3.ZERO
		for d in leaves:
			mid += (d as ZoneDoor).doorway()
		mid /= leaves.size()
		# across the doorway: square to the line between two leaves (a double gate), else to the leaf's widest side
		var along := Vector3.RIGHT
		if leaves.size() >= 2:
			along = (leaves[1] as ZoneDoor).doorway() - (leaves[0] as ZoneDoor).doorway()
		else:
			along = (leaves[0] as Node3D).global_transform.basis.x
		along.y = 0
		along = along.normalized()
		var through := along.cross(Vector3.UP).normalized()
		v.player.global_position = mid + through * 6.0 + Vector3(0, 1, 0)
		await get_tree().create_timer(2.0).timeout
		var a := mid + through * 3.0 + Vector3(0, 1.2, 0)
		var b := mid - through * 3.0 + Vector3(0, 1.2, 0)
		var shut := _blocked(a, b)
		v.player.visible = false
		cam.global_position = mid + through * 9.0 + Vector3(0, 3.0, 0)
		cam.look_at(mid + Vector3(0, 2.0, 0))
		await _shot(cam, "gate%d_shut" % id)
		(leaves[0] as ZoneDoor).open_door()
		await get_tree().create_timer(maxf(1.5, (leaves[0] as ZoneDoor).seconds + 0.5)).timeout
		var open := _blocked(a, b)
		await _shot(cam, "gate%d_open" % id)
		(leaves[0] as ZoneDoor).close_door()
		v.player.visible = true
		var good := shut and not open
		ok = ok and good
		print("[gate] %d: %d leaves at %s - shut blocks %s, open clear %s  %s" % [id, leaves.size(), mid, shut, not open,
				"OK" if good else "WRONG"])
	print("[gate] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
