extends Node
## Live check (owner 2026-11-06: realm gates - "friendly players can get thru them still like keep gates ... enemy
## faction having to attack/siege to get thru"), on the local test server with two of Claude's own test characters:
##   --role=solo   the GM test character alone: jumps itself to each gate and runs the plain steps (a GM's click
##                 forces doors open, so this checks ids, door states and damage, not the realm rule)
##   --role=gm     the GM test character: moves the plain one to each gate (/jump <name> to x y z region) and waits
##   --role=plain  a plain (PrivLevel 1) Hibernian test character - a GM forces every door open, so the realm check is
##                 only seen by a plain player:
##     1. Emain Macha's gate fort, outer gate 214000101 (Hibernia's): click it - it should open (DoorState), close again
##     2. Hadrian's Wall's milegate, outer gate 15000401 (Albion's): click it - "it belongs to an enemy realm", stays
##        shut; target it and hit it - its health should drop, and it still stays shut
## Every server message and door state is logged ("[gate]").
##   Godot --path godot res://tests/server_session.tscn -- --server=127.0.0.1 --account=<a> --password=<p> --char=<c>
##       --world-mode=1.65 --probe=gate_live_probe --role=gm|plain [--plain=<plain character's name>]
var v: Node
const EMAIN := {"door": 214000101, "region": 200, "x": 449025 + 260, "y": 298626, "z": 4256}
const HADRIAN := {"door": 15000401, "region": 1, "x": 610032, "y": 319259 - 260, "z": 3872}


func _log(s: String) -> void:
	print("[gate] %s" % s)


func _ready() -> void:
	# (the viewer adds the probe again after every zone change - a /jump: while the first one lives it runs on; if it
	# was freed with the old zone, this one carries on from the step it reached)
	var old = Engine.get_meta("gate_live_probe") if Engine.has_meta("gate_live_probe") else null
	if old != null and is_instance_valid(old) and old != self:
		return
	Engine.set_meta("gate_live_probe", self)
	v = get_tree().current_scene
	var role := String(v.args.get("role", "plain"))
	Server.world.connect(func(kind: String, d: Dictionary):
		if kind == "message":
			_log("message: %s" % String(d.get("text", "")))
		elif kind == "door_state":
			_log("door state %d open %s" % [int(d.door_id), d.open]))
	await get_tree().create_timer(8.0).timeout
	if role == "gm":
		await _gm(String(v.args.get("plain", "")))
	else:
		await _plain(role == "solo")
	get_tree().quit()


func _gm(plain: String) -> void:
	for g in [EMAIN, HADRIAN]:
		_log("gm: moving %s to door %d" % [plain, g.door])
		Server.command("/jump %s to %d %d %d %d" % [plain, g.x, g.y, g.z, g.region])
		await get_tree().create_timer(70.0).timeout


func _leaves(door_id: int) -> Array:
	var sw := v.npcs as ServerWorld
	var loaders: Array = (v.region.zones.values() if v.get("region") and v.region else [])
	if loaders.is_empty() and v.zone:
		loaders = [v.zone]
	for zl in loaders:
		if zl and (zl as ZoneLoader).door_by_id.has(door_id):
			return zl.door_by_id[door_id]
	return []


func _wait_near(g: Dictionary, secs: float) -> Array:
	var t := 0.0
	while t < secs:
		await get_tree().create_timer(1.0).timeout
		t += 1.0
		var leaves := _leaves(int(g.door))
		if not leaves.is_empty() and (leaves[0] as ZoneDoor).doorway().distance_to(v.player.global_position) < 30.0:
			await get_tree().create_timer(6.0).timeout                 # (the zone settled, the door's object in)
			return _leaves(int(g.door))
	return []


func _jump_self(g: Dictionary) -> void:
	Server.command("/jump to %d %d %d %d" % [g.x, g.y, g.z, g.region])


func _plain(solo := false) -> void:
	var sw := v.npcs as ServerWorld
	var leaves := []
	var step: int = Engine.get_meta("gate_live_step") if Engine.has_meta("gate_live_step") else 0
	if step == 0:
		await _emain(solo)
		Engine.set_meta("gate_live_step", 1)
	await _hadrian(solo)


func _emain(solo: bool) -> void:
	var sw := v.npcs as ServerWorld
	# 1. a friendly gate
	if solo:
		_jump_self(EMAIN)
	var leaves := await _wait_near(EMAIN, 60.0)
	if leaves.is_empty():
		_log("plain: never reached Emain Macha's gate (at %s)" % v.player.global_position)
	else:
		var d: ZoneDoor = leaves[0]
		_log("plain at Emain gate %d, %.1f m away; shut: %s; server object: %s" % [EMAIN.door,
				d.doorway().distance_to(v.player.global_position), not d.is_open, sw.doors.get(EMAIN.door, {})])
		sw.request_door(d)
		await get_tree().create_timer(2.5).timeout
		_log("FRIENDLY click: gate open now %s" % d.is_open)
		await get_tree().create_timer(6.0).timeout
		_log("FRIENDLY after 8.5 s: gate open %s" % d.is_open)


func _hadrian(solo: bool) -> void:
	var sw := v.npcs as ServerWorld
	# 2. an enemy gate
	if solo:
		_jump_self(HADRIAN)
	var leaves := await _wait_near(HADRIAN, 90.0)
	if leaves.is_empty():
		_log("plain: never reached Hadrian's Wall's gate (at %s)" % v.player.global_position)
		return
	var g: ZoneDoor = leaves[0]
	_log("plain at Hadrian's gate %d, %.1f m away; shut: %s; server object: %s" % [HADRIAN.door,
			g.doorway().distance_to(v.player.global_position), not g.is_open, sw.doors.get(HADRIAN.door, {})])
	sw.request_door(g)                                                # (also targets it: the server's door object)
	await get_tree().create_timer(2.5).timeout
	_log("ENEMY click: gate open now %s; target door oid %d" % [g.is_open, sw.target_door])
	# step up to it (2 m out, facing it) and hit it
	var mid := Vector3.ZERO
	for l in leaves:
		mid += (l as ZoneDoor).doorway() / float(leaves.size())
	var from: Vector3 = v.player.global_position
	var out := Vector3(from.x - mid.x, 0, from.z - mid.z).normalized()
	var stand := Vector3(mid.x, from.y, mid.z) + out * 0.6               # (against the shut gate, as a player stands)
	stand.y = (v.zone as ZoneLoader).height_at(stand.x, stand.z, from.y) if v.zone else from.y
	v.player.global_position = stand
	_log("ENEMY standing %.1f m from the gate's doorway" % Vector2(stand.x - mid.x, stand.z - mid.z).length())
	var dd: Vector3 = mid - v.player.global_position
	v.player.face(atan2(-dd.x, -dd.z))                                  # (the yaw the position packets send)
	await get_tree().create_timer(1.5).timeout
	_log("ENEMY in view of the gate: %s; health before %s" % [sw.target_in_view(v.player.global_position),
			sw.doors.get(HADRIAN.door, {}).get("health", "?")])
	PlayerData.attack_toggle_requested.emit()
	for i in 6:
		await get_tree().create_timer(4.0).timeout
		_log("ENEMY hitting: health %s, gate open %s" % [sw.doors.get(HADRIAN.door, {}).get("health", "?"), g.is_open])
	PlayerData.attack_toggle_requested.emit()
	_log("done")
