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
## CRAFT ALL (owner 2026-11-08, a quality-of-life option, not in 1.65 - off by default, Settings > Interface > Crafting): makes the
## chosen recipe again each time one finishes, until the materials run out or the backpack is full.
## Overhaul (owner 2026-11-08: "its not clear what the difference is between clothworking and tailoring and which
## required number of skill level each recipe requires", "something can show craftable in red but say i dont have
## enough leatherworking", "there aren't enough filters to see what items are best to craft"): a skills header with a
## plain explanation, each row's required level and the secondary skills it trains, a clear "NEEDS LEATHER 705" when a
## secondary minimum is not met (the server's GetSecondaryCraftingSkillMinimumLevel), filters / sorting, a RECOMMENDED
## group, chances from the server's 1.65 formulas, the server's prices (op 42) and the crafting assistant page.
## The 1.65 crafting rules themselves are the server's and stay as they are: this window only shows them.

const W := 480.0
const H := 560.0
# the server's con buckets (AbstractCraftingSkill.GetItemCon: recipe level - skill)
const CON := [[-50, Color(0.62, 0.62, 0.62)], [-31, Color(0.35, 0.85, 0.35)], [-11, Color(0.45, 0.65, 1.0)],
		[0, Color(1.0, 0.95, 0.4)], [19, Color(1.0, 0.65, 0.25)], [49, Color(1.0, 0.35, 0.3)]]
const PURPLE := Color(0.8, 0.5, 1.0)
const CON_NAMES := {-3: "grey", -2: "green", -1: "blue", 0: "yellow", 1: "orange", 2: "red", 3: "purple"}
# AbstractCraftingSkill.CalculateChanceToMakeItem / CalculateChanceToGainPoint by con (the capital-city gain bonus is 0
# in True 1.65: capital_city_crafting_skill_gain_bonus = 0)
const MAKE_PCT := {-3: 100, -2: 100, -1: 100, 0: 92, 1: 84, 2: 68, 3: 0}
const GAIN_PCT := {-3: 0, -2: 15, -1: 30, 0: 45, 1: 55, 2: 65, 3: 0}
# GetCraftingTime's con factor (easier recipes are quicker)
const TIME_MOD := {-3: 0.4, -2: 0.6, -1: 0.8}
# The material -> secondary skill table of AbstractCraftingSkill.GainCraftingSkillPoints and
# CheckSecondCraftingSkillRequirement (they match the material's model: 522 cloth square and 537 heavy thread ->
# Clothworking, 521 leather square -> Leathercrafting, 519 metal bars -> Metalworking, 520 wooden boards (also the
# treant / wizened wood) -> Woodworking). Here by the name's ending.
const MATERIAL_SKILL := [["cloth square", 8], ["heavy thread", 8], ["leather square", 7], ["metal bars", 6],
		["wooden boards", 14], ["treant wood", 14], ["wizened wood", 14]]
const SKILL_NAMES := {6: "Metalworking", 7: "Leathercrafting", 8: "Clothworking", 9: "Gemcutting", 10: "Herbcraft",
		14: "Woodworking"}
const SKILL_TAGS := {6: "METAL", 7: "LEATHER", 8: "CLOTH", 9: "GEM", 10: "HERB", 14: "WOOD"}
const MATERIAL_WORDS := {6: "metal bars", 7: "leather squares", 8: "cloth squares and heavy thread",
		14: "wooden boards", 10: "herbs", 9: "gems"}
const PER_MATERIAL_TRADES := [1, 2, 11, 12]       # Weaponcraft, Armorcraft, Tailoring, Fletching: base.GainCraftingSkillPoints
const ALWAYS_TRAINS := {4: 10, 13: 9}             # Alchemy -> +1 Herbcraft, Spellcrafting -> +1 Gemcutting on every point
const MID_LEFT_AXES := ["hand axe", "bearded axe", "war axe", "cleaver", "exceptional hatchet", "weighted bearded axe"]
const LEVEL_RANGES := [["ANY LEVEL", -3, 3], ["CAN GAIN (GREEN-RED)", -2, 2], ["BLUE TO RED", -1, 2],
		["YELLOW TO RED", 0, 2], ["YELLOW TO ORANGE", 0, 1]]
const SORTS := ["SORT: LEVEL", "SORT: COST/POINT", "SORT: TIME/POINT", "SORT: NAME"]
const SLOTS := {"gloves": "hands", "gauntlets": "hands", "mittens": "hands", "cap": "head", "helm": "head",
		"coif": "head", "hat": "head", "crown": "head", "circlet": "head", "wreath": "head", "boots": "feet",
		"pants": "legs", "leggings": "legs", "legs": "legs", "greaves": "legs", "arms": "arms", "sleeves": "arms",
		"vambraces": "arms", "vest": "chest", "jerkin": "chest", "hauberk": "chest", "breastplate": "chest",
		"robe": "chest"}
const ASSIST_NET := "res://game/net/craft_assist_net.gd"
const ASSIST_PANEL := "res://game/ui/craft_assist_panel.gd"
const ENCHANT_PANEL := "res://game/ui/craft_enchant_panel.gd"
const ASSIST_BUSY :="The crafting assistant is running - stop it in the ASSISTANT panel"
const RECOMMENDED := 5                            # rows in the RECOMMENDED FOR YOU group
const BEST_MAX := 25                              # rows in the BEST TO TRAIN view
const AUTO_OPEN := 80                             # a filtered list this short opens all its categories
# layout (y of each band)
const Y_HEAD := 58.0
const Y_HINT := 71.0
const Y_TABS := 96.0
const Y_OPTS := 115.0
const Y_TOGGLES := 134.0
const Y_TREE := 153.0
const TREE_H := 186.0
const Y_DETAIL := 345.0

var trade := 1                                       # eCraftingSkill id shown
var selected := {}                                   # the chosen recipe
var _making := -1                                    # the recipe the server is making now (its timer is open)
var _title: Label
var _trade_label: Label
var _tree: Tree
var _icon: TextureRect
var _name: Label
var _levels: Label
var _ingredients: RichTextLabel                      # the recipe's details: chances, trains, needs, prices, ingredients
var _make: Button
var _open_bases := {}                                # "trade:base" -> true for the categories left open
var _craft_all: Button
var _all := {}                                       # the recipe CRAFT ALL is repeating ({} = not running)
var _timer_secs := 0                                 # the open craft timer's length and when it opened (ticks ms)
var _timer_at := 0
static var _craft_all_on := -1                       # the option (-1: not read yet)
const DONE_PART := 0.85                              # a timer closed this far through: the item was made (else interrupted)
const REFUSED_MS := 3000                             # no timer this long after asking: the server refused (gold, room ...)

# --- the overhaul's parts
static var _filters := {}                            # trade -> its filter choices (kept while the game runs)
static var _net_script: Variant = null               # craft_assist_net.gd once looked up (null: not in this build)
static var _net_checked := false
var _f := {}                                         # this trade's choices (a dictionary inside _filters)
var _page: Control                                   # the recipe page (hidden while the assistant is shown)
var _header: RichTextLabel                           # "TAILORING 734   LEATHERCRAFTING 675 - CLOTHWORKING 788"
var _hint: Label
var _tab_box: Control
var _tabs := {}                                      # view key -> its toggle button
var _type_opt: OptionButton
var _tier_opt: OptionButton
var _level_opt: OptionButton
var _sort_opt: OptionButton
var _grey_btn: Button
var _afford_btn: Button
var _sell_btn: Button
var _count: Label
var _assist_btn: Button
var _assist: Control = null                          # the crafting assistant panel (craft_assist_panel.gd), made once
var _assist_on := false
var _enchant_btn: Button
var _enchant: Control = null                         # the enchant helper page (craft_enchant_panel.gd), made once
var _enchant_on := false
var _built_for := -1                                 # the trade the tabs / type / tier lists were made for
var _types: Array = []                               # the TYPE list's keys ("" = all)
var _tiers: Array = []
var _secondaries: Array = []                         # the secondary skills this trade's recipes use / train
var _facts_cache := {}                               # recipe id -> its fixed facts (materials, tags, tier ...)
var _have_map := {}                                  # backpack: lower-case name -> count (made once a refresh)
var _priced := false                                 # the server's prices (op 42) are in for this trade
var _rows: Array = []                                # this refresh's rows (all recipes of the trade)
var _shown: Array = []                               # the rows the filters let through (tree order)
var _best: Array = []                                # the filtered rows ranked best to train first
var _blocker := ""                                   # "LEATHERCRAFTING 675 HOLDS TAILORING BACK ..." ("" = none)
var _blocker_long := ""
var _keep: TreeItem = null
var _refresh_queued := false


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
	# the skills header and its plain explanation (hover for the whole rule)
	_header = RichTextLabel.new()
	_header.bbcode_enabled = true
	_header.scroll_active = false
	_header.autowrap_mode = TextServer.AUTOWRAP_OFF
	_header.position = Vector2(12, Y_HEAD)
	_header.size = Vector2(W - 29, 13)
	_header.add_theme_font_override("normal_font", UiArt.font(true))
	_header.add_theme_font_size_override("normal_font_size", 8)
	_header.add_theme_color_override("default_color", UiArt.CREAM)
	_header.mouse_filter = Control.MOUSE_FILTER_PASS
	UiArt.themed(_header)
	add_child(_header)
	_hint = CompanionUi.text(self, "", Vector2(12, Y_HINT), Vector2(W - 29, 22), 8, UiArt.DIM, true)
	_hint.mouse_filter = Control.MOUSE_FILTER_PASS
	# the recipe page: filters, the list and the chosen recipe
	_page = Control.new()
	_page.position = Vector2.ZERO
	_page.size = Vector2(W, H)
	_page.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_page)
	_tab_box = Control.new()
	_tab_box.position = Vector2(12, Y_TABS)
	_tab_box.size = Vector2(W - 29, 16)
	_tab_box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_page.add_child(_tab_box)
	var ow := (W - 29 - 9) / 4.0
	_type_opt = CompanionUi.option(_page, ["ALL TYPES"], Vector2(12, Y_OPTS), Vector2(ow, 16), func(i: int):
		_f.type = String(_types[i]) if i < _types.size() else ""
		refresh())
	_type_opt.tooltip_text = "Only one kind of item (the slot for armour: hands, head, chest ...)"
	_tier_opt = CompanionUi.option(_page, ["ALL TIERS"], Vector2(12 + (ow + 3), Y_OPTS), Vector2(ow, 16), func(i: int):
		_f.tier = String(_tiers[i]) if i < _tiers.size() else ""
		refresh())
	_tier_opt.tooltip_text = "Only one material tier (the first word of the name: bronze, iron ... / woolen, linen ...)"
	var levels := []
	for l in LEVEL_RANGES:
		levels.append(l[0])
	_level_opt = CompanionUi.option(_page, levels, Vector2(12 + (ow + 3) * 2, Y_OPTS), Vector2(ow, 16), func(i: int):
		_f.level = i
		refresh())
	_level_opt.tooltip_text = "Only recipes of these difficulty colours for your skill"
	_sort_opt = CompanionUi.option(_page, SORTS, Vector2(12 + (ow + 3) * 3, Y_OPTS), Vector2(ow, 16), func(i: int):
		_f.sort = i
		refresh())
	_sort_opt.tooltip_text = "Order inside each category: required level, money per skill point (the server's prices), " \
			+ "time per skill point (normal craft times), or name"
	_grey_btn = _toggle("HIDE GREY", Vector2(12, Y_TOGGLES), 78, "grey",
			"Hide the grey recipes (they give no skill points any more)")
	_afford_btn = _toggle("CAN AFFORD", Vector2(93, Y_TOGGLES), 82, "afford", "")
	_sell_btn = _toggle("SELLABLE", Vector2(178, Y_TOGGLES), 72, "sell", "")
	var reset := UiArt.button("RESET", Vector2(56, 16), 8)
	reset.position = Vector2(253, Y_TOGGLES)
	reset.tooltip_text = "Clear every filter for this trade"
	reset.pressed.connect(func():
		_f.merge(_default_filters(), true)
		_sync_filter_controls()
		refresh())
	_page.add_child(reset)
	_count = UiArt.label("", 8, UiArt.DIM)
	_count.position = Vector2(312, Y_TOGGLES + 2)
	_count.size = Vector2(W - 329, 14)
	_count.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_page.add_child(_count)
	_tree = Tree.new()
	_tree.position = Vector2(10, Y_TREE)
	_tree.size = Vector2(W - 27, TREE_H)
	_tree.hide_root = true
	_tree.select_mode = Tree.SELECT_ROW
	_tree.columns = 3
	_tree.column_titles_visible = true
	_tree.set_column_title(0, "RECIPE")
	_tree.set_column_title(1, "NEEDS")
	_tree.set_column_title(2, "ALSO TRAINS")
	_tree.set_column_expand(0, true)
	_tree.set_column_expand(1, false)
	_tree.set_column_expand(2, false)
	_tree.set_column_custom_minimum_width(1, 44)
	_tree.set_column_custom_minimum_width(2, 132)
	_tree.set_column_clip_content(0, true)
	_tree.set_column_clip_content(2, true)
	_tree.add_theme_font_override("font", UiArt.font(false))
	_tree.add_theme_font_size_override("font_size", 9)
	_tree.add_theme_font_override("title_button_font", UiArt.font(true))
	_tree.add_theme_font_size_override("title_button_font_size", 8)
	_tree.add_theme_color_override("title_button_color", UiArt.GOLD)
	_tree.add_theme_color_override("font_color", UiArt.CREAM)
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(0.02, 0.02, 0.02, 0.85)
	_tree.add_theme_stylebox_override("panel", bg)
	var tb := StyleBoxFlat.new()
	tb.bg_color = Color(0.07, 0.06, 0.05, 0.95)
	for k in ["title_button_normal", "title_button_hover", "title_button_pressed"]:
		_tree.add_theme_stylebox_override(k, tb)
	UiArt.themed(_tree)
	_tree.item_selected.connect(_on_pick)
	_tree.item_activated.connect(func(): make_selected())
	_tree.item_collapsed.connect(func(it: TreeItem):
		var b = it.get_metadata(0)
		if b is String:
			_open_bases["%d:%s" % [trade, b]] = not it.collapsed)
	_page.add_child(_tree)
	_icon = TextureRect.new()
	_icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_icon.stretch_mode = TextureRect.STRETCH_SCALE
	_icon.position = Vector2(12, Y_DETAIL)
	_icon.size = Vector2(32, 32)
	_page.add_child(_icon)
	_name = UiArt.label("", 10, UiArt.GOLD_HI)
	_name.position = Vector2(50, Y_DETAIL)
	_name.size = Vector2(W - 77, 16)
	_name.clip_text = true
	_page.add_child(_name)
	_levels = UiArt.label("", 8, UiArt.CREAM)
	_levels.position = Vector2(50, Y_DETAIL + 17)
	_levels.size = Vector2(W - 77, 14)
	_levels.clip_text = true
	_page.add_child(_levels)
	_ingredients = RichTextLabel.new()
	_ingredients.bbcode_enabled = true
	_ingredients.position = Vector2(12, Y_DETAIL + 36)
	_ingredients.size = Vector2(W - 29, H - 36 - (Y_DETAIL + 36) - 2)
	_ingredients.add_theme_font_override("normal_font", UiArt.font(false))
	_ingredients.add_theme_font_override("bold_font", UiArt.font(true))
	_ingredients.add_theme_font_size_override("normal_font_size", 9)
	_ingredients.add_theme_font_size_override("bold_font_size", 9)
	_ingredients.add_theme_color_override("default_color", UiArt.CREAM)
	_ingredients.mouse_filter = Control.MOUSE_FILTER_PASS
	UiArt.themed(_ingredients)
	_page.add_child(_ingredients)
	_make = UiArt.button("MAKE", Vector2(80, 18), 10)
	_make.position = Vector2(W * 0.5 - 126, H - 30)
	_make.pressed.connect(make_selected)
	add_child(_make)
	_craft_all = UiArt.button("CRAFT ALL", Vector2(80, 18), 10)
	_craft_all.position = Vector2(W * 0.5 - 40, H - 30)
	_craft_all.pressed.connect(craft_all)
	add_child(_craft_all)
	_assist_btn = UiArt.button("ASSISTANT", Vector2(80, 18), 10)
	_assist_btn.position = Vector2(W * 0.5 + 46, H - 30)
	_assist_btn.tooltip_text = "The crafting assistant: set a skill goal, see the cost and time, let it craft " \
			+ "(a quality of life option, not in 1.65 - Settings > Interface > Crafting > CRAFTING ASSISTANT)"
	_assist_btn.pressed.connect(func(): show_assistant(not _assist_on))
	add_child(_assist_btn)
	# the enchant helper (owner 2026-11-08: "add an enchant button ... dont give anything out for free just streamline
	# it"): always there - every gem / tincture is still crafted with your own skill and costs real gold
	_enchant_btn = UiArt.button("ENCHANT", Vector2(80, 18), 10)
	_enchant_btn.position = Vector2(W * 0.5 + 132, H - 30)
	_enchant_btn.tooltip_text = "The enchant helper: spellcraft gems or an alchemy tincture onto an item in your " \
			+ "backpack - it buys the materials, crafts them with your skill at the normal time and combines them"
	_enchant_btn.pressed.connect(func(): show_enchant(not _enchant_on))
	add_child(_enchant_btn)
	visibility_changed.connect(func():
		if not visible and not _all.is_empty():
			_stop_all("")                                # (the item being made goes on; no more after it)
		)
	PlayerData.hook(self, Server.craft_changed, func():
		if visible:
			_refresh_soon())
	PlayerData.hook(self, PlayerData.inventory_changed, func():
		if visible:
			if String(_f.get("view", "all")) == "have":
				_refresh_soon()                          # (the HAVE MATS list changes with the backpack)
			else:
				_have_map = _backpack_counts()
				_show_recipe())
	if Server.has_signal("craft_assist_changed"):        # the server's prices arrived (op 42)
		PlayerData.hook(self, Signal(Server, "craft_assist_changed"), func(kind):
			if String(kind) == "status":
				_assist_status()                         # (MAKE / CRAFT ALL wait while the assistant runs)
			elif visible and String(kind) == "recipes":
				_refresh_soon())
	PlayerData.hook(self, Server.craft_timer, func(secs: int, title: String):
		if secs >= 0 and title.begins_with("Currently Making"):
			_timer_secs = secs
			_timer_at = Time.get_ticks_msec()
		elif _timer_at == 0 and not _all.is_empty():
			pass                                         # a close with no timer open: the server closes twice when an
			                                             # item is done (CraftAction.MakeItem, then CleanupCraftAction)
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


## One refresh at the end of the frame however many updates came in it (a gained point sends several).
func _refresh_soon() -> void:
	if not _refresh_queued:
		_refresh_queued = true
		(func():
			_refresh_queued = false
			if visible:
				refresh()).call_deferred()


func _toggle(caption: String, pos: Vector2, width: float, key: String, tip: String) -> Button:
	var b := UiArt.button(caption, Vector2(width, 16), 8)
	b.toggle_mode = true
	b.position = pos
	b.tooltip_text = tip
	b.toggled.connect(func(on: bool):
		if bool(_f.get(key, false)) != on:
			_f[key] = on
			refresh())
	_page.add_child(b)
	return b


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
	_ask_prices()


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
	_ask_prices()
	if _assist and _assist.has_method("refresh"):
		_assist.call("refresh")                          # (the panel follows the window's trade)


func skill() -> int:
	return int(Server.craft_skills.get(trade, {}).get("points", 0))


static func skill_of(id: int) -> int:
	return int(Server.craft_skills.get(id, {}).get("points", 0))


static func skill_name(id: int) -> String:
	if SKILL_NAMES.has(id):
		return String(SKILL_NAMES[id])
	return Server.trade_name(id)


static func con_color(recipe_level: int, skill_points: int) -> Color:
	var d := recipe_level - skill_points
	for c in CON:
		if d <= int(c[0]):
			return c[1]
	return PURPLE


## The server's con (AbstractCraftingSkill.GetItemCon): -3 grey, -2 green, -1 blue, 0 yellow, 1 orange, 2 red, 3 purple.
static func item_con(skill_points: int, recipe_level: int) -> int:
	var d := recipe_level - skill_points
	if d <= -50:
		return -3
	if d <= -31:
		return -2
	if d <= -11:
		return -1
	if d <= 0:
		return 0
	if d <= 19:
		return 1
	if d <= 49:
		return 2
	return 3


static func make_chance(con: int) -> int:
	return int(MAKE_PCT.get(clampi(con, -3, 3), 0))


static func gain_chance(con: int) -> int:
	return int(GAIN_PCT.get(clampi(con, -3, 3), 0))


## A player's craft time in seconds (AbstractCraftingSkill.GetCraftingTime with the player rules: no GM 0 s, crafting
## speed 1, no True 1.65 bonuses): (recipe level / 100 + 1) x materials / 4, quicker for easy recipes, at least 1.
static func player_secs(recipe_level: int, units: int, con: int) -> int:
	var mult := maxi(recipe_level / 100 + 1, 1)          # (an integer division on the server too)
	var t := int(mult * units / 4.0)
	t = int(t * float(TIME_MOD.get(con, 1.0)))
	return maxi(t, 1)


## The secondary-skill minimum when the server's own number (op 42) is not in: the per-trade rules of
## GetSecondaryCraftingSkillMinimumLevel (Tailoring.cs, ArmorCrafting.cs, WeaponCrafting.cs, Fletching.cs) - they go by
## the product's object type, which the client's recipe list does not carry, so by the product's name here (checked
## against the server's tables: right for 99% of recipes; op 42 replaces it).
static func rule_minimum(trade_id: int, r: Dictionary, realm: String) -> int:
	var b := String(r.get("base", "")).to_lower()
	var s := int(r.get("skill", 0))
	match trade_id:
		11:                                              # cloth, leather and studded armour: recipe level - 30
			return 0 if _has_any(b, ["cloak", "harness", "trigger", "cushion"]) else s - 30
		2:                                               # studded, chain, plate, reinforced, scale: level - 60
			return 0 if b in ["ram cladding", "gimbal", "bucket", "counterweight"] else s - 60
		1:                                               # weapons: level - 60 (not shields, siege parts, left axes)
			if _has_any(b, ["shield", "buckler", "ram beak", "spring arms", "winding crank", "pivot"]):
				return 0
			if realm == "mid" and b in MID_LEFT_AXES:
				return 0
			return s - 60
		12:                                              # bows / instruments - 20, arrows / bolts / thrown - 15, staves - 35
			if b.contains("torsion"):
				return 0
			if _has_any(b, ["arrow", "bolt", "throwing"]):
				return s - 15
			if b.contains("staff"):
				return s - 35
			return s - 20
	return 0


static func _has_any(s: String, words: Array) -> bool:
	for w in words:
		if s.contains(String(w)):
			return true
	return false


## The crafting assistant's net script (C2's craft_assist_net.gd), or null when it is not there.
static func assist_net() -> Variant:
	if not _net_checked:
		_net_checked = true
		_net_script = load(ASSIST_NET) if ResourceLoader.exists(ASSIST_NET) else null
	return _net_script


func _ask_prices() -> void:
	var net = assist_net()
	if net != null:
		net.ask_recipes(trade)


## The server's prices / numbers for a recipe (op 42: level, cost, sell, sec_min, trains, status, secs), or {}.
func _info(id: int) -> Dictionary:
	var net = assist_net()
	if net == null:
		return {}
	var d = net.recipe_info.get(id, {})
	return d if d is Dictionary else {}


func _default_filters() -> Dictionary:
	return {"view": "all", "type": "", "tier": "", "level": 0, "sort": 0, "grey": false, "afford": false,
			"sell": false}


## A recipe's fixed facts (made once): the secondary skills its materials belong to, what a point in the trade adds to
## each (one per ingredient entry), the material units (craft time), its tier and its type.
func _facts(r: Dictionary) -> Dictionary:
	var id := int(r.id)
	if _facts_cache.has(id):
		return _facts_cache[id]
	var mats: Array = []
	var trains := {}
	var units := 0
	for ing in r.get("ingredients", []):
		var n := String(ing[0]).to_lower()
		var cnt := int(ing[1])
		units += cnt / 10 if cnt > 100 else cnt
		for m in MATERIAL_SKILL:
			if n.ends_with(String(m[0])):
				var s := int(m[1])
				if not mats.has(s):
					mats.append(s)
				if PER_MATERIAL_TRADES.has(trade):
					trains[s] = int(trains.get(s, 0)) + 1
				break
	if ALWAYS_TRAINS.has(trade):
		trains[int(ALWAYS_TRAINS[trade])] = 1
	var base := String(r.get("base", "")).to_lower()
	var name := String(r.get("name", "")).to_lower()
	var tier := ""
	if base != "" and name.ends_with(base) and name.length() > base.length():
		tier = name.substr(0, name.length() - base.length()).strip_edges()
	elif base != "" and name == base:
		tier = "plain"
	else:
		tier = name.get_slice(" ", 0)
	var last := base.get_slice(" ", base.get_slice_count(" ") - 1) if base != "" else "other"
	var f := {"mats": mats, "trains": trains, "units": maxi(units, 1), "tier": tier,
			"slot": String(SLOTS.get(last, last)), "rule_min": rule_minimum(trade, r, Server.my_realm())}
	_facts_cache[id] = f
	return f


## A recipe's row for this refresh: con, chances, the secondary minimum and who falls short, what it trains, time,
## cost per point (with the server's prices), have-all-materials.
func _row(r: Dictionary) -> Dictionary:
	var f := _facts(r)
	var id := int(r.id)
	var lvl := int(r.get("skill", 0))
	var con := item_con(skill(), lvl)
	var info := _info(id)
	var need: int = int(info.sec_min) if info.has("sec_min") else int(f.rule_min)
	var short := []                                      # [skill id, needed, you have] for each one short
	var reqs := []                                       # every secondary requirement [skill id, needed, you have]
	if need > 0:
		for s in f.mats:
			var have_s := skill_of(int(s))
			reqs.append([int(s), need, have_s])
			if have_s < need:
				short.append([int(s), need, have_s])
	var trains: Dictionary = f.trains
	if info.has("trains") and int(info.trains) > 0:
		var net = assist_net()
		var t := {}
		for s in net.trained_skills(int(info.trains)):
			t[int(s)] = int(f.trains.get(int(s), 1))
		trains = t
	var pm := make_chance(con)
	var pg := gain_chance(con)
	var secs: int = int(info.secs) if int(info.get("secs", 0)) > 0 else player_secs(lvl, int(f.units), con)
	var point := pm * pg / 10000.0
	var spp := secs / point if point > 0.0 else INF
	var cpp := INF
	if info.has("cost") and pg > 0:
		cpp = float(int(info.cost) - int(info.get("sell", 0))) / (pg / 100.0)
	var have_all := true
	for ing in r.get("ingredients", []):
		if int(_have_map.get(String(ing[0]).to_lower(), 0)) < int(ing[1]):
			have_all = false
			break
	return {"r": r, "id": id, "lvl": lvl, "con": con, "need": need, "short": short, "reqs": reqs, "trains": trains,
			"pm": pm, "pg": pg, "secs": secs, "spp": spp, "cpp": cpp, "info": info, "have_all": have_all,
			"can_gain": con > -3 and con < 3 and short.is_empty(), "tier": f.tier, "slot": f.slot,
			"base": String(r.get("group", "")) if String(r.get("group", "")) != "" else String(r.get("base", ""))}


func _backpack_counts() -> Dictionary:
	var m := {}
	for s in Server.inv:
		if int(s) >= 40 and int(s) <= 79:
			var it := ItemDb.item("srv:%d" % int(s))
			var n := String(it.get("name", "")).to_lower()
			if n != "":
				m[n] = int(m.get(n, 0)) + maxi(1, int((Server.inv[s] as Dictionary).get("count", 1)))
	return m


func refresh() -> void:
	var sk := skill()
	_title.text = ("%s  (%d)" % [Server.trade_name(trade), sk]).to_upper() if Server.craft_skills.has(trade) else Server.trade_name(trade).to_upper()
	var trades := _trades()
	_trade_label.text = "%s  -  %d OF %d" % [Server.trade_name(trade).to_upper(), trades.find(str(trade)) + 1, trades.size()]
	var list: Array = Server.recipes_for(Server.my_realm()).get(str(trade), [])
	if not _filters.has(trade):
		_filters[trade] = _default_filters()
	_f = _filters[trade]
	if _built_for != trade:
		_build_trade_controls(list)
	_have_map = _backpack_counts()
	_rows = []
	_priced = false
	for r in list:
		var row := _row(r)
		_rows.append(row)
		if not (row.info as Dictionary).is_empty():
			_priced = true
	_update_header()
	_update_filter_tips()
	_blocker_check()
	# the filters (all but the view), then the view
	var passed := []
	for row in _rows:
		if _passes(row):
			passed.append(row)
	_best = _rank_best(passed)
	var view := String(_f.view)
	var shown := []
	if view == "best":
		shown = _best.slice(0, BEST_MAX)
	else:
		for row in passed:
			if view == "have" and not bool(row.have_all):
				continue
			if view.begins_with("trains:") and not (row.trains as Dictionary).has(int(view.get_slice(":", 1))):
				continue
			shown.append(row)
	_fill_tree(shown, view)
	_count.text = "%d SHOWN OF %d" % [_shown.size(), list.size()]
	_show_recipe()


## Tabs, TYPE and TIER lists and the secondary skills for a new trade.
func _build_trade_controls(list: Array) -> void:
	_built_for = trade
	_facts_cache.clear()
	var counts := {}
	var used := {}
	var types := {}
	var tiers := {}                                      # tier -> lowest required level (the list's order)
	for r in list:
		var f := _facts(r)
		for s in f.trains:
			counts[s] = int(counts.get(s, 0)) + 1
			used[s] = true
		for s in f.mats:
			used[s] = true
		types[String(f.slot)] = true
		var t := String(f.tier)
		tiers[t] = mini(int(tiers.get(t, 99999)), int(r.get("skill", 0)))
	_secondaries = used.keys()
	_secondaries.sort()
	# tabs: ALL, BEST TO TRAIN, HAVE MATS, then TRAINS <skill> for each skill only some recipes train
	for c in _tab_box.get_children():
		_tab_box.remove_child(c)
		c.queue_free()
	_tabs = {}
	var x := 0.0
	var tname := Server.trade_name(trade)
	for t in [["ALL", "all", 40.0, "Every recipe of this trade, by category (RECOMMENDED FOR YOU first)"],
			["BEST TO TRAIN", "best", 86.0, "The recipes that give the most %s for your money right now: the " % tname
				+ "cheapest per skill point once the server's prices are in, else the orange and yellow ones you can make"],
			["HAVE MATS", "have", 66.0, "Only recipes whose materials are all in your backpack"]]:
		_tabs[t[1]] = _tab(String(t[0]), String(t[1]), x, float(t[2]), String(t[3]))
		x += float(t[2]) + 3.0
	var train_tabs := []
	for s in _secondaries:
		if int(counts.get(s, 0)) > 0 and int(counts.get(s, 0)) < list.size():
			train_tabs.append(int(s))
	if not train_tabs.is_empty():
		var cap := UiArt.label("TRAINS:", 8, UiArt.GOLD)
		cap.position = Vector2(x + 2, 3)
		cap.size = Vector2(44, 12)
		_tab_box.add_child(cap)
		x += 46.0
		var bw := minf(70.0, (_tab_box.size.x - x - 3.0 * (train_tabs.size() - 1)) / train_tabs.size())
		for s in train_tabs:
			var key := "trains:%d" % s
			_tabs[key] = _tab(String(SKILL_TAGS.get(s, skill_name(s))), key, x, bw,
					"Recipes that also raise %s (they use %s): +1 for each such material each time you gain a %s point"
					% [skill_name(s), String(MATERIAL_WORDS.get(s, "its materials")), tname])
			x += bw + 3.0
	if not _tabs.has(String(_f.view)):
		_f.view = "all"
	# TYPE and TIER lists
	_types = [""]
	var tk := types.keys()
	tk.sort()
	_types.append_array(tk)
	_type_opt.clear()
	for t in _types:
		_type_opt.add_item("ALL TYPES" if t == "" else String(t).to_upper())
	_tiers = [""]
	var rk := tiers.keys()
	rk.sort_custom(func(a, b): return int(tiers[a]) < int(tiers[b]) or (int(tiers[a]) == int(tiers[b]) and String(a) < String(b)))
	_tiers.append_array(rk)
	_tier_opt.clear()
	for t in _tiers:
		_tier_opt.add_item("ALL TIERS" if t == "" else String(t).to_upper())
	if not _types.has(String(_f.type)):
		_f.type = ""
	if not _tiers.has(String(_f.tier)):
		_f.tier = ""
	_sync_filter_controls()


func _tab(caption: String, key: String, x: float, width: float, tip: String) -> Button:
	var b := UiArt.button(caption, Vector2(width, 16), 8)
	b.toggle_mode = true
	b.position = Vector2(x, 0)
	b.tooltip_text = tip
	b.pressed.connect(func(): set_view(key))
	_tab_box.add_child(b)
	return b


## Show one view: "all", "best", "have" or "trains:<skill id>".
func set_view(key: String) -> void:
	_f.view = key
	_sync_filter_controls()
	refresh()


func _sync_filter_controls() -> void:
	for k in _tabs:
		(_tabs[k] as Button).set_pressed_no_signal(k == String(_f.get("view", "all")))
	_type_opt.select(maxi(_types.find(String(_f.get("type", ""))), 0))
	_tier_opt.select(maxi(_tiers.find(String(_f.get("tier", ""))), 0))
	_level_opt.select(int(_f.get("level", 0)))
	_sort_opt.select(int(_f.get("sort", 0)))
	_grey_btn.set_pressed_no_signal(bool(_f.get("grey", false)))
	_afford_btn.set_pressed_no_signal(bool(_f.get("afford", false)))
	_sell_btn.set_pressed_no_signal(bool(_f.get("sell", false)))


func _update_filter_tips() -> void:
	var wait := "" if _priced else " (needs the server's prices - they come when the window opens)"
	_afford_btn.tooltip_text = "Only recipes whose materials you can pay for at the merchant's price" + wait
	_sell_btn.tooltip_text = "Only recipes whose product a merchant buys" + wait


## The skills header and its explanation, for this trade.
func _update_header() -> void:
	var tname := Server.trade_name(trade)
	var parts := []
	for s in _secondaries:
		parts.append("%s %d" % [skill_name(int(s)).to_upper(), skill_of(int(s))])
	var head := "[color=#ffd973]%s %d[/color]" % [tname.to_upper(), skill()]
	if not parts.is_empty():
		head += "     [color=#9c9285]USES[/color]  " + "   -   ".join(parts)
	_header.text = head
	var ex := explain(trade)
	_hint.text = String(ex[0])
	_header.tooltip_text = String(ex[1])
	_hint.tooltip_text = String(ex[1])


## [one or two short lines, the whole rule] for a trade.
func explain(trade_id: int) -> Array:
	var tname := Server.trade_name(trade_id)
	var names := []
	for s in _secondaries:
		names.append(skill_name(int(s)))
	if PER_MATERIAL_TRADES.has(trade_id):
		var rule := ""
		match trade_id:
			11: rule = "Cloth, leather and studded armour needs each of those skills at least the recipe's level - 30 (a 735 recipe with leather squares needs Leathercrafting 705)."
			2: rule = "Studded, chain, plate, reinforced and scale armour needs each of those skills at least the recipe's level - 60."
			1: rule = "Weapons need each of those skills at least the recipe's level - 60 (shields and siege parts need none)."
			12: rule = "Bows, crossbows and instruments need each of those skills at least the recipe's level - 20, arrows, bolts and throwing weapons the level - 15, staves the level - 35."
		var mats := []
		for s in _secondaries:
			mats.append("%s raise %s" % [String(MATERIAL_WORDS.get(int(s), "its materials")), skill_name(int(s))])
		var short := "%s is your trade: the NEEDS number is the %s level a recipe needs. Each %s point you gain also gives +1 to %s for each of its materials (ALSO TRAINS). Hover for the rules." \
				% [tname, tname, tname, " / ".join(names) if not names.is_empty() else "the material skills"]
		var long := "%s decides which recipes you can make; the number by each recipe is the %s level it needs, and its colour is how hard it is for your %s (grey gives no points, purple can't be made yet).\n\n" % [tname, tname, tname] \
				+ "Whenever you gain a %s point, each ingredient of that recipe that is a crafting material also gives +1 to its own skill: %s.\n\n" % [tname, "; ".join(mats)] \
				+ rule + " The window marks a recipe NEEDS when you are short."
		return [short, long]
	if ALWAYS_TRAINS.has(trade_id):
		var sub := skill_name(int(ALWAYS_TRAINS[trade_id]))
		var t := "%s is your trade: the NEEDS number is the %s level a recipe needs. Each %s point also gives +1 %s; no other skill is needed." % [tname, tname, tname, sub]
		return [t, t]
	if SKILL_NAMES.has(trade_id):
		var t2 := "%s's own recipes raise only %s. It also rises when you gain a point in Weaponcraft, Armorcraft, Tailoring or Fletching with a recipe that uses %s - their armour and weapons need it." % [tname, tname, String(MATERIAL_WORDS.get(trade_id, "its materials"))]
		if trade_id == 10 or trade_id == 9:
			t2 = "%s's own recipes raise only %s. It also rises with every %s point." % [tname, tname, "Alchemy" if trade_id == 10 else "Spellcrafting"]
		return [t2, t2]
	var t3 := "The NEEDS number is the %s level a recipe needs; its colour is how hard it is for you." % tname
	return [t3, t3]


## Yellow-to-red recipes (the ones that train well) held back by a short secondary skill: say which skill, how many it
## blocks and what to do ("" when none is blocked).
func _blocker_check() -> void:
	_blocker = ""
	_blocker_long = ""
	var open := 0                                        # yellow-red recipes you can make now
	var total := 0
	var tally := {}                                      # skill -> [recipes it blocks, lowest need]
	for row in _rows:
		var con := int(row.con)
		if con < 0 or con > 2:
			continue
		total += 1
		if bool(row.can_gain):
			open += 1
		for s in row.short:
			var k := int(s[0])
			var t: Array = tally.get(k, [0, 99999])
			tally[k] = [int(t[0]) + 1, mini(int(t[1]), int(s[1]))]
	if tally.is_empty():
		return
	var worst := -1
	for k in tally:
		if worst < 0 or int(tally[k][0]) > int(tally[worst][0]):
			worst = int(k)
	var tname := Server.trade_name(trade)
	var sname := skill_name(worst)
	var tag := String(SKILL_TAGS.get(worst, ""))
	var n := int(tally[worst][0])
	if open == 0:
		_blocker = "%s %d HOLDS %s BACK - SEE TRAINS %s" % [sname.to_upper(), skill_of(worst), tname.to_upper(), tag]
	else:
		_blocker = "%s %d BLOCKS %d OF %d GOOD RECIPES - SEE TRAINS %s" % [sname.to_upper(), skill_of(worst), n, total, tag]
	_blocker_long = ("%d of the %d yellow, orange and red %s recipes (the ones that train %s well) need %s %d or more, "
			+ "and you have %d. Raise %s with the TRAINS %s recipes you can make (each %s point also gives it +1 per %s) "
			+ "or with %s's own recipes (< > to that trade).") 			% [n, total, tname, tname, sname, int(tally[worst][1]), skill_of(worst), sname, tag, tname,
			String(MATERIAL_WORDS.get(worst, "material")), sname]


## The filters other than the view (type, tier, level range, hide grey, can afford, sellable).
func _passes(row: Dictionary) -> bool:
	if String(_f.type) != "" and String(row.slot) != String(_f.type):
		return false
	if String(_f.tier) != "" and String(row.tier) != String(_f.tier):
		return false
	var con := int(row.con)
	var lr: Array = LEVEL_RANGES[clampi(int(_f.level), 0, LEVEL_RANGES.size() - 1)]
	if con < int(lr[1]) or con > int(lr[2]):
		return false
	if bool(_f.grey) and con <= -3:
		return false
	var info: Dictionary = row.info
	if bool(_f.afford) and _priced and (info.is_empty() or int(info.cost) > PlayerData.total_copper()):
		return false
	if bool(_f.sell) and _priced and (info.is_empty() or int(info.get("sell", 0)) <= 0):
		return false
	return true


## Best to train first: only recipes that can raise the trade now (not grey / purple, secondary minimum met). With the
## server's prices: the lowest net money per skill point, then the fewest seconds per point; without: orange, yellow,
## red, blue, green, then the fewest material units (cheaper), then the lower level.
func _rank_best(rows: Array) -> Array:
	var out := []
	for row in rows:
		if bool(row.can_gain):
			out.append(row)
	if _priced:
		out.sort_custom(func(a, b):
			if float(a.cpp) != float(b.cpp):
				return float(a.cpp) < float(b.cpp)
			return float(a.spp) < float(b.spp))
	else:
		var order := {1: 0, 0: 1, 2: 2, -1: 3, -2: 4}
		out.sort_custom(func(a, b):
			var oa := int(order.get(int(a.con), 9))
			var ob := int(order.get(int(b.con), 9))
			if oa != ob:
				return oa < ob
			var ua := int(_facts(a.r).units)
			var ub := int(_facts(b.r).units)
			if ua != ub:
				return ua < ub
			return int(a.lvl) < int(b.lvl))
	return out


func _sort_key_less(a: Dictionary, b: Dictionary) -> bool:
	match int(_f.sort):
		1:
			if _priced and float(a.cpp) != float(b.cpp):
				return float(a.cpp) < float(b.cpp)
		2:
			if float(a.spp) != float(b.spp):
				return float(a.spp) < float(b.spp)
		3:
			return String(a.r.name) < String(b.r.name)
	if int(a.lvl) != int(b.lvl):
		return int(a.lvl) < int(b.lvl)
	return String(a.r.name) < String(b.r.name)


func _filters_active() -> bool:
	return String(_f.type) != "" or String(_f.tier) != "" or int(_f.level) != 0 or bool(_f.grey) \
			or bool(_f.afford) or bool(_f.sell)


func _fill_tree(rows: Array, view: String) -> void:
	_tree.clear()
	_shown = []
	_keep = null
	var root := _tree.create_item()
	if _blocker != "" and (view == "all" or view == "best"):
		var b := _tree.create_item(root)
		b.set_text(0, "! " + _blocker)
		b.set_custom_color(0, Color(1.0, 0.55, 0.45))
		b.set_expand_right(0, true)
		b.set_selectable(0, false)
		b.set_selectable(1, false)
		b.set_selectable(2, false)
		b.set_tooltip_text(0, _blocker_long)
	if view == "best":
		for row in rows:
			_add_row(root, row)
		if rows.is_empty():
			var e := _tree.create_item(root)
			e.set_text(0, "NOTHING HERE CAN RAISE %s NOW" % Server.trade_name(trade).to_upper())
			e.set_expand_right(0, true)
			e.set_custom_color(0, UiArt.DIM)
			e.set_selectable(0, false)
		_open_keep()
		return
	if view == "all" and not _best.is_empty():
		var rec := _tree.create_item(root)
		rec.set_text(0, "RECOMMENDED FOR YOU")
		rec.set_custom_color(0, UiArt.GOLD_HI)
		rec.set_tooltip_text(0, "The best recipes to raise %s now (as BEST TO TRAIN)" % Server.trade_name(trade))
		rec.set_selectable(0, false)
		rec.set_selectable(1, false)
		rec.set_selectable(2, false)
		rec.set_metadata(0, "*recommended")
		rec.collapsed = not bool(_open_bases.get("%d:*recommended" % trade, true))
		for row in _best.slice(0, RECOMMENDED):
			_add_row(rec, row, false)
	# categories in the list's order (or by their best row for the other sorts), rows sorted inside
	var groups := {}
	var order := []
	for row in rows:
		var base := String(row.base)
		if not groups.has(base):
			groups[base] = []
			order.append(base)
		(groups[base] as Array).append(row)
	for base in order:
		(groups[base] as Array).sort_custom(_sort_key_less)
	if int(_f.sort) != 0:
		order.sort_custom(func(a, b): return _sort_key_less(groups[a][0], groups[b][0]))
	var open_all := (view != "all" or _filters_active()) and rows.size() <= AUTO_OPEN
	for base in order:
		var p := _tree.create_item(root)
		p.set_text(0, String(base).to_upper())
		p.set_custom_color(0, UiArt.GOLD)
		p.set_selectable(0, false)
		p.set_selectable(1, false)
		p.set_selectable(2, false)
		p.set_metadata(0, base)
		var tags := {}
		for row in groups[base]:
			for s in row.trains:
				tags[s] = true
		var tk := tags.keys()
		tk.sort()
		var tt := []
		for s in tk:
			tt.append(String(SKILL_TAGS.get(s, "")))
		p.set_text(2, " ".join(tt))
		p.set_custom_color(2, UiArt.DIM)
		p.collapsed = not (open_all or bool(_open_bases.get("%d:%s" % [trade, base], false)))
		for row in groups[base]:
			_add_row(p, row)
	_open_keep()


func _open_keep() -> void:
	if _keep:
		var p := _keep.get_parent()
		if p and p != _tree.get_root():
			p.collapsed = false
		_keep.select(0)


func _add_row(parent: TreeItem, row: Dictionary, count := true) -> TreeItem:
	var r: Dictionary = row.r
	var it := _tree.create_item(parent)
	var col := con_color(int(row.lvl), skill())
	var short: Array = row.short
	it.set_text(0, String(r.name))
	it.set_custom_color(0, col)
	it.set_text(1, str(int(row.lvl)))
	it.set_custom_color(1, col)
	it.set_text_alignment(1, HORIZONTAL_ALIGNMENT_RIGHT)
	var tip := "%s - needs %s %d (you %d), %s for you: %d%% to make, %d%% to gain a point" % [String(r.name),
			Server.trade_name(trade), int(row.lvl), skill(), String(CON_NAMES.get(int(row.con), "")), int(row.pm), int(row.pg)]
	if not short.is_empty():
		var s: Array = short[0]
		it.set_text(2, "NEEDS %s %d" % [String(SKILL_TAGS.get(int(s[0]), "")), int(s[1])])
		it.set_custom_color(2, UiArt.RED)
		var lines := []
		for x in short:
			lines.append(needs_text(x))
		tip += "\n" + "\n".join(lines)
	else:
		it.set_text(2, trains_tags(row.trains))
		it.set_custom_color(2, Color(0.62, 0.78, 0.6))
	for c in 3:
		it.set_tooltip_text(c, tip)
	it.set_metadata(0, r)
	if count:
		_shown.append(row)
	if _keep == null and not selected.is_empty() and int(selected.id) == int(r.id):
		_keep = it
	return it


## "Needs Leathercrafting 705 - you have 675".
static func needs_text(x: Array) -> String:
	return "Needs %s %d - you have %d" % [skill_name(int(x[0])), int(x[1]), int(x[2])]


## "LEATHER+1 CLOTH+2" (what each point gained in the trade adds).
static func trains_tags(trains: Dictionary) -> String:
	var ks := trains.keys()
	ks.sort()
	var out := []
	for s in ks:
		out.append("%s+%d" % [String(SKILL_TAGS.get(int(s), "?")), int(trains[s])])
	return " ".join(out)


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


static func _money(c: int) -> String:
	if c < 0:
		return "a profit of " + PlayerData.money_string(-c)
	return PlayerData.money_string(c)


## The chosen recipe's row (made fresh: the skills may have changed).
func selected_row() -> Dictionary:
	if selected.is_empty():
		return {}
	return _row(selected)


func _show_recipe() -> void:
	if selected.is_empty():
		_icon.texture = null
		_name.text = ""
		_levels.text = "PICK A RECIPE" if _tree.get_root() and _tree.get_root().get_child_count() > 0 else "NO RECIPES FOR THIS TRADE"
		_ingredients.text = ("[color=#ff8c73]%s[/color]" % _blocker_long) if _blocker_long != "" else ""
		_update_make()
		return
	var row := selected_row()
	var tname := Server.trade_name(trade)
	var lvl := int(row.lvl)
	var con := int(row.con)
	_icon.texture = ItemDb.icon(Server.item_icon(int(selected.get("model", 0))))
	_name.text = String(selected.name).to_upper()
	_name.add_theme_color_override("font_color", con_color(lvl, skill()))
	_levels.text = "REQUIRES %s %d (YOU %d)   ITEM LEVEL %d   %s" % [tname.to_upper(), lvl, skill(),
			int(selected.get("level", 0)), String(CON_NAMES.get(con, "")).to_upper()]
	var lines := []
	if con >= 3:
		lines.append("[color=#cc80ff]Too hard for you yet: 0% to make it.[/color]")
	elif con <= -3:
		lines.append("Chance to make [b]%d%%[/b] - grey: [b]0%%[/b] chance of a %s point (too easy now)." % [int(row.pm), tname])
	else:
		lines.append("Chance to make [b]%d%%[/b]   -   chance of a %s point [b]%d%%[/b] on a success" % [int(row.pm), tname, int(row.pg)])
	var tr := [tname]
	var ks := (row.trains as Dictionary).keys()
	ks.sort()
	for s in ks:
		tr.append("%s (+%d)" % [skill_name(int(s)), int(row.trains[s])])
	lines.append("Trains: [b]%s[/b]%s" % [" + ".join(tr), "" if ks.is_empty() else "  (when you gain a %s point)" % tname])
	for x in row.reqs:
		if int(x[2]) < int(x[1]):
			lines.append("[color=#ff6b59][b]%s[/b][/color]" % needs_text(x))
		else:
			lines.append("[color=#9fd59a]%s %d needed - you have %d[/color]" % [skill_name(int(x[0])), int(x[1]), int(x[2])])
	var secs := int(row.secs)
	if float(row.spp) < INF:
		lines.append("About %d s a try (normal time); a %s point every %d s on average" % [secs, tname, int(round(float(row.spp)))])
	else:
		lines.append("About %d s a try (normal time)" % secs)
	var info: Dictionary = row.info
	if not info.is_empty():
		var cost := int(info.get("cost", 0))
		var sell := int(info.get("sell", 0))
		var eco := "Materials %s   -   sells for %s" % [_money(cost), _money(sell) if sell > 0 else "nothing"]
		if int(row.pg) > 0 and con > -3 and con < 3:
			eco += "   -   about %s a point (selling what you make)" % _money(int(round(float(row.cpp))))
		lines.append(eco)
	elif assist_net() != null:
		lines.append("[color=#9c9285]Prices: waiting for the server.[/color]")
	lines.append("[color=#d9b661]Ingredients (need / you have):[/color]")
	var short := false
	for ing in selected.get("ingredients", []):
		var need := int(ing[1])
		var got := int(_have_map.get(String(ing[0]).to_lower(), 0))
		var col := "#e8dcc0" if got >= need else "#ff6b59"
		lines.append("[color=%s]%d %s   (%d)[/color]" % [col, need, String(ing[0]).replace("[", "[lb]"), got])
		short = short or got < need
	if short:
		# the server buys what's missing when MAKE is pressed, if you can afford it (owner 2026-11-08)
		lines.append("[color=#9fd59a]Missing materials are bought for you when you press MAKE (if you can afford them).[/color]")
	_ingredients.text = "\n".join(lines)
	_update_make()


func _update_make() -> void:
	if _make == null:
		return
	if _making >= 0:
		_make.text = "STOP"
		_make.disabled = false
		_make.tooltip_text = ""
	else:
		_make.text = "MAKE"
		_make.disabled = selected.is_empty()
		var short: Array = selected_row().get("short", []) if not selected.is_empty() else []
		_make.tooltip_text = ("The server will refuse it: " + needs_text(short[0])) if not short.is_empty() else ""
	var on := craft_all_enabled()
	_craft_all.text = "STOP ALL" if not _all.is_empty() else "CRAFT ALL"
	_craft_all.disabled = not on or (_all.is_empty() and (selected.is_empty() or _making >= 0))
	_craft_all.tooltip_text = ("Make it again and again until the materials run out or the backpack is full" if on
			else "Turn it on in Settings > Interface > Crafting > CRAFT ALL BUTTON (a quality of life option, not in 1.65)")
	if assist_running():                                 # (the server takes a MAKE then as "stop the run")
		for b in [_make, _craft_all]:
			b.disabled = true
			b.tooltip_text = ASSIST_BUSY


## The crafting assistant is running a goal on the server (op 40 state 1).
static func assist_running() -> bool:
	var net = assist_net()
	return net != null and bool(net.running())


## An assistant status (op 40): a run that starts ends CRAFT ALL; MAKE / CRAFT ALL follow its state.
func _assist_status() -> void:
	if assist_running() and not _all.is_empty():
		_stop_all("Craft all stopped: the crafting assistant is running.")
	_update_make()


## The crafting assistant page (C2's craft_assist_panel.gd) in place of the recipe page, or back.
func show_assistant(on: bool) -> void:
	if on and _enchant_on:
		show_enchant(false)
	_assist_on = on
	if on and _assist == null:
		var rect_pos := Vector2(10, Y_TABS)
		var rect_size := Vector2(W - 20, H - 36 - Y_TABS - 2)
		var scr = load(ASSIST_PANEL) if ResourceLoader.exists(ASSIST_PANEL) else null
		if scr != null:
			_assist = scr.new()
			_assist.position = rect_pos
			_assist.size = rect_size
			add_child(_assist)
			_assist.call("setup", self)
		else:
			_assist = Control.new()
			_assist.position = rect_pos
			_assist.size = rect_size
			add_child(_assist)
			CompanionUi.text(_assist, "The crafting assistant is not in this build.", Vector2(4, 8),
					Vector2(rect_size.x - 8, 30), 9, UiArt.DIM, true)
	if _assist:
		_assist.visible = on
	_page.visible = not on and not _enchant_on
	_assist_btn.text = "RECIPES" if on else "ASSISTANT"


## The enchant helper page (craft_enchant_panel.gd) in place of the recipe page, or back - like the assistant's.
func show_enchant(on: bool) -> void:
	if on and _assist_on:
		show_assistant(false)
	_enchant_on = on
	if on and _enchant == null:
		var rect_pos := Vector2(10, Y_TABS)
		var rect_size := Vector2(W - 20, H - 36 - Y_TABS - 2)
		var scr = load(ENCHANT_PANEL) if ResourceLoader.exists(ENCHANT_PANEL) else null
		if scr != null:
			_enchant = scr.new()
			_enchant.position = rect_pos
			_enchant.size = rect_size
			add_child(_enchant)
			_enchant.call("setup", self)
		else:
			_enchant = Control.new()
			_enchant.position = rect_pos
			_enchant.size = rect_size
			add_child(_enchant)
			CompanionUi.text(_enchant, "The enchant helper is not in this build.", Vector2(4, 8),
					Vector2(rect_size.x - 8, 30), 9, UiArt.DIM, true)
	if _enchant:
		_enchant.visible = on
	_page.visible = not on and not _assist_on
	_enchant_btn.text = "RECIPES" if on else "ENCHANT"


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
## (Missing materials no longer stop it: the server buys them on each MAKE when you can afford them, owner 2026-11-08 -
## when you can't, it refuses, no timer opens and CRAFT ALL stops with the server's reason in chat.)
func _cannot_make(_r: Dictionary) -> String:
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
