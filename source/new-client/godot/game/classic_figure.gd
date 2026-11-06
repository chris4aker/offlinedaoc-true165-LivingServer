class_name ClassicFigure
extends Fig3Figure
## True 1.65 Mode: a player-race figure drawn as the original client
## draws it with its figure version set to Classic (the 1.65 profile: user.dat [character_version] 0). Built by
## pipeline/build_classic_races.py (content/characters/classic/<prefix>, the same prefixes as the Catacombs figures) and
## dressed from pipeline/build_classic_armor.py (content/characters/classic_armor.json). Spec: TRUE 1.65 MODE CLASSIC
## FIGURES.md. Fig3Figure.make() gives one of these in True 1.65 Mode; every caller keeps Fig3Figure's
## public face (load_figure, apply, set_armor, set_helm, hood_up, cloak_hidden, helm_hidden, cloak_emblem, anim, play).
##
## The figure: one NIF per race and gender with every part variant (Body1..5 + the cloak-cut Body1b..5b, Arms1..6,
## Gloves1..3, LBody1..4, Legs1..4, Boots1..5, HeadA1..5 / HeadB1..5, Cloak1..3); the client shows one per slot.
## A look is the server's model id (a monsters.csv row of the race: its face, hair colour and head shape) - or a
## Catacombs look (face, hair, hair_color) mapped onto a row the client's way (fig3tofigmain / fig3tofighaircolor).

const CLASSIC_ROOT := "res://content/characters/classic"
const SLOTS := ["body", "arms", "gloves", "lbody", "legs", "boots"]
const NODE_OF := {"body": "Body", "arms": "Arms", "gloves": "Gloves", "lbody": "LBody", "legs": "Legs", "boots": "Boots"}
static var _classic_armor := {}
## Short / Average / Tall (the period creation page; a player's model bits 11-12; a hired companion's HEIGHT): drawn
## as the NPC Size byte 48 / 50 / 52 would be (size / 50, as every NPC), so a companion the server sends that Size is
## exactly as tall as a player of the same race and height (server protocol, 2026-10-31). The 1.64 game.dll's own player
## scale is not known; the Catacombs figures used +-5%.
const HEIGHT_SIZE := {1: 48, 2: 50, 3: 52}


static func height_factor(h: int) -> float:
	return float(HEIGHT_SIZE.get(h, 50)) / 50.0

var _row := {}                       # the look's monsters.csv row: {face, hair, geom, hair_set}
var _hair_tex := ""
var _shown := {}                     # slot -> the node shown ("" none)
var _head_node := ""
var _skip_helm := false


## The classic armour record of an item model (classic_armor.json), or {}.
static func armor_record(model: int) -> Dictionary:
	if _classic_armor.is_empty():
		var t = JSON.parse_string(FileAccess.get_file_as_string("res://content/characters/classic_armor.json")) \
				if FileAccess.file_exists("res://content/characters/classic_armor.json") else null
		_classic_armor = t.get("models", {}) if t is Dictionary else {"_": {}}
	return _classic_armor.get(str(model), {})


## What a worn item gives the figure (Fig3Figure.item_armor / armor_parts in True 1.65 Mode): the cloak under "cloak"
## (the callers' cloak path), any other piece under "item_<model>" - ClassicFigure works the meshes out from them all
## together, as the client does.
static func classic_parts(model: int) -> Dictionary:
	var rec := armor_record(model)
	if rec.is_empty():
		return {}
	if (rec.get("skins", {}) as Dictionary).has("cloak") or int(rec.get("mesh", {}).get("cloak", 0)) > 0:
		return {"cloak": [model]}
	return {"item_%d" % model: [model]}


## The texture keys a figure with this look and gear draws (NpcManager prepares them on a worker).
static func classic_texture_keys(meta: Dictionary, look: Dictionary, parts: Dictionary) -> Array:
	var out := []
	var row := look_row(meta, look)
	out.append(String(row.get("face", "")))
	out.append(hair_texture(meta, look, row))
	for k in (meta.get("defaults", {}) as Dictionary):
		out.append(String(meta.defaults[k]))
	var female := String(meta.get("gender", "")) == "female"
	for k in parts:
		var p: Array = parts[k]
		if p.is_empty():
			continue
		var skins: Dictionary = armor_record(int(p[0])).get("skins", {})
		for s in skins:
			if s != "helm" and not (s == "body_m" and female) and not (s == "body_f" and not female):
				out.append(String(skins[s]))
	return out.filter(func(x): return x != "").map(func(x): return String(x).to_lower())


## The monsters.csv row a look stands for (figure.json looks): a character's own face + hair style through
## fig3tofigmain, as the original client maps its customize screen onto the classic figure (original client
## 2026-10-30: [character_version]=0 keeps the face / hair / colour pickers), else the server's model id when it is one
## of the race's rows (NPCs, other players), else the race's first row.
static func look_row(meta: Dictionary, look: Dictionary) -> Dictionary:
	var looks: Dictionary = meta.get("looks", {})
	if look.has("face") or look.has("hair"):
		var key := "%d,%d" % [int(look.get("face", 0)), int(look.get("hair", 0))]
		var mapped := str(int(meta.get("cata_to_classic", {}).get(key, 0)))
		if looks.has(mapped):
			return looks[mapped]
	var model := str(int(look.get("model", 0)))
	if looks.has(model):
		return looks[model]
	if look.has("seed") and looks.size() > 0:                 # an NPC without a row of its own: one of the race's
		var ids: Array = looks.keys()
		ids.sort()
		return looks[ids[absi(int(look.seed)) % ids.size()]]
	return looks.get(String(meta.get("first_look", "")), looks.values()[0] if looks.size() > 0 else {})


## The race's monsters.csv row id a look stands for (look_row's), 0 when the race has none - the model a True 1.65
## Mode character is created with, so the server keeps its classic look.
static func look_model(meta: Dictionary, look: Dictionary) -> int:
	var row := look_row(meta, look)
	var looks: Dictionary = meta.get("looks", {})
	for id in looks:
		if looks[id] == row:
			return int(id)
	return 0


## The race prefix ("syl_m" ...) whose classic figure has this look model, "" for none - every race's figure.json
## looks, read once. (GitHub issue #9: a companion whose look model has no monsters.json row - Sylvan look 12 = 852 -
## was taken for an objects.csv item by that number and drawn as the Alb Thorn Bow.)
static var _look_race := {}


static func race_of_look(model: int) -> String:
	if _look_race.is_empty():
		_look_race[-1] = ""                                   # (read once, even when nothing is built)
		for prefix in DirAccess.get_directories_at(CLASSIC_ROOT):
			for id in (meta_of(prefix).get("looks", {}) as Dictionary):
				_look_race[int(id)] = prefix
	return String(_look_race.get(model, ""))


## The classic figure.json of a race prefix ({} if not built).
static func meta_of(prefix: String) -> Dictionary:
	var d = JSON.parse_string(FileAccess.get_file_as_string("%s/%s/figure.json" % [CLASSIC_ROOT, prefix]))
	return d if d is Dictionary else {}


## The hair texture: the row's own, or a Catacombs hair colour mapped to the old colour of the row's hair set.
## hair_index: a hair.csv colour picked straight from the row's set, 0-based (True 1.65 Mode companions' HAIR COLOR) -
## game.dll 0x417878: only when the row has a face (monsters.csv col 18) and a hair set (col 19), else its own hair (col 4).
static func hair_texture(meta: Dictionary, look: Dictionary, row: Dictionary) -> String:
	if look.has("hair_index") and String(row.get("face", "")) != "" and int(row.get("hair_set", 0)) != 0:
		var hs: Array = meta.get("hair_sets", {}).get(str(int(row.get("hair_set", 0))), [])
		var i := int(look.hair_index)
		if i >= 0 and i < hs.size() and String(hs[i]) != "":
			return String(hs[i])
	if look.has("hair_color"):
		var old := int(meta.get("cata_hair_to_classic", {}).get(str(int(look.hair_color)), 0))
		var set_: Array = meta.get("hair_sets", {}).get(str(int(row.get("hair_set", 0))), [])
		if old >= 1 and old <= set_.size() and String(set_[old - 1]) != "":
			return String(set_[old - 1])
	return String(row.get("hair", ""))


func load_figure(prefix: String, texture_set := "x4plus") -> bool:
	var dir := "%s/%s" % [CLASSIC_ROOT, prefix]
	meta = NpcManager._metas[prefix] if NpcManager._metas.has(prefix) and not (NpcManager._metas[prefix] as Dictionary).is_empty() \
			else JSON.parse_string(FileAccess.get_file_as_string(dir + "/figure.json"))
	if not meta is Dictionary or meta.is_empty():
		push_warning("classic figure missing: " + prefix)
		meta = {}
		return false
	# the customize screen's pickers stay the Catacombs ones (the original client with [character_version]=0 keeps the
	# face / hair style / hair colour / eye / skin pickers and maps them onto the classic figure - original client
	# 2026-10-30): the race's Catacombs option lists, on this figure's own copy of the shared meta
	var cata := CharacterFactory.figure_meta(prefix)
	if not cata.is_empty():
		meta = (meta as Dictionary).duplicate()
		for k in ["faces", "eyes", "skin_tones", "hair_styles", "tattoos", "sliders"]:
			if cata.has(k):
				meta[k] = cata[k]
	var d := "res://content/characters/classic_textures/" + texture_set
	_texture_dir = d if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(d)) else "res://content/characters/classic_textures/original"
	if not _scenes.has(prefix) or _scenes[prefix] == null:
		_scenes[prefix] = Fig3Figure.pack_figure(prefix)
		if _scenes[prefix] == null:
			push_warning("classic figure missing: " + prefix)
			return false
	model = (_scenes[prefix] as PackedScene).instantiate() as Node3D
	CacheTrim.track("figure", prefix, model)
	add_child(model)
	for node in model.find_children("*", "MeshInstance3D", true, false):
		var part := canonical(String(node.name).get_slice("_mesh", 0))   # nif_character.build: "<part>_mesh" (+ a number)
		if not _meshes.has(part):
			_meshes[part] = []
		_meshes[part].append(node)
	var players := model.find_children("*", "AnimationPlayer", true, false)
	if players.size() > 0:
		anim = players[0]
		for clip in anim.get_animation_list():
			anim.get_animation(clip).loop_mode = Animation.LOOP_LINEAR if loops(clip) else Animation.LOOP_NONE
		add_rest_reset(anim)
		if anim.has_animation("idle"):
			anim.play("idle")
	for sk in model.find_children("*", "Skeleton3D", true, false):
		(sk as Skeleton3D).child_entered_tree.connect(_on_attached)
	apply({})
	return true


## Anything hung on a hand, shield, back or belt bone (weapons, shields, sheathed gear - Player.dress, Npc._hang_weapons
## ...) at the race's scale for held objects (game.dll 1.64 0x417ad6): monsters.csv 'Shield Scale' on a Shield bone,
## else 'Obj Scale' - a Troll's or Firbolg's sword 1.15 of a Briton's.
func _on_attached(n: Node) -> void:
	if not n is BoneAttachment3D:
		return
	var bone := String((n as BoneAttachment3D).bone_name)
	if not (bone.contains("Held") or bone.contains("Shield") or bone.contains("Back") or bone.contains("Belt")):
		return
	var k := float(meta.get("shield_scale" if bone.contains("Shield") else "obj_scale", 1.0))
	if is_equal_approx(k, 1.0):
		return
	var grow := func(c: Node) -> void:
		if c is Node3D and not c.has_meta("classic_scaled"):
			c.set_meta("classic_scaled", true)
			(c as Node3D).scale *= k
	for c in n.get_children():
		grow.call(c)
	n.child_entered_tree.connect(grow)


func apply(changes: Dictionary) -> void:
	look.merge(changes, true)
	_row = look_row(meta, look)
	_hair_tex = hair_texture(meta, look, _row)
	scale = Vector3.ONE * float(meta.get("scale", 1.0)) * height_factor(int(look.get("height", 2)))
	_apply_armor()


## Worn gear, the original client's way (game.dll 1.64 0x418e71 / 0x41917c / 0x417176): every slot starts at its
## part 1 with the race's own variants (kilt: lower body 2; Saracen: body, arms, lower body 2; the "Norse" flag: body
## and arms 2), then each worn item's mesh numbers pick the parts, its skins go on them, a cloak cuts the body (the
## "b" bodies) and a hood or a helm picks the head.
func _apply_armor() -> void:
	var flags: Dictionary = meta.get("flags", {})
	var female := String(meta.get("gender", "")) == "female"
	var s := {"body": 1, "arms": 1, "gloves": 1, "lbody": 1, "legs": 1, "boots": 1}
	if bool(flags.get("kilt", false)):
		s.lbody = 2
	if bool(flags.get("sarac", false)):
		s.body = 2
		s.arms = 2
		s.lbody = 2
	if bool(flags.get("norse", false)):
		s.body = 2
		s.arms = 2
	var head_n := 1
	var cloak_n := 0
	var robe := false
	var tex := {}                                     # slot -> texture key (worn items)
	var tint := {}                                    # slot -> the item's dye
	var items := []                                   # [record, dye] in the client's order: body pieces first
	for k in armor:
		var p: Array = armor[k]
		if p.is_empty():
			continue
		var rec := armor_record(int(p[0]))
		if rec.is_empty():
			continue
		var m: Dictionary = rec.get("mesh", {})
		var first := int(m.get("body", 0)) > 0 or int(m.get("lbody", 0)) > 0
		var entry := [rec, armor_tints.get(k, Color.WHITE), k == "cloak"]
		if first:
			items.push_front(entry)
		else:
			items.append(entry)
	for e in items:
		var rec: Dictionary = e[0]
		var m: Dictionary = (rec.get("mesh", {}) as Dictionary).duplicate()
		if female and rec.has("fem"):
			m.merge(rec.fem, true)
		var is_cloak: bool = e[2]
		if is_cloak and cloak_hidden:
			continue
		var b := int(m.get("body", 0))
		match b:
			1:
				s.body = 2 if bool(flags.get("sarac", false)) else 1
				s.arms = s.body
			2, 3:
				s.body = b
				s.arms = b
			4:
				s.body = 2
				s.arms = 4
			5:
				s.body = 5
				s.arms = 6
		var lb := int(m.get("lbody", 0))
		if lb == 1 or lb == 2:
			if not bool(flags.get("kilt", false)):
				s.lbody = lb
			s.legs = 1
			s.boots = 1
		elif lb == 3:                                  # a robe: no legs, boots 2
			s.lbody = 3
			s.legs = 0
			s.boots = 2
			robe = true
		for slot in ["arms", "gloves", "legs", "boots"]:
			if int(m.get(slot, 0)) > 0 and not (slot == "legs" and robe):
				s[slot] = int(m[slot])
		if int(m.get("head", 0)) > 0:
			head_n = int(m.head)
		if is_cloak:
			cloak_n = maxi(1, int(m.get("cloak", 1)))
		var skins: Dictionary = rec.get("skins", {})
		for k in skins:
			var slot := String(k)
			if slot == "body_m" or slot == "body_f":
				if (slot == "body_f") != female:
					continue
				slot = "body"
			if slot == "helm":
				continue
			# a kilted race's kilt takes the worn CHEST item's lower-body skin (a leather tunic: a leather kilt), as the
			# client (reset day C20, 2026-11-05, the old client chat's Q3 - Virsha and Wystan wore a grey cloth kilt
			# whatever their tunic); the race's own plaid only with no chest item. Another piece's (leggings) never.
			if slot == "lbody" and bool(flags.get("kilt", false)) and lb != 3 and int(m.get("body", 0)) <= 0:
				continue
			tex[slot] = String(skins[k])
			tint[slot] = e[1]
	# the hood: a hooded cloak (Cloak# 2), or /hood over any cloak (the server's hood flag) - Cloak2 and head 5
	if cloak_n > 0 and hood_up:
		cloak_n = 2
	_skip_helm = cloak_n == 2
	if cloak_n == 2:
		head_n = 5
	_head_n_from_items = head_n
	var cloak_cut := cloak_n > 0 and bool(flags.get("cloak_body", false))
	var defaults: Dictionary = meta.get("defaults", {})
	_shown.clear()
	for slot in SLOTS:
		var n := int(s[slot])
		var node := ""
		if n > 0:
			node = "%s%d" % [NODE_OF[slot], n] + ("b" if slot == "body" and cloak_cut else "")
			if not _meshes.has(node) and slot == "body" and cloak_cut:
				node = "%s%d" % [NODE_OF[slot], n]     # (a race without that cut body)
			if not _meshes.has(node):
				node = "%s1" % NODE_OF[slot] if _meshes.has("%s1" % NODE_OF[slot]) else ""
		_shown[slot] = node
		_show_slot(NODE_OF[slot], node)
		if node != "":
			var key := String(tex.get(slot, defaults.get(slot, "")))
			_paint(node, key, tint.get(slot, Color.WHITE) if tex.has(slot) else Color.WHITE)
	# the cloak
	var cloak_node := "Cloak%d" % cloak_n if cloak_n > 0 else ""
	_show_slot("Cloak", cloak_node if _meshes.has(cloak_node) else "")
	if cloak_node != "" and _meshes.has(cloak_node):
		_paint_cloak(cloak_node, String(tex.get("cloak", defaults.get("cloak", ""))), tint.get("cloak", Color.WHITE))
	_apply_head()


var _head_n_from_items := 1


## The head: HeadA<n> / HeadB<n> by the row's head shape - n = 1, a helm's Head# (set_helm), 5 under a hood - the hair
## texture on UV set 0 and the face on set 1 over it.
func _apply_head() -> void:
	var n := _head_n_from_items
	if _helm_head > 0 and not _skip_helm:
		n = _helm_head
	var geom := "B" if int(_row.get("geom", 1)) == 2 else "A"
	var node := "Head%s%d" % [geom, n]
	if not _meshes.has(node):
		node = "Head%s1" % geom
	_head_node = node
	_show_slot("Head", node)
	var m := _classic_material(false)
	m.set_shader_parameter("albedo_tex", _tex(_hair_tex))
	var face := String(_row.get("face", ""))
	m.set_shader_parameter("has_decal", face != "")
	if face != "":
		m.set_shader_parameter("decal_tex", _tex(face))
	for mi in _parts(node):
		mi.material_override = m


var _helm_head := 0


## The helm (classic_armor.json "helm": the race's helm NIF by its 'Alt Helm', the item's helm skin on it) on
## Bip01 Helmet at the race's helm scale; its Head# picks the head under it. A hood takes its place.
func set_helm(helm_model: int, hidden := false, color := 0) -> void:
	if _helm and is_instance_valid(_helm):
		_helm.queue_free()
	_helm = null
	_helm_head = 0
	var rec := armor_record(helm_model) if helm_model > 0 and not hidden else {}
	var path := classic_helm_path(helm_model, meta) if not rec.is_empty() else ""
	if not rec.is_empty() and not _skip_helm:
		_helm_head = int(rec.get("mesh", {}).get("head", 0))
	if path != "" and not _skip_helm:
		var skel: Skeleton3D = null
		var sk := find_children("*", "Skeleton3D", true, false)
		if sk.size() > 0:
			skel = sk[0]
		var bone := "Bip01 Helmet" if skel and skel.find_bone("Bip01 Helmet") >= 0 else "Bip01 Head"
		if skel and skel.find_bone(bone) >= 0:
			if not _helm_scenes.has(path):
				_helm_scenes[path] = pack_helm(path)
			var ps: PackedScene = _helm_scenes[path]
			if ps:
				var at := BoneAttachment3D.new()
				at.bone_name = bone
				skel.add_child(at)
				var h := ps.instantiate() as Node3D
				CacheTrim.track("helm", path, h)
				for c in h.find_children("collision*", "", true, false):
					c.queue_free()
				h.transform = WeaponModels.TO_NIF.scaled_local(Vector3.ONE * float(meta.get("helm_scale", 1.0)))
				at.add_child(h)
				var dyed := dye(color)
				for mi in h.find_children("*", "MeshInstance3D", true, false):
					var mesh_i := mi as MeshInstance3D
					mesh_i.layers = 3
					if dyed != Color.WHITE and mesh_i.mesh:
						for si in mesh_i.mesh.get_surface_count():
							var mat := mesh_i.get_active_material(si)
							if mat is BaseMaterial3D:                 # (one shared dyed copy: WeaponModels.dyed_material)
								mesh_i.set_surface_override_material(si, WeaponModels.dyed_material(mat, dyed))
				_helm = at
	_apply_head()


## The helm mesh file for a helm model on this race (by its monsters.csv 'Alt Helm': 1 -> objects.csv col 24,
## 2 -> col 28, 3 -> col 29, else / empty col 2); "" none - no classic helm NIF draws no helm.
static func classic_helm_path(helm_model: int, figure_meta: Dictionary) -> String:
	var files: Array = armor_record(helm_model).get("helm", [])
	if files.is_empty():
		return ""
	var alt := int(figure_meta.get("flags", {}).get("alt_helm", 0))
	var f := String(files[alt]) if alt >= 1 and alt < files.size() else ""
	if f == "":
		f = String(files[0])
	return "res://content/helms_classic/" + f if f != "" else ""


func _apply_cloak() -> void:
	pass                                       # (_apply_armor draws the cloak with the rest)


func _refresh_hair() -> void:
	pass                                       # (the hair is the head's texture)


const STEMS := ["LBody", "Body", "Arms", "Gloves", "Legs", "Boots", "Cloak", "HeadA", "HeadB", "Lod"]


## A part's name as this script spells it: the NIFs differ in case (the Norse woman's and the Troll's "Lbody3").
static func canonical(part: String) -> String:
	var low := part.to_lower()
	for stem in STEMS:
		if low.begins_with(stem.to_lower()):
			return stem + part.substr(stem.length())
	return part


## A texture: the classic set's, else the Catacombs figures' (the customize screen's swatches - eyes, faces, hair
## colours - are Catacombs textures).
func _tex(base: String) -> Texture2D:
	var key := base.to_lower()
	var orig := "%s/original/%s.png" % [_texture_dir.get_base_dir(), key]
	if _textures.has(key) or _tex_cache.has(_texture_dir + key) or FileAccess.file_exists(orig) 			or FileAccess.file_exists("%s/%s.png" % [_texture_dir, key]):
		return super._tex(base)
	var fig3 := ROOT + "/fig3_textures/x4plus"
	if not DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(fig3)):
		fig3 = ROOT + "/fig3_textures/original"
	if not _tex_cache.has(fig3 + key):
		_tex_cache[fig3 + key] = load_texture(fig3, key)
	_textures[key] = _tex_cache[fig3 + key]
	return _textures[key]


## Every variant of a slot hidden but `node` ("" hides them all); "Head" covers HeadA* and HeadB*.
func _show_slot(stem: String, node: String) -> void:
	var stems := ["HeadA", "HeadB"] if stem == "Head" else [stem]
	for part in _meshes:
		var p := String(part)
		for st in stems:
			if p.begins_with(st) and p.substr(String(st).length(), 1).is_valid_int():
				for mi in _meshes[part]:
					mi.visible = p == node


func _paint(node: String, key: String, dyed: Color) -> void:
	if key == "":
		return
	var m := _classic_material(false)
	m.set_shader_parameter("albedo_tex", _tex(key))
	m.set_shader_parameter("tint", dyed)
	m.set_shader_parameter("tint_mode", 1 if dyed != Color.WHITE else 0)
	for mi in _parts(node):
		mi.material_override = m


func _paint_cloak(node: String, key: String, dyed: Color) -> void:
	var m := _classic_material(false, true, true)
	if key != "":
		m.set_shader_parameter("albedo_tex", _tex(key))
	m.set_shader_parameter("tint", dyed)
	m.set_shader_parameter("tint_mode", 1 if dyed != Color.WHITE else 0)
	m.set_shader_parameter("view_pull", CLOAK_PULL)
	m.set_shader_parameter("has_emblem", cloak_emblem > 0)
	if cloak_emblem > 0:
		m.set_shader_parameter("pattern_tex", Emblems.pattern_texture(cloak_emblem))
		var logo := Emblems.logo_texture(Emblems.decode(cloak_emblem).logo)
		m.set_shader_parameter("logo_tex", logo)
		m.set_shader_parameter("has_logo", logo != null)
	for mi in _parts(node):
		mi.material_override = m


static var _classic_shaders := {}


## Fig3Figure's shader for the classic parts: the face is a decal on UV set 1 over the hair (by the face's alpha), a
## cloak's emblem pattern is on the cloak's own set 0 (UV) and its logo on set 2 (CUSTOM0, nif_character.build).
## cloak: the cloak's skin on its UV set 2 (CUSTOM0) - the set the client lays cloak skins on (owner 2026-10-30: the
## Albion guard cloak's gold border drew as swirls on set 0; on set 2 it runs round the edge, the hood purple).
static func _classic_material(cutout: bool, single_sided := false, cloak := false) -> ShaderMaterial:
	var key := ("cutout" if cutout else "solid") + ("_cull" if single_sided else "") + ("_cloak" if cloak else "")
	if not _classic_shaders.has(key):
		var code := SHADER
		code = code.replace("vec4 d = texture(decal_tex, UV);\n\t\tcol *= mix(vec3(1.0), d.rgb * 2.0, d.a);",
				"vec4 d = texture(decal_tex, UV2);\n\t\tcol = mix(col, d.rgb, d.a);")
		code = code.replace("texture(pattern_tex, UV2)", "texture(pattern_tex, UV)")
		if single_sided:
			code = code.replace("cull_disabled", "cull_back")
		if cloak:
			code = code.replace("texture(albedo_tex, UV)", "texture(albedo_tex, logo_uv)")
			# the guild logo on the classic cloak's set 1 (UV2): on set 2 - its own skin's set - the logo was stretched
			# over the whole cloak, the skull's eyes two black blots down the back (owner 2026-11-06: "the shield is
			# right but cloak is wrong"; set 1 puts the skull on the upper back, as the client draws it)
			code = code.replace("texture(logo_tex, logo_uv)", "texture(logo_tex, UV2)")
		if cutout:
			code = code.replace("//CUTOUT", "ALPHA = c.a;\n\tALPHA_SCISSOR_THRESHOLD = 0.5;")
		var s := Shader.new()
		s.code = code
		_classic_shaders[key] = s
	var m := ShaderMaterial.new()
	m.shader = _classic_shaders[key]
	return m
