extends Node
## Offline check of the enchant helper (owner 2026-11-08: "add an enchant button ... it can auto buy and enchant it if
## you have the skill level. dont give anything out for free"): hand-built 'H' ops 43 / 44 through CompanionNet.handle,
## a fake server recording the panel's /craftassist commands.
##   A  the item list (gear only, crafted quality first)   B  picking an item asks enchantinfo
##   C  op 43 over 5 packets: nothing until the end, then the fields, rows, names, slot rows, bonus order
##   D  the imbue meter: within (green) / overcharge (orange, needs the tick) / over the cap (red), can't-make gem
##   E  alchemy: tinctures, the alch command          F  op 44: live status, STOP, destroyed, info asked again
##   G  no skill: disabled with the plain reason      H  ops 43 / 44 don't redraw the companion windows
##   I  the craft window's ENCHANT page (and ASSISTANT still toggles)
##   --skip-login --zone=N --world-mode=1.65 --probe=craft_enchant_probe

const Net := preload("res://game/net/craft_assist_net.gd")
const EnchantPanel := preload("res://game/ui/craft_enchant_panel.gd")


class FakeClient extends Node:
	var sent: Array = []

	func Command(t: String) -> void:
		sent.append(t)

	func Craft(_id: int) -> void:
		pass


class FakeWindow extends Control:
	var trade := 13


var ok := true
var _kinds: Array = []


func _check(name: String, good: bool, detail := "") -> void:
	ok = ok and good
	print("[enchant] %s: %s  %s" % [name, detail, "OK" if good else "WRONG"])


static func _body(op: int) -> PackedByteArray:
	var b := PackedByteArray()
	b.resize(128)
	b[1] = 0x48
	b[2] = 1
	b[3] = op
	return b


static func _put_str(b: PackedByteArray, at: int, s: String) -> int:
	var bytes := s.to_utf8_buffer()
	for i in bytes.size():
		b[at + i] = bytes[i]
	b[at + bytes.size()] = 0
	return at + bytes.size() + 1


static func _header(slot: int, reason: int, can_spell: int, can_alch: int, sc: int, al: int) -> PackedByteArray:
	var b := _body(43)
	b[4] = 0
	b[5] = slot
	b[6] = 50
	b[7] = 99
	b[8] = 4
	b[9] = 0
	b[10] = can_spell
	b[11] = can_alch
	b.encode_u16(12, 28)                                # round(0.55 * 50)
	b.encode_u16(14, 0)
	b.encode_u16(16, sc)
	b.encode_u16(18, al)
	b.encode_u16(20, sc / 20)
	b[22] = reason
	_put_str(b, 23, "Bright Sword")
	return b


## op 43 kind 4: the item's own bonuses' raw points / biggest, success % 0..6 over, preserve modifier (signed),
## bonuses on it, gem quality, charges
static func _imbue(raw: int, biggest: int, over: Array, preserve: int, charges: int) -> PackedByteArray:
	var b := _body(43)
	b[4] = 4
	b.encode_u16(6, raw)
	b.encode_u16(8, biggest)
	for k in 7:
		b[10 + k] = over[k]
	b.encode_s8(17, preserve)
	b[18] = 0
	b[19] = 94
	b[20] = charges
	return b


# id, bonus type, value, imbue x10, cost, secs, required SC, owned, can, gem quality
static func _gems(rows: Array) -> PackedByteArray:
	var b := _body(43)
	b[4] = 1
	b[5] = rows.size()
	for k in rows.size():
		var i := 6 + k * 20
		var r: Array = rows[k]
		b.encode_u16(i, r[0])
		b.encode_u16(i + 2, r[1])
		b.encode_u16(i + 4, r[2])
		b.encode_u16(i + 6, r[3])
		b.encode_u32(i + 8, r[4])
		b.encode_u16(i + 12, r[5])
		b.encode_u16(i + 14, r[6])
		b[i + 16] = r[7]
		b[i + 17] = r[8]
		b[i + 18] = r[9] if r.size() > 9 else 94
	return b


# id, kind, spell, level req, owned, cost, secs, required alchemy, can, charges, proc chance
static func _tinctures(rows: Array) -> PackedByteArray:
	var b := _body(43)
	b[4] = 2
	b[5] = rows.size()
	for k in rows.size():
		var i := 6 + k * 20
		var r: Array = rows[k]
		b.encode_u16(i, r[0])
		b[i + 2] = r[1]
		b.encode_u32(i + 4, r[2])
		b[i + 8] = r[3]
		b[i + 9] = r[4]
		b.encode_u32(i + 10, r[5])
		b.encode_u16(i + 14, r[6])
		b.encode_u16(i + 16, r[7])
		b[i + 18] = r[8]
		b[i + 3] = r[9]
		b[i + 19] = r[10]
	return b


static func _names(rows: Array) -> PackedByteArray:
	var b := _body(43)
	b[4] = 3
	b[5] = rows.size()
	var at := 6
	for r in rows:
		b.encode_u16(at, r[0])
		at = _put_str(b, at + 2, r[1])
	return b


static func _end() -> PackedByteArray:
	var b := _body(43)
	b[4] = 0xFF
	return b


static func _status(state: int, reason: int, destroyed: int, step: int, steps: int, spent: int, recipe: int,
		secs: int, exploded := 0) -> PackedByteArray:
	var b := _body(44)
	b[4] = state
	b[5] = reason
	b[6] = destroyed
	b[7] = step
	b[8] = steps
	b[9] = exploded
	b.encode_u32(10, spent)
	b.encode_u16(14, recipe)
	b.encode_u32(16, secs)
	_put_str(b, 20, "Bright Sword")
	return b


func _item(slot: int, name: String, ot: int, level: int, quality: int) -> void:
	ItemDb.add_runtime("srv:%d" % slot, {"id": "srv:%d" % slot, "name": name, "object_type": ot, "level": level,
			"quality": quality, "icon": 0})
	Server.inv[slot] = {"id": "srv:%d" % slot, "count": 1}


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(3.0).timeout
	var old_client = Server.client
	var old_skills: Dictionary = Server.craft_skills.duplicate(true)
	var old_inv: Dictionary = Server.inv.duplicate(true)
	var fc := FakeClient.new()
	add_child(fc)
	Server.client = fc
	Server.craft_skills = {13: {"points": 800, "name": "Spellcrafting"}, 4: {"points": 600, "name": "Alchemy"}}
	Server.inv.clear()
	_item(40, "Bright Sword", 11, 50, 99)
	_item(41, "bear tooth", 0, 0, 85)
	_item(42, "Fine Studded Boots", 34, 40, 94)
	_item(43, "Dropped Axe", 13, 51, 85)
	Server.craft_assist_changed.connect(func(k: String): _kinds.append(k))
	var net := CompanionNet.new()
	add_child(net)
	var redraws := [0]
	net.changed.connect(func(): redraws[0] += 1)
	Net.enchant_info = {}
	Net.enchant_status = {}

	var w := FakeWindow.new()
	w.size = Vector2(480, 560)
	v.ui.root.add_child(w)
	var p := EnchantPanel.new()
	p.position = Vector2(10, 96)
	p.size = Vector2(460, 426)
	w.add_child(p)
	p.setup(w)
	await get_tree().process_frame

	# A
	var order := []
	for r in p.items:
		order.append(int(r.slot))
	_check("A item list", order == [40, 42, 43] and p._list.item_count == 3 and p._enchant.disabled
			and p.why_not() == "Pick an item from your backpack.", "%s, why '%s'" % [order, p.why_not()])

	# B
	p.pick_item(40)
	_check("B pick asks", fc.sent == ["&craftassist enchantinfo 40"] and p.asking and p.why_not() == "Asking the server...",
			str(fc.sent))
	fc.sent.clear()

	# C
	net.handle(_header(40, 0, 1, 1, 800, 600))
	net.handle(_imbue(0, 0, [100, 90, 75, 55, 30, 10, 0], -5, 6))
	net.handle(_gems([[501, 1, 4, 30, 1000, 20, 100, 0, 1], [502, 1, 7, 50, 2000, 25, 300, 0, 1],
			[503, 1, 10, 70, 3000, 30, 500, 0, 1], [504, 1, 13, 90, 4000, 35, 900, 0, 0],
			[511, 10, 40, 100, 5000, 30, 600, 1, 1, 99], [521, 11, 9, 160, 6000, 40, 700, 0, 1]]))
	net.handle(_gems([[522, 11, 20, 380, 9000, 50, 800, 0, 1], [531, 33, 3, 100, 7000, 45, 600, 0, 1]]))
	net.handle(_tinctures([[601, 1, 12345, 40, 0, 8000, 60, 500, 1, 0, 15], [602, 3, 23456, 45, 2, 9000, 70, 900, 0, 6, 0]]))
	net.handle(_names([[503, "Fiery Essence Jewel"], [601, "Volcanic Tincture|Fire damage 45 on hit"],
			[602, "Healing Charge Tincture|Heal 120"]]))
	var before_end: bool = _kinds.is_empty() and Net.enchant_info.is_empty()
	net.handle(_end())
	await get_tree().process_frame
	var e := Net.enchant_info
	var g503: Dictionary = {}
	for g in e.get("gems", []):
		if int(g.id) == 503:
			g503 = g
	var t601: Dictionary = e.tinctures[0] if e.get("tinctures", []).size() > 0 else {}
	var t602: Dictionary = e.tinctures[1] if e.get("tinctures", []).size() > 1 else {}
	var good_c: bool = before_end and _kinds == ["enchant_info"] and e.slot == 40 and e.level == 50 and e.quality == 99 \
			and e.free_slots == 4 and e.has_proc == false and e.can_spell and e.can_alch and e.imbue_max == 28 \
			and e.imbue_used == 0 and e.spell_skill == 800 and e.alch_skill == 600 and e.imbue_cap == 40 and e.reason == 0 \
			and e.name == "Bright Sword" and e.gems.size() == 8 and g503.get("name") == "Fiery Essence Jewel" \
			and g503.get("imbue10") == 70 and g503.get("cost") == 3000 and g503.get("secs") == 30 and g503.get("req") == 500 \
			and g503.get("can") == true and g503.get("owned") == 0 \
			and t601.get("kind") == 1 and t601.get("spell") == 12345 and t601.get("level_req") == 40 \
			and t601.get("cost") == 8000 and t601.get("req") == 500 and t601.get("name") == "Volcanic Tincture" \
			and t601.get("desc") == "Fire damage 45 on hit" and t602.get("owned") == 2 and t602.get("can") == false \
			and not p.asking and p.picks.size() == 4 and p.bonus_types == [1, 10, 11, 33] \
			and e.has_imbue and e.raw_existing == 0 and e.over_success == [100, 90, 75, 55, 30, 10, 0] \
			and e.preserve_mod == -5 and e.gem_quality == 94 and e.charges == 6 and t601.get("proc_chance") == 15 \
			and t602.get("charges") == 6 and int(e.gems[4].quality) == 99
	_check("C op 43", good_c, "signals %s, gem 503 %s, tincture 601 %s, rows %d, bonus order %s" % [_kinds, g503, t601,
			p.picks.size(), p.bonus_types])
	print("[enchant]    header: %s" % p._head.get_parsed_text().replace("\n", " / "))

	# D: within / over / cap
	p.choose(0, 1, 2)                                    # Strength +10 (7.0)
	p.choose(1, 10)                                      # Hits +40 (10.0, owned) - the default tier
	p.choose(2, 11, 0)                                   # Body 9% (16.0)
	var within: bool = p.imbue_total() == 24 and p.meter_state() == "within" and p.meter_color() == UiArt.GREEN \
			and p.why_not() == "" and not p._enchant.disabled and p._meter_fill.color == UiArt.GREEN
	var totals_txt: String = p._totals.get_parsed_text()
	p.enchant()
	var sent_within: Array = fc.sent.duplicate()
	fc.sent.clear()
	_check("D within (green)", within and sent_within == ["&craftassist enchant 40 spell 503,511,521 0"]
			and totals_txt.contains("1 of 3") and totals_txt.contains("CRAFT TIME: 1M"),
			"total %d, '%s', sent %s, totals '%s'" % [p.imbue_total(), p._meter_text.text, sent_within,
			totals_txt.replace("\n", " / ")])
	p.choose(3, 33)                                      # Flexible +3 (10.0): 29 > 28
	var over: bool = p.imbue_total() == 29 and p.meter_state() == "over" and p.meter_color() == EnchantPanel.ORANGE \
			and p.why_not().contains("ALLOW OVERCHARGE") and p._enchant.disabled \
			and p._meter_text.text.contains("90% SUCCESS, 8% THE GEMS EXPLODE") and p.preserve_chance() == 18 \
			and p._over_note.text.contains("YOU DIE - THE ITEM IS KEPT") and not p._over_note.text.contains("DESTROY")
	p.enchant()
	var none_sent: bool = fc.sent.is_empty()
	p._over.button_pressed = true
	var ticked: bool = p.why_not() == "" and not p._enchant.disabled
	p.enchant()
	_check("D over (orange)", over and none_sent and ticked
			and fc.sent == ["&craftassist enchant 40 spell 503,511,521,531 1"],
			"total %d, '%s', sent %s" % [p.imbue_total(), p._meter_text.text, fc.sent])
	fc.sent.clear()
	Net.enchant_info.raw_existing = 10                   # the item's own bonuses: raw 10, biggest 20
	Net.enchant_info.biggest_existing = 20
	var with_own: int = p.imbue_total()                  # (10 + 7 + 10 + 16 + 10 + 20) / 2 = 36
	Net.enchant_info.raw_existing = 0
	Net.enchant_info.biggest_existing = 0
	_check("D the item's own bonuses count", with_own == 36, str(with_own))
	p.choose(2, 11, 1)                                   # Body 20% (38.0): 51 > cap 40
	var cap: bool = p.imbue_total() == 51 and p.meter_state() == "cap" and p.meter_color() == UiArt.RED \
			and p.why_not().contains("cap") and p._enchant.disabled
	_check("D over the cap (red)", cap, "total %d, '%s', why '%s'" % [p.imbue_total(), p._meter_text.text, p.why_not()])
	p.choose(2, 11, 0)
	p.choose(0, 1, 3)                                    # Strength +13: can't make
	var cant: bool = p.why_not().contains("Spellcrafting 900") and p._enchant.disabled
	p.choose(0, 1, 2)
	p.choose(1, 1, 0)                                    # the same bonus twice
	var dup: bool = p.why_not().contains("only once")
	_check("D can't make / same bonus twice", cant and dup, p.why_not())
	_check("D overcharge chance", EnchantPanel.overcharge_chance(99, 1, 800) == 100
			and EnchantPanel.overcharge_chance(94, 3, 400) == 35 and EnchantPanel.overcharge_chance(99, 6, 1000) == 0,
			"%d %d %d" % [EnchantPanel.overcharge_chance(99, 1, 800), EnchantPanel.overcharge_chance(94, 3, 400),
			EnchantPanel.overcharge_chance(99, 6, 1000)])

	# E: alchemy
	p.set_tab(1)
	var listed: bool = p._tlist.item_count == 2 and p._tlist.get_item_text(0).contains("Fire damage 45 on hit") \
			and p._tlist.get_item_text(1).contains("HAVE 2") \
			and p._tlist.get_item_text(0).contains("15% CHANCE") and p._tlist.get_item_text(1).contains("6 CHARGES") and p.why_not() == "Choose a tincture." and p._alch_box.visible \
			and not p._spell_box.visible
	p.tincture = 601
	p.refresh()
	var alch_ok: bool = p.why_not() == "" and p._totals.get_parsed_text().contains("CRAFT TIME")
	p.enchant()
	_check("E alchemy", listed and alch_ok and fc.sent == ["&craftassist enchant 40 alch 601"],
			"rows '%s' | '%s', sent %s" % [p._tlist.get_item_text(0), p._tlist.get_item_text(1), fc.sent])
	fc.sent.clear()

	# F: op 44
	_kinds.clear()
	net.handle(_status(2, 0, 0, 2, 4, 12000, 503, 95))
	var st := Net.enchant_status
	var live: bool = _kinds == ["enchant_status"] and st.state == 2 and st.reason == 0 and st.destroyed == false \
			and st.step == 2 and st.steps == 4 and st.spent == 12000 and st.recipe == 503 and st.secs_left == 95 \
			and st.name == "Bright Sword" and Net.enchanting() and not p._stop.disabled and p._enchant.disabled \
			and p._status.get_parsed_text().contains("Step 2 of 4: Fiery Essence Jewel")
	print("[enchant]    status: %s" % p._status.get_parsed_text().replace("\n", " / "))
	p.stop()
	var stop_sent: Array = fc.sent.duplicate()
	fc.sent.clear()
	net.handle(_status(5, 8, 0, 4, 4, 30000, 0, 0, 1))
	var stxt: String = p._status.get_parsed_text()
	var dead: bool = stxt.contains("THE GEMS EXPLODED") and stxt.contains("THE ITEM IS KEPT") \
			and not stxt.contains("DESTROYED") and Net.enchant_status.exploded == true \
			and Net.enchant_status.destroyed == false and not Net.enchanting() and p._stop.disabled \
			and fc.sent == ["&craftassist enchantinfo 40"]
	print("[enchant]    status: %s" % p._status.get_parsed_text().replace("\n", " / "))
	_check("F op 44", live and stop_sent == ["&craftassist enchantstop"] and dead, "stop %s, after %s" % [stop_sent, fc.sent])
	fc.sent.clear()

	# G: no skill
	Server.craft_skills = {}
	net.handle(_header(40, 4, 0, 0, 0, 0))
	net.handle(_end())
	var no_skill: bool = p.why_not() == String(Net.ENCHANT_WHY[4]) and p._enchant.disabled \
			and p._head.get_parsed_text().contains("skill") and p.picks.is_empty()
	p.slot = -1
	p.refresh()
	var no_skill2: bool = p.why_not() == "Pick an item from your backpack." or p.why_not() == EnchantPanel.NO_SKILL
	Net.enchant_info = {}
	p.refresh()
	no_skill2 = no_skill2 and p.why_not() == EnchantPanel.NO_SKILL and p._skills.get_theme_color("font_color") == UiArt.RED
	p.enchant()
	_check("G no skill", no_skill and no_skill2 and fc.sent.is_empty(), "'%s' / '%s'" % [String(Net.ENCHANT_WHY[4]),
			p.why_not()])

	# H
	_check("H companion redraws", redraws[0] == 0, str(redraws[0]))

	# I: the craft window's pages
	Server.craft_skills = {13: {"points": 800, "name": "Spellcrafting"}}
	var cw := CraftWindow.new()
	cw.size = Vector2(CraftWindow.W, CraftWindow.H)
	v.ui.root.add_child(cw)
	cw.build()
	cw.open_trade(13)
	cw.show_enchant(true)
	var page_on: bool = cw._enchant != null and cw._enchant.visible and not cw._page.visible \
			and cw._enchant_btn.text == "RECIPES" and cw._enchant.has_method("why_not")
	cw.show_assistant(true)
	var swapped: bool = cw._assist.visible and not cw._enchant.visible and not cw._page.visible \
			and cw._enchant_btn.text == "ENCHANT" and cw._assist_btn.text == "RECIPES"
	cw.show_assistant(false)
	var back: bool = cw._page.visible and not cw._assist.visible and not cw._enchant.visible
	_check("I craft window ENCHANT page", page_on and swapped and back, "on %s, swapped %s, back %s" % [page_on, swapped, back])
	cw.queue_free()

	w.queue_free()
	Net.enchant_info = {}
	Net.enchant_status = {}
	Server.client = old_client
	Server.craft_skills = old_skills
	Server.inv = old_inv
	print("[enchant] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
