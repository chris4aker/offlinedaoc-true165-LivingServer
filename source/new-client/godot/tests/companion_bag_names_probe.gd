extends Node
## Offline check (owner 2026-11-07: "a long name like silvered lion embossed greaves will do like a standard ... to show
## the name is shortened rather than cut off from the box" - no smaller text): a companion bag row's name stays inside
## its row at its usual font size and ends in an ellipsis; a short name is shown whole.
##   --skip-login --zone=N --world-mode=1.65 --probe=companion_bag_names_probe


class FakeClient extends Node:
	func Command(_t: String) -> void:
		pass


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(3.0).timeout
	Server.client = FakeClient.new()
	add_child(Server.client)
	var net := CompanionNet.new()
	add_child(net)
	var w := SrvCompanionWindow.new()
	w.net = net
	v.ui.root.add_child(w)
	w.build()
	net.gear[0] = {40: {"slot": 40, "model": 50, "color": 0, "level": 20, "quality": 90, "condition": 100,
			"object_type": 37, "icon": 0, "name": "Silvered Lion Embossed Greaves", "count": 0},
			41: {"slot": 41, "model": 4, "color": 0, "level": 5, "quality": 90, "condition": 60, "object_type": 3,
			"icon": 0, "name": "Longsword", "count": 0}}
	w.open_row(0, 0)
	w.refresh()
	await get_tree().process_frame
	await get_tree().process_frame
	var ok := true
	for cell in w.find_children("*", "", true, false):
		if not (cell is Control) or cell.get("slot") == null or int(cell.get("slot")) < 40 or (cell.get("item") as Dictionary).is_empty():
			continue
		var c := cell as Control
		for l in c.get_children():
			if l is Label and String((l as Label).text).length() > 6:
				var lab := l as Label
				var inside := lab.position.x + lab.size.x <= c.size.x + 0.5
				var ell := lab.text_overrun_behavior == TextServer.OVERRUN_TRIM_ELLIPSIS
				var fs := lab.get_theme_font_size("font_size")
				var good := inside and ell and fs >= 7
				ok = ok and good
				print("[bagnames] '%s': label %.0f..%.0f of row %.0f, ellipsis %s, font %d  %s" % [lab.text,
						lab.position.x, lab.position.x + lab.size.x, c.size.x, ell, fs, "OK" if good else "WRONG"])
	print("[bagnames] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
