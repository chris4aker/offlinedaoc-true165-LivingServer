extends Node
## Debug (owner 2026-11-06 via the server chat): the 1.65 siege engines players craft - a deployed engine is a moving
## object (MovingObjectCreate) whose model is its item's: 482 trebuchet, 561 ballista, 562 ram, 563 mangonel. Checks
## that ServerWorld draws a moving object with that model from objects.csv (WeaponModels) and a living with the same
## number still as its monsters.csv figure, and shoots the four engines standing in a row.
##   --skip-login --zone=250 --world-mode=1.65 --probe=siege165_probe --out=<dir>
var v: Node


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(8.0).timeout
	var sw := ServerWorld.new()
	var m = JSON.parse_string(FileAccess.get_file_as_string("res://content/monsters/monsters.json"))
	sw.models = m if m is Dictionary else {}
	var ok := true
	for model in [482, 561, 562, 563]:
		var as_object: Dictionary = sw._model_info(model, true)
		var as_living: Dictionary = sw._model_info(model, false)
		var good: bool = int(as_object.get("object_model", -1)) == model and not as_living.has("object_model")
		ok = ok and good
		print("[siege] %d moving object -> %s | living -> %s  %s" % [model, as_object, String(as_living.get("name", "?")),
				"OK" if good else "WRONG"])
	sw.free()
	var at: Vector3 = v.player.global_position
	var fwd := -(v.player as Node3D).global_transform.basis.z
	fwd.y = 0
	fwd = fwd.normalized()
	var side := fwd.cross(Vector3.UP)
	var i := 0
	for model in [482, 561, 562, 563]:
		var node: Node3D = WeaponModels.make_world(model)
		if node == null:
			print("[siege] %d: no model" % model)
			ok = false
			continue
		v.add_child(node)
		var p := at + fwd * 14.0 + side * (float(i) - 1.5) * 7.0
		p.y = (v.zone as ZoneLoader).height_at(p.x, p.z)
		node.global_position = p
		var box := AABB()
		for mi in node.find_children("*", "MeshInstance3D", true, false):
			box = box.merge((mi as MeshInstance3D).get_aabb())
		print("[siege] %d %s: %d meshes, size %s" % [model, WeaponModels.info(model).get("nif", ""),
				node.find_children("*", "MeshInstance3D", true, false).size(), box.size])
		i += 1
	v.player.visible = false
	var cam := Camera3D.new()
	v.add_child(cam)
	cam.fov = 60
	cam.global_position = at - fwd * 2.0 + Vector3(0, 6, 0)
	cam.look_at(at + fwd * 14.0 + Vector3(0, 1.5, 0))
	cam.current = true
	await get_tree().create_timer(1.0).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("%s/siege165.png" % String(v.args.get("out", "user://")))
	print("[siege] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
