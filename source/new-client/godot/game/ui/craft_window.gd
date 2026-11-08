class_name CraftWindow
extends ClassicWindow
## The tradeskill window (owner 2026-10-30: "the crafting UI for new client"). The original client's TradeSkillWindow
## is drawn by game.dll from its own recipe file (no xml; rows "%s (%d)"); the server sends no recipe list, only your
## tradeskills (VariousUpdate 8), so the list is the client's own: content/crafting/recipes.json (pipeline/
## build_recipes.py - the server's tables in normal mode, the 1.64 client's tdl.irf in 1.65 Mode).
## One trade at a time (< >), its products as categories with their material tiers under them, each tier coloured by
## its difficulty for your skill (the server's con: grey, green, blue, yellow, orange, red, purple); the chosen recipe's
## ingredients with what your backpack holds; MAKE asks the server to make it (0xED) - asked again while it is being
## made, the server stops it. Open with /craft or a trade in the skills sheet's TRADESKILLS list.
## CRAFT ALL (owner 2026-11-08, a quality-of-life option, not in 1.65 - off by default, Options > Crafting): makes the
## chosen recipe again each time one finishes, until the materials run out or the backpack is full.

const W := 320.0
const H := 448.0
# the server's con buckets (AbstractCraftingSkill.GetItemCon: recipe level - skill)
const CON := [[-50, Color(0.62, 0.62, 0.62)], [-31, Color(0.35, 0.85, 0.35)], [-11, Color(0.45, 0.65, 1.0)],
		[0, Color(1.0, 0.95, 0.4)], [19, Color(1.0, 0.65, 0.25)], [49, Color(1.0, 0.35, 0.3)]]
const PURPLE := Color(0.8, 0.5, 1.0)

var trade := 1                                       # eCraftingSkill id shown
var selected := {}                                   # the chosen recipe
var _making := -1                                    # the recipe the server is making now (its timer is open)
var _title: Label
var _trade_label: Label
var _tree: Tree
var _icon: TextureRect
var _name: Label
var _levels: Label
var _ingredients: RichTextLabel
var _make: Button
var _open_bases := {}                                # "trade:base" -> true for the categories left open
var _craft_all: Button
var _all := {}                                       # the recipe CRAFT ALL is repeating ({} = not running)
var _timer_secs := 0                                 # the open craft timer's length and when it opened (ticks ms)
var _timer_at := 0
static var _craft_all_on := -1                       # the option (-1: not read yet)
const DONE_PART := 0.85                              # a timer closed this far through: the item was made (else interrupted)
const REFUSED_MS := 3000                             # no timer this long after asking: the server refused (no forge ...)


static func craft_all_enabled() -> bool:
	if _craft_all_on < 0:
		var cfg := ConfigFile.new()
		cfg.load(UiArt.settings_path)
		_craft_all_on = 1 if bool(cfg.get_value("interface", "craft_all", false)) else 0
	return _craft_all_on == 1


static func set_craft_all(on: bool) -> void:
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)
	cfg.set_value("interface", "craft_all", on)
	cfg.save(UiArt.settings_path)
	_craft_all_on = 1 if on else 0


func build() -> void:
	closable = true
	_title = UiArt.label("TRADESKILLS", 11, UiArt.GOLD_HI)
	_title.position = Vector2(12, 21)
	_title.size = Vector2(W - 27, 16)
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_title)
	var prev := UiArt.button("<", Vector2(22, 16), 9)
	prev.position = Vector2(12, 40)
	prev.pressed.connect(func(): _step_trade(-1))
	add_child(prev)
	var nxt := UiArt.button(">", Vector2(22, 16), 9)
	nxt.position = Vector2(W - 39, 40)
	nxt.pressed.connect(func(): _step_trade(1))
	add_child(nxt)
	_trade_label = UiArt.label("", 10, UiArt.GOLD)
	_trade_label.position = Vector2(38, 41)
	_trade_label.size = Vector2(W - 81, 16)
	_trade_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_trade_label)
	_tree = Tree.new()
	_tree.position = Vector2(10, 60)
	_tree.size = Vector2(W - 27, 228)
	_tree.hide_root = true
	_tree.select_mode = Tree.SELECT_ROW
	_tree.add_theme_font_override("font", UiArt.font(false))
	_tree.add_theme_font_size_override("font_size", 10)
	_tree.add_theme_color_override("font_color", UiArt.CREAM)
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(0.02, 0.02, 0.02, 0.85)
	_tree.add_theme_stylebox_override("panel", bg)
	UiArt.themed(_tree)
	_tree.item_selected.connect(_on_pick)
	_tree.item_activated.connect(func(): make_selected())
	_tree.item_collapsed.connect(func(it: TreeItem):
		var b = it.get_metadata(0)
		if b is String:
			_open_bases["%d:%s" % [trade, b]] = not it.collapsed)
	add_child(_tree)
	_icon = TextureRect.new()
	_icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_icon.stretch_mode = TextureRect.STRETCH_SCALE
	_icon.position = Vector2(12, 296)
	_icon.size = Vector2(32, 32)
	add_child(_icon)
	_name = UiArt.label("", 10, UiArt.GOLD_HI)
	_name.position = Vector2(50, 296)
	_name.size = Vector2(W - 77, 16)
	_name.clip_text = true
	add_child(_name)
	_levels = UiArt.label("", 9, UiArt.DIM)
	_levels.position = Vector2(50, 313)
	_levels.size = Vector2(W - 77, 14)
	add_child(_levels)
	_ingredients = RichTextLabel.new()
	_ingredients.bbcode_enabled = true
	_ingredients.position = Vector2(12, 332)
	_ingredients.size = Vector2(W - 29, 78)
	_ingredients.add_theme_font_override("normal_font", UiArt.font(false))
	_ingredients.add_theme_font_size_override("normal_font_size", 9)
	_ingredients.add_theme_color_override("default_color", UiArt.CREAM)
	_ingredients.mouse_filter = Control.MOUSE_FILTER_PASS
	UiArt.themed(_ingredients)
	add_child(_ingredients)
	_make = UiArt.button("MAKE", Vector2(80, 18), 10)
	_make.position = Vector2(W * 0.5 - 84, H - 30)
	_make.pressed.connect(make_selected)
	add_child(_make)
	_craft_all = UiArt.button("CRAFT ALL", Vector2(80, 18), 10)
	_craft_all.position = Vector2(W * 0.5 + 4, H - 30)
	_craft_all.pressed.connect(craft_all)
	add_child(_craft_all)
	visibility_changed.connect(func():
		if not visible and not _all.is_empty():
			_stop_all("")                                # (the item being made goes on; no more after it)
		)
	PlayerData.hook(self, Server.craft_changed, func():
		if visible:
			refresh())
	PlayerData.hook(self, PlayerData.inventory_changed, func():
		if visible:
			_show_recipe())
	PlayerData.hook(self, Server.craft_timer, func(secs: int, title: String):
		if secs >= 0 and title.begins_with("Currently Making"):
			_timer_secs = secs
			_timer_at = Time.get_ticks_msec()
		else:
			var made := _timer_at > 0 and Time.get_ticks_msec() - _timer_at >= int(_timer_secs * 1000.0 * DONE_PART)
			_making = -1
			_timer_at = 0
			if not _all.is_empty():
				if made:
					_next_soon()
				else:
					_stop_all("Craft all stopped: the item was interrupted.")
		_update_make())


## Open on a trade (0: the primary tradeskill, else the last one shown).
func open_trade(trade_id := 0) -> void:
	if trade_id > 0:
		trade = trade_id
	elif not visible:
		var primary := String(Server.info.get("craft_skill", ""))
		for id in Server.craft_skills:
			if String(Server.craft_skills[id].name) == primary:
				trade = int(id)
	if not Server.recipes_for(Server.my_realm()).has(str(trade)):
		var trades := _trades()
		if not trades.is_empty():
			trade = int(trades[0])
	selected = {}
	refresh()
	show_window()


func _trades() -> Array:
	var keys: Array = Server.recipes_for(Server.my_realm()).keys()
	keys.sort_custom(func(a, b): return int(a) < int(b))
	return keys


func _step_trade(d: int) -> void:
	var trades := _trades()
	if trades.is_empty():
		return
	var i := trades.find(str(trade))
	trade = int(trades[posmod(i + d, trades.size())])
	selected = {}
	refresh()


func skill() -> int:
	return int(Server.craft_skills.get(trade, {}).get("points", 0))


static func con_color(recipe_level: int, skill_points: int) -> Color:
	var d := recipe_level - skill_points
	for c in CON:
		if d <= int(c[0]):
			return c[1]
	return PURPLE


func refresh() -> void:
	var sk := skill()
	_title.text = ("%s  (%d)" % [Server.trade_name(trade), sk]).to_upper() if Server.craft_skills.has(trade) else Server.trade_name(trade).to_upper()
	var trades := _trades()
	_trade_label.text = "%s  -  %d OF %d" % [Server.trade_name(trade).to_upper(), trades.find(str(trade)) + 1, trades.size()]
	_tree.clear()
	var root := _tree.create_item()
	var list: Array = Server.recipes_for(Server.my_realm()).get(str(trade), [])
	var parents := {}
	var keep: TreeItem = null
	for r in list:
		var group := String(r.get("group", ""))
		var base := String(r.base) if group == "" else group
		if not parents.has(base):
			var p := _tree.create_item(root)
			p.set_text(0, base.to_upper())
			p.set_custom_color(0, UiArt.GOLD)
			p.set_selectable(0, false)
			p.set_metadata(0, base)
			p.collapsed = not bool(_open_bases.get("%d:%s" % [trade, base], false))
			parents[base] = p
		var it := _tree.create_item(parents[base])
		it.set_text(0, "%s (%d)" % [String(r.name), int(r.skill)])
		it.set_custom_color(0, con_color(int(r.skill), sk))
		it.set_metadata(0, r)
		if not selected.is_empty() and int(selected.id) == int(r.id):
			keep = it
	if keep:
		keep.get_parent().collapsed = false
		keep.select(0)
	_show_recipe()


func _on_pick() -> void:
	var it := _tree.get_selected()
	var r = it.get_metadata(0) if it else null
	if r is Dictionary:
		selected = r
	_show_recipe()


## How many of an ingredient your backpack holds (the server counts backpack slots 40-79 by the item's name).
static func have(name: String) -> int:
	var n := 0
	for s in Server.inv:
		if int(s) >= 40 and int(s) <= 79:
			var it := ItemDb.item("srv:%d" % int(s))
			if String(it.get("name", "")).to_lower() == name.to_lower():
				n += maxi(1, int((Server.inv[s] as Dictionary).get("count", 1)))
	return n


func _show_recipe() -> void:
	if selected.is_empty():
		_icon.texture = null
		_name.text = ""
		_levels.text = "PICK A RECIPE" if _tree.get_root() and _tree.get_root().get_child_count() > 0 else "NO RECIPES FOR THIS TRADE"
		_ingredients.text = ""
		_update_make()
		return
	_icon.texture = ItemDb.icon(Server.item_icon(int(selected.get("model", 0))))
	_name.text = String(selected.name).to_upper()
	_name.add_theme_color_override("font_color", con_color(int(selected.skill), skill()))
	_levels.text = "SKILL %d   ITEM LEVEL %d   YOURS %d" % [int(selected.skill), int(selected.get("level", 0)), skill()]
	var lines := []
	for ing in selected.get("ingredients", []):
		var need := int(ing[1])
		var got := have(String(ing[0]))
		var col := "#e8dcc0" if got >= need else "#ff6b59"
		lines.append("[color=%s]%d %s   (%d)[/color]" % [col, need, String(ing[0]).replace("[", "[lb]"), got])
	_ingredients.text = "\n".join(lines)
	_update_make()


func _update_make() -> void:
	if _make == null:
		return
	if _making >= 0:
		_make.text = "STOP"
		_make.disabled = false
	else:
		_make.text = "MAKE"
		_make.disabled = selected.is_empty()
	var on := craft_all_enabled()
	_craft_all.text = "STOP ALL" if not _all.is_empty() else "CRAFT ALL"
	_craft_all.disabled = not on or (_all.is_empty() and (selected.is_empty() or _making >= 0))
	_craft_all.tooltip_text = ("Make it again and again until the materials run out or the backpack is full" if on
			else "Turn it on in Options > Crafting (a quality of life option, not in 1.65)")


func make_selected() -> void:
	if _making >= 0:
		_all = {}                                        # (STOP ends a CRAFT ALL too)
		Server.craft(_making)                            # (the server's only cancel: the same request again)
		_making = -1
		_update_make()
		return
	if selected.is_empty():
		return
	_making = int(selected.id)
	Server.craft(int(selected.id))
	_update_make()


## CRAFT ALL: the chosen recipe now and again after each one, while there are materials and backpack room. Pressed
## while running: stops (the item being made is cancelled, as STOP does).
func craft_all() -> void:
	if not _all.is_empty():
		_stop_all("Craft all stopped.")
		if _making >= 0:
			Server.craft(_making)
			_making = -1
		_update_make()
		return
	if selected.is_empty() or _making >= 0 or not craft_all_enabled():
		return
	_all = selected
	_next()


func _next_soon() -> void:
	await get_tree().create_timer(0.6).timeout           # (the product and the used materials reach the backpack)
	if not _all.is_empty() and _making < 0:
		_next()


func _next() -> void:
	var why := _cannot_make(_all)
	if why != "":
		_stop_all("Craft all finished: " + why)
		return
	var id := int(_all.id)
	_making = id
	_timer_at = 0
	Server.craft(id)
	_update_make()
	await get_tree().create_timer(REFUSED_MS / 1000.0).timeout
	if not _all.is_empty() and _making == id and _timer_at == 0:
		_making = -1
		_stop_all("Craft all stopped: the item could not be started.")   # (the server said why)


## Why the recipe can't be made again ("" when it can): an ingredient short, or no free backpack slot.
func _cannot_make(r: Dictionary) -> String:
	for ing in r.get("ingredients", []):
		if have(String(ing[0])) < int(ing[1]):
			return "out of %s." % String(ing[0])
	for s in range(40, 80):
		var it = Server.inv.get(s)
		if it == null or (it is Dictionary and (it as Dictionary).is_empty()):
			return ""
	return "your backpack is full."


func _stop_all(message: String) -> void:
	_all = {}
	if message != "":
		PlayerData.message.emit(message)
	_update_make()
