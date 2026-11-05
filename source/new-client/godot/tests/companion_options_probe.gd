extends Node
## Offline check (reset day C17, 2026-11-05, the client side of S33): the companion window's AI OPTIONS tab from op 14
## rows - shown only for a companion with choices; a Bonedancer's sub-pets with the x/75 count, a minstrel's songs per
## slot and Twist; picking sends the agreed /companionui verb. A stand-in client, made-up rows, a shot to --out.
##   --skip-login --zone=N --world-mode=1.65 --probe=companion_options_probe --out=<dir>


class FakeClient extends Node:
	var commands: Array = []

	func Command(t: String) -> void:
		commands.append(t)


func _op14(row: int, kind: int, slot: int, chosen: int, names: Array, used := 0, cap := 0) -> PackedByteArray:
	var b := PackedByteArray([0, 0x48, 1, 14, row, kind, slot, chosen, names.size()])
	b.resize(13)
	b.encode_u16(9, used)
	b.encode_u16(11, cap)
	for n in names:
		b.append_array(String(n).to_ascii_buffer())
		b.append(0)
	return b


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(5.0).timeout
	var fake := FakeClient.new()
	add_child(fake)
	var was = Server.client
	Server.client = fake
	var net := CompanionNet.new()
	add_child(net)
	var w := SrvCompanionWindow.new()
	w.net = net
	v.ui.root.add_child(w)
	w.build()
	w.open_row(1, 2)
	await get_tree().process_frame
	var hidden_without: bool = not (w._tabs[4] as Button).visible
	# row 0: a Bonedancer (sub-pets), row 1: a minstrel (songs + Twist)
	for m in [_op14(0, 3, 1, 1, ["Automatic", "None", "Bone Guard (10)", "Boneknitter (10)", "Bone Archer (10)"], 30, 75),
			_op14(0, 3, 2, 0, ["Automatic", "None", "Bone Guard (10)", "Boneknitter (10)", "Bone Archer (10)"], 30, 75),
			_op14(0, 3, 3, 2, ["Automatic", "None", "Bone Guard (10)", "Boneknitter (10)", "Bone Archer (10)"], 30, 75),
			[0, 0x48, 1, 14, 0, 0xFF],
			_op14(1, 4, 1, 0, ["Automatic", "Battle Song", "Power Song"]),
			_op14(1, 4, 2, 1, ["Automatic", "Battle Song", "Power Song"]),
			_op14(1, 4, 3, 0, ["Automatic", "Speed Song"]),
			_op14(1, 1, 0, 1, ["Off", "On"]),
			[0, 0x48, 1, 14, 1, 0xFF],
			_op14(2, 2, 0, 0, ["Automatic", "Matter Simulacrum", "Spirit Simulacrum", "Body Simulacrum"]),
			_op14(2, 5, 1, 1, ["Pull with", "Automatic", "Root", "Bolt"]),     # (a later AI choice: kind 5, generic)
			[0, 0x48, 1, 14, 2, 0xFF],
			# row 3: a Paladin tank - chants, then the server's generic rows (kind 5 Aggro, 10 Guard and protect, 6 Heal at)
			_op14(3, 4, 1, 0, ["Automatic", "Battle Zeal", "Fury of the Gods"]),
			_op14(3, 4, 2, 0, ["Automatic", "Rejuvenating Chant"]),
			_op14(3, 5, 0, 0, ["Aggro", "Automatic", "Hold it (taunt)", "Shed it (detaunt)"]),
			_op14(3, 10, 0, 2, ["Guard and protect", "Automatic", "You", "The healer", "Off"]),
			_op14(3, 6, 0, 0, ["Heal at", "Automatic", "Early (90%)", "Late (50%)"]),
			_op14(3, 8, 0, 1, ["Bolts", "Automatic", "Nukes only"]),
			_op14(3, 11, 0, 0, ["Pet", "Automatic (on)", "Off"]),
			[0, 0x48, 1, 14, 3, 0xFF],
			# row 4: the server's worst case (2026-11-05), a Minstrel with 9 rows and its longest choice chosen
			_op14(4, 1, 0, 1, ["Off", "On"]),
			_op14(4, 4, 1, 0, ["Automatic", "Battle Song", "Power Song"]),
			_op14(4, 4, 2, 0, ["Automatic", "Battle Song", "Power Song"]),
			_op14(4, 4, 3, 0, ["Automatic", "Speed Song"]),
			_op14(4, 5, 0, 0, ["Aggro", "Automatic", "Hold it (taunt)", "Shed it (detaunt)"]),
			_op14(4, 7, 0, 0, ["Debuff before nuking", "Automatic", "Always", "Never"]),
			_op14(4, 8, 0, 0, ["Bolts", "Automatic", "Nukes only"]),
			_op14(4, 17, 0, 0, ["Amnesia", "Automatic", "Off"]),
			_op14(4, 18, 0, 1, ["Fight", "Automatic", "Support (stay back: songs, spells, no melee)", "Melee"]),
			[0, 0x48, 1, 14, 4, 0xFF]]:
		net.handle(PackedByteArray(m))
	await get_tree().process_frame
	var shown_with: bool = (w._tabs[4] as Button).visible
	w.show_page(4)
	await get_tree().process_frame
	fake.commands.clear()
	var buttons := []
	var lists := []
	for c in w._options_box.get_children():
		if c is OptionButton:
			lists.append(c)
		elif c is Button:
			buttons.append(c)
	(buttons[0] as Button).pressed.emit()                              # charm: "Off"
	(lists[0] as OptionButton).item_selected.emit(2)                   # in combat: Power Song
	print("[opts] tab hidden without choices %s, shown with %s; rows %d lists %d buttons %d; sent %s" % [hidden_without,
			shown_with, (net.options.get(1, []) as Array).size(), lists.size(), buttons.size(), fake.commands])
	var ok: bool = hidden_without and shown_with and fake.commands == ["&companionui charm 1 off", "&companionui song 1 1 2"]
	await get_tree().create_timer(0.3).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(String(v.args.get("out", "user://")) + "/companion_options_songs.png")
	# the Bonedancer's tab
	w.open_row(0, 4)
	fake.commands.clear()
	await get_tree().process_frame
	var caps := []
	lists.clear()
	for c in w._options_box.get_children():
		if c is Label and (c as Label).text.begins_with("SUB-PETS"):
			caps.append((c as Label).text)
		if c is OptionButton:
			lists.append(c)
	(lists[2] as OptionButton).item_selected.emit(3)                   # sub-pet 3: Boneknitter
	print("[opts] bonedancer: %s, lists %d, sent %s" % [caps, lists.size(), fake.commands])
	ok = ok and caps == ["SUB-PETS 30 / 75"] and lists.size() == 3 and fake.commands.has("&companionui subpet 0 3 3")
	await get_tree().create_timer(0.3).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(String(v.args.get("out", "user://")) + "/companion_options_bd.png")
	(lists[0] as OptionButton).show_popup()                           # (its choices open, for the owner's shot)
	await get_tree().create_timer(0.4).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(String(v.args.get("out", "user://")) + "/companion_options_bd_open.png")
	(lists[0] as OptionButton).get_popup().hide()
	# a Cabalist's pet choice, its list open
	w.open_row(2, 4)
	await get_tree().process_frame
	var opt_lists := []
	var gen_caption := false
	for c in w._options_box.get_children():
		if c is OptionButton:
			opt_lists.append(c)
		if c is Label and (c as Label).text == "PULL WITH":
			gen_caption = true
	var pet_list: OptionButton = opt_lists[0]
	var generic: OptionButton = opt_lists[1]
	fake.commands.clear()
	generic.item_selected.emit(2)
	print("[opts] a later AI choice: caption %s, sent %s" % [gen_caption, fake.commands])
	ok = ok and gen_caption and fake.commands == ["&companionui option 2 5 1 2"]
	pet_list.show_popup()
	await get_tree().create_timer(0.4).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(String(v.args.get("out", "user://")) + "/companion_options_pet_open.png")
	pet_list.get_popup().hide()
	# the Paladin's 7 rows: all drawn inside the tab, the two-choice ones as buttons, a pick sends "option"
	w.open_row(3, 4)
	fake.commands.clear()
	await get_tree().process_frame
	var labels := []
	var lowest := 0.0
	var two_buttons := 0
	var olists := []
	for c in w._options_box.get_children():
		if c is Label:
			labels.append((c as Label).text)
		if c is Control:
			lowest = maxf(lowest, (c as Control).position.y + (c as Control).size.y)
		if c is Button and not (c is OptionButton):
			two_buttons += 1
		if c is OptionButton:
			olists.append(c)
	(olists[olists.size() - 1] as OptionButton).item_selected.emit(2)            # Heal at: Late (50%)
	print("[opts] paladin rows %s, lowest %.0f of the tab's %.0f, buttons %d, sent %s" % [labels, lowest,
			w._options_box.get_parent().size.y, two_buttons, fake.commands])
	ok = ok and labels.size() == 7 and lowest <= w._options_box.get_parent().size.y and two_buttons == 4 			and fake.commands == ["&companionui option 3 6 0 2"]
	await get_tree().create_timer(0.3).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(String(v.args.get("out", "user://")) + "/companion_options_paladin.png")
	# the Minstrel's 9 rows: all inside the tab, the long choice not clipped
	w.open_row(4, 4)
	await get_tree().process_frame
	var low := 0.0
	var clipped := []
	var nrows := 0
	for c in w._options_box.get_children():
		if c is Control:
			low = maxf(low, (c as Control).position.y + (c as Control).size.y)
		if c is Label:
			nrows += 1
		if c is OptionButton:
			var ob := c as OptionButton
			var tw := ob.get_theme_font("font").get_string_size(ob.text, HORIZONTAL_ALIGNMENT_LEFT, -1,
					ob.get_theme_font_size("font_size")).x
			if tw > ob.size.x - 27.0:                     # (inside the padding: 7 left, 20 right for the arrow)
				clipped.append(ob.text)
	print("[opts] minstrel %d rows, lowest %.0f of %.0f, clipped %s" % [nrows, low, w._options_box.get_parent().size.y, clipped])
	ok = ok and nrows == 9 and low <= w._options_box.get_parent().size.y and clipped.is_empty()
	await get_tree().create_timer(0.3).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(String(v.args.get("out", "user://")) + "/companion_options_minstrel.png")
	Server.client = was
	print("[opts] RESULT ", "PASS" if ok else "FAIL")
	get_tree().quit()
