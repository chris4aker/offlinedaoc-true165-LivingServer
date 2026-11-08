extends RefCounted
## The crafting assistant's messages (owner 2026-11-08: "set a skill goal and it automatically crafts and trains and
## estimates cost to that skill goal"; a quality of life option, not in 1.65 - off by default, Options > Crafting).
## The server runs it and plans it; this only reads its 'H' messages and sends its /craftassist commands:
##   op 40 status (a run: after every craft), op 41 an estimate, op 42 the recipes' prices (several packets, K = 0 ends)
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
