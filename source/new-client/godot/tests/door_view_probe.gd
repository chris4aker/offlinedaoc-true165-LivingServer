extends Node
## Debug (server chat 2026-11-06, a player's "Thid BG - the second keep door can't be seen to hit it"):
##   1. a targeted door is "in view" (ServerWorld.target_in_view -> the position packets' TargetInView flag, which the
##      server's melee and siege engines need): Emain Macha's gate fort gate 214000101 from in front of it, from far
##      behind the fort, and with no door known
##   2. a door with no open pose of its own (ZoneDoor.still): open hides it and clears its doorway, shut blocks again
##   --skip-login --zone=214 --world-mode=1.65 --probe=door_view_probe
var v: Node


func _blocked(a: Vector3, b: Vector3) -> bool:
	var q := PhysicsRayQueryParameters3D.create(a, b, ZoneLoader.LAYER_WALL)
	return not (v.player as Node3D).get_world_3d().direct_space_state.intersect_ray(q).is_empty()


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(8.0).timeout
	var ok := true
	var zl: ZoneLoader = v.zone
	var gate: Array = zl.door_by_id.get(214000101, [])
	if gate.is_empty():
		print("[view] no gate 214000101")
		get_tree().quit()
		return
	var mid := ((gate[0] as ZoneDoor).doorway() + (gate[-1] as ZoneDoor).doorway()) / 2.0
	var along := (gate[-1] as ZoneDoor).doorway() - (gate[0] as ZoneDoor).doorway()
	along.y = 0
	var through := along.normalized().cross(Vector3.UP).normalized()
	var sw := ServerWorld.new()
	v.add_child(sw)
	sw.set_process(false)
	sw.set_physics_process(false)
	sw.zone = zl
	sw.door_oids = {7001: 214000101}
	sw.target_door = 7001
	# the gate fort's gates come in pairs down one passage (outer 214000101, inner 214000102): seen from the inner side
	# the shut inner gate stands between - in view once it is open
	var inner: Array = zl.door_by_id.get(214000102, [])
	var sides := {}
	for side in [1.0, -1.0]:
		var front: Vector3 = mid + through * float(side) * 12.0
		front.y = zl.height_at(front.x, front.z) + 1.0
		sides[side] = front
		var seen: bool = sw.target_in_view(front)
		print("[view] gate from 12 m on side %d, inner gate shut: in view %s" % [int(side), seen])
	for d2 in inner:
		(d2 as ZoneDoor).set_server_state(true)
	await get_tree().create_timer(3.0).timeout
	for side in [1.0, -1.0]:
		var seen2: bool = sw.target_in_view(sides[side])
		ok = ok and seen2
		print("[view] gate from 12 m on side %d, inner gate open: in view %s" % [int(side), seen2])
	for d2 in inner:
		(d2 as ZoneDoor).set_server_state(false)
	var far := mid + along.normalized() * 60.0
	far.y = zl.height_at(far.x, far.z) + 1.0
	print("[view] gate from 60 m along the fort wall (%s): in view %s (a wall between should say false)" % [far,
			sw.target_in_view(far)])
	sw.door_oids = {}
	var none := sw.target_in_view(mid + through * 12.0)
	ok = ok and not none
	print("[view] unknown door: in view %s" % none)
	sw.queue_free()

	# a still door: one leaf whose open pose is its closed pose, a 3 m x 3 m panel across a clear spot
	var spot := mid + through * 20.0
	spot.y = zl.height_at(spot.x, spot.z)
	var d := ZoneDoor.new()
	zl.add_child(d)
	var xf := Transform3D(Basis.IDENTITY, zl.to_local(spot))
	d.setup(xf, xf, 1.0)
	var box := BoxMesh.new()
	box.size = Vector3(3, 3, 0.2)
	d.add_panel(box)
	var faces := box.get_faces()
	for i in faces.size():
		faces[i] += Vector3(0, 1.5, 0)
	d.add_collision(faces, ZoneLoader.LAYER_WALL)
	await get_tree().physics_frame
	await get_tree().physics_frame
	var a := spot + Vector3(0, 1.2, 2.0)
	var b := spot + Vector3(0, 1.2, -2.0)
	var shut := _blocked(a, b)
	d.set_server_state(true)
	await get_tree().physics_frame
	await get_tree().physics_frame
	var open_clear := not _blocked(a, b)
	var hidden := not (d.get_child(0) as MeshInstance3D).visible
	d.set_server_state(false)
	await get_tree().physics_frame
	await get_tree().physics_frame
	var shut_again := _blocked(a, b) and (d.get_child(0) as MeshInstance3D).visible
	var good := d.still and shut and open_clear and hidden and shut_again
	ok = ok and good
	print("[view] still door: still %s, shut blocks %s, open clear %s, open hidden %s, shut again %s  %s" % [d.still,
			shut, open_clear, hidden, shut_again, "OK" if good else "WRONG"])
	print("[view] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
