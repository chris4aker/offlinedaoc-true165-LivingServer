extends Node
## Offline check of the tradeskill window overhaul (owner 2026-11-08: "which required number of skill level each recipe
## requires", "something can show craftable in red but say i dont have enough leatherworking", filters / best to craft).
## The owner's skills: Tailoring 734, Leathercrafting 675, Clothworking 788 (Albion), no server (a fake client records
## the /craftassist commands).
##   A  every row shows its required Tailoring level      B  a leather recipe above 705 says NEEDS LEATHER (detail too)
##   C  BEST TO TRAIN: makeable rows only; held back by Leathercrafting; with it raised: orange / yellow first
##   D  each filter narrows correctly                     E  the detail's chances = the server's 1.65 con table
##   F  the server's prices (op 42 rows) drive cost / afford / sellable    G  the ASSISTANT page   H  other trades
##   --skip-login --zone=N --world-mode=1.65 --probe=craft_window_overhaul_probe


class FakeClient extends Node:
	var commands: Array = []

	func Command(text: String) -> void:
		commands.append(text)

	func Craft(_id: int) -> void:
		pass


var ok := true


func _check(name: String, good: bool, detail := "") -> void:
	ok = ok and good
	print("[craftui] %s: %s  %s" % [name, detail, "OK" if good else "WRONG"])


func _items(w: CraftWindow) -> Array:
	var out := []
	var stack: Array = [w._tree.get_root()]
	while not stack.is_empty():
		var it: TreeItem = stack.pop_back()
		if it == null:
			continue
		if it.get_metadata(0) is Dictionary:
			out.append(it)
		var c := it.get_first_child()
		var kids := []
		while c:
			kids.append(c)
			c = c.get_next()
		kids.reverse()
		stack.append_array(kids)
	return out


func _find(w: CraftWindow, name: String) -> TreeItem:
	for it in _items(w):
		if String(it.get_metadata(0).name) == name:
			return it
	return null


func _recipe(name: String) -> Dictionary:
	for r in Server.recipes_for("alb").get("11", []):
		if String(r.name) == name:
			return r
	return {}


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(3.0).timeout
	var old_client = Server.client
	var old_skills: Dictionary = Server.craft_skills.duplicate(true)
	var old_sel: Dictionary = Server.selected.duplicate(true)
	var old_inv: Dictionary = Server.inv.duplicate(true)
	var fc := FakeClient.new()
	add_child(fc)
	Server.client = fc
	Server.selected["realm"] = "alb"
	Server.craft_skills = {11: {"points": 734, "name": "Tailoring"}, 7: {"points": 675, "name": "Leathercrafting"},
			8: {"points": 788, "name": "Clothworking"}}
	Server.inv.clear()
	ItemDb.add_runtime("srv:40", {"id": "srv:40", "name": "nightshade cloth square"})
	Server.inv[40] = {"id": "srv:40", "count": 80}
	ItemDb.add_runtime("srv:41", {"id": "srv:41", "name": "nightshade heavy thread"})
	Server.inv[41] = {"id": "srv:41", "count": 44}
	var w := CraftWindow.new()
	w.size = Vector2(CraftWindow.W, CraftWindow.H)
	v.ui.root.add_child(w)
	w.build()
	var t0 := Time.get_ticks_usec()
	w.open_trade(11)
	print("[craftui] open + refresh of %d Tailoring recipes: %.1f ms" % [w._rows.size(), (Time.get_ticks_usec() - t0) / 1000.0])
	t0 = Time.get_ticks_usec()
	w.refresh()
	print("[craftui] warm refresh: %.1f ms" % ((Time.get_ticks_usec() - t0) / 1000.0))
	var list: Array = Server.recipes_for("alb").get("11", [])
	t0 = Time.get_ticks_usec()
	for r in list:
		w._row(r)
	var t_rows := (Time.get_ticks_usec() - t0) / 1000.0
	t0 = Time.get_ticks_usec()
	w._backpack_counts()
	var t_bag := (Time.get_ticks_usec() - t0) / 1000.0
	t0 = Time.get_ticks_usec()
	w._fill_tree(w._shown.duplicate(), "all")
	var t_tree := (Time.get_ticks_usec() - t0) / 1000.0
	t0 = Time.get_ticks_usec()
	w._show_recipe()
	print("[craftui] parts: rows %.1f ms, backpack %.1f ms, tree %.1f ms, detail %.1f ms" % [t_rows, t_bag, t_tree,
			(Time.get_ticks_usec() - t0) / 1000.0])
	_check("asks prices", fc.commands.has("&craftassist recipes 11"), str(fc.commands))
	_check("header", w._header.text.contains("TAILORING 734") and w._header.text.contains("LEATHERCRAFTING 675")
			and w._header.text.contains("CLOTHWORKING 788") and w._header.text.contains("METALWORKING 0"),
			w._header.text)
	print("[craftui] hint: ", w._hint.text)

	# A: the required level in every row
	var items := _items(w)
	var bad := 0
	for it in items:
		if it.get_text(1) != str(int(it.get_metadata(0).skill)):
			bad += 1
	_check("A rows show the required level", items.size() > 100 and bad == 0, "%d rows, %d wrong" % [items.size(), bad])

	# B: leather above Leathercrafting + 30 -> NEEDS; a cloth-only one at 715 does not
	var helm := _find(w, "runed roman helm")                      # 730, runed leather squares: needs 700
	var robe := _find(w, "nightshade plain robe")                 # 715, cloth + thread: needs Clothworking 685 (788)
	var low := _find(w, "imbued cymric sleeves")                  # 700, leather: needs 670 (675) - fine
	_check("B leather 730 flagged", helm != null and helm.get_text(2) == "NEEDS LEATHER 700",
			helm.get_text(2) if helm else "missing")
	_check("B cloth robe 715 not flagged", robe != null and not robe.get_text(2).begins_with("NEEDS")
			and robe.get_text(2).contains("CLOTH+2"), robe.get_text(2) if robe else "missing")
	_check("B leather 700 not flagged", low != null and not low.get_text(2).begins_with("NEEDS"),
			low.get_text(2) if low else "missing")
	w.selected = _recipe("runed roman helm")
	w._show_recipe()
	_check("B detail says so", w._ingredients.text.contains("Needs Leathercrafting 700 - you have 675"),
			w._ingredients.text.get_slice("\n", 2))
	var studded := _find(w, "asterite studded helm")              # leather + metal bars, Metalworking 0
	_check("B metal too", studded != null and w._row(studded.get_metadata(0)).short.size() == 2,
			str(w._row(studded.get_metadata(0)).short) if studded else "missing")

	# C: BEST TO TRAIN
	w.set_view("best")
	var all_gain := true
	for row in w._shown:
		all_gain = all_gain and bool(row.can_gain)
	_check("C best (LC 675): makeable only", not w._shown.is_empty() and all_gain, "%d rows, first %s (%d)"
			% [w._shown.size(), w._shown[0].r.name if not w._shown.is_empty() else "-",
			w._shown[0].lvl if not w._shown.is_empty() else 0])
	_check("C held back by Leathercrafting", w._blocker.begins_with("LEATHERCRAFTING 675 BLOCKS"), w._blocker)
	Server.craft_skills[7] = {"points": 760, "name": "Leathercrafting"}
	Server.craft_skills[6] = {"points": 760, "name": "Metalworking"}
	w.refresh()
	var top_ok := w._shown.size() >= 5 and w._blocker == ""
	for row in w._shown.slice(0, 5):
		top_ok = top_ok and (int(row.con) == 0 or int(row.con) == 1) and bool(row.can_gain)
	_check("C best (LC 760): orange / yellow first", top_ok, ", ".join(w._shown.slice(0, 5).map(func(r):
			return "%s %d %s" % [r.r.name, r.lvl, CraftWindow.CON_NAMES[r.con]])))
	Server.craft_skills[7] = {"points": 675, "name": "Leathercrafting"}
	Server.craft_skills.erase(6)
	w.set_view("all")
	var rec := w._tree.get_root().get_first_child()
	while rec and str(rec.get_metadata(0)) != "*recommended":
		rec = rec.get_next()
	_check("C recommended group", rec != null and rec.get_child_count() > 0 and rec.get_child_count() <= 5,
			str(rec.get_child_count()) if rec else "missing")

	# D: filters
	var total := w._shown.size()
	var views := {"have": func(row): return bool(row.have_all),
			"trains:7": func(row): return (row.trains as Dictionary).has(7),
			"trains:6": func(row): return (row.trains as Dictionary).has(6)}
	for k in views:
		var tabbed: bool = w._tabs.has(k)
		w.set_view(k)
		var good := tabbed and w._shown.size() > 0 and w._shown.size() < total
		for row in w._shown:
			good = good and bool(views[k].call(row))
		_check("D view " + k, good, "%d of %d" % [w._shown.size(), total])
	_check("D no CLOTH tab (every Tailoring recipe trains it)", not w._tabs.has("trains:8"), str(w._tabs.keys()))
	w.set_view("all")
	var filters := [["type", "hands", func(row): return String(row.slot) == "hands"],
			["tier", "nightshade", func(row): return String(row.tier) == "nightshade"],
			["level", 4, func(row): return int(row.con) >= 0 and int(row.con) <= 1],
			["grey", true, func(row): return int(row.con) > -3]]
	for f in filters:
		w._f[f[0]] = f[1]
		w.refresh()
		var good := w._shown.size() > 0 and w._shown.size() < total
		for row in w._shown:
			good = good and bool(f[2].call(row))
		_check("D filter %s=%s" % [f[0], str(f[1])], good, "%d of %d" % [w._shown.size(), total])
		w._f.merge(w._default_filters(), true)
	w._f.sort = 3
	w.refresh()
	var sorted_ok := true
	var p := w._tree.get_root().get_first_child()
	while p:
		if p.get_metadata(0) is String and String(p.get_metadata(0)) != "*recommended":
			var prev := ""
			var c := p.get_first_child()
			while c:
				sorted_ok = sorted_ok and String(c.get_metadata(0).name) >= prev
				prev = String(c.get_metadata(0).name)
				c = c.get_next()
		p = p.get_next()
	_check("D sort by name", sorted_ok)
	w._f.sort = 0
	w.refresh()

	# E: chances by con (AbstractCraftingSkill: make 100/100/100/92/84/68, gain 15/30/45/55/65)
	var want := {-2: [100, 15], -1: [100, 30], 0: [92, 45], 1: [84, 55], 2: [68, 65]}
	for con in want:
		var row = null
		for rr in w._rows:
			if int(rr.con) == int(con):
				row = rr
				break
		if row == null:
			_check("E con %d" % con, false, "no recipe")
			continue
		w.selected = row.r
		w._show_recipe()
		var txt := w._ingredients.text
		var good: bool = txt.contains("Chance to make [b]%d%%[/b]" % want[con][0]) \
				and txt.contains("point [b]%d%%[/b]" % want[con][1])
		_check("E con %d (%s %d)" % [con, row.r.name, row.lvl], good, txt.get_slice("\n", 0))
	_check("E time formula", CraftWindow.player_secs(735, 20, 1) == 40 and CraftWindow.player_secs(15, 9, -3) == 1,
			"%d %d" % [CraftWindow.player_secs(735, 20, 1), CraftWindow.player_secs(15, 9, -3)])

	# F: the server's prices
	var net = CraftWindow.assist_net()
	if net == null:
		_check("F prices", false, "craft_assist_net.gd missing")
	else:
		var robe_r := _recipe("nightshade plain robe")
		var gloves_r := _recipe("nightshade cloth gloves")
		var keep: Dictionary = net.recipe_info.duplicate()
		var money := PlayerData.total_copper()
		PlayerData.set_copper(100000)
		net.recipe_info[int(robe_r.id)] = {"level": 715, "cost": 5000, "sell": 1000, "sec_min": 685, "trains": 1,
				"status": 1, "secs": 12, "trade": 11}
		net.recipe_info[int(gloves_r.id)] = {"level": 715, "cost": PlayerData.total_copper() + 100000, "sell": 0,
				"sec_min": 685, "trains": 3, "status": 1, "secs": 3, "trade": 11}
		w.selected = robe_r
		w.refresh()
		var txt: String = w._ingredients.text
		_check("F detail prices", w._priced and txt.contains("Materials") and txt.contains("a point"),
				txt.get_slice("\n", 4))
		var row: Dictionary = w._row(robe_r)
		_check("F cost per point", is_equal_approx(float(row.cpp), 4000.0 / 0.30), str(row.cpp))
		w._f.afford = true
		w.refresh()
		var ids := w._shown.map(func(r): return int(r.id))
		_check("F can afford", ids.has(int(robe_r.id)) and not ids.has(int(gloves_r.id)) and ids.size() == 1, str(ids))
		w._f.afford = false
		w._f.sell = true
		w.refresh()
		ids = w._shown.map(func(r): return int(r.id))
		_check("F sellable", ids == [int(robe_r.id)], str(ids))
		w._f.merge(w._default_filters(), true)
		net.recipe_info = keep
		PlayerData.set_copper(money)
		w.refresh()

	# G0: while the assistant runs, MAKE / CRAFT ALL wait (the server would take a press as "stop the run")
	if net != null:
		w.selected = _recipe("nightshade plain robe")
		w._update_make()
		var keep_status: Dictionary = net.status
		net.status = {"state": 1}
		Server.craft_assist_changed.emit("status")
		var busy: bool = w._make.disabled and w._craft_all.disabled and w._make.tooltip_text == CraftWindow.ASSIST_BUSY
		net.status = {"state": 3}
		Server.craft_assist_changed.emit("status")
		var free: bool = not w._make.disabled and w._make.tooltip_text != CraftWindow.ASSIST_BUSY 				and w._craft_all.tooltip_text != CraftWindow.ASSIST_BUSY
		net.status = keep_status
		_check("G0 assistant running locks MAKE / CRAFT ALL", busy and free, "busy %s, free after %s" % [busy, free])

	# G: the assistant page
	w.show_assistant(true)
	_check("G assistant page", w._assist != null and w._assist.visible and not w._page.visible
			and w._assist_btn.text == "RECIPES", str(w._assist))
	w.show_assistant(false)
	_check("G back to recipes", w._page.visible and not w._assist.visible)

	# H: other trades adapt
	Server.craft_skills[1] = {"points": 300, "name": "Weaponcraft"}
	w.open_trade(1)
	_check("H weaponcraft header", w._header.text.contains("METALWORKING") and w._header.text.contains("WOODWORKING")
			and w._tabs.has("trains:14"), w._header.text)
	w.open_trade(4)
	_check("H alchemy header", w._header.text.contains("HERBCRAFT") and w._hint.text.contains("Herbcraft"),
			w._hint.text)
	w.open_trade(7)
	_check("H leathercrafting trade", w._hint.text.contains("own recipes raise only"), w._hint.text)
	w.open_trade(12)
	_check("H fletching", w._rows.size() > 0 and w._header.text.contains("WOODWORKING"), w._header.text)

	w.queue_free()
	Server.client = old_client
	Server.craft_skills = old_skills
	Server.selected = old_sel
	Server.inv = old_inv
	print("[craftui] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
