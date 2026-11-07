extends Control
## (no class_name - loaded by its path: SrvCompanionWindow.CompanionDoll)
## A hired companion on its GEAR page's paper doll (owner 2026-11-07: "add a paper doll there that doesn't have any
## conflicts or major performance issues when its closed (like the player has)"): the same classic figure as in the
## world, idle, in a small 3D world of its own - your inventory's DollView, for the companion. Dressed from the gear the
## server lists (op 7: model, dye, the armour's mesh), with its race, face and hair from its view in the world (its
## roster model's race when it isn't near). Drag it sideways to turn it.
## Nothing of it is kept when unseen: SrvCompanionWindow makes it when the GEAR page shows and frees it (viewport,
## figure, gear) when the page changes or the window closes. Its viewport draws only while it is on screen.

var net: CompanionNet
var window: Control                               # its SrvCompanionWindow (equip_from)
var row := 0
var _vp: SubViewport
var _pivot: Node3D
var _figure: Fig3Figure
var _gear_nodes: Array = []
var _dressed := ""                                # the gear it wears (slot:model:dye ...): dressed again only on a change
var _turn := false
var _npc: Npc                                     # its view in the world, when it is near (race, look, emblems)

const FIT_HEIGHT := 1.85


func build() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	_vp = SubViewport.new()
	_vp.size = Vector2i(size * 3.0)
	_vp.own_world_3d = true
	_vp.transparent_bg = true
	_vp.msaa_3d = Viewport.MSAA_4X
	_vp.render_target_update_mode = SubViewport.UPDATE_WHEN_VISIBLE
	add_child(_vp)
	var view := TextureRect.new()
	view.texture = _vp.get_texture()
	view.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	view.stretch_mode = TextureRect.STRETCH_SCALE
	view.set_anchors_preset(Control.PRESET_FULL_RECT)
	view.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(view)
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_CLEAR_COLOR
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(0.55, 0.55, 0.6)
	_vp.add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-35, 25, 0)
	sun.light_energy = 1.1
	_vp.add_child(sun)
	var cam := Camera3D.new()
	cam.fov = 30
	cam.keep_aspect = Camera3D.KEEP_HEIGHT
	cam.position = Vector3(0, 0.95, 4.2)
	_vp.add_child(cam)
	cam.look_at(Vector3(0, 0.92, 0))
	_pivot = Node3D.new()
	_pivot.rotation.y = deg_to_rad(15)
	_vp.add_child(_pivot)
	_make_figure()


## Its figure: the race and look of its view in the world, or its roster model's race with its chosen hair.
func _make_figure() -> void:
	var r: Dictionary = net.roster.get(row, {})
	var sw = Server.viewer().get("npcs") if Server.viewer() else null
	var v: Node = (sw as ServerWorld)._view_of(int(r.get("oid", 0))) if sw is ServerWorld and int(r.get("oid", 0)) > 0 else null
	_npc = v as Npc if v is Npc and is_instance_valid(v) and (v as Npc).model is Fig3Figure else null
	var prefix := String(_npc.info.get("race", "")) if _npc else ""
	if prefix == "" and sw is ServerWorld:
		prefix = String((sw as ServerWorld)._model_info(int(r.get("model", 0))).get("race", ""))
	if prefix == "" or prefix == "<null>":
		return
	var f := Fig3Figure.make()
	_pivot.add_child(f)
	if not f.load_figure(prefix):
		f.queue_free()
		return
	var look: Dictionary = _npc._look(f) if _npc else {}
	if _npc == null and Fig3Figure.classic() and r.has("hair"):
		look["hair_index"] = int(r.hair)
	f.apply(look)
	_figure = f
	dress()
	f.play("idle")


## The gear the server listed, worn as on its figure in the world (Player.dress: armour with its dyes, cloak, helm, the
## weapons - the active set in hand, the others hung). Again only when the list changed.
func dress() -> void:
	if _figure == null or not is_instance_valid(_figure):
		return
	var items: Dictionary = net.gear.get(row, {})
	var key := ""
	for s in items:
		if int(s) < 40:
			var it: Dictionary = items[s]
			key += "%d:%d:%d:%d;" % [int(s), int(it.model), int(it.color), int(it.icon)]
	if key == _dressed:
		return
	_dressed = key
	var npc_ok := _npc != null and is_instance_valid(_npc)
	var fig_emblem := ((_npc.model as Fig3Figure).cloak_emblem if npc_ok and _npc.model is Fig3Figure else 0)
	var item_at := func(slot: int) -> Dictionary:
		var it: Dictionary = items.get(slot, {})
		if it.is_empty():
			return {}
		# op 7's [14..15] is the item's extension (the armour's mesh), not an icon (srv_companion_window GearSlot)
		return {"model": int(it.model), "color": int(it.color), "extension": int(it.icon),
				"object_type": int(it.object_type),
				"emblem": fig_emblem if slot == 26 else (int(_npc._weapon_emblems.get(slot, 0)) if npc_ok else 0),
				"effect": int(_npc._weapon_fx.get(slot, 0)) if npc_ok else 0}
	var active := "twohand" if items.has(12) else ("standard" if items.has(10) or items.has(11) else "ranged")
	if npc_ok:
		var ws := int(_npc.spawn.get("weapon_slots", -1))
		if ws >= 0 and ws != 0xFF:
			match ws & 0x0F:
				2: active = "twohand"
				3: active = "ranged"
				0, 1: active = "standard"
	var flags: Dictionary = _npc.spawn.get("gear", {}) if npc_ok and _npc.spawn.get("gear") is Dictionary else {}
	for g in _gear_nodes:
		if is_instance_valid(g):
			g.queue_free()
	_gear_nodes = Player.dress(_figure, item_at, active, false, true, flags)
	_fit.call_deferred()


## Every race and size fills the doll the same (DollView._fit): its real height scaled to one height, feet on the floor.
func _fit() -> void:
	if not is_inside_tree() or _pivot == null:
		return
	await get_tree().process_frame
	if not is_instance_valid(_pivot):
		return
	_pivot.scale = Vector3.ONE
	_pivot.position = Vector3.ZERO
	var lo := INF
	var hi := -INF
	for mi in _pivot.find_children("*", "MeshInstance3D", true, false):
		var m := mi as MeshInstance3D
		if not m.is_visible_in_tree() or m.mesh == null:
			continue
		var box: AABB = _pivot.global_transform.affine_inverse() * m.global_transform * m.get_aabb()
		lo = minf(lo, box.position.y)
		hi = maxf(hi, box.end.y)
	if hi > lo:
		var k := FIT_HEIGHT / (hi - lo)
		_pivot.scale = Vector3.ONE * k
		_pivot.position.y = -lo * k


## An item from your backpack dropped on the figure: put on where it fits (the server's equip picks the slot).
func _can_drop_data(_p: Vector2, data: Variant) -> bool:
	return window != null and InventorySheet.is_item(data) and int(data.slot) >= 40 and int(data.slot) <= 79


func _drop_data(_p: Vector2, data: Variant) -> void:
	window.call("equip_from", int(data.slot), 0, false)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and (event as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT:
		_turn = event.pressed
	elif event is InputEventMouseMotion and _turn and _pivot:
		_pivot.rotation.y += (event as InputEventMouseMotion).relative.x * 0.02
