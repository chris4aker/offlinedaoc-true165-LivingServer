extends Node
## Debug (owner 2026-11-06: "why does my guild emblem look like this? its supposed to be a skull ... the shield is right
## but cloak is wrong"): the player's classic figure in cloak 57 + heater shield with the owner's guild emblem 17040
## (logo 33 = the skull, pattern 1, primary 2, secondary 0), shot from behind with the cloak's logo read from each UV
## set: <out>/emblem_cloak_set2.png (CUSTOM0 - as drawn), _set1.png (UV2), _set0.png (UV).
##   --skip-login --zone=N --world-mode=1.65 --probe=emblem_cloak_probe --out=<dir>
const EMBLEM := 17040
var v: Node


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(6.0).timeout
	var fig: Node3D = null
	for n in (v.player as Node).find_children("*", "", true, false):
		if n is Fig3Figure:
			fig = n
			break
	if fig == null:
		print("[emblem] no figure on the player")
		get_tree().quit()
		return
	var item_at := func(slot: int) -> Dictionary:
		match slot:
			26: return {"model": int(v.args.get("cloak", "57")), "emblem": EMBLEM}
			11: return {"model": 60, "emblem": EMBLEM, "object_type": 42}
		return {}
	Player.dress(fig, item_at, "standard", false, true)
	await get_tree().create_timer(1.0).timeout
	var cloaks := []
	for mi in fig.find_children("*", "MeshInstance3D", true, false):
		var m = (mi as MeshInstance3D).material_override
		if m is ShaderMaterial and (m as ShaderMaterial).get_shader_parameter("has_emblem") and (mi as MeshInstance3D).visible:
			cloaks.append([mi, m])
	print("[emblem] %s, cloak parts with the emblem: %d, logo 33 texture %s" % [fig.get_class(), cloaks.size(),
			Emblems.logo_texture(33) != null])
	var cam := Camera3D.new()
	v.add_child(cam)
	cam.fov = 40
	var p: Vector3 = fig.global_position
	var bz: Vector3 = fig.global_transform.basis.z
	bz.y = 0
	bz = bz.normalized()
	print("[emblem] figure at %s, player at %s, basis z %s" % [p, v.player.global_position, bz])
	cam.current = true
	for side in [1.0, -1.0]:
		cam.global_position = p + bz * 2.6 * side + Vector3(0, 1.3, 0)
		cam.look_at(p + Vector3(0, 1.0, 0))
		await get_tree().create_timer(0.6).timeout
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("%s/emblem_cloak_side%d.png" % [String(v.args.get("out", "user://")), int(side)])
	if v.get("ui") and v.ui is CanvasItem:
		(v.ui as CanvasItem).visible = false               # (the whole back in view, no windows over it)
	elif v.get("ui"):
		for c in (v.ui as Node).find_children("*", "CanvasItem", false, false):
			(c as CanvasItem).visible = false
	cam.global_position = p + bz * 2.4 * float(v.args.get("side", "1")) + Vector3(0, 1.1, 0)
	cam.look_at(p + Vector3(0, 1.05, 0))
	for k in [["set2", "logo_uv"], ["set1", "UV2"], ["set0", "UV"]]:
		for c in cloaks:
			var sm := (c[1] as ShaderMaterial).duplicate() as ShaderMaterial
			var sh := Shader.new()
			sh.code = (c[1] as ShaderMaterial).shader.code.replace("texture(logo_tex, logo_uv)", "texture(logo_tex, %s)" % k[1])
			sm.shader = sh
			(c[0] as MeshInstance3D).material_override = sm
		await get_tree().create_timer(0.8).timeout
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("%s/emblem_cloak_%s.png" % [String(v.args.get("out", "user://")), k[0]])
		print("[emblem] shot with the logo on %s" % k[1])
	get_tree().quit()
