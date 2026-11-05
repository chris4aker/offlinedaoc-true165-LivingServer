class_name SrvCompanionWindow
extends ClassicWindow
## True 1.65 Mode: one hired companion's window, from the server's messages (CompanionNet, plan 17h).
##   GEAR   its worn items on a paper doll and its bag - 10 slots, no weight limit (op 7; owner 2026-10-30). Drag an item from your backpack onto a slot to put it on
##          (/companionui equip, or swap when the slot is taken), right-click or double-click one to take it back
##          (unequip). REPAIR repairs it all at a smith (the server asks the price first).
##   TRAIN  its spec lines, points, the next level's cost and what each level gives (op 8 / 9): TRAIN, and RESPEC (free)
##   STATS  level and XP bubbles, its stats, health / power / endurance, armour, resists, money, realm points (op 10)
##   REALM  its realm rank and abilities (op 11): BUY the next level
##   AI OPTIONS  (only for a companion with such choices - op 14, reset day C17; owner 2026-11-05: "AI Options", for
##          more classes' AI choices later): charm on / off, the Cabalist's /
##          Enchanter's pet, the Bonedancer's three sub-pets (x/75), the songs or chants it uses in combat, out of
##          combat and travelling, Twist
## Nothing here decides: every button is a /companionui command and the server answers with fresh messages.

const SIZE := Vector2(470, 330)
const DOLL := [[21, 29, 26], [25, 28, 22], [32, 27, 23], [33, 34, 24], [35, 36, -1], [10, 11, 12], [13, -1, -1]]

var net: CompanionNet
var row := 0
var _title: Label
var _tabs: Array = []
var _pages: Array = []
var _page := 0
var _problem: Label
# gear
var _doll: Control
var _bag: VBoxContainer
var _bag_title: Label
const BAG_SLOTS := 10
const BAG_FIRST := 40                               # (the companion's own backpack slots 40 - 49)
# spec
var _spec_points: Label
var _spec_list: VBoxContainer
var _preview_list: VBoxContainer
var _preview_line := -1
# stats
var _stats_box: Control
# realm
var _ra_head: Label
var _ra_list: VBoxContainer


func build() -> void:
	closable = true
	_title = CompanionUi.frame(self, "companion_manage", SIZE, Vector2(200, 80), "COMPANION")
	_tabs = CompanionUi.tabs(self, ["Gear", "Train", "Stats", "Realm abilities", "AI Options"], Vector2(12, 20),
			SIZE.x - 24, show_page)
	for i in 5:
		var page := Control.new()
		page.position = Vector2(0, 40)
		page.size = Vector2(SIZE.x, SIZE.y - 60)
		page.mouse_filter = Control.MOUSE_FILTER_PASS
		add_child(page)
		_pages.append(page)
	_build_gear(_pages[0])
	_build_spec(_pages[1])
	_build_stats(_pages[2])
	_build_realm(_pages[3])
	_build_options(_pages[4])
	_layout_tabs()
	_problem = CompanionUi.text(self, "", Vector2(14, SIZE.y - 18), Vector2(SIZE.x - 28, 12), 8, CompanionUi.RED)
	net.changed.connect(refresh)


## Open this window on a roster row, at a page (0 gear, 1 spec, 2 stats, 3 realm abilities).
func open_row(r: int, page := 0) -> void:
	row = r
	show_window()
	net.command("stats %d" % row)
	show_page(page)


func show_page(i: int) -> void:
	_page = i
	for k in _pages.size():
		(_pages[k] as Control).visible = k == i
	CompanionUi.select_tab(_tabs, i)
	match i:
		0: net.command("gear %d" % row)
		1: net.command("spec %d" % row)
		2: net.command("stats %d" % row)
		3: net.command("ra %d" % row)
	refresh()


## The AI OPTIONS tab only for a companion that has such choices: the other four share the row when it has none.
func _layout_tabs() -> void:
	var n := 5 if not (net.options.get(row, []) as Array).is_empty() else 4
	var w := (SIZE.x - 24 - (n - 1) * 3.0) / n
	for i in _tabs.size():
		var b := _tabs[i] as Button
		b.visible = i < n
		b.size = Vector2(w, 15)
		b.position = Vector2(12 + i * (w + 3.0), 20)
	if n == 4 and _page == 4:
		show_page(0)


## The GEAR tab asks again every few seconds while it is open: the server sends a stack's count (arrows, poisons, thrown
## weapons) only when the gear is sent, and the companion uses them up as it fights.
const GEAR_POLL_S := 5.0
var _gear_poll := 0.0


func _process(delta: float) -> void:
	if not visible or net == null or _page != 0:
		_gear_poll = 0.0
		return
	_gear_poll += delta
	if _gear_poll >= GEAR_POLL_S:
		_gear_poll = 0.0
		net.command("gear %d" % row)


func refresh() -> void:
	if not visible or net == null:
		return
	var nm := net.row_name(row)
	var st: Dictionary = net.stats.get(row, {})
	_title.text = ("%s - LEVEL %d" % [nm, int(st.level)] if not st.is_empty() else nm).to_upper() if nm != "" else "COMPANION"
	_problem.text = String(net.texts.get(8, ""))
	_layout_tabs()
	match _page:
		0: _refresh_gear()
		1: _refresh_spec()
		2: _refresh_stats()
		3: _refresh_realm()
		4: _refresh_options()


static func _scroll_list(parent: Control, pos: Vector2, size: Vector2) -> VBoxContainer:
	var box := CompanionUi.inset(parent, pos, size)
	var sc := ScrollContainer.new()
	sc.position = Vector2(3, 3)
	sc.size = box.size - Vector2(6, 6)
	sc.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	sc.mouse_filter = Control.MOUSE_FILTER_PASS        # (drops on it reach its parent: the bag's drop target)
	box.add_child(sc)
	var list := VBoxContainer.new()
	list.custom_minimum_size = Vector2(sc.size.x - 10, 0)
	list.add_theme_constant_override("separation", 2)
	sc.add_child(list)
	return list


static func _clear(c: Control) -> void:
	for k in c.get_children():
		k.queue_free()


# --- gear -----------------------------------------------------------------------------------------------------

func _build_gear(page: Control) -> void:
	CompanionUi.text(page, "WORN - DRAG FROM YOUR BACKPACK TO EQUIP, RIGHT-CLICK TO TAKE BACK", Vector2(14, 0),
			Vector2(300, 12), 7, CompanionUi.DIM)
	_doll = Control.new()
	_doll.position = Vector2(12, 14)
	_doll.size = Vector2(300, 230)
	_doll.mouse_filter = Control.MOUSE_FILTER_PASS
	page.add_child(_doll)
	# its bag: 10 slots, no weight limit (owner 2026-10-30, as the server built it) - drag an item onto the list to put it
	# in, right-click one to take it back
	_bag_title = CompanionUi.text(page, "ITS BAG", Vector2(318, 0), Vector2(140, 12), 8, UiArt.GOLD)
	var drop := BagDrop.new()                         # (the list's parent: a drop anywhere on it lands here)
	drop.window = self
	drop.position = Vector2(318, 14)
	drop.size = Vector2(140, 206)
	page.add_child(drop)
	_bag = _scroll_list(drop, Vector2.ZERO, Vector2(140, 206))
	CompanionUi.button(page, "Repair", Vector2(318, 226), Vector2(140, 16), func(): net.command("repair %d" % row), 9)


func _refresh_gear() -> void:
	_clear(_doll)
	var items: Dictionary = net.gear.get(row, {})
	for y in DOLL.size():
		for x in 3:
			var s := int(DOLL[y][x])
			if s < 0:
				continue
			var cell := GearSlot.new()
			cell.window = self
			cell.slot = s
			cell.item = items.get(s, {})
			cell.position = Vector2(x * 100.0, y * 32.0)
			cell.size = Vector2(96, 30)
			_doll.add_child(cell)
	# the bag: its 10 slots (the companion's backpack 40 - 49, server protocol, 2026-10-30), each a drop target
	_clear(_bag)
	var used := 0
	for s in range(BAG_FIRST, BAG_FIRST + BAG_SLOTS):
		var cell := GearSlot.new()
		cell.window = self
		cell.slot = s
		cell.item = items.get(s, {})
		cell.custom_minimum_size = Vector2(124, 30)
		_bag.add_child(cell)
		if items.has(s):
			used += 1
	_bag_title.text = "ITS BAG (%d / %d)" % [used, BAG_SLOTS]


## An item from your backpack onto one of its slots: a bag slot (or the bag, -1) puts it in the bag - "bag", which takes
## anything; a worn slot puts it on - "equip" (or "swap" when the slot is taken).
func equip_from(backpack_slot: int, onto: int, taken: bool) -> void:
	if onto < 0 or (onto >= BAG_FIRST and onto < BAG_FIRST + BAG_SLOTS):
		net.command("bag %d %d%s" % [row, backpack_slot, (" %d" % onto) if onto >= 0 and not taken else ""])
		return
	if taken:
		net.command("swap %d %d %d" % [row, onto, backpack_slot])
	else:
		net.command("equip %d %d" % [row, backpack_slot])


func unequip(slot: int) -> void:
	net.command("unequip %d %d" % [row, slot])


## The bag list as a drop target: an item from your backpack put into the bag (the server's equip puts what it can't
## wear into the bag).
class BagDrop extends Control:
	var window: SrvCompanionWindow

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_PASS

	func _can_drop_data(_p: Vector2, data: Variant) -> bool:
		return InventorySheet.is_item(data) and int(data.slot) >= 40 and int(data.slot) <= 79

	func _drop_data(_p: Vector2, data: Variant) -> void:
		window.equip_from(int(data.slot), -1, false)


class GearSlot extends Control:
	const STACKABLE := [16, 43, 44, 46]                # eObjectType Thrown, Arrow, Bolt, Poison
	var window: SrvCompanionWindow
	var slot := 0
	var item := {}
	var _hi := false

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_STOP
		if size.x <= 0.0:
			size = custom_minimum_size
		var name_text := String(CompanionNet.SLOT_NAMES.get(slot, "BAG %d" % (slot - SrvCompanionWindow.BAG_FIRST + 1)))
		if not item.is_empty():
			var icon := TextureRect.new()
			# its icon by its model (objects.csv: Server.item_icon) - op 7's [14..15] is the item's extension, not an icon
			var ic := Server.item_icon(int(item.model))
			icon.texture = ItemDb.icon(ic if ic > 0 else int(item.icon))
			icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			icon.position = Vector2(2, 2)
			icon.size = Vector2(26, 26)
			icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
			add_child(icon)
			var cond := int(item.condition)
			var nm := CompanionUi.text(self, String(item.name), Vector2(30, 2), Vector2(size.x - 32, 12), 7,
					CompanionUi.RED if cond < 70 else UiArt.CREAM)
			nm.clip_text = true
			# how many are left of a stack - arrows, bolts, thrown weapons, poisons (owner 2026-10-30: "how am i suppose to see
			# how much ammo/poison/throwing weapons that my companion bots have left"); else the condition
			var count := int(item.get("count", 0))
			var stack := int(item.object_type) in STACKABLE or count > 1
			var second := ("%s  x%d" % [name_text, count]) if stack and count > 0 else "%s  %d%%" % [name_text, cond]
			CompanionUi.text(self, second, Vector2(30, 15), Vector2(size.x - 32, 12), 7,
					CompanionUi.RED if stack and count > 0 and count <= 5 else CompanionUi.DIM)
			tooltip_text = "%s\nLEVEL %d  QUALITY %d%%  CONDITION %d%%" % [String(item.name).to_upper(), int(item.level),
					int(item.quality), cond] + (("\n%d LEFT" % count) if stack and count > 0 else "")
			_summary = tooltip_text
		else:
			CompanionUi.text(self, name_text, Vector2(4, 9), Vector2(size.x - 6, 12), 7, CompanionUi.DIM)

	func _draw() -> void:
		draw_rect(Rect2(Vector2.ZERO, size), Color(0.05, 0.045, 0.04, 0.9))
		draw_rect(Rect2(Vector2.ZERO, size), UiArt.GOLD if _hi else UiArt.GOLD.darkened(0.55), false, 1.0)

	func _can_drop_data(_p: Vector2, data: Variant) -> bool:
		var ok := InventorySheet.is_item(data) and int(data.slot) >= 40 and int(data.slot) <= 79
		if _hi != ok:
			_hi = ok
			queue_redraw()
		return ok

	func _drop_data(_p: Vector2, data: Variant) -> void:
		_hi = false
		queue_redraw()
		window.equip_from(int(data.slot), slot, not item.is_empty())

	func _notification(what: int) -> void:
		if what == NOTIFICATION_MOUSE_ENTER and not item.is_empty():
			Server.prefetch_command(_key(), "/companionui delve %d item %d" % [window.row, slot])
		if (what == NOTIFICATION_MOUSE_EXIT or what == NOTIFICATION_DRAG_END) and _hi:
			_hi = false
			queue_redraw()

	var _summary := ""

	func _key() -> String:
		return "cg:%d:%d" % [window.row, slot]

	## The item's full delve as the server writes it for your own items (reset day C15, 2026-11-05: the hover showed
	## only level / quality / condition), in your items' hover box; the summary until it is here.
	func _make_custom_tooltip(_for_text: String) -> Object:
		var d := Server.item_lines(_key())
		if d.is_empty() or (d.get("lines", []) as Array).is_empty():
			return null
		return ItemTooltip.text_box(String(d.get("caption", item.get("name", ""))), d.lines)

	## Dragged out onto your backpack (owner 2026-10-30: "i cant drag off weapons and bag items, i can only right click"):
	## the backpack takes it with the server's "unequip" - right-click / double-click still do the same.
	func _get_drag_data(_p: Vector2) -> Variant:
		if item.is_empty():
			return null
		var prev := TextureRect.new()
		var ic := Server.item_icon(int(item.model))
		prev.texture = ItemDb.icon(ic if ic > 0 else int(item.icon))
		prev.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		prev.size = Vector2(32, 32) * get_global_transform().get_scale()
		set_drag_preview(prev)
		return {"kind": "companion_item", "slot": slot, "name": String(item.name), "take": window.unequip.bind(slot)}

	func _gui_input(event: InputEvent) -> void:
		if item.is_empty() or not event is InputEventMouseButton or not event.pressed:
			return
		var mb := event as InputEventMouseButton
		if mb.button_index == MOUSE_BUTTON_RIGHT or (mb.button_index == MOUSE_BUTTON_LEFT and mb.double_click):
			window.unequip(slot)
			accept_event()


# --- spec -----------------------------------------------------------------------------------------------------

func _build_spec(page: Control) -> void:
	_spec_points = CompanionUi.text(page, "", Vector2(14, 0), Vector2(130, 12), 8, UiArt.GOLD)
	# (no confirm box of its own: as the original client, the server's answer is the respec - it's free). Over the spec
	# list, beside the points (owner 2026-10-30: on the right it lay over "WHAT EACH LEVEL GIVES")
	CompanionUi.button(page, "Respec (free)", Vector2(12 + 246 - 104, -2), Vector2(104, 15), func(): net.command("respec %d" % row), 8)
	_spec_list = _scroll_list(page, Vector2(12, 16), Vector2(246, 230))
	CompanionUi.text(page, "WHAT EACH LEVEL GIVES", Vector2(266, 2), Vector2(190, 12), 8, UiArt.GOLD)
	_preview_list = _scroll_list(page, Vector2(264, 16), Vector2(194, 230))


func _refresh_spec() -> void:
	var sp: Dictionary = net.spec.get(row, {})
	_spec_points.text = "SPEC POINTS: %d" % int(sp.get("points", 0))
	_clear(_spec_list)
	var lines: Array = sp.get("lines", [])
	for ln in lines:
		if not ln is Dictionary:
			continue
		var line: Dictionary = ln
		var r := Control.new()
		r.custom_minimum_size = Vector2(_spec_list.custom_minimum_size.x, 20)
		var idx := int(line.index)
		var nm := CompanionUi.button(r, String(line.name), Vector2(0, 2), Vector2(112, 15), func():
			_preview_line = idx
			_refresh_spec(), 8)
		nm.toggle_mode = true
		nm.button_pressed = idx == _preview_line
		CompanionUi.text(r, "%d / %d" % [int(line.level), int(line.cap)], Vector2(116, 4), Vector2(44, 12), 8)
		var cost := int(line.cost)
		var can := int(line.level) < int(line.cap) and cost > 0 and cost <= int(sp.get("points", 0))
		CompanionUi.text(r, ("COST %d" % cost) if int(line.level) < int(line.cap) else "MAX", Vector2(158, 4),
				Vector2(40, 12), 7, UiArt.CREAM if can else CompanionUi.DIM)
		var t := CompanionUi.button(r, "Train", Vector2(196, 2), Vector2(38, 15), func(): net.command("train %d %d" % [row, idx]), 8)
		t.disabled = not can
		_spec_list.add_child(r)
	if _preview_line < 0 and not lines.is_empty() and lines[0] is Dictionary:
		_preview_line = int(lines[0].index)
	_clear(_preview_list)
	# kind 4: a BASELINE spell (server protocol, 2026-10-31) - it comes with the companion's level, not its trained points,
	# drawn in its own cool grey (owner: "clear what each spec line actually unlocks"), never the dim of "not reached"
	var kinds := {1: "STYLE", 2: "SPELL", 3: "ABILITY", 4: "BASELINE"}
	var cur := -1
	for ln in lines:
		if ln is Dictionary and int(ln.index) == _preview_line:
			cur = int(ln.level)
	for e in (net.previews.get(row, {}) as Dictionary).get(_preview_line, []):
		var got := int(e.level) <= cur
		var col := CompanionUi.BASELINE if int(e.kind) == 4 else (UiArt.CREAM if got else CompanionUi.DIM)
		var l := CompanionUi.text(_preview_list, "%d  %s - %s" % [int(e.level), String(kinds.get(int(e.kind), "")), String(e.name)],
				Vector2.ZERO, Vector2(180, 12), 7, col)
		_skill_tip(l, e)


## Hovering a preview line shows what the style / spell / ability does (owner 2026-10-30: "when hovering over a spell or
## style it should tell me what it does") - the delve box from the client's own class data (SkillInfo), found by its
## name and icon: the previews are the companion's, not yours, so there is no server delve of them.
func _skill_tip(l: Label, e: Dictionary) -> void:
	var kind := String({1: "style", 2: "spell", 3: "ability", 4: "spell"}.get(int(e.kind), ""))
	if kind == "":
		return
	var id := ClassDb.find(kind, String(e.name), int(e.get("icon", -1)))
	if id == "" and kind != "style":
		return
	var box := SkillTipLabel.new()
	box.entry = {"kind": kind, "id": (id if kind == "ability" else int(id)) if id != "" else "", "name": String(e.name)}
	if kind == "style":
		# a style's own delve from the server - endurance cost, threat, opening, to-hit ... (reset day C15: the class
		# data's lines had neither cost nor threat); the class data's until it is here
		box.server_key = "cs:%d:%d:%d" % [row, _preview_line, int(e.level)]
		box.server_cmd = "/companionui delve %d style %d %d" % [row, _preview_line, int(e.level)]
	box.tooltip_text = String(e.name)                      # (any text: Godot asks _make_custom_tooltip only then)
	box.mouse_filter = Control.MOUSE_FILTER_STOP
	box.size = l.size
	box.custom_minimum_size = Vector2(l.size.x, 12)
	l.add_child(box)


class SkillTipLabel extends Control:
	var entry := {}
	var server_key := ""
	var server_cmd := ""

	func _notification(what: int) -> void:
		if what == NOTIFICATION_MOUSE_ENTER and server_key != "":
			Server.prefetch_command(server_key, server_cmd)

	func _make_custom_tooltip(_for_text: String) -> Object:
		if server_key != "":
			var d := Server.item_lines(server_key)
			if not (d.get("lines", []) as Array).is_empty():
				return ItemTooltip.text_box(String(d.get("caption", entry.name)), d.lines)
		if str(entry.get("id", "")) == "":
			return null
		return ItemTooltip.text_box(String(entry.name), SkillInfo.lines(entry))


# --- stats ----------------------------------------------------------------------------------------------------

const BUFFED := Color(0.45, 0.9, 0.35)                     # a buffed stat's green


func _build_stats(page: Control) -> void:
	_stats_box = Control.new()
	_stats_box.position = Vector2(12, 0)
	_stats_box.size = Vector2(SIZE.x - 24, 250)
	page.add_child(_stats_box)


func _refresh_stats() -> void:
	_clear(_stats_box)
	var st: Dictionary = net.stats.get(row, {})
	if st.is_empty():
		CompanionUi.text(_stats_box, "ASKING...", Vector2(2, 2), Vector2(200, 12), 8, CompanionUi.DIM)
		return
	CompanionUi.text(_stats_box, "LEVEL %d  -  %s" % [int(st.level), net.row_class_line(row)], Vector2(2, 0), Vector2(300, 12), 9, UiArt.GOLD_HI)
	_stats_box.add_child(SrvHireWindow.XpBubbles.make(int(st.xp), Vector2(2, 16), Vector2(200, 8)))
	var names := ["STRENGTH", "CONSTITUTION", "DEXTERITY", "QUICKNESS", "INTELLIGENCE", "PIETY", "EMPATHY", "CHARISMA"]
	# buffed green, debuffed red against the unbuffed values (reset day C15, 2026-11-05; the server's op 10 [63..80])
	var base: Array = st.get("base", [])
	for i in 8:
		CompanionUi.text(_stats_box, names[i], Vector2(2, 32 + i * 14), Vector2(100, 12), 8, UiArt.GOLD)
		var col := UiArt.CREAM
		if base.size() == 8:
			col = BUFFED if int(st.stats[i]) > int(base[i]) else (CompanionUi.RED if int(st.stats[i]) < int(base[i]) else UiArt.CREAM)
		CompanionUi.text(_stats_box, str(int(st.stats[i])), Vector2(104, 32 + i * 14), Vector2(40, 12), 8, col)
	var right := [["HEALTH", "%d / %d" % [int(st.health), int(st.max_health)]], ["POWER", "%d / %d" % [int(st.power), int(st.max_power)]],
			["ENDURANCE", "%d / %d" % [int(st.endurance), int(st.max_endurance)]], ["ARMOR FACTOR", str(int(st.af))],
			["ABSORB", "%d%%" % int(st.absorb)], ["MONEY", PlayerData.money_string(int(st.money)).to_upper()],
			["REALM POINTS", "%d" % (int(st.rp) * 1000)]]
	for i in right.size():
		CompanionUi.text(_stats_box, String(right[i][0]), Vector2(170, 32 + i * 14), Vector2(100, 12), 8, UiArt.GOLD)
		var col := UiArt.CREAM
		if String(right[i][0]) == "ARMOR FACTOR" and st.has("base_af"):
			col = BUFFED if int(st.af) > int(st.base_af) else (CompanionUi.RED if int(st.af) < int(st.base_af) else UiArt.CREAM)
		CompanionUi.text(_stats_box, String(right[i][1]), Vector2(272, 32 + i * 14), Vector2(170, 12), 8, col)
	var resists := ["CRUSH", "SLASH", "THRUST", "HEAT", "COLD", "MATTER", "BODY", "SPIRIT", "ENERGY"]
	CompanionUi.text(_stats_box, "RESISTS", Vector2(2, 152), Vector2(100, 12), 9, UiArt.GOLD_HI)
	for i in 9:
		var x := 2.0 + (i % 5) * 88.0
		var y := 168.0 + (i / 5) * 14.0
		CompanionUi.text(_stats_box, "%s %d%%" % [resists[i], int(st.resists[i])], Vector2(x, y), Vector2(86, 12), 8)


# --- AI options -----------------------------------------------------------------------------------------------

var _options_box: Control
const SONG_SLOTS := {1: "IN COMBAT", 2: "OUT OF COMBAT", 3: "TRAVELLING", 4: "TWIST"}
const OPT_X := 142.0                                # where an AI option's choices start (its caption left of it)


func _build_options(page: Control) -> void:
	_options_box = Control.new()
	_options_box.position = Vector2(12, 0)
	_options_box.size = Vector2(SIZE.x - 24, 250)
	page.add_child(_options_box)


## One row a choice (op 14): its caption and the server's choices - two-way ones (charm, Twist) as buttons, the others
## a drop-down list; picking one is the agreed /companionui verb and the server sends the rows again.
func _refresh_options() -> void:
	_clear(_options_box)
	var rows: Array = net.options.get(row, [])
	if rows.is_empty():
		CompanionUi.text(_options_box, "NO AI OPTIONS FOR THIS COMPANION", Vector2(2, 2), Vector2(300, 12), 8, CompanionUi.DIM)
		return
	var y := 0.0
	var shown_cap := false
	for o in rows:
		var kind := int(o.kind)
		var slot := int(o.slot)
		var names: Array = o.names
		var chosen := int(o.chosen)
		var caption := ""
		match kind:
			1: caption = "CHARM"
			2: caption = "PET"
			3: caption = "SUB-PET %d" % slot
			4: caption = "SONG / CHANT - " + String(SONG_SLOTS.get(slot, str(slot))) if slot != 4 else "TWIST SONGS"
			_:
				# any other AI choice the server adds later (owner 2026-11-05: "we will use it for all kinds of classes
				# later"): its first name is the row's caption, the rest its choices; picked with "option"
				caption = String(names[0]) if names.size() > 0 else "CHOICE"
				names = names.slice(1)
		if kind == 3 and not shown_cap and int(o.cap) > 0:
			shown_cap = true                                  # the Bonedancer's sub-pet points, once: x / 75
			CompanionUi.text(_options_box, "SUB-PETS %d / %d" % [int(o.used), int(o.cap)], Vector2(2, y), Vector2(300, 12),
					9, UiArt.GOLD_HI)
			y += 16.0
		# (the captions in a narrower column, the choices to the tab's edge - the server's long choices such as "Support
		# (stay back: songs, spells, no melee)" fit; the server chat 2026-11-05)
		var lc := CompanionUi.text(_options_box, caption, Vector2(2, y + 2), Vector2(OPT_X - 6, 12), 8, UiArt.GOLD)
		lc.clip_text = true
		lc.tooltip_text = caption
		lc.mouse_filter = Control.MOUSE_FILTER_PASS
		var pick := func(i: int): _choose(kind, slot, i, names)
		var room := _options_box.size.x - OPT_X - 2.0
		if names.size() == 2 and (kind == 1 or (kind == 4 and slot == 4) or kind >= 5):   # (two-way ones: buttons)
			for i in 2:
				var b := CompanionUi.button(_options_box, String(names[i]), Vector2(OPT_X + i * (room * 0.5 + 2.0), y),
						Vector2(room * 0.5 - 2.0, 16), pick.bind(i), 8)
				b.toggle_mode = true
				b.set_pressed_no_signal(i == chosen)
				b.clip_text = true
				b.tooltip_text = String(names[i])
		else:
			var ob := ExchangeWindow._choice(names)
			ob.position = Vector2(OPT_X, y)
			ob.add_theme_font_size_override("font_size", 9)
			ExchangeWindow._fix_size(ob, Vector2(room, 18))
			if chosen < names.size():
				ob.select(chosen)
				ob.tooltip_text = String(names[chosen])           # (the whole choice on hover, should it ever clip)
			ob.item_selected.connect(func(i: int):
				ob.tooltip_text = String(names[i]) if i < names.size() else ""
				pick.call(i))
			_options_box.add_child(ob)
		y += 22.0


func _choose(kind: int, slot: int, i: int, names: Array) -> void:
	match kind:
		1:
			var on := String(names[i]).to_lower() in ["on", "yes"] if i < names.size() else false
			net.command("charm %d %s" % [row, "on" if on else "off"])
		2: net.command("pet %d %d" % [row, i])
		3: net.command("subpet %d %d %d" % [row, slot, i])
		4: net.command("song %d %d %d" % [row, slot, i])
		_: net.command("option %d %d %d %d" % [row, kind, slot, i])   # (a later AI choice: kind 5 and up)


# --- realm abilities ------------------------------------------------------------------------------------------

func _build_realm(page: Control) -> void:
	_ra_head = CompanionUi.text(page, "", Vector2(14, 0), Vector2(SIZE.x - 28, 12), 8, UiArt.GOLD)
	_ra_list = _scroll_list(page, Vector2(12, 16), Vector2(SIZE.x - 24, 230))


func _refresh_realm() -> void:
	_clear(_ra_list)
	var list: Array = (net.ras.get(row, []) as Array).filter(func(e): return e is Dictionary)
	if list.is_empty():
		_ra_head.text = "REALM ABILITIES"
		CompanionUi.text(_ra_list, "NONE YET.", Vector2.ZERO, Vector2(200, 12), 8, CompanionUi.DIM)
		return
	var first: Dictionary = list[0]
	_ra_head.text = "REALM RANK %dL%d   -   REALM ABILITY POINTS: %d" % [int(first.rank), int(first.sub), int(first.points)]
	for e in list:
		var r := Control.new()
		r.custom_minimum_size = Vector2(_ra_list.custom_minimum_size.x, 18)
		CompanionUi.text(r, String(e.name), Vector2(4, 3), Vector2(220, 12), 8, UiArt.CREAM)
		CompanionUi.text(r, "%d / %d" % [int(e.level), int(e.max)], Vector2(230, 3), Vector2(50, 12), 8)
		var maxed := int(e.level) >= int(e.max)
		var can := not maxed and int(e.cost) <= int(e.points)
		CompanionUi.text(r, "MAX" if maxed else "COST %d" % int(e.cost), Vector2(286, 3), Vector2(60, 12), 7,
				UiArt.CREAM if can else CompanionUi.DIM)
		var idx := int(e.index)
		var b := CompanionUi.button(r, "Buy", Vector2(352, 1), Vector2(44, 15), func(): net.command("buyra %d %d" % [row, idx]), 8)
		b.disabled = not can
		_ra_list.add_child(r)
