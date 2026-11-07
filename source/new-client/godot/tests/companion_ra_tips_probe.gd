extends Node
## Offline check (owner 2026-11-06: "hovering over realm abilities inside a companions realm abilities menu still doesnt
## show tooltip info"): a companion's REALM ABILITIES row hovered - its tooltip at once from the class data
## (abilities.json's description), the server asked for that companion's own delve ("/companionui delve <row> ra
## <index>"), and the server's text shown once it answers. A stand-in client and a made-up answer.
##   --skip-login --zone=N --world-mode=1.65 --probe=companion_ra_tips_probe


class FakeClient extends Node:
	var commands: Array = []

	func Command(t: String) -> void:
		commands.append(t)


func _texts(c: Object) -> String:
	if c == null:
		return "(none)"
	var out := []
	for l in (c as Node).find_children("*", "", true, false):
		if l is Label or l is RichTextLabel:
			out.append(String(l.text))
	if c is Label or c is RichTextLabel:
		out.append(String(c.text))
	return " | ".join(out)


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(5.0).timeout
	var fake := FakeClient.new()
	add_child(fake)
	Server.client = fake
	var net := CompanionNet.new()
	add_child(net)
	var w := SrvCompanionWindow.new()
	w.net = net
	v.ui.root.add_child(w)
	w.build()
	net.ras[0] = [{"name": "Augmented Strength", "rank": 2, "sub": 3, "points": 7, "level": 1, "max": 5, "cost": 3, "index": 0},
			{"name": "Mastery of Pain", "rank": 2, "sub": 3, "points": 7, "level": 0, "max": 5, "cost": 1, "index": 4}]
	w.open_row(0, 3)
	await get_tree().process_frame
	var tips := []
	for c in w.find_children("*", "", true, false):
		if c.get("server_cmd") is String and String(c.get("server_cmd")).contains(" ra "):
			tips.append(c)
	var ok := tips.size() == 2
	print("[ratips] realm ability rows with a tooltip: %d" % tips.size())
	if tips.size() > 0:
		var t: Control = tips[0]
		var before := _texts(t._make_custom_tooltip(""))
		fake.commands.clear()
		t.notification(Control.NOTIFICATION_MOUSE_ENTER)
		Server.take_detail({"mode": 0, "caption": "Augmented Strength",
				"lines": ["Increases Strength by:", "Level 1: 4", "Level 2: 8", "Level 3: 12"]})
		var after := _texts(t._make_custom_tooltip(""))
		var c1: bool = before.containsn("Increases Strength")
		var c2: bool = fake.commands == ["&companionui delve 0 ra 0"]
		var c3: bool = after.containsn("Level 3: 12")
		ok = ok and c1 and c2 and c3
		print("[ratips] before the server answers: '%s'  %s" % [before, "OK" if c1 else "WRONG"])
		print("[ratips] hover asked: %s  %s" % [fake.commands, "OK" if c2 else "WRONG"])
		print("[ratips] after the answer: '%s'  %s" % [after, "OK" if c3 else "WRONG"])
	print("[ratips] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
