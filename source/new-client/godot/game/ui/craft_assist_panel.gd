extends Control
## The crafting assistant's page in the tradeskill window (owner 2026-11-08: "set a skill goal and it automatically
## crafts and trains and estimates cost to that skill goal ... an eta until the goal is reached for each tradeskill ...
## auto adjust whenever overall estimated cost goes up or down with a baseline cost based on average luck"; "the gold
## cost still applies and the time it takes still applies"). A quality of life option, not in 1.65: greyed out unless
## Options > Crafting > CRAFTING ASSISTANT is on.
## Pick the skill to raise (the window's trade or a secondary skill its recipes train) and a goal; ESTIMATE asks the
## server for the expected crafts, time and gold; START runs it on the server (anywhere, no tools) - it keeps going with
## this window closed, and reopening shows the live run (op 40). Nothing is decided here: every button is a
## /craftassist command (game/net/craft_assist_net.gd).
## The craft window makes it (CraftAssistPanel.new()), sets its position and size, adds it and calls setup(window).

const Net := preload("res://game/net/craft_assist_net.gd")
const MAX_SKILL := 1100
const STEP := 10                                     # the goal's - / + step
# the secondary skills, in the order the drop-down lists them, and the materials that raise them
# (AbstractCraftingSkill.GainCraftingSkillPoints: one point per ingredient entry of that material)
const SECONDARY := [7, 8, 6, 14, 10, 9]
const MATERIALS := {"leather square": 7, "cloth square": 8, "heavy thread": 8, "metal bars": 6, "wooden boards": 14}
const EXTRA := {4: [10], 13: [9]}                    # Alchemy needs Herbcraft, Spellcraft Gemcutting
const OFF_NOTE := "Turn on in Options > Crafting (a quality of life option, not in 1.65)"
const TIME_NOTE := "Every craft takes the normal 1.65 time and costs real gold. Works anywhere, no tools needed, and keeps going with this window closed."

var window: Control = null
var trade := 0                                       # the window's trade the drop-down was built for
var targets: Array = []                              # the drop-down's skill ids
var view := ""                                       # "estimate" / "status": what the result area shows
var _skill: OptionButton
var _goal: LineEdit
var _minus: Button
var _plus: Button
var _now_label: Label
var _buy: Button
var _sell: Button
var _estimate: Button
var _start: Button
var _stop: Button
var _note: Label
var _head: Label
var _info: RichTextLabel
var _baseline: Label
var _current: Label
var _table: RichTextLabel
var _tick := 0.0
var _asked_at := -100000
var _on := false


func setup(w: Control) -> void:
	window = w
	clip_contents = true
	var width := size.x if size.x > 40.0 else 300.0
	var scroll := ScrollContainer.new()
	scroll.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	var box := VBoxContainer.new()
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	box.add_theme_constant_override("separation", 4)
	scroll.add_child(box)
	var inner := width - 14.0
	var title := UiArt.label("CRAFTING ASSISTANT", 10, UiArt.GOLD_HI)
	box.add_child(title)
	_note = _wrap(box, OFF_NOTE, inner, UiArt.RED)
	# what to train
	var row := _row(box)
	row.add_child(_caption("TRAIN", 52))
	_skill = CompanionUi.option(row, [], Vector2.ZERO, Vector2(inner - 58, 18), func(_i: int): _pick_skill())
	row = _row(box)
	row.add_child(_caption("GOAL", 52))
	_minus = UiArt.button("-", Vector2(22, 18), 10)
	_minus.pressed.connect(func(): _set_goal(goal() - STEP))
	row.add_child(_minus)
	_goal = CompanionUi.field(row, Vector2.ZERO, Vector2(54, 18), "goal")
	_goal.max_length = 4
	_goal.text_submitted.connect(func(_t: String): _set_goal(goal()))
	_goal.focus_exited.connect(func(): _set_goal(goal()))
	_plus = UiArt.button("+", Vector2(22, 18), 10)
	_plus.pressed.connect(func(): _set_goal(goal() + STEP))
	row.add_child(_plus)
	_now_label = _caption("", 0)
	row.add_child(_now_label)
	row = _row(box)
	row.add_child(_caption("AUTO-BUY MATERIALS", inner - 76))
	_buy = _toggle(row)
	row = _row(box)
	row.add_child(_caption("AUTO-SELL WHAT I MAKE", inner - 76))
	_sell = _toggle(row)
	_wrap(box, TIME_NOTE, inner, UiArt.DIM)
	row = _row(box)
	var bw := (inner - 8.0) / 3.0
	_estimate = UiArt.button("ESTIMATE", Vector2(bw, 18), 10)
	_estimate.pressed.connect(ask_estimate)
	row.add_child(_estimate)
	_start = UiArt.button("START", Vector2(bw, 18), 10)
	_start.pressed.connect(start)
	row.add_child(_start)
	_stop = UiArt.button("STOP", Vector2(bw, 18), 10)
	_stop.pressed.connect(stop)
	row.add_child(_stop)
	# the result: an estimate or the live run
	_head = UiArt.label("", 10, UiArt.GOLD)
	_head.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_head.custom_minimum_size = Vector2(inner, 0)
	box.add_child(_head)
	_info = _rich(box, inner)
	_baseline = _wrap(box, "", inner, UiArt.CREAM)
	_current = _wrap(box, "", inner, UiArt.CREAM)
	_table = _rich(box, inner)
	PlayerData.hook(self, Server.craft_assist_changed, _on_message)
	PlayerData.hook(self, Server.craft_changed, func():
		if is_visible_in_tree():
			_rebuild_skills(false))
	visibility_changed.connect(_on_shown)
	if window:
		window.visibility_changed.connect(_on_shown)
	_rebuild_skills(true)
	refresh()
	_on_shown()


# ---------------------------------------------------------------- building helpers

func _row(box: Control) -> HBoxContainer:
	var r := HBoxContainer.new()
	r.add_theme_constant_override("separation", 4)
	box.add_child(r)
	return r


func _caption(t: String, w: float) -> Label:
	var l := UiArt.label(t, 9, UiArt.CREAM)
	l.custom_minimum_size = Vector2(w, 18)
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	return l


func _wrap(box: Control, t: String, w: float, color: Color) -> Label:
	var l := UiArt.label(t, 8, color, false)
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	l.custom_minimum_size = Vector2(w, 0)
	box.add_child(l)
	return l


func _toggle(row: Control) -> Button:
	var b := UiArt.button("ON", Vector2(70, 18), 9)
	b.toggle_mode = true
	b.button_pressed = true                          # (both on by default)
	b.toggled.connect(func(v: bool): b.text = "ON" if v else "OFF")
	row.add_child(b)
	return b


func _rich(box: Control, w: float) -> RichTextLabel:
	var r := RichTextLabel.new()
	r.bbcode_enabled = true
	r.fit_content = true
	r.scroll_active = false
	r.custom_minimum_size = Vector2(w, 0)
	r.add_theme_font_override("normal_font", UiArt.font(false))
	r.add_theme_font_override("bold_font", UiArt.font(true))
	r.add_theme_font_size_override("normal_font_size", 9)
	r.add_theme_font_size_override("bold_font_size", 9)
	r.add_theme_color_override("default_color", UiArt.CREAM)
	r.mouse_filter = Control.MOUSE_FILTER_PASS
	UiArt.themed(r)
	box.add_child(r)
	return r


# ---------------------------------------------------------------- the skill and the goal

static func points(skill: int) -> int:
	var s = Server.craft_skills.get(skill, {})
	return int(s.get("points", 0)) if s is Dictionary else 0


func _window_trade() -> int:
	var t = window.get("trade") if window else null
	if t == null or int(t) <= 0:
		t = int(Server.craft_skills.keys()[0]) if not Server.craft_skills.is_empty() else 1
	return int(t)


## The skills a trade's recipes train: its own, the secondaries its ingredients raise (the client's recipe list), and
## any the server's prices say (op 42 trains bits).
static func skills_for(t: int) -> Array:
	var found := {}
	for r in Server.recipes_for(Server.my_realm()).get(str(t), []):
		for ing in r.get("ingredients", []):
			var words := String(ing[0]).to_lower().split(" ")
			if words.size() >= 2:
				var k := words[-2] + " " + words[-1]
				if MATERIALS.has(k):
					found[int(MATERIALS[k])] = true
	for s in EXTRA.get(t, []):
		found[int(s)] = true
	for info in Net.recipe_info.values():
		if int(info.trade) == t:
			for s in Net.trained_skills(int(info.trains)):
				found[s] = true
	var out := [t]
	for s in SECONDARY:
		if found.has(s) and s != t:
			out.append(s)
	return out


func _rebuild_skills(reset_goal: bool) -> void:
	var t := _window_trade()
	var keep := target()
	if t != trade:
		reset_goal = true
		trade = t
	targets = skills_for(t)
	_skill.clear()
	for s in targets:
		_skill.add_item(("%s (%d)" % [Server.trade_name(s), points(s)]).to_upper())
	var i := targets.find(keep)
	_skill.select(i if i >= 0 and not reset_goal else 0)
	if reset_goal or goal() <= 0:
		_pick_skill()
	else:
		_now_label.text = "NOW %d" % points(target())


func target() -> int:
	if _skill == null or _skill.selected < 0 or _skill.selected >= targets.size():
		return 0
	return int(targets[_skill.selected])


func goal() -> int:
	return int(_goal.text) if _goal and _goal.text.is_valid_int() else 0


func _pick_skill() -> void:
	var now := points(target())
	_now_label.text = "NOW %d" % now
	_set_goal(now + 50)


func _set_goal(g: int) -> void:
	var now := points(target())
	_goal.text = str(clampi(g, mini(now + 1, MAX_SKILL), MAX_SKILL))


# ---------------------------------------------------------------- the buttons

func ask_estimate() -> void:
	if not Net.enabled() or target() <= 0:
		return
	_set_goal(goal())
	view = "estimate"
	_head.text = "ASKING THE SERVER..."
	Net.estimate_for(target(), goal(), _sell.button_pressed)


func start() -> void:
	if not Net.enabled() or target() <= 0 or Net.running():
		return
	_set_goal(goal())
	view = "status"
	Net.start(target(), goal(), _buy.button_pressed, _sell.button_pressed)


func stop() -> void:
	Net.stop()                                       # (allowed with the option off too: never leave a run going)


func _on_shown() -> void:
	if not is_visible_in_tree():
		return
	_rebuild_skills(false)
	refresh()
	if Net.enabled() and Time.get_ticks_msec() - _asked_at > 500:
		_asked_at = Time.get_ticks_msec()
		Net.status_now()                             # (a run may be going on the server: show it live)
		var known := false
		for info in Net.recipe_info.values():
			if int(info.trade) == trade:
				known = true
				break
		if not known:
			Net.ask_recipes(trade)


func _on_message(kind: String) -> void:
	match kind:
		"status":
			if Net.running() or view == "status" or view == "":
				view = "status"
		"estimate":
			if not Net.running():
				view = "estimate"
		"recipes":
			_rebuild_skills(false)
	refresh()


func _process(delta: float) -> void:
	_tick += delta
	if _tick < 1.0 or not is_visible_in_tree():
		return
	_tick = 0.0
	if Net.enabled() != _on or (window and _window_trade() != trade and not Net.running()):
		_rebuild_skills(false)
		refresh()
	elif Net.running() and view == "status":
		refresh()                                    # (the time left / elapsed tick)


# ---------------------------------------------------------------- what it shows

func refresh() -> void:
	if _skill == null:
		return
	_on = Net.enabled()
	var run := Net.running()
	if run:                                          # a live run: the choices show the run's
		var i := targets.find(int(Net.status.get("target", 0)))
		if i >= 0:
			_skill.select(i)
		_goal.text = str(int(Net.status.get("goal", 0)))
		_buy.set_pressed_no_signal(bool(Net.status.get("auto_buy", true)))
		_sell.set_pressed_no_signal(bool(Net.status.get("auto_sell", true)))
		_buy.text = "ON" if _buy.button_pressed else "OFF"
		_sell.text = "ON" if _sell.button_pressed else "OFF"
	var free := _on and not run
	_skill.disabled = not free
	_goal.editable = free
	_minus.disabled = not free
	_plus.disabled = not free
	_buy.disabled = not free
	_sell.disabled = not free
	_estimate.disabled = not free
	_start.disabled = not free
	_stop.disabled = not run
	_note.visible = not _on
	_baseline.text = ""
	_current.text = ""
	_info.text = ""
	_table.text = ""
	_head.text = ""
	if not _on and not run:
		return
	if view == "status" and not Net.status.is_empty():
		_show_status()
	elif view == "estimate" and not Net.estimate.is_empty():
		_show_estimate()


func _skill_table(rows: Array, eta_title: String) -> void:
	if rows.is_empty():
		return
	var t := "[table=3][cell][b]SKILL  [/b][/cell][cell][b]NOW -> AT GOAL  [/b][/cell][cell][b]%s[/b][/cell]" % eta_title
	for r in rows:
		t += "[cell]%s  [/cell][cell]%d -> %d  [/cell][cell]%s[/cell]" % [Server.trade_name(int(r.skill)), int(r.now),
				int(r.projected), Net.time_text(int(r.eta))]
	_table.text = t + "[/table]"


func _show_estimate() -> void:
	var e := Net.estimate
	var sk := Server.trade_name(int(e.target))
	if not bool(e.feasible):
		_head.text = "CAN'T RAISE %s" % sk.to_upper()
		_head.add_theme_color_override("font_color", UiArt.RED)
		_info.text = "%s %d: %s." % [sk, int(e.from), Net.reason_text(int(e.reason))]
		return
	_head.text = "ESTIMATE: %s %d -> %d (AVERAGE LUCK)" % [sk.to_upper(), int(e.from), int(e.goal)]
	_head.add_theme_color_override("font_color", UiArt.GOLD)
	var short := int(e.money) < int(e.net)
	var lines := [
		"[b]TIME: %s[/b] - about %d crafts at the normal craft time" % [Net.time_text(int(e.secs)), int(e.crafts)],
		"First recipe: %s" % Net.recipe_name(int(e.recipe)),
		"Materials: %s" % Net.money_text(int(e.cost)),
		"Resale: %s" % (Net.money_text(int(e.resale)) if _sell.button_pressed else "not selling"),
		"[b]NET COST: %s[/b]" % Net.money_text(int(e.net)),
		"[color=#%s]You have: %s[/color]" % [(UiArt.RED if short else UiArt.CREAM).to_html(false), Net.money_text(int(e.money))],
	]
	_info.text = "\n".join(lines)
	_skill_table(e.skills, "TIME")


func _show_status() -> void:
	var s := Net.status
	var state := int(s.state)
	var reason := Net.reason_text(int(s.reason))
	_head.text = (Net.state_text(state) + (": " + reason if reason != "" and state != 1 else "")).to_upper()
	_head.add_theme_color_override("font_color", UiArt.GREEN if state == 1 or state == 2 else
			(UiArt.RED if state == 3 and int(s.reason) != 7 else UiArt.GOLD))
	var since := (Time.get_ticks_msec() - int(s.get("at", 0))) / 1000 if state == 1 else 0
	var lines := ["Training %s: %d (goal %d)" % [Server.trade_name(int(s.target)), int(s.now), int(s.goal)]]
	if state == 1 and int(s.recipe) > 0:
		lines.append("Making: %s" % Net.recipe_name(int(s.recipe)))
	lines.append("Crafts: %d   points gained: %d" % [int(s.crafts), int(s.gained)])
	lines.append("Spent on materials: %s" % Net.money_text(int(s.spent)))
	lines.append("Earned from sales: %s" % Net.money_text(int(s.earned)))
	lines.append("[b]TIME LEFT: %s[/b]   elapsed: %s" % [Net.time_text(int(s.secs_left) - since) if state == 1 else "-",
			Net.time_text(int(s.elapsed) + since)])
	_info.text = "\n".join(lines)
	# the cost to the goal: fixed at the start (average luck) against now (luck so far + the rest)
	var base := int(s.baseline)
	var cur := int(s.current)
	_baseline.text = "BASELINE COST: %s" % Net.money_text(base).to_upper()
	var diff := cur - base
	var color := UiArt.CREAM
	var tail := ""
	if diff < 0:
		color = UiArt.GREEN
		tail = " (%s LESS)" % PlayerData.money_string(-diff).to_upper()
	elif diff > 0:
		color = UiArt.RED
		tail = " (%s MORE)" % PlayerData.money_string(diff).to_upper()
	_current.text = "CURRENT ESTIMATE: %s%s" % [Net.money_text(cur).to_upper(), tail]
	_current.add_theme_color_override("font_color", color)
	_skill_table(s.skills, "ETA")
