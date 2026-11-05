extends Node
## Offline check (reset day C14, 2026-11-05: a minstrel blinked out when combat started - new gear rebuilt the whole
## model): new gear on a race figure is put on in place (Npc.redress) - the same view and model stay, the new weapon
## hangs, the old one is gone.
##   --skip-login --zone=26 --world-mode=1.65 --probe=redress_probe


const StealthLook := preload("res://game/stealth_look.gd")


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(8.0).timeout
	var n: Npc = null
	for k in v.npcs.active:
		var c: Npc = v.npcs.active[k]
		if c.model is Fig3Figure and not c._weapon_slots.is_empty():
			n = c
			break
	if n == null:
		var kinds := {}
		for k in v.npcs.active:
			var c: Npc = v.npcs.active[k]
			var cls := c.model.get_class() + ("/" + str(c.model.get_script().get_global_name()) if c.model and c.model.get_script() else "") if c.model else "none"
			kinds[cls] = int(kinds.get(cls, 0)) + 1
		print("[redress] FAIL: no dressed race figure; models: ", kinds, " active ", v.npcs.active.size())
		get_tree().quit()
		return
	v.player.global_position = n.global_position + Vector3(2, 0.5, 0)
	await get_tree().create_timer(0.5).timeout
	var model_before := n.model.get_instance_id()
	var old_weapons := n._weapon_nodes.size()
	var key := String(n.spawn.get("equipment", ""))
	var rows: Array = (v.npcs.data.equipment.get(key, []) as Array).duplicate(true)
	# a minstrel's swap: a lute (model 2116) in the ranged slot, the right-hand weapon taken away
	rows = rows.filter(func(e): return int(e.slot) not in [10, 11, 12, 13])
	rows.append({"slot": 13, "model": 2116, "color": 0})
	v.npcs.data.equipment[key] = rows
	n.spawn["weapon_slots"] = 0x33
	var ok := n.redress()
	await get_tree().process_frame
	await get_tree().process_frame
	var same_model := n.model != null and is_instance_valid(n.model) and n.model.get_instance_id() == model_before
	print("[redress] %s: redress %s, same model %s, weapons %d -> %d, slots %s" % [n.spawn.get("name", "?"), ok,
			same_model, old_weapons, n._weapon_nodes.size(), n._weapon_slots])
	ok = ok and same_model and n._weapon_slots.has(13) and not n._weapon_slots.has(10)
	# a hidden-but-seen one (the green stealth look, a stealthed assassin bot): re-dressed in place too, its new parts
	# green at once (server chat 2026-11-05: "bots don't flicker out of existence when they swap weapons")
	n.state.stealth_look = true
	StealthLook.set_on(n.model, true)
	await get_tree().process_frame
	rows.append({"slot": 10, "model": 4, "color": 0})
	v.npcs.data.equipment[key] = rows
	n.spawn["weapon_slots"] = 0x10
	var ok2 := n.redress()
	var ghost: ShaderMaterial = StealthLook.ghost_material()
	var all_green := true
	for mi in n.model.find_children("*", "MeshInstance3D", true, false):
		if (mi as MeshInstance3D).visible and (mi as MeshInstance3D).material_override != ghost:
			all_green = false
	print("[redress] stealthed: redress %s, same model %s, every part green at once %s" % [ok2,
			n.model.get_instance_id() == model_before, all_green])
	ok = ok and ok2 and n.model.get_instance_id() == model_before and all_green
	print("[redress] RESULT ", "PASS" if ok else "FAIL")
	get_tree().quit()
