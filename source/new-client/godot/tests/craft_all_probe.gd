extends Node
## Offline check (owner 2026-11-08: a CRAFT ALL button next to MAKE that "makes all items until your inventory fills up
## or you run out of materials"; greyed out unless the quality-of-life option is on; craft timers get shorter as a
## recipe gets easier): a fake server answers each Craft with a timer (a shorter one each time), uses the materials and
## puts the product in a free backpack slot.
##   A  materials for 3, room: 3 made, then "out of"     B  an interrupted timer: stops after it
##   C  one free slot: 1 made, then "backpack is full"  D  option off: the button greyed, nothing made
##   --skip-login --zone=N --world-mode=1.65 --probe=craft_all_probe


class FakeClient extends Node:
	var crafts := 0
	var secs := 1.2
	var interrupt := false

	func Craft(_id: int) -> void:
		crafts += 1
		_run.call_deferred()

	func _run() -> void:
		var t := secs
		secs = maxf(0.4, secs - 0.3)                    # (easier each time: a shorter timer)
		Server.craft_timer.emit(int(ceil(t)), "Currently Making: bronze dagger")
		await get_tree().create_timer(0.3 if interrupt else ceil(t)).timeout
		Server.craft_timer.emit(-1, "")
		if interrupt:
			return
		var bar: Dictionary = Server.inv[40]
		bar.count = int(bar.count) - 2
		if int(bar.count) <= 0:
			Server.inv.erase(40)
		for s in range(41, 80):
			if not Server.inv.has(s):
				ItemDb.add_runtime("srv:%d" % s, {"id": "srv:%d" % s, "name": "bronze dagger"})
				Server.inv[s] = {"id": "srv:%d" % s, "count": 1}
				break


var _msgs: Array = []


func _setup(bars: int, filled_to: int) -> void:
	Server.inv.clear()
	ItemDb.add_runtime("srv:40", {"id": "srv:40", "name": "bronze bar"})
	Server.inv[40] = {"id": "srv:40", "count": bars}
	for s in range(41, filled_to + 1):
		ItemDb.add_runtime("srv:%d" % s, {"id": "srv:%d" % s, "name": "junk"})
		Server.inv[s] = {"id": "srv:%d" % s, "count": 1}


func _wait_done(w: CraftWindow, limit: float) -> void:
	var until := Time.get_ticks_msec() + int(limit * 1000.0)
	while (not w._all.is_empty() or w._making >= 0) and Time.get_ticks_msec() < until:
		await get_tree().process_frame


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(3.0).timeout
	var was_on := CraftWindow.craft_all_enabled()
	var fc := FakeClient.new()
	Server.client = fc
	add_child(fc)
	PlayerData.message.connect(func(m): _msgs.append(String(m)))
	var w := CraftWindow.new()
	v.ui.root.add_child(w)
	w.build()
	w.show()
	var recipe := {"id": 7, "name": "bronze dagger", "skill": 1, "model": 0, "ingredients": [["bronze bar", 2]]}
	var ok := true
	var cases := [["A", 6, 41, false, 3, "out of bronze bar"], ["B", 6, 41, true, 1, "interrupted"],
			["C", 6, 78, false, 1, "backpack is full"]]
	CraftWindow.set_craft_all(true)
	for c in cases:
		_setup(int(c[1]), int(c[2]))
		fc.crafts = 0
		fc.secs = 1.2
		fc.interrupt = bool(c[3])
		_msgs.clear()
		w.selected = recipe
		w._update_make()
		w.craft_all()
		await _wait_done(w, 15.0)
		var last := String(_msgs[-1]) if _msgs.size() > 0 else ""
		var good: bool = fc.crafts == int(c[4]) and last.contains(String(c[5])) and w._all.is_empty()
		ok = ok and good
		print("[craftall] %s: %d made, said '%s'  %s" % [c[0], fc.crafts, last, "OK" if good else "WRONG"])
	CraftWindow.set_craft_all(false)
	_setup(6, 41)
	fc.crafts = 0
	w._update_make()
	var greyed: bool = w._craft_all.disabled
	w.craft_all()
	await get_tree().create_timer(0.5).timeout
	var good_d: bool = greyed and fc.crafts == 0
	ok = ok and good_d
	print("[craftall] D: option off - button greyed %s, %d made  %s" % [greyed, fc.crafts, "OK" if good_d else "WRONG"])
	CraftWindow.set_craft_all(was_on)
	print("[craftall] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
