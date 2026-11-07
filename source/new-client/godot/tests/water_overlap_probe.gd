extends Node
## Offline check (owner 2026-11-07: Lyonesse's water in blocky darker squares, as Avalon Marsh's was): the zone's water
## meshes (ZoneLoader._build_water, one per body) - no spot drawn by two bodies at once (the stacked see-through sheets
## of a body's shore border lying on its neighbour's water). Counts the triangles of each body whose middle lies on a
## triangle of another body.
##   --skip-login --zone=N --world-mode=1.65 --probe=water_overlap_probe


func _ready() -> void:
	await get_tree().create_timer(12.0).timeout
	var bodies := {}                                     # body mesh name -> {cell key: true}
	for mi in get_tree().root.find_children("Water_*", "MeshInstance3D", true, false):
		var m := (mi as MeshInstance3D).mesh
		if m == null:
			continue
		var cells := {}
		for s in m.get_surface_count():
			var v: PackedVector3Array = m.surface_get_arrays(s)[Mesh.ARRAY_VERTEX]
			var idx = m.surface_get_arrays(s)[Mesh.ARRAY_INDEX]
			var tri: Array = []
			if idx is PackedInt32Array and (idx as PackedInt32Array).size() > 0:
				for i in (idx as PackedInt32Array):
					tri.append(v[i])
			else:
				tri = Array(v)
			for t in range(0, tri.size() - 2, 3):
				var c: Vector3 = ((mi as Node3D).global_transform * ((tri[t] + tri[t + 1] + tri[t + 2]) / 3.0))
				cells["%d,%d" % [roundi(c.x * 2.0), roundi(c.z * 2.0)]] = [c, tri[t], tri[t + 1], tri[t + 2]]
		bodies[String(mi.name) + "#" + str(mi.get_instance_id())] = cells
	var names := bodies.keys()
	var shared := 0
	for i in names.size():
		for j in range(i + 1, names.size()):
			var a: Dictionary = bodies[names[i]]
			var b: Dictionary = bodies[names[j]]
			var small := a if a.size() < b.size() else b
			var big := b if small == a else a
			var n := 0
			for k in small:
				if big.has(k):
					if n == 0:
						print("[water]   e.g. %s / %s" % [str(small[k]), str(big[k])])
					n += 1
			if n > 0:
				print("[water] %s and %s share %d spots" % [names[i].get_slice("#", 0), names[j].get_slice("#", 0), n])
			shared += n
	print("[water] zone %s: %d water bodies drawn, %d spots drawn twice  %s" % [
			String(get_tree().current_scene.get("args").get("zone", "?")) if get_tree().current_scene.get("args") is Dictionary else "?",
			names.size(), shared, "OK" if shared == 0 and names.size() > 0 else "WRONG"])
	print("[water] %s" % ("PASS" if shared == 0 and names.size() > 0 else "FAIL"))
	get_tree().quit()
