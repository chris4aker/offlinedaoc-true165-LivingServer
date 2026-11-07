extends Node
## Offline check (owner 2026-11-06 via the server chat: companions' and gamebots' spare weapons sheathed "same as the
## player's own character"): an NPC view on a classic 1.65 figure given weapons as the server sends them (slots 10-13 +
## the active set), and where each one hangs (its BoneAttachment3D bone):
##   A. one-hander + shield active, two-hander spare: sheathed - sword L Belt, shield L Shield, two-hander L Back;
##      drawn - sword R Held, shield L Shield, two-hander still L Back
##   B. the two-hander active (the set changed by the server, set_weapon_slots 0x22): two-hander R Held, the
##      one-hander on the hip (L Belt), the shield hidden
##   C. a dual-wield set spare while the two-hander is out: one weapon on each hip (L Belt, R Belt)
## and a shot of A sheathed from behind: <out>/npc_sheathe_A.png.
##   --skip-login --zone=N --world-mode=1.65 --probe=npc_sheathe_probe --out=<dir>
var v: Node


func _bones(n: Npc) -> Array:
	var out := []
	for w in n._weapon_nodes:
		if is_instance_valid(w) and w is BoneAttachment3D:
			var model := ""
			for c in (w as Node).get_children():
				model = c.name
			out.append(String((w as BoneAttachment3D).bone_name))
	out.sort()
	return out


func _check(label: String, got: Array, want: Array) -> bool:
	var w := want.duplicate()
	w.sort()
	var ok := got == w
	print("[sheathe] %s: %s  %s" % [label, got, "OK" if ok else "WRONG (want %s)" % [w]])
	return ok


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(10.0).timeout
	var npc: Npc = null
	for n in get_tree().root.find_children("*", "", true, false):
		if n is Npc:
			for c in (n as Node).find_children("*", "", true, false):
				if c is Fig3Figure:
					npc = n
					npc._weapon_root = c
					break
		if npc:
			break
	if npc == null:
		print("[sheathe] no NPC on a classic figure in this zone")
		get_tree().quit()
		return
	print("[sheathe] NPC '%s' on %s" % [String(npc.spawn.get("name", "")), npc._weapon_root.get_class()])
	var ok := true
	npc._weapon_fx = {}
	npc._weapon_colors = {}
	npc._weapon_emblems = {}
	# A. one-hander (4 longsword) + shield (60 heater) active, two-hander (6) spare
	npc._weapon_slots = {10: 4, 11: 60, 12: 6}
	npc.spawn["weapon_slots"] = 0x10
	npc._hang_weapons(false)
	ok = _check("A sheathed (1h + shield active, 2h spare)", _bones(npc), ["Bip01 L Belt", "Bip01 L Shield", "Bip01 L Back"]) and ok
	var cam := Camera3D.new()
	v.add_child(cam)
	cam.fov = 45
	var p: Vector3 = (npc as Node3D).global_position
	var bz: Vector3 = (npc as Node3D).global_transform.basis.z
	bz.y = 0
	bz = bz.normalized() if bz.length() > 0.01 else Vector3(0, 0, 1)
	if v.get("ui") and v.ui is CanvasItem:
		(v.ui as CanvasItem).visible = false
	for k in [["A_back", -1.0], ["A_front", 1.0]]:
		cam.global_position = p + bz * 2.6 * float(k[1]) + Vector3(0, 1.3, 0)
		cam.look_at(p + Vector3(0, 1.0, 0))
		cam.current = true
		await get_tree().create_timer(0.8).timeout
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("%s/npc_sheathe_%s.png" % [String(v.args.get("out", "user://")), k[0]])
	npc._hang_weapons(true)
	ok = _check("A drawn", _bones(npc), ["Bip01 R Held", "Bip01 L Shield", "Bip01 L Back"]) and ok
	# B. the server changes the active set to the two-hander
	npc.set_weapon_slots(0x22)
	ok = _check("B two-hander active, drawn", _bones(npc), ["Bip01 R Held", "Bip01 L Belt"]) and ok
	npc._hang_weapons(false)
	ok = _check("B two-hander active, sheathed", _bones(npc), ["Bip01 L Back", "Bip01 L Belt"]) and ok
	# C. a dual-wield set spare (longsword + short sword 3) while the two-hander is out
	npc._weapon_slots = {10: 4, 11: 3, 12: 6}
	npc.spawn["weapon_slots"] = 0x22
	npc._hang_weapons(true)
	ok = _check("C dual-wield spare, two-hander drawn", _bones(npc), ["Bip01 R Held", "Bip01 L Belt", "Bip01 R Belt"]) and ok
	print("[sheathe] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
