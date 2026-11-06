extends Node3D
## Debug (owner 2026-11-06: "why does my guild emblem look like this? its supposed to be a skull ... the shield is right
## but cloak is wrong"): True 1.65 Mode's classic figures in cloak 57 with the owner's guild emblem 17040 (logo 33,
## pattern 1, primary 2, secondary 0) and a heater shield - the cloak's logo read from each of its UV sets: [set 2
## (CUSTOM0, as drawn), set 1 (UV2), set 0 (UV)], left to right. From behind: <out>/emblem_classic_cloak_back.png.
##   Godot --path godot res://tests/emblem_classic_cloak_shot.tscn -- [out dir]
const EMBLEM := 17040


func _ready() -> void:
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color(0.35, 0.4, 0.45)
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(0.8, 0.8, 0.8)
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-45, 160, 0)
	add_child(sun)
	var out := String(OS.get_cmdline_user_args()[0]) if OS.get_cmdline_user_args().size() > 0 else "user://"
	var sets := ["logo_uv", "UV2", "UV"]
	for i in 3:
		var f := ClassicFigure.new()
		f.position = Vector3(-1.2 + 1.2 * i, 0, 0)
		add_child(f)
		f.load_figure("bri_f")
		f.apply({})
		var item_at := func(slot: int) -> Dictionary:
			match slot:
				26: return {"model": 57, "emblem": EMBLEM}
				11: return {"model": 60, "emblem": EMBLEM, "object_type": 42}
				10: return {"model": 1}
			return {}
		Player.dress(f, item_at, "standard", true, true)
		var aps := f.model.find_children("*", "AnimationPlayer", true, false) if f.model else []
		if aps.size() > 0:
			(aps[0] as AnimationPlayer).play("idle")
			(aps[0] as AnimationPlayer).seek(0.0, true)
		# the cloak's material with its logo on set sets[i]
		for mi in f.find_children("*", "MeshInstance3D", true, false):
			var m = (mi as MeshInstance3D).material_override
			if m is ShaderMaterial and (m as ShaderMaterial).get_shader_parameter("has_emblem"):
				var sm := (m as ShaderMaterial).duplicate() as ShaderMaterial
				var sh := Shader.new()
				sh.code = sm.shader.code.replace("texture(logo_tex, logo_uv)", "texture(logo_tex, %s)" % sets[i])
				sm.shader = sh
				(mi as MeshInstance3D).material_override = sm
		print("[emblem] figure %d: logo on %s, logo texture %s" % [i, sets[i], Emblems.logo_texture(33) != null])
	var cam := Camera3D.new()
	add_child(cam)
	cam.make_current()
	cam.position = Vector3(0, 1.2, -3.6)
	cam.look_at(Vector3(0, 1.0, 0))
	await get_tree().create_timer(1.0).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("%s/emblem_classic_cloak_back.png" % out)
	cam.position = Vector3(0, 1.2, 3.6)
	cam.look_at(Vector3(0, 1.0, 0))
	await get_tree().create_timer(0.6).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("%s/emblem_classic_cloak_front.png" % out)
	get_tree().quit()
