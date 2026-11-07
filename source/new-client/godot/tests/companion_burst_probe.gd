extends Node
## Offline check (owner 2026-11-07: "my game freezes every time I train a companion, equip them, add or remove an item
## from their inventory"): a /companionui answer's burst - text slots (op 3), the original client's list lines (ops 12 /
## 13), a gear list (op 7 rows + its end) - handled in one frame redraws the windows ONCE (CompanionNet._changed_soon),
## every message still stored at once; a burst in a later frame redraws once more.
##   --skip-login --zone=N --world-mode=1.65 --probe=companion_burst_probe


func _msg(op: int, rest: Array) -> PackedByteArray:
	var b := PackedByteArray([0, 0x48, 1, op])
	b.append_array(PackedByteArray(rest))
	return b


func _text_msg(slot: int, s: String) -> PackedByteArray:
	var b := _msg(3, [slot])
	b.append_array(s.to_ascii_buffer())
	b.resize(130)
	return b


func _gear_row(row: int, slot: int, model: int, item_name: String) -> PackedByteArray:
	var b := _msg(7, [row, slot, model & 0xFF, model >> 8, 0, 0, 10, 90, 100, 0, 0, 0])
	b.append_array(item_name.to_ascii_buffer())
	b.resize(80)
	return b


func _ready() -> void:
	await get_tree().create_timer(2.0).timeout
	var net := CompanionNet.new()
	add_child(net)
	var redraws := [0]
	net.changed.connect(func(): redraws[0] += 1)
	# one action's burst: 7 texts, 31 list lines, a 3-item gear list
	for i in 7:
		net.handle(_text_msg(40 + i, "STAT %d" % i))
	for i in 31:
		net.handle(_msg(12 if i > 0 else 13, [0, i, 0, 0]))
	net.handle(_gear_row(0, 10, 4, "Longsword"))
	net.handle(_gear_row(0, 25, 50, "Mithril Chain Hauberk"))
	net.handle(_gear_row(0, 41, 6, "Greatsword"))
	net.handle(_msg(7, [0, 0xFF]))
	var stored := String(net.texts.get(46, "")) == "STAT 6" and (net.gear.get(0, {}) as Dictionary).size() == 3
	var before: int = redraws[0]
	await get_tree().process_frame
	await get_tree().process_frame
	var one: int = redraws[0]
	net.handle(_text_msg(3, "later"))
	net.handle(_text_msg(4, "later"))
	await get_tree().process_frame
	await get_tree().process_frame
	var two: int = redraws[0]
	var ok := stored and before == 0 and one == 1 and two == 2
	print("[burst] stored at once: %s  redraws during the burst: %d  after its frame: %d  after a later burst: %d  %s" % [
			stored, before, one, two, "OK" if ok else "WRONG"])
	print("[burst] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
