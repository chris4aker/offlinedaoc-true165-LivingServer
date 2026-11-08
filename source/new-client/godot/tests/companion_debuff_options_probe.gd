extends Node
## Offline check (0.5, owner 2026-11-08 via the server chat: "pick which debuffs are used before nuking" + "hovering over
## items in the AI options shows the spell tooltips"): op 14 rows with spell ids after the names. The debuff lines
## (kind 28) are drawn indented under "Debuff before nuking" (kind 7), dimmed and not pickable on Never; their captions
## and the pet choice ask the server's spell delve on hover; picking a debuff choice sends "option <row> 28 <slot> <i>".
##   --skip-login --zone=N --world-mode=1.65 --probe=companion_debuff_options_probe


class FakeClient extends Node:
	var sent: Array = []

	func Command(t: String) -> void:
		sent.append(t)


static func _row(r: int, kind: int, slot: int, chosen: int, names: Array, ids: Array) -> PackedByteArray:
	var b := PackedByteArray([0, 0x48, 0, 14, r, kind, slot, chosen, names.size(), 0, 0, 0, 0])
	for n in names:
		b.append_array(String(n).to_ascii_buffer())
		b.append(0)
	for id in ids:
		for k in 4:
			b.append((int(id) >> (8 * k)) & 0xFF)
	return b


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(3.0).timeout
	var fc := FakeClient.new()
	Server.client = fc
	add_child(fc)
	var net := CompanionNet.new()
	add_child(net)
	var w := SrvCompanionWindow.new()
	w.net = net
	v.ui.root.add_child(w)
	w.build()
	var ok := true
	for master in [0, 2]:                                # Automatic, then Never
		net.handle(_row(0, 2, 0, 1, ["Lesser Zombie", "Greater Zombie"], [5001, 5002]))      # pet: ids per choice
		net.handle(_row(0, 7, 1, master, ["Debuff before nuking", "Automatic", "Always", "Never"], []))
		net.handle(_row(0, 28, 1, 1, ["Dread Shriek", "Automatic", "Use", "Skip"], [6001, 0, 0, 0]))
		net.handle(_row(0, 28, 2, 2, ["Withering Ward", "Automatic", "Use", "Skip"], [6002, 0, 0, 0]))
		net.handle(_row(0, 9, 1, 0, ["Old row with no ids", "Off", "On"], []))
		net.handle(PackedByteArray([0, 0x48, 0, 14, 0, 0xFF]))
		w.open_row(0, 4)
		w.refresh()
		await get_tree().process_frame
		await get_tree().process_frame
		var rows: Array = net.options.get(0, [])
		var ids_ok: bool = rows.size() == 5 and (rows[0].ids as Array) == [5001, 5002] and (rows[1].ids as Array).is_empty() \
				and (rows[2].ids as Array) == [6001, 0, 0, 0] and (rows[4].ids as Array).is_empty()
		var tips := {}
		for t in w.find_children("*", "", true, false):
			if t is SrvCompanionWindow.SkillTipLabel:
				tips[String((t as SrvCompanionWindow.SkillTipLabel).server_key)] = t
		var tips_ok: bool = tips.has("csp:0:5002") and tips.has("csp:0:6001") and tips.has("csp:0:6002") and tips.size() == 3
		var shriek: Label = null
		var dd: Array = []                               # the debuff rows' drop-downs
		for c in w.find_children("*", "", true, false):
			if c is Label and (c as Label).text == "DREAD SHRIEK":
				shriek = c
			if c is OptionButton and (c as OptionButton).item_count == 3 and (c as OptionButton).get_item_text(1) == "USE":
				dd.append(c)
		var indent_ok: bool = shriek != null and shriek.position.x > 6.0
		var off_ok := true
		for o in dd:
			off_ok = off_ok and (o as OptionButton).disabled == (master == 2)
		fc.sent.clear()
		if master == 0 and dd.size() > 0:
			(dd[0] as OptionButton).item_selected.emit(1)
		var cmd_ok: bool = master != 0 or fc.sent == ["&companionui option 0 28 1 1"]
		var good: bool = ids_ok and tips_ok and indent_ok and dd.size() == 2 and off_ok and cmd_ok
		ok = ok and good
		print("[debuffopts] master %s: ids %s, tips %s, indented %s, %d debuff rows disabled=%s, pick sent %s  %s" % [
				["Automatic", "", "Never"][master], ids_ok, tips.keys(), indent_ok, dd.size(), master == 2 if off_ok else "WRONG",
				fc.sent, "OK" if good else "WRONG"])
	print("[debuffopts] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
