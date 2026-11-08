extends SceneTree
## Offline check (owner 2026-11-08: dismissing Gwendolyn - row 3 of 4 - hid Osbert, row 4, until a new hire): the server
## keeps fixed row numbers, so after a refresh without row 2 (0-based) the roster keeps rows 0, 1 and 3 and drops 2.
##   tools/godot/Godot_v4.7.2-stable_win64_console.exe --headless --path godot -s res://tests/companion_dismiss_rows_probe.gd


func _row(r: int) -> PackedByteArray:
	var b := PackedByteArray()
	b.resize(21)
	b[1] = 0x48
	b[3] = 4
	b[4] = 1
	b[5] = r
	b[7] = 10 + r
	return b


func _op5(used: int) -> PackedByteArray:
	var b := PackedByteArray([0, 0x48, 0, 5, used, 4, 7, 0])
	return b


func _initialize() -> void:
	var net = load("res://game/companions/companion_net.gd").new()
	for r in 4:
		net.handle(_row(r))
	net.handle(_op5(4))
	var first: Array = net.roster.keys()
	first.sort()
	for r in [0, 1, 3]:                                  # row 2 dismissed: the server re-sends the others in place
		net.handle(_row(r))
	net.handle(_op5(3))
	var after: Array = net.roster.keys()
	after.sort()
	net.handle(_op5(0))                                  # everyone released: no rows sent
	var none: int = net.roster.size()
	var ok: bool = first == [0, 1, 2, 3] and after == [0, 1, 3] and none == 0
	print("[dismiss] before %s, after dismissing row 2 %s, after releasing all %d  %s" % [first, after, none,
			"OK" if ok else "WRONG"])
	print("[dismiss] %s" % ("PASS" if ok else "FAIL"))
	if net is Node:
		net.free()
	quit()
