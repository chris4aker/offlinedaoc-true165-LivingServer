extends Node
## Offline check (owner 2026-11-08: a necroservant's / abomination's crouching cast drew the hand glow on the ground,
## not over its hands): a Roman Dead (model 106, the necroservant) and a Glowing Demon Knight (653, the abomination)
## cast; their hand sparkles (1.65 Mode effect 1: Bip01 L / R Finger1, bones these skeletons lack) must sit at their
## hands - about 0.9 m up in the crouch - not at the feet. A Zombie (110, which has Finger1) as a control.
##   --skip-login --zone=N --world-mode=1.65 --probe=monster_cast_glow_probe


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(10.0).timeout
	var mgr: NpcManager = null
	for n in get_tree().root.find_children("*", "", true, false):
		if n is NpcManager:
			mgr = n
			break
	var models: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://content/monsters/monsters.json"))
	Perf.brute = true
	var ok := true
	var i := 0
	for model in ["106", "653", "110"]:
		var at: Vector3 = (v.player as Node3D).global_position + Vector3(1.5, 0, 1.5)              # (one at a time, near you: effects far off are culled)
		i += 1
		var spawn := {"id": "probe%s" % model, "oid": 0, "name": "probe", "guild": "", "realm": 0, "flags": 0, "level": 10,
				"size": 50, "model": model, "heading": 0, "pos": [at.x, at.y, at.z], "equipment": "", "aggro_level": 0,
				"aggro_range": 0, "roam": 0, "pet": true, "temp": true}
		var st := NpcState.new(9000 + i, spawn, models[model])
		st.pos = at
		st.wait = 1e9
		var view := Npc.new()
		mgr.add_child(view)
		view.setup(st, mgr)
		st.view = view
		SpellFx.drop(SpellFx.begin(1, view, view.sounds))      # (the sparkle's files load on the first cast)
		await get_tree().create_timer(2.0).timeout
		var e := SpellFx.effect(1)
		view.cast_start(e.get("anims", []), 3.0)
		var glows := SpellFx.begin(1, view, view.sounds)
		var worst := 99.0
		for k in 6:
			await get_tree().create_timer(0.35).timeout
			for g in glows:
				if is_instance_valid(g):
					worst = minf(worst, (g as Node3D).global_position.y - view.global_position.y)
		var good: bool = glows.size() == 2 and worst > 0.4
		ok = ok and good
		print("[castglow] model %s (%s): %d sparkles, lowest %.2f m above its feet  %s" % [model,
				String(models[model].name), glows.size(), worst, "OK" if good else "WRONG"])
		SpellFx.drop(glows)
		view.queue_free()
		await get_tree().process_frame
	print("[castglow] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
