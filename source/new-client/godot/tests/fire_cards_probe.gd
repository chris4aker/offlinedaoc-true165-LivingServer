extends Node
## Offline check (owner 2026-11-07: a ruby simulacrum's Fire Storm left "a large line of effects" on the ground): the
## seven fire hits that kept nine leftover template flame cards in a row beside the effect (nif_static.stray_cards) -
## each effect built as a spell plays it, and every mesh and particle system it draws within 4 m of where it lands.
##   --skip-login --zone=N --world-mode=1.65 --probe=fire_cards_probe
const EFFECTS := ["alb_ddfire3_hit", "alb_ddfire4_hit", "alb_ddfire5_hit", "alb_fexplo3_hit", "alb_fexplo4_hit",
		"alb_fexplo5_hit", "alb_fexplo6_hit"]


func _ready() -> void:
	await get_tree().create_timer(3.0).timeout
	var ok := true
	for e in EFFECTS:
		var path := "res://content/monsters/effects/%s/model.gltf" % e
		var fx := NifScene.copy_of(path)
		if fx == null:
			print("[firecards] %s: did not load  WRONG" % e)
			ok = false
			continue
		add_child(fx)
		fx.global_position = Vector3.ZERO
		var far := 0.0
		var meshes := 0
		for mi in fx.find_children("*", "MeshInstance3D", true, false):
			meshes += 1
			var m := mi as MeshInstance3D
			var o: Vector3 = m.global_position
			far = maxf(far, Vector2(o.x, o.z).length())
			# (a morphing mesh's box holds every morph target added up - the fire ring growing to ~5.5 m read 10 m)
			if m.mesh and m.mesh.get_blend_shape_count() == 0:
				var b: AABB = m.global_transform * m.get_aabb()
				for i in 8:
					var c := b.get_endpoint(i)
					far = maxf(far, Vector2(c.x, c.z).length())
		var parts := 0
		for p in fx.find_children("*", "GPUParticles3D", true, false):
			parts += 1
			var o: Vector3 = (p as Node3D).global_position
			far = maxf(far, Vector2(o.x, o.z).length())
		var good := far < 4.0
		ok = ok and good
		print("[firecards] %s: %d meshes, %d particle systems, farthest %.1f m  %s" % [e, meshes, parts, far, "OK" if good else "WRONG"])
		fx.queue_free()
	print("[firecards] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
