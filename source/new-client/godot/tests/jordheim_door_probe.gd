extends Node
## Debug (owner 2026-11-06, a player's "door broken in Jordheim loc; 21506,27835,8015" and "... 23549,24359,8272"): the
## nearest door leaf to each spot, shot closed and open from in front of it, its pose and whether its collision stops
## a walker in the doorway (closed) and lets one through (open).
##   --skip-login --zone=120 --world-mode=1.65 --probe=jordheim_door_probe --out=<dir>
var v: Node


func _shot(cam: Camera3D, name: String) -> void:
	cam.current = true
	await get_tree().create_timer(0.6).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("%s/%s.png" % [String(v.args.get("out", "user://")), name])


func _blocked(from: Vector3, to: Vector3) -> bool:
	var q := PhysicsRayQueryParameters3D.create(from, to, ZoneLoader.LAYER_WALL)
	return not (v.player as Node3D).get_world_3d().direct_space_state.intersect_ray(q).is_empty()


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(8.0).timeout
	v.player.visible = false
	var cam := Camera3D.new()
	v.add_child(cam)
	cam.fov = 70
	var n := 0
	for loc in [Vector3(21506, 27835, 8015), Vector3(23549, 24359, 8272)]:
		n += 1
		var at := Vector3(loc.x * 0.025, loc.z * 0.025, loc.y * 0.025)
		v.player.global_position = at + Vector3(0, 0.5, 0)
		await get_tree().create_timer(3.0).timeout
		var best: ZoneDoor = null
		var bd := 1e9
		for d in get_tree().root.find_children("*", "ZoneDoor", true, false):
			var dist := (d as Node3D).global_position.distance_to(at)
			if dist < bd:
				bd = dist
				best = d
		if best == null:
			print("[door] %d: no door" % n)
			continue
		var mid := best.doorway()
		for d2 in get_tree().root.find_children("*", "ZoneDoor", true, false):
			if (d2 as Node3D).global_position.distance_to(at) < 12.0:
				print("[door] %d near: id %d partner %s pos %s parent %s" % [n, d2.door_id, d2.partner != null,
						d2.global_position, d2.get_parent().name])
		print("[door] %d: nearest leaf %.1f m away at %s, doorway %s, basis x %s z %s" % [n, bd, best.global_position,
				mid, best.global_transform.basis.x, best.global_transform.basis.z])
		# look at the doorway from the side the loc is on, 4 m back, 1.6 m up
		var side := (at - mid)
		side.y = 0
		side = side.normalized() if side.length() > 0.2 else Vector3(0, 0, 1)
		cam.global_position = mid + side * 4.0 + Vector3(0, 1.6, 0)
		cam.look_at(mid + Vector3(0, 1.0, 0))
		var through_a := mid + side * 1.5 + Vector3(0, 1.0, 0)
		var through_b := mid - side * 1.5 + Vector3(0, 1.0, 0)
		print("[door] %d closed: walk through blocked %s" % [n, _blocked(through_a, through_b)])
		await _shot(cam, "door%d_closed" % n)
		best.open_door()
		await get_tree().create_timer(1.5).timeout
		print("[door] %d open: walk through blocked %s, leaf basis x %s" % [n, _blocked(through_a, through_b), best.global_transform.basis.x])
		await _shot(cam, "door%d_open" % n)
		cam.global_position = mid + Vector3(0, 7, 0) + side * 2.0
		cam.look_at(mid)
		await _shot(cam, "door%d_top" % n)
	get_tree().quit()
