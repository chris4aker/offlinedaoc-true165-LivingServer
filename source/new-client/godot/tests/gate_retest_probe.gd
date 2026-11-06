extends Node
## Live re-test (owner 2026-11-06: the period door rules - milegates / gate forts open for anyone and can be broken;
## keep doors port their own realm through, enemies break them; border / portal keep doors cannot be damaged), with
## Claude's GM test character (a Hibernian; a GM's click forces plain doors open, so the realm rule itself is not
## seen here):
##   1. Hadrian's Wall milegate 15000401 (Albion): its realm on the door object, a click opens it, melee from ~2 m
##   2. Emain Macha's keep door 214015801 (the keep's realm): a click ports you through, the door stays shut
##   3. Castle Sauvage's border keep door 11020501 (Albion): melee does no damage
##   Godot --path godot res://tests/server_session.tscn -- --server=127.0.0.1 --account=<a> --password=<p> --char=<c>
##       --world-mode=1.65 --probe=gate_retest_probe
var v: Node
const STEPS := [
	{"what": "milegate", "door": 15000401, "region": 1, "x": 610032, "y": 319259, "z": 3872},
	{"what": "keepdoor", "door": 214015801, "region": 200, "x": 427319, "y": 318463, "z": 4728},
	{"what": "border", "door": 11020501, "region": 1, "x": 585088, "y": 478181, "z": 2600},
]


func _log(s: String) -> void:
	print("[retest] %s" % s)


func _ready() -> void:
	# (the viewer adds the probe again after every zone change: the first one runs on while it lives; a later one
	# carries on from the step reached if it was freed)
	var old = Engine.get_meta("gate_retest") if Engine.has_meta("gate_retest") else null
	if old != null and is_instance_valid(old) and old != self:
		return
	Engine.set_meta("gate_retest", self)
	v = get_tree().current_scene
	if not Engine.has_meta("gate_retest_hooked"):
		Engine.set_meta("gate_retest_hooked", true)
		Server.world.connect(func(kind: String, d: Dictionary):
			if kind == "message":
				var t := String(d.get("text", ""))
				if not (t.contains("casts a spell") or t.contains("appeal") or t.contains("Welcome back")):
					print("[retest] message: %s" % t)
			elif kind == "door_state":
				print("[retest] door state %d open %s" % [int(d.door_id), d.open]))
	await get_tree().create_timer(8.0).timeout
	var step: int = Engine.get_meta("gate_retest_step") if Engine.has_meta("gate_retest_step") else 0
	while step < STEPS.size():
		await _run(STEPS[step])
		step += 1
		Engine.set_meta("gate_retest_step", step)
	_log("done")
	get_tree().quit()


func _leaves(door_id: int) -> Array:
	var loaders: Array = (v.region.zones.values() if v.get("region") and v.region else [])
	if loaders.is_empty() and v.zone:
		loaders = [v.zone]
	for zl in loaders:
		if zl and (zl as ZoneLoader).door_by_id.has(door_id):
			return zl.door_by_id[door_id]
	return []


func _run(s: Dictionary) -> void:
	var sw := v.npcs as ServerWorld
	_log("== %s %d" % [s.what, s.door])
	Server.command("/jump to %d %d %d %d" % [int(s.x) + 300, s.y, int(s.z) + 50, s.region])
	var leaves := []
	for i in 60:
		await get_tree().create_timer(1.0).timeout
		leaves = _leaves(int(s.door))
		if not leaves.is_empty() and (leaves[0] as ZoneDoor).doorway().distance_to(v.player.global_position) < 40.0:
			break
	if leaves.is_empty():
		_log("never reached door %d (at %s)" % [s.door, v.player.global_position])
		return
	await get_tree().create_timer(6.0).timeout
	leaves = _leaves(int(s.door))
	var mid := Vector3.ZERO
	for l in leaves:
		mid += (l as ZoneDoor).doorway() / float(leaves.size())
	var along := Vector3.RIGHT
	if leaves.size() >= 2:
		along = (leaves[1] as ZoneDoor).doorway() - (leaves[0] as ZoneDoor).doorway()
	else:
		along = (leaves[0] as Node3D).global_transform.basis.x
	along.y = 0
	var through := along.normalized().cross(Vector3.UP).normalized()
	# the side the player came from (the jump spot) is "this side"
	if through.dot(v.player.global_position - mid) < 0:
		through = -through
	var stand := mid + through * 2.0
	stand.y = (v.zone as ZoneLoader).height_at(stand.x, stand.z, mid.y) if v.zone else mid.y
	v.player.global_position = stand
	var dd: Vector3 = mid - stand
	v.player.face(atan2(-dd.x, -dd.z))
	await get_tree().create_timer(2.0).timeout
	var info: Dictionary = sw.doors.get(int(s.door), {})
	_log("%s %d: server object %s; shut %s; standing %.1f m from its doorway" % [s.what, s.door, info,
			not (leaves[0] as ZoneDoor).is_open, Vector2(stand.x - mid.x, stand.z - mid.z).length()])
	if s.what == "milegate" or s.what == "keepdoor":
		var before: Vector3 = v.player.global_position
		sw.request_door(leaves[0])
		await get_tree().create_timer(3.0).timeout
		var after: Vector3 = v.player.global_position
		_log("%s click: door open %s; you moved %.1f m (%s side of the door now)" % [s.what,
				(leaves[0] as ZoneDoor).is_open, before.distance_to(after),
				"same" if through.dot(after - mid) > 0 else "OTHER"])
		await get_tree().create_timer(6.0).timeout
		_log("%s after 9 s: door open %s" % [s.what, (leaves[0] as ZoneDoor).is_open])
		if s.what == "keepdoor":
			return
		v.player.global_position = stand
		v.player.face(atan2(-dd.x, -dd.z))
		await get_tree().create_timer(1.0).timeout
	if sw.target_door == 0 and not info.is_empty():                    # (a click targets it; for the border door too)
		sw.target_door = int(info.oid)
		Server.client.call("Target", int(info.oid), 0x8000 | 0x6000)
		await get_tree().create_timer(1.0).timeout
	_log("%s: hitting from %.1f m, in view %s, health before %s" % [s.what,
			Vector2(v.player.global_position.x - mid.x, v.player.global_position.z - mid.z).length(),
			sw.target_in_view(v.player.global_position), sw.doors.get(int(s.door), {}).get("health", "?")])
	PlayerData.attack_toggle_requested.emit()
	await get_tree().create_timer(16.0).timeout
	PlayerData.attack_toggle_requested.emit()
	_log("%s: after 16 s health %s, open %s" % [s.what, sw.doors.get(int(s.door), {}).get("health", "?"),
			(leaves[0] as ZoneDoor).is_open])
