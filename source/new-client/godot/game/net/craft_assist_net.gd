extends RefCounted
## The crafting assistant's messages (owner 2026-11-08: "set a skill goal and it automatically crafts and trains and
## estimates cost to that skill goal"; a quality of life option, not in 1.65 - off by default, Options > Crafting).
## The server runs it and plans it; this only reads its 'H' messages and sends its /craftassist commands:
##   op 40 status (a run: after every craft), op 41 an estimate, op 42 the recipes' prices (several packets, K = 0 ends)
##   op 43 / 44 the enchant helper (an item's enchant options / an enchant's status; see below)
## Every craft still takes the normal 1.65 time and costs real gold - the assistant only clicks, buys and sells.
## No class_name (a new one needs an editor class scan): preload("res://game/net/craft_assist_net.gd").

const OP_STATUS := 40
const OP_ESTIMATE := 41
const OP_RECIPES := 42
# op 42's trains bits -> the secondary skill they raise (eCraftingSkill)
const TRAINS := {1: 8, 2: 7, 4: 6, 8: 14, 16: 10, 32: 9}
const STATES := ["Not running", "Running", "Finished - goal reached", "Stopped"]
const REASONS := {0: "", 1: "the goal was reached", 2: "you ran out of money",
		3: "your backpack is full", 4: "interrupted - you moved, were attacked or were stunned",
		5: "nothing can raise it now: every recipe is grey or its requirements are not met",
		6: "the craft could not start (see the chat)", 7: "you stopped it",
		8: "the server refused - the chat says why"}

# The enchant helper (owner 2026-11-08: "add an enchant button ... it can auto buy and enchant it if you have the skill
# level. dont give anything out for free"): op 43 an item's enchant options (header, gem rows, tincture rows, names, end),
# op 44 a run's status. Always available - it changes no rules.
const OP_ENCHANT_INFO := 43
const OP_ENCHANT_STATUS := 44
const ENCHANT_STATES := ["Idle", "Buying materials", "Crafting", "Combining", "Done", "Stopped"]
const ENCHANT_REASONS := {0: "", 1: "done", 2: "you ran out of money", 3: "your backpack is full",
		4: "interrupted - you moved, fought or logged out (what was made is kept)", 5: "not enough skill",
		6: "the item is gone or can't be enchanted", 7: "you stopped it",
		8: "the combine failed", 9: "the server refused - the chat says why"}
# op 43 header reason byte
const ENCHANT_WHY := {0: "", 1: "Only crafted items can be enchanted (this item is not crafted or not allowed).",
		2: "The item's level is too low to be enchanted.",
		3: "No free bonus slots, and it already has a proc or charges.",
		4: "You don't have the skill: Spellcrafting or Alchemy is needed."}

## the last op 43 list: {slot, level, quality, free_slots, has_proc, can_spell, can_alch, imbue_max, imbue_used,
## spell_skill, alch_skill, imbue_cap, reason, name, gems: [ {id, bonus, value, imbue10, cost, secs, req, owned,
## can, quality, name} ], tinctures: [ {id, kind, charges, spell, level_req, owned, cost, secs, req, can, proc_chance,
## name, desc} ], at} + the kind 4 imbue facts (raw_existing, biggest_existing, over_success [7], preserve_mod,
## bonus_count, gem_quality, charges; has_imbue true once it came)
static var enchant_info := {}
## the last op 44: {state, reason, destroyed, exploded, step, steps, spent, recipe, secs_left, name, at}
static var enchant_status := {}
static var _enchant_new := {}                       # the op 43 list while it arrives (the end marker commits it)

## recipe id -> {level, cost, sell, sec_min, trains, status, secs, trade} (op 42; the last full list of each trade)
static var recipe_info := {}
## op 40's fields (see _status); {} until the first one
static var status := {}
## op 41's fields (see _estimate); {} until the first one
static var estimate := {}
static var _pending := {}                         # trade -> {recipe id: row} while a list is arriving
static var _on := -1                              # the option (-1: not read yet)
static var _names := {}                           # recipe id -> name (from the client's recipe list)


# ---------------------------------------------------------------- the option

static func enabled() -> bool:
	if _on < 0:
		var cfg := ConfigFile.new()
		cfg.load(UiArt.settings_path)
		_on = 1 if bool(cfg.get_value("interface", "craft_assist", false)) else 0
	return _on == 1


static func set_enabled(on: bool) -> void:
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)                 # (re-read: other parts of the UI write the file too)
	cfg.set_value("interface", "craft_assist", on)
	cfg.save(UiArt.settings_path)
	_on = 1 if on else 0


# ---------------------------------------------------------------- commands (true when sent)

static func _send(args: String) -> bool:
	if Server.client == null:
		return false
	Server.command("/craftassist " + args)
	return true


static func ask_recipes(trade: int) -> bool:
	return _send("recipes %d" % trade)


static func estimate_for(target: int, goal: int, sell: bool) -> bool:
	return _send("estimate %d %d %d" % [target, goal, 1 if sell else 0])


static func start(target: int, goal: int, buy: bool, sell: bool) -> bool:
	return _send("start %d %d %d %d" % [target, goal, 1 if buy else 0, 1 if sell else 0])


static func stop() -> bool:
	return _send("stop")


static func status_now() -> bool:
	return _send("status")


static func running() -> bool:
	return int(status.get("state", 0)) == 1


## The enchant helper: an item's options (op 43 answers).
static func enchant_info_for(slot: int) -> bool:
	return _send("enchantinfo %d" % slot)


## kind "spell" (ids: gem recipe ids, one per bonus) or "alch" (ids: the one tincture recipe id).
static func enchant(slot: int, kind: String, ids: Array, allow_overcharge := false) -> bool:
	if ids.is_empty():
		return false
	var list := []
	for id in ids:
		list.append(str(int(id)))
	if kind == "alch":
		return _send("enchant %d alch %s" % [slot, list[0]])
	return _send("enchant %d spell %s %d" % [slot, ",".join(list), 1 if allow_overcharge else 0])


static func enchant_stop() -> bool:
	return _send("enchantstop")


## An enchant is going on (op 44 buying / crafting / combining).
static func enchanting() -> bool:
	var s := int(enchant_status.get("state", 0))
	return s >= 1 and s <= 3


# ---------------------------------------------------------------- the messages (CompanionNet.handle sends ops 40-42)

static func handle(body: PackedByteArray) -> void:
	if body.size() < 8:
		return
	match int(body[3]):
		OP_STATUS:
			_status(body)
			Server.craft_assist_changed.emit("status")
		OP_ESTIMATE:
			_estimate(body)
			Server.craft_assist_changed.emit("estimate")
		OP_RECIPES:
			if _recipes(body):
				Server.craft_assist_changed.emit("recipes")
		OP_ENCHANT_INFO:
			if _enchant_info(body):
				Server.craft_assist_changed.emit("enchant_info")
		OP_ENCHANT_STATUS:
			_enchant_status(body)
			Server.craft_assist_changed.emit("enchant_status")


static func _u16(b: PackedByteArray, i: int) -> int:
	return b.decode_u16(i) if i + 2 <= b.size() else 0


static func _u32(b: PackedByteArray, i: int) -> int:
	return b.decode_u32(i) if i + 4 <= b.size() else 0


static func _s32(b: PackedByteArray, i: int) -> int:
	return b.decode_s32(i) if i + 4 <= b.size() else 0


## N x 9 bytes from `at` (the count at at - 1): skill u8, now u16, projected u16, eta seconds u32.
static func _skill_rows(b: PackedByteArray, at: int) -> Array:
	var out := []
	var n := int(b[at - 1]) if at - 1 < b.size() else 0
	for k in mini(n, 8):
		var i := at + k * 9
		if i + 9 > b.size():
			break
		out.append({"skill": int(b[i]), "now": _u16(b, i + 1), "projected": _u16(b, i + 3), "eta": _u32(b, i + 5)})
	return out


static func _status(b: PackedByteArray) -> void:
	status = {"state": int(b[4]), "reason": int(b[5]), "target": int(b[6]), "flags": int(b[7]),
			"auto_buy": (b[7] & 1) != 0, "auto_sell": (b[7] & 2) != 0,
			"goal": _u16(b, 8), "now": _u16(b, 10), "crafts": _u16(b, 12), "gained": _u16(b, 14),
			"spent": _u32(b, 16), "earned": _u32(b, 20), "recipe": _u16(b, 24),
			"baseline": _s32(b, 26), "current": _s32(b, 30), "secs_left": _u32(b, 34), "elapsed": _u32(b, 38),
			"skills": _skill_rows(b, 43), "at": Time.get_ticks_msec()}


static func _estimate(b: PackedByteArray) -> void:
	estimate = {"target": int(b[4]), "feasible": b[5] != 0, "reason": int(b[6]),
			"from": _u16(b, 8), "goal": _u16(b, 10), "crafts": _u32(b, 12), "secs": _u32(b, 16),
			"cost": _u32(b, 20), "resale": _u32(b, 24), "net": _s32(b, 28), "money": _u32(b, 32),
			"recipe": _u16(b, 36), "skills": _skill_rows(b, 39), "at": Time.get_ticks_msec()}


## One op 42 packet; true when it ended a list (K = 0): that trade's rows replace its old ones.
static func _recipes(b: PackedByteArray) -> bool:
	var trade := int(b[4])
	var k := int(b[5])
	if k == 0:
		for id in recipe_info.keys():
			if int(recipe_info[id].trade) == trade:
				recipe_info.erase(id)
		recipe_info.merge(_pending.get(trade, {}), true)
		_pending.erase(trade)
		return true
	var rows: Dictionary = _pending.get(trade, {})
	for n in mini(k, 6):
		var i := 8 + n * 18
		if i + 18 > b.size():
			break
		rows[_u16(b, i)] = {"level": _u16(b, i + 2), "cost": _u32(b, i + 4), "sell": _u32(b, i + 8),
				"sec_min": _u16(b, i + 12), "trains": int(b[i + 14]), "status": int(b[i + 15]),
				"secs": _u16(b, i + 16), "trade": trade}
	_pending[trade] = rows
	return false


static func _zstr(b: PackedByteArray, from: int) -> Array:
	var end := from
	while end < b.size() and b[end] != 0:
		end += 1
	return [b.slice(from, end).get_string_from_utf8() if end > from else "", end + 1]


## One op 43 packet; true when it was the end marker (the list it ended is now enchant_info).
static func _enchant_info(b: PackedByteArray) -> bool:
	var kind := int(b[4])
	match kind:
		0:                                           # header: a new list starts
			_enchant_new = {"slot": int(b[5]), "level": int(b[6]), "quality": int(b[7]), "free_slots": int(b[8]),
					"has_proc": b[9] != 0, "can_spell": b[10] != 0, "can_alch": b[11] != 0,
					"imbue_max": _u16(b, 12), "imbue_used": _u16(b, 14), "spell_skill": _u16(b, 16),
					"alch_skill": _u16(b, 18), "imbue_cap": _u16(b, 20), "reason": int(b[22]),
					"name": String(_zstr(b, 23)[0]), "gems": [], "tinctures": [], "names": {}, "desc": {},
					"has_imbue": false, "raw_existing": 0, "biggest_existing": 0, "over_success": [], "preserve_mod": 0,
					"bonus_count": 0, "gem_quality": 94, "charges": 0}
		1:                                           # gem rows, 20 bytes each from [6]
			if _enchant_new.is_empty():
				return false
			for k in int(b[5]):
				var i := 6 + k * 20
				if i + 20 > b.size():
					break
				(_enchant_new.gems as Array).append({"id": _u16(b, i), "bonus": _u16(b, i + 2), "value": _u16(b, i + 4),
						"imbue10": _u16(b, i + 6), "cost": _u32(b, i + 8), "secs": _u16(b, i + 12), "req": _u16(b, i + 14),
						"owned": int(b[i + 16]), "can": b[i + 17] != 0, "quality": int(b[i + 18]), "name": ""})
		2:                                           # tincture rows, 20 bytes each from [6]
			if _enchant_new.is_empty():
				return false
			for k in int(b[5]):
				var i := 6 + k * 20
				if i + 20 > b.size():
					break
				(_enchant_new.tinctures as Array).append({"id": _u16(b, i), "kind": int(b[i + 2]), "charges": int(b[i + 3]),
						"spell": _u32(b, i + 4), "level_req": int(b[i + 8]), "owned": int(b[i + 9]), "cost": _u32(b, i + 10),
						"secs": _u16(b, i + 14), "req": _u16(b, i + 16), "can": b[i + 18] != 0, "proc_chance": int(b[i + 19]),
						"name": "", "desc": ""})
		3:                                           # names: id u16 + zero-ended "name" or "name|spell description"
			if _enchant_new.is_empty():
				return false
			var at := 6
			for k in int(b[5]):
				if at + 2 > b.size():
					break
				var id := _u16(b, at)
				var s: Array = _zstr(b, at + 2)
				at = int(s[1])
				var parts := String(s[0]).split("|", true, 1)
				_enchant_new.names[id] = parts[0].strip_edges()
				if parts.size() > 1:
					_enchant_new.desc[id] = parts[1].strip_edges()
		4:                                           # the item's imbue facts (right after the header)
			if _enchant_new.is_empty():
				return false
			var over := []
			for k in 7:
				over.append(int(b[10 + k]))
			_enchant_new.merge({"has_imbue": true, "raw_existing": _u16(b, 6), "biggest_existing": _u16(b, 8),
					"over_success": over, "preserve_mod": b.decode_s8(17), "bonus_count": int(b[18]),
					"gem_quality": int(b[19]), "charges": int(b[20])}, true)
		0xFF:                                        # the end: names into the rows, the list replaces the old one
			if _enchant_new.is_empty():
				return false
			for g in _enchant_new.gems:
				g.name = String(_enchant_new.names.get(int(g.id), ""))
			for t in _enchant_new.tinctures:
				t.name = String(_enchant_new.names.get(int(t.id), ""))
				t.desc = String(_enchant_new.desc.get(int(t.id), ""))
			_enchant_new.at = Time.get_ticks_msec()
			enchant_info = _enchant_new
			_enchant_new = {}
			return true
	return false


static func _enchant_status(b: PackedByteArray) -> void:
	enchant_status = {"state": int(b[4]), "reason": int(b[5]), "destroyed": b[6] != 0,
			"exploded": int(b[5]) == 8 and b[9] != 0, "step": int(b[7]),
			"steps": int(b[8]), "spent": _u32(b, 10), "recipe": _u16(b, 14), "secs_left": _u32(b, 16),
			"name": String(_zstr(b, 20)[0]), "at": Time.get_ticks_msec()}


static func enchant_state_text(state: int) -> String:
	return ENCHANT_STATES[state] if state >= 0 and state < ENCHANT_STATES.size() else "State %d" % state


static func enchant_reason_text(reason: int) -> String:
	return String(ENCHANT_REASONS.get(reason, "code %d" % reason))


# ---------------------------------------------------------------- helpers for the windows

## The secondary skills a trains-bits value raises: [7, 8 ...].
static func trained_skills(bits: int) -> Array:
	var out := []
	for bit in TRAINS:
		if bits & int(bit):
			out.append(int(TRAINS[bit]))
	return out


## Expected net copper per point of the recipe's own trade at these skills ({id: points} or Server.craft_skills'
## {id: {points}}), from op 42's prices: (materials - resale) / chance to gain - only a success uses materials and only
## a success can gain, so the chance to make cancels out. The gain chance is the server's con table
## (CalculateChanceToGainPoint; no capital bonus). INF when grey, impossible, its secondary minimum is not met or unknown.
static func cost_per_point(recipe_id: int, skills: Dictionary, sell := true) -> float:
	var r: Dictionary = recipe_info.get(recipe_id, {})
	if r.is_empty() or (int(r.sec_min) > 0 and (int(r.status) & 1) == 0):
		return INF
	var s = skills.get(int(r.trade), 0)
	var pts := int(s.get("points", 0)) if s is Dictionary else int(s)
	var diff := int(r.level) - pts
	var gain := 0.0
	if diff <= -50 or diff > 49:
		return INF
	elif diff <= -31:
		gain = 0.15
	elif diff <= -11:
		gain = 0.30
	elif diff <= 0:
		gain = 0.45
	elif diff <= 19:
		gain = 0.55
	else:
		gain = 0.65
	return float(int(r.cost) - (int(r.sell) if sell else 0)) / gain


## A recipe's name from the client's recipe list (any trade of your realm).
static func recipe_name(id: int) -> String:
	if id <= 0:
		return ""
	if not _names.has(id):
		for list in Server.recipes_for(Server.my_realm()).values():
			for r in list:
				_names[int(r.id)] = String(r.name)
	return String(_names.get(id, "recipe %d" % id))


static func state_text(state: int) -> String:
	return STATES[state] if state >= 0 and state < STATES.size() else "State %d" % state


static func reason_text(reason: int) -> String:
	return String(REASONS.get(reason, "code %d" % reason))


## "3h 05m", "12m", "40s".
static func time_text(secs: int) -> String:
	secs = maxi(secs, 0)
	if secs < 60:
		return "%ds" % secs
	if secs < 3600:
		return "%dm" % (secs / 60)
	return "%dh %02dm" % [secs / 3600, (secs % 3600) / 60]


## Money, with "0 copper" spelled out and a negative amount as a profit.
static func money_text(c: int) -> String:
	if c < 0:
		return "a profit of " + PlayerData.money_string(-c)
	return PlayerData.money_string(c)
