extends Control
## The enchant helper's page in the tradeskill window (owner 2026-11-08: "add an enchant button that when an item that
## can be enchanted with either a proc or has slots for stats it can auto buy and enchant it if you have the skill level.
## dont give anything out for free just streamline it with an enchant helper").
## Pick an item from your backpack; the server says what it can take (op 43): SPELLCRAFT - one gem per free bonus slot,
## with the imbue points against the item's maximum and your spellcraft cap - or ALCHEMY - one tincture (a proc or
## charges). ENCHANT asks the server to buy the missing materials at merchant prices, craft each gem / the tincture with
## your own skill at the normal time and combine them with the normal 1.65 rolls (op 44 shows it live). It changes no
## rules, so it is always there (no option). Nothing is decided here: every button is a /craftassist command
## (game/net/craft_assist_net.gd).
## The craft window makes it (load(...).new()), sets its position and size, adds it and calls setup(window).

const Net := preload("res://game/net/craft_assist_net.gd")
const SPELLCRAFTING := 13
const ALCHEMY := 4
const MAX_SLOTS := 4
# SpellCrafting.CalculateChanceToOverchargeItem's tables: points over the maximum -> start penalty, quality 94..100 -> bonus
const OC_START := [0, 10, 20, 30, 50, 70]
const QUAL_OC := [0, 0, 6, 8, 10, 18, 26]
const ORANGE := Color(1.0, 0.65, 0.25)
const KIND_TEXT := {1: "WEAPON PROC", 2: "ARMOUR PROC", 3: "CHARGES"}
const NOTE := "Nothing is free: every gem and tincture is crafted with your own skill at the normal craft time, and " \
		+ "its missing materials are bought at merchant prices with your gold. Gems and tinctures already in your " \
		+ "backpack are used first. Moving, fighting or logging out stops it (what was made is kept)."
# what a failed overcharge does in this server (SpellCrafting.ApplySpellcraftGems): the success roll fails and then the
# preserve roll fails -> the gems explode (lost) and you die; the item itself is kept. Preserve roll made: nothing lost.
const OC_WARN := "If it fails, the gems can explode: they are lost and you die - the item is kept."
const CHARGE_NOTE := "Procs are permanent (weapon procs on weapons, armour procs on armour). Charges go on armour, " 		+ "shields and instruments only: a few uses by the item's quality, recharged at a Recharger for 10 gold each."
const NO_SKILL := "You need Spellcrafting or Alchemy to enchant items."

var window: Control = null
var slot := -1                                       # the picked backpack slot (-1: none)
var items: Array = []                                # the item list's rows: {slot, name, level, quality, icon}
var tab := 0                                         # 0 SPELLCRAFT, 1 ALCHEMY
var picks: Array = []                                # one per free bonus slot: {bonus: OptionButton, tier: OptionButton}
var bonus_types: Array = []                          # the bonus drop-downs' types (after "NONE")
var tinctures: Array = []                            # the tincture list's rows (enchant_info tinctures that fit)
var tincture := -1                                   # the chosen tincture recipe id
var asking := false                                  # enchantinfo sent, its answer not in yet
var inner := 300.0
var _skills: Label
var _list: ItemList
var _head: RichTextLabel
var _tab_spell: Button
var _tab_alch: Button
var _spell_box: VBoxContainer
var _rows_box: VBoxContainer
var _alch_box: VBoxContainer
var _meter: Control
var _meter_fill: ColorRect
var _meter_mark: ColorRect
var _meter_text: Label
var _over: CheckBox
var _over_note: Label
var _tlist: ItemList
var _totals: RichTextLabel
var _why: Label
var _enchant: Button
var _stop: Button
var _status: RichTextLabel
var _tick := 0.0


func setup(w: Control) -> void:
	window = w
	clip_contents = true
	var width := size.x if size.x > 40.0 else 300.0
	inner = width - 14.0
	var scroll := ScrollContainer.new()
	scroll.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	var box := VBoxContainer.new()
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	box.add_theme_constant_override("separation", 4)
	scroll.add_child(box)
	box.add_child(UiArt.label("ENCHANT HELPER", 10, UiArt.GOLD_HI))
	_wrap(box, NOTE, UiArt.DIM)
	_skills = _wrap(box, "", UiArt.CREAM)
	# the backpack's items
	_list = ItemList.new()
	_list.custom_minimum_size = Vector2(inner, 74)
	_list.fixed_icon_size = Vector2i(16, 16)
	_list.add_theme_font_override("font", UiArt.font(false))
	_list.add_theme_font_size_override("font_size", 9)
	_list.add_theme_color_override("font_color", UiArt.CREAM)
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(0.02, 0.02, 0.02, 0.85)
	_list.add_theme_stylebox_override("panel", bg)
	UiArt.themed(_list)
	_list.item_selected.connect(func(i: int):
		if i < items.size():
			pick_item(int(items[i].slot)))
	box.add_child(_list)
	_head = _rich(box)
	# the two ways to enchant
	var row := _row(box)
	var tw := (inner - 4.0) / 2.0
	_tab_spell = UiArt.button("SPELLCRAFT", Vector2(tw, 16), 9)
	_tab_spell.toggle_mode = true
	_tab_spell.pressed.connect(func(): set_tab(0))
	row.add_child(_tab_spell)
	_tab_alch = UiArt.button("ALCHEMY", Vector2(tw, 16), 9)
	_tab_alch.toggle_mode = true
	_tab_alch.pressed.connect(func(): set_tab(1))
	row.add_child(_tab_alch)
	# spellcraft: a bonus and a tier per free slot, the imbue meter, overcharge
	_spell_box = VBoxContainer.new()
	_spell_box.add_theme_constant_override("separation", 3)
	box.add_child(_spell_box)
	_rows_box = VBoxContainer.new()
	_rows_box.add_theme_constant_override("separation", 3)
	_spell_box.add_child(_rows_box)
	_meter = Control.new()
	_meter.custom_minimum_size = Vector2(inner, 10)
	_meter.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var back := ColorRect.new()
	back.color = Color(0.05, 0.045, 0.04, 0.95)
	back.size = Vector2(inner, 10)
	_meter.add_child(back)
	_meter_fill = ColorRect.new()
	_meter_fill.size = Vector2(0, 10)
	_meter.add_child(_meter_fill)
	_meter_mark = ColorRect.new()                    # where the item's maximum is
	_meter_mark.color = UiArt.GOLD_HI
	_meter_mark.size = Vector2(2, 10)
	_meter.add_child(_meter_mark)
	_spell_box.add_child(_meter)
	_meter_text = _wrap(_spell_box, "", UiArt.CREAM)
	_over = CheckBox.new()
	_over.text = "ALLOW OVERCHARGE (THE GEMS CAN EXPLODE AND KILL YOU)"
	_over.add_theme_font_override("font", UiArt.font(true))
	_over.add_theme_font_size_override("font_size", 8)
	_over.add_theme_color_override("font_color", ORANGE)
	_over.button_pressed = false
	_over.toggled.connect(func(_v: bool): refresh())
	UiArt.themed(_over)
	_spell_box.add_child(_over)
	_over_note = _wrap(_spell_box, OC_WARN, ORANGE)
	# alchemy: the tinctures that fit
	_alch_box = VBoxContainer.new()
	_alch_box.add_theme_constant_override("separation", 3)
	box.add_child(_alch_box)
	_tlist = ItemList.new()
	_tlist.custom_minimum_size = Vector2(inner, 96)
	_tlist.add_theme_font_override("font", UiArt.font(false))
	_tlist.add_theme_font_size_override("font_size", 9)
	_tlist.add_theme_color_override("font_color", UiArt.CREAM)
	_tlist.add_theme_stylebox_override("panel", bg)
	UiArt.themed(_tlist)
	_tlist.item_selected.connect(func(i: int):
		tincture = int(tinctures[i].id) if i < tinctures.size() else -1
		refresh())
	_alch_box.add_child(_tlist)
	_wrap(_alch_box, CHARGE_NOTE, UiArt.DIM)
	# totals, the buttons, why it can't, the live status
	_totals = _rich(box)
	_why = _wrap(box, "", UiArt.RED)
	row = _row(box)
	var bw := (inner - 4.0) / 2.0
	_enchant = UiArt.button("ENCHANT", Vector2(bw, 18), 10)
	_enchant.pressed.connect(enchant)
	row.add_child(_enchant)
	_stop = UiArt.button("STOP", Vector2(bw, 18), 10)
	_stop.pressed.connect(stop)
	row.add_child(_stop)
	_status = _rich(box)
	PlayerData.hook(self, Server.craft_assist_changed, _on_message)
	PlayerData.hook(self, PlayerData.inventory_changed, func():
		if is_visible_in_tree():
			rebuild_items())
	visibility_changed.connect(_on_shown)
	set_tab(0)
	rebuild_items()
	_build_rows()
	refresh()


# ---------------------------------------------------------------- building helpers

func _row(box: Control) -> HBoxContainer:
	var r := HBoxContainer.new()
	r.add_theme_constant_override("separation", 4)
	box.add_child(r)
	return r


func _wrap(box: Control, t: String, color: Color) -> Label:
	var l := UiArt.label(t, 8, color, false)
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	l.custom_minimum_size = Vector2(inner, 0)
	box.add_child(l)
	return l


func _rich(box: Control) -> RichTextLabel:
	var r := RichTextLabel.new()
	r.bbcode_enabled = true
	r.fit_content = true
	r.scroll_active = false
	r.custom_minimum_size = Vector2(inner, 0)
	r.add_theme_font_override("normal_font", UiArt.font(false))
	r.add_theme_font_override("bold_font", UiArt.font(true))
	r.add_theme_font_size_override("normal_font_size", 9)
	r.add_theme_font_size_override("bold_font_size", 9)
	r.add_theme_color_override("default_color", UiArt.CREAM)
	r.mouse_filter = Control.MOUSE_FILTER_PASS
	UiArt.themed(r)
	box.add_child(r)
	return r


static func _hex(c: Color) -> String:
	return c.to_html(false)


static func skill_of(id: int) -> int:
	var s = Server.craft_skills.get(id, {})
	return int(s.get("points", 0)) if s is Dictionary else 0


## A bonus type's readable name (eProperty: the item delve's table content/items/bonus_names.json) and its unit.
static func bonus_name(t: int) -> String:
	var nm = ItemDb._names("bonus").get(str(t), null)
	return String(nm[0]) if nm is Array and not nm.is_empty() else "Bonus %d" % t


static func bonus_amount(t: int, v: int) -> String:
	return ("%d%%" % v) if t >= 11 and t <= 19 else "+%d" % v


## The drop-down's order: stats, hits, power, resists, skills, focus, the rest.
static func _bonus_group(t: int) -> int:
	if t >= 1 and t <= 8:
		return 0
	if t == 10:
		return 1
	if t == 9:
		return 2
	if t >= 11 and t <= 19:
		return 3
	if t >= 20 and t <= 115:
		return 4
	if t >= 120 and t <= 165:
		return 5
	return 6


# ---------------------------------------------------------------- the item list

## The backpack's gear (weapons, armour, shields, instruments), crafted-looking quality (94%+) first, then by level.
## Whether one can really be enchanted is the server's answer (op 43).
static func candidates() -> Array:
	var out := []
	for s in range(40, 80):
		if not Server.inv.has(s):
			continue
		var it := ItemDb.item("srv:%d" % s)
		if it.is_empty():
			continue
		var ot := int(it.get("object_type", 0))
		if not ((ot >= 1 and ot <= 38) or ot == 42 or ot == 45):
			continue
		out.append({"slot": s, "name": String(it.get("name", "item")), "level": int(it.get("level", 0)),
				"quality": int(it.get("quality", 0)), "icon": int(it.get("icon", -1))})
	out.sort_custom(func(a, b):
		var ca: bool = int(a.quality) >= 94
		var cb: bool = int(b.quality) >= 94
		if ca != cb:
			return ca
		if int(a.level) != int(b.level):
			return int(a.level) > int(b.level)
		return String(a.name) < String(b.name))
	return out


func rebuild_items() -> void:
	if _list == null:
		return
	items = candidates()
	_list.clear()
	var keep := -1
	for i in items.size():
		var r: Dictionary = items[i]
		var tex: Texture2D = ItemDb.icon(int(r.icon)) if int(r.icon) >= 0 else null
		_list.add_item("%s   (LEVEL %d, %d%%)" % [String(r.name).to_upper(), int(r.level), int(r.quality)], tex)
		if int(r.slot) == slot:
			keep = i
	if keep >= 0:
		_list.select(keep)
	elif slot >= 0 and not Net.enchanting():
		slot = -1                                    # (the item left the backpack)
		asking = false
		_build_rows()
	refresh()


## An item picked: ask the server what it can take.
func pick_item(s: int) -> void:
	if Net.enchanting():
		return
	slot = s
	asking = true
	tincture = -1
	Net.enchant_info_for(s)
	_build_rows()
	refresh()


## The server's answer is for the picked item.
func info() -> Dictionary:
	if slot < 0 or Net.enchant_info.is_empty() or int(Net.enchant_info.get("slot", -1)) != slot:
		return {}
	return Net.enchant_info


func set_tab(t: int) -> void:
	tab = t
	_tab_spell.set_pressed_no_signal(t == 0)
	_tab_alch.set_pressed_no_signal(t == 1)
	refresh()


# ---------------------------------------------------------------- spellcraft rows

## One bonus + tier row per free bonus slot of the item, from the server's gem rows.
func _build_rows() -> void:
	if _rows_box == null:
		return
	for c in _rows_box.get_children():
		_rows_box.remove_child(c)
		c.queue_free()
	picks.clear()
	bonus_types.clear()
	var inf := info()
	if inf.is_empty() or int(inf.reason) != 0 or not bool(inf.can_spell):
		return                                       # (no gems for it: the header says why)
	var seen := {}
	for g in inf.gems:
		seen[int(g.bonus)] = true
	bonus_types = seen.keys()
	bonus_types.sort_custom(func(a, b):
		var ga := _bonus_group(int(a))
		var gb := _bonus_group(int(b))
		if ga != gb:
			return ga < gb
		return bonus_name(int(a)) < bonus_name(int(b)))
	var names := ["- NO BONUS -"]
	for t in bonus_types:
		names.append(bonus_name(int(t)))
	for n in mini(int(inf.free_slots), MAX_SLOTS):
		var row := _row(_rows_box)
		var cap := UiArt.label("SLOT %d" % (n + 1), 8, UiArt.GOLD)
		cap.custom_minimum_size = Vector2(40, 18)
		cap.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		row.add_child(cap)
		var pick := {}
		var bw := (inner - 48.0) * 0.45
		var tw := inner - 48.0 - bw - 8.0
		var idx := picks.size()
		pick.bonus = CompanionUi.option(row, names, Vector2.ZERO, Vector2(bw, 18), func(_i: int): _pick_bonus(idx))
		pick.tier = CompanionUi.option(row, ["-"], Vector2.ZERO, Vector2(tw, 18), func(_i: int): refresh())
		pick.tier.disabled = true
		pick.gems = []
		picks.append(pick)


## The gems of a bonus type, weakest first.
func gems_of(t: int) -> Array:
	var out := []
	for g in info().get("gems", []):
		if int(g.bonus) == t:
			out.append(g)
	out.sort_custom(func(a, b): return int(a.value) < int(b.value))
	return out


func _pick_bonus(i: int) -> void:
	var p: Dictionary = picks[i]
	var b: OptionButton = p.bonus
	var tier: OptionButton = p.tier
	tier.clear()
	p.gems = []
	if b.selected <= 0:
		tier.add_item("-")
		tier.disabled = true
		refresh()
		return
	var t := int(bonus_types[b.selected - 1])
	p.gems = gems_of(t)
	var best := 0
	for k in (p.gems as Array).size():
		var g: Dictionary = p.gems[k]
		var txt := "%s  %.1f IMBUE  SC %d" % [bonus_amount(t, int(g.value)), int(g.imbue10) / 10.0, int(g.req)]
		if int(g.owned) > 0:
			txt += "  (HAVE %d)" % int(g.owned)
		elif not bool(g.can):
			txt += "  (CAN'T MAKE)"
		tier.add_item(txt.to_upper())
		if String(g.name) != "":
			tier.set_item_tooltip(k, String(g.name))
		if bool(g.can) or int(g.owned) > 0:
			best = k                                 # (default: the strongest you can make or have)
	tier.disabled = false
	tier.select(best)
	refresh()


## Choose a bonus / tier from code (the probe): tier -1 = the default one.
func choose(i: int, bonus_type: int, tier := -1) -> void:
	if i >= picks.size():
		return
	var k := bonus_types.find(bonus_type)
	var b: OptionButton = picks[i].bonus
	b.select(k + 1)
	_pick_bonus(i)
	if tier >= 0:
		(picks[i].tier as OptionButton).select(tier)
		refresh()


## The gems chosen now (one per row with a bonus).
func chosen_gems() -> Array:
	var out := []
	for p in picks:
		var b: OptionButton = p.bonus
		var tier: OptionButton = p.tier
		if b.selected > 0 and tier.selected >= 0 and tier.selected < (p.gems as Array).size():
			out.append(p.gems[tier.selected])
	return out


## SpellCrafting.GetTotalImbuePoints: (the item's own bonuses' raw points + the gems' + the biggest of all) / 2,
## rounded down - op 43 kind 4 gives the item's raw points and its biggest. (Without it: the used number counted as one
## gem of that size.)
func imbue_total() -> int:
	var inf := info()
	var gems := chosen_gems()
	var total := 0
	var big := 0
	if bool(inf.get("has_imbue", false)):
		total = int(inf.raw_existing) * 10
		big = int(inf.biggest_existing) * 10
	else:
		total = int(inf.get("imbue_used", 0)) * 10
		big = total
		if gems.is_empty():
			return total / 10
	for g in gems:
		total += int(g.imbue10)
		big = maxi(big, int(g.imbue10))
	return int(floor((total + big) / 20.0))


## SpellCrafting.CalculateChanceToOverchargeItem (success %).
static func overcharge_chance(quality: int, over: int, skill: int) -> int:
	if over > 5:
		return 0
	if over <= 0 or quality < 94 or quality > 100:
		return 100
	var sb := mini(skill / 10, 100)
	var s := 34 + int(QUAL_OC[quality - 94]) - int(OC_START[over]) + sb
	s += int(100.0 * ((sb / 100.0 - 1.0) * (int(OC_START[over]) / 200.0)))
	return mini(s, 100)


## The success % of an overcharge this many points over the item's maximum: the server's own numbers (op 43 kind 4),
## else its formula.
func over_success(over: int) -> int:
	if over <= 0:
		return 100
	var inf := info()
	var table: Array = inf.get("over_success", [])
	if table.size() == 7:
		return int(table[over]) if over < 7 else 0
	return overcharge_chance(int(inf.get("quality", 0)), over, int(inf.get("spell_skill", 0)))


## SpellCrafting.CalculateChanceToPreserveItem for the chosen gems: the skill modifier (op 43 kind 4) + each gem's
## (quality - 92) x 1.8, times the item's quality / 100, 0 - 100.
func preserve_chance() -> int:
	var inf := info()
	var gm := 0.0
	for g in chosen_gems():
		var q := int(g.get("quality", 0))
		gm += ((q if q > 0 else int(inf.get("gem_quality", 94))) - 92) * 1.8
	gm = gm * int(inf.get("quality", 0)) / 100.0
	return clampi(int(float(int(inf.get("preserve_mod", 0))) + gm), 0, 100)


## The chance the gems explode (you die, the gems are lost, the item is kept): the overcharge fails, then the
## preserve roll fails.
func explode_chance() -> int:
	var over := imbue_total() - int(info().get("imbue_max", 0))
	if over <= 0:
		return 0
	return (100 - over_success(over)) * (100 - preserve_chance()) / 100


## "within" (green), "over" (orange: overcharge), "cap" (red: over your spellcraft cap, more than 6 over the item's
## maximum, or no chance at all - the server refuses / it can't work).
func meter_state() -> String:
	var inf := info()
	var total := imbue_total()
	var over := total - int(inf.get("imbue_max", 0))
	if total > int(inf.get("imbue_cap", 0)) or over > 6 or (over > 0 and over_success(over) <= 0):
		return "cap"
	if total > int(inf.get("imbue_max", 0)):
		return "over"
	return "within"


func meter_color() -> Color:
	match meter_state():
		"cap":
			return UiArt.RED
		"over":
			return ORANGE
	return UiArt.GREEN


# ---------------------------------------------------------------- the buttons

func enchant() -> void:
	if why_not() != "":
		return
	if tab == 0:
		var ids := []
		for g in chosen_gems():
			ids.append(int(g.id))
		Net.enchant(slot, "spell", ids, _over.button_pressed)
	else:
		Net.enchant(slot, "alch", [tincture])


func stop() -> void:
	Net.enchant_stop()


func _on_shown() -> void:
	if is_visible_in_tree():
		rebuild_items()


func _on_message(kind: String) -> void:
	match kind:
		"enchant_info":
			if int(Net.enchant_info.get("slot", -1)) == slot:
				asking = false
				tincture = -1
				_build_rows()
		"enchant_status":
			var st := int(Net.enchant_status.get("state", 0))
			if (st == 4 or st == 5) and slot >= 0 and Server.inv.has(slot):
				asking = true
				Net.enchant_info_for(slot)               # (the item changed: its options again)
		_:
			return
	refresh()


func _process(delta: float) -> void:
	_tick += delta
	if _tick < 1.0 or not is_visible_in_tree():
		return
	_tick = 0.0
	if Net.enchanting():
		refresh()                                    # (the time left ticks)


# ---------------------------------------------------------------- what it shows

## Why ENCHANT can't be pressed now ("" when it can) - the plain reason shown under the totals.
func why_not() -> String:
	if Net.enchanting():
		return "Enchanting - STOP to end it."
	if skill_of(SPELLCRAFTING) <= 0 and skill_of(ALCHEMY) <= 0 and info().is_empty():
		return NO_SKILL
	if slot < 0:
		return "Pick an item from your backpack."
	if asking:
		return "Asking the server..."
	var inf := info()
	if inf.is_empty():
		return "No answer from the server for this item yet."
	if int(inf.reason) != 0:
		return String(Net.ENCHANT_WHY.get(int(inf.reason), "This item can't be enchanted."))
	if tab == 0:
		if int(inf.spell_skill) <= 0:
			return "You don't have Spellcrafting."
		if not bool(inf.can_spell):
			return "This item can't take spellcraft gems (no free bonus slots)."
		var gems := chosen_gems()
		if gems.is_empty():
			return "Choose at least one bonus."
		var types := {}
		for g in gems:
			if types.has(int(g.bonus)):
				return "Each bonus can be used only once on an item."
			types[int(g.bonus)] = true
			if not bool(g.can) and int(g.owned) <= 0:
				return "You can't make %s yet: Spellcrafting %d needed." % [_gem_text(g), int(g.req)]
		var total := imbue_total()
		if total > int(inf.imbue_cap):
			return "%d imbue points is over your spellcraft cap of %d (Spellcrafting / 20)." % [total, int(inf.imbue_cap)]
		if total - int(inf.imbue_max) > 6 or (total > int(inf.imbue_max) and over_success(total - int(inf.imbue_max)) <= 0):
			return "Too far over the item's %d imbue points: it can't be overcharged that much." % int(inf.imbue_max)
		if total > int(inf.imbue_max) and not _over.button_pressed:
			return "Over the item's %d imbue points: tick ALLOW OVERCHARGE to try it anyway." % int(inf.imbue_max)
		return ""
	if int(inf.alch_skill) <= 0:
		return "You don't have Alchemy."
	if not bool(inf.can_alch):
		return "This item can't take a tincture (it has a proc or charges already, or it isn't a weapon or armour)."
	var t := _tincture_row(tincture)
	if t.is_empty():
		return "Choose a tincture."
	if not bool(t.can) and int(t.owned) <= 0:
		return "You can't make it yet: Alchemy %d needed." % int(t.req)
	return ""


func _gem_text(g: Dictionary) -> String:
	return String(g.name) if String(g.name) != "" else "%s %s" % [bonus_amount(int(g.bonus), int(g.value)),
			bonus_name(int(g.bonus))]


func _tincture_row(id: int) -> Dictionary:
	for t in info().get("tinctures", []):
		if int(t.id) == id:
			return t
	return {}


func refresh() -> void:
	if _list == null:
		return
	var sc := skill_of(SPELLCRAFTING)
	var al := skill_of(ALCHEMY)
	_skills.text = "YOUR SKILLS: SPELLCRAFTING %d   ALCHEMY %d" % [sc, al]
	_skills.add_theme_color_override("font_color", UiArt.RED if sc <= 0 and al <= 0 else UiArt.CREAM)
	var busy := Net.enchanting()
	_list.mouse_filter = Control.MOUSE_FILTER_IGNORE if busy else Control.MOUSE_FILTER_STOP
	_show_head()
	_spell_box.visible = tab == 0
	_alch_box.visible = tab == 1
	for p in picks:
		(p.bonus as OptionButton).disabled = busy
		(p.tier as OptionButton).disabled = busy or (p.bonus as OptionButton).selected <= 0
	_over.disabled = busy
	if tab == 0:
		_show_meter()
	else:
		_show_tinctures()
	_show_totals()
	var why := why_not()
	_why.text = why
	_why.visible = why != "" and not busy
	_enchant.disabled = why != ""
	_stop.disabled = not busy
	_show_status()


func _show_head() -> void:
	var inf := info()
	if slot < 0:
		_head.text = "[color=#%s]%s[/color]" % [_hex(UiArt.DIM), "Pick an item above." if not items.is_empty()
				else "No weapons or armour in your backpack."]
		return
	if inf.is_empty():
		_head.text = "[color=#%s]ASKING THE SERVER...[/color]" % _hex(UiArt.DIM)
		return
	var lines := ["[b]%s[/b]   level %d, quality %d%%" % [String(inf.name).to_upper(), int(inf.level), int(inf.quality)],
		"Imbue points used %d / %d   -   your cap %d   -   free bonus slots %d%s" % [int(inf.imbue_used),
				int(inf.imbue_max), int(inf.imbue_cap), int(inf.free_slots),
				"   -   has a proc / charges" if bool(inf.has_proc) else ""]]
	if int(inf.reason) != 0:
		lines.append("[color=#%s]%s[/color]" % [_hex(UiArt.RED), String(Net.ENCHANT_WHY.get(int(inf.reason), ""))])
	_head.text = "\n".join(lines)


func _show_meter() -> void:
	var inf := info()
	var on := not inf.is_empty() and bool(inf.get("can_spell", false))
	_meter.visible = on
	_over.visible = on
	if not on:
		_meter_text.text = ""
		_over_note.visible = false
		return
	var total := imbue_total()
	var mx := int(inf.imbue_max)
	var scale := float(maxi(maxi(mx + 6, int(inf.imbue_cap)), maxi(total, 1)))
	var col := meter_color()
	_meter_fill.color = col
	_meter_fill.size = Vector2(inner * clampf(total / scale, 0.0, 1.0), 10)
	_meter_mark.position = Vector2(clampf(inner * mx / scale, 0.0, inner - 2.0), 0)
	var t := "IMBUE %d / %d" % [total, mx]
	match meter_state():
		"over":
			t += "  -  OVERCHARGE BY %d: %d%% SUCCESS, %d%% THE GEMS EXPLODE" % [total - mx, over_success(total - mx),
					explode_chance()]
		"cap":
			t += "  -  TOO HIGH (CAP %d, ITEM MAX %d)" % [int(inf.imbue_cap), mx]
	_meter_text.text = t
	_meter_text.add_theme_color_override("font_color", col)
	_over_note.visible = meter_state() != "within" or _over.button_pressed


func _show_tinctures() -> void:
	var inf := info()
	_tlist.clear()
	tinctures = []
	if inf.is_empty():
		return
	var keep := -1
	for t in inf.tinctures:
		var nm := String(t.name) if String(t.name) != "" else "tincture %d" % int(t.id)
		var txt := "%s: %s" % [String(KIND_TEXT.get(int(t.kind), "TINCTURE")), nm.to_upper()]
		if String(t.desc) != "":
			txt += " - " + String(t.desc)
		if int(t.kind) == 3:
			var ch := int(t.get("charges", 0)) if int(t.get("charges", 0)) > 0 else int(inf.get("charges", 0))
			if ch > 0:
				txt += "  %d CHARGES" % ch
		elif int(t.get("proc_chance", 0)) > 0:
			txt += "  %d%% CHANCE" % int(t.proc_chance)
		if int(t.level_req) > 0:
			txt += "  (USE LEVEL %d)" % int(t.level_req)
		txt += "  ALCHEMY %d" % int(t.req)
		if int(t.owned) > 0:
			txt += "  HAVE %d" % int(t.owned)
		elif not bool(t.can):
			txt += "  CAN'T MAKE"
		_tlist.add_item(txt)
		var i := _tlist.item_count - 1
		if not bool(t.can) and int(t.owned) <= 0:
			_tlist.set_item_custom_fg_color(i, UiArt.DIM)
		tinctures.append(t)
		if int(t.id) == tincture:
			keep = i
	if keep >= 0:
		_tlist.select(keep)


func _show_totals() -> void:
	var inf := info()
	if inf.is_empty() or int(inf.reason) != 0:
		_totals.text = ""
		return
	var make := []                                   # what has to be crafted (not in the backpack already)
	var have := 0
	var count := 0
	if tab == 0:
		for g in chosen_gems():
			count += 1
			if int(g.owned) > 0:
				have += 1
			else:
				make.append(g)
	else:
		var t := _tincture_row(tincture)
		if not t.is_empty():
			count = 1
			if int(t.owned) > 0:
				have = 1
			else:
				make.append(t)
	if count == 0:
		_totals.text = ""
		return
	var cost := 0
	var secs := 0
	for m in make:
		cost += int(m.cost)
		secs += int(m.secs)
	var word := "gem" if tab == 0 else "tincture"
	var lines := ["[b]MATERIALS: %s[/b]   (at merchant prices - what you already have is not bought again)" %
			Net.money_text(cost).to_upper(),
		"[b]CRAFT TIME: %s[/b]   (%d %s%s to make at the normal time, then the combine)" % [Net.time_text(secs).to_upper(),
				make.size(), word, "s" if make.size() != 1 else ""]]
	if have > 0:
		lines.append("%d of %d %s%s already in your backpack - used first." % [have, count, word, "s" if count != 1 else ""])
	_totals.text = "\n".join(lines)


func _show_status() -> void:
	var s := Net.enchant_status
	if s.is_empty() or int(s.get("state", 0)) == 0:
		_status.text = ""
		return
	var state := int(s.state)
	var reason := Net.enchant_reason_text(int(s.reason))
	var col := UiArt.GREEN if state == 4 else (UiArt.GOLD if state >= 1 and state <= 3 or int(s.reason) == 7 else UiArt.RED)
	var lines := ["[b][color=#%s]%s%s[/color][/b]" % [_hex(col), Net.enchant_state_text(state).to_upper(),
			(": " + reason.to_upper()) if reason != "" and state >= 4 else ""]]
	if String(s.name) != "":
		lines.append("Item: %s" % String(s.name))
	if state >= 1 and state <= 3 and int(s.steps) > 0:
		var what := String(info().get("names", {}).get(int(s.recipe), ""))
		lines.append("Step %d of %d%s" % [int(s.step), int(s.steps), (": " + what) if what != "" else ""])
	lines.append("Gold spent: %s" % Net.money_text(int(s.spent)))
	if state >= 1 and state <= 3:
		var since := (Time.get_ticks_msec() - int(s.get("at", 0))) / 1000
		lines.append("Time left: about %s" % Net.time_text(int(s.secs_left) - since))
	if bool(s.get("exploded", false)):
		lines.append("[b][color=#%s]THE GEMS EXPLODED: THEY ARE LOST AND YOU DIED - THE ITEM IS KEPT.[/color][/b]" %
				_hex(UiArt.RED))
	if bool(s.destroyed):
		lines.append("[b][color=#%s]THE ITEM WAS DESTROYED.[/color][/b]" % _hex(UiArt.RED))
	_status.text = "\n".join(lines)
