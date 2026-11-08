class_name CompanionNet
extends Node
## True 1.65 Mode hired companions, as the server tells them: the
## DebugMode 0x21 'H' messages of plan section 17g / 17h ("TRUE 1.65 MODE HIRE WINDOW PROTOCOL.md"), read
## into one place for the hire window (SrvHireWindow) and each companion's window (SrvCompanionWindow). The same server
## logic drives the original client (its native trainer / vault windows) - nothing here decides anything: every button
## is a /companionui command and the server answers with fresh messages.
##   body: [0] 0, [1] 0x48, [2] version 1, [3] op, then the op's fields (little-endian)

signal opened(mode: int)                         # op 1: the hire window (0 innkeeper, 1 healer)
signal closed                                    # op 2
signal changed                                   # anything else: the windows redraw what they show
signal horn_opened                               # op 20: the Horn of Rallying's window (owner 2026-10-30)
signal horn_closed                               # op 22
signal horn_changed                              # op 21 rows / op 23 message

const CraftAssistNet := preload("res://game/net/craft_assist_net.gd")   # ops 40-42: the crafting assistant
const SLOT_NAMES := {10: "RIGHT HAND", 11: "LEFT HAND", 12: "TWO HANDED", 13: "RANGED", 21: "HEAD", 22: "HANDS",
		23: "FEET", 24: "JEWEL", 25: "TORSO", 26: "CLOAK", 27: "LEGS", 28: "ARMS", 29: "NECK", 32: "BELT",
		33: "LEFT WRIST", 34: "RIGHT WRIST", 35: "LEFT RING", 36: "RIGHT RING"}

var mode := 0                                    # 0 innkeeper, 1 healer
var slots := {"used": 0, "open": 0, "total": 7, "next": 0}
var texts := {}                                  # text slot -> String (op 3)
var hire := {}                                   # the hire page's numbers (op 4 kind 0)
var roster := {}                                 # row -> {state, level, class, race, gender, buttons, model, oid, id, xp}
var _rows_seen := {}                             # rows sent (op 4 kind 1) since the last op 5
var preview_oid := 0
var _own_preview := false                        # "/companionui ownpreview" sent this login
var gear := {}                                   # row -> {slot: item}, rebuilt on each list (op 7, ends with 0xFF)
var _gear_new := {}
var spec := {}                                   # row -> {lines: [ {...} ], points}
var previews := {}                               # row -> {line: [ {level, kind, icon, name} ]}
var _preview_new := {}
var stats := {}                                  # row -> op 10's numbers
var ras := {}                                    # row -> [ {...} ] (op 11)
## The companion's choices (op 14, reset day C17 / S33, agreed with the server chat): row -> [ {kind, slot, chosen,
## names, used, cap} ] - kind 1 charm, 2 pet (Cabalist / Enchanter), 3 sub-pet (Bonedancer, slot 1-3, used / cap the
## x/75), 4 song or chant (slot 1 in combat, 2 out of combat, 3 travelling, 4 Twist "No"/"Yes"); kind 0xFF ends a list.
var options := {}
var _options_new := {}
static var asked_gear_at := -100000              # (the server's HouseVault gear window for the original client: ignored)
# the Horn of Rallying (ops 20 - 23): its title, realm, the PvE events it can start and the last sound attempt's result
var horn := {"title": "", "realm": 0, "rows": [], "message": "", "at": 0}


static func _u16(b: PackedByteArray, i: int) -> int:
	return b[i] | (b[i + 1] << 8) if i + 1 < b.size() else 0


static func _u32(b: PackedByteArray, i: int) -> int:
	return _u16(b, i) | (_u16(b, i + 2) << 16)


static func _text(b: PackedByteArray, from: int, to: int) -> String:
	var out := PackedByteArray()
	for i in range(from, mini(to + 1, b.size())):
		if b[i] == 0:
			break
		out.append(b[i])
	return out.get_string_from_ascii()


func handle(body: PackedByteArray) -> void:
	if body.size() < 4 or body[1] != 0x48:
		return
	var op := body[3]
	if op >= 20 and op <= 23:
		_horn(op, body)
		return
	if op >= 40 and op <= 42:                            # the crafting assistant (owner 2026-11-08; not the companions)
		CraftAssistNet.handle(body)
		return
	if op == 15:                                         # the Realm Exchange chest page's time left (server S38)
		Server.listing_times(body)
		return
	if op == 12 or op == 13:
		# the original client's list windows (its trainer / vault lists: the gear, spec and realm ability lists the
		# server sends after every action) - this client draws those pages from ops 7 / 8 / 11 and never read them; each
		# one redrew the companion window for nothing (owner 2026-11-07: the game froze on every train, equip, bag move)
		return
	match op:
		1:
			mode = body[4]
			slots.used = body[5]
			slots.open = body[6]
			slots.next = body[7]
			if not _own_preview:                      # (once a login: the server stands no preview figure in the world for
				_own_preview = true                   # us - the hire window draws its own; server protocol, 2026-10-30)
				command("ownpreview")
			opened.emit(mode)
		2:
			closed.emit()
		3:
			texts[int(body[4])] = _text(body, 5, 126)
		4:
			if body[4] == 0:
				hire = {"race": body[5], "gender": body[6], "class": body[7], "face": body[8], "faces": body[9],
						"hair": body[10], "hairs": body[11], "hair_color": body[12], "colors": body[13],
						"model": _u16(body, 14), "cost": _u32(body, 16), "enabled": body[20] != 0,
						# HEIGHT (owner 2026-10-31; server protocol): [21] 1 short / 2 average / 3 tall, [22] its count (3)
						"height": body[21] if body.size() > 22 and body[22] == 3 else 2,
						"heights": body[22] if body.size() > 22 else 0}
			else:
				roster[int(body[5])] = {"state": body[6], "level": body[7], "class": body[8], "race": body[9],
						"gender": body[10], "buttons": body[11], "model": _u16(body, 12), "oid": _u16(body, 14),
						"id": _u16(body, 16), "xp": _u16(body, 18), "hair": body[20]}   # [20] its hair colour (1.65)
				_rows_seen[int(body[5])] = true
		5:
			slots = {"used": body[4], "open": body[5], "total": body[6], "next": body[7]}
			# Op 5 closes the server's full window refresh (Hire165.SendAll), which sends a row packet for every occupied
			# row first. A row it didn't send is empty now (dismissed / released). The server keeps fixed row numbers, so
			# rows are not compared with the used count (owner 2026-11-08: dismissing row 3 of 4 hid row 4 until a hire).
			for r in roster.keys():
				if not _rows_seen.has(int(r)):
					roster.erase(r)
			_rows_seen.clear()
		6:
			preview_oid = _u16(body, 4)
		7:
			var row := int(body[4])
			if body[5] == 0xFF:
				gear[row] = _gear_new.get(row, {})
				_gear_new.erase(row)
				Server.forget_details("cg:%d:" % row)          # (its items' delves asked again: they may have changed)
			else:
				if not _gear_new.has(row):
					_gear_new[row] = {}
				_gear_new[row][int(body[5])] = {"slot": body[5], "model": _u16(body, 6), "color": _u16(body, 8),
						"level": body[10], "quality": body[11], "condition": body[12], "object_type": body[13],
						"icon": _u16(body, 14), "name": _text(body, 16, 76),
						"count": _u16(body, 78) if body.size() > 79 else 0}   # [78..79] the stack count (0: not sent)
				return                                       # (the window redraws at the end of the list)
		8:
			var row := int(body[4])
			if not spec.has(row):
				spec[row] = {"lines": [], "points": 0}
			var lines: Array = spec[row].lines
			lines.resize(maxi(int(body[6]), 0))
			lines[int(body[5])] = {"index": body[5], "level": body[7], "cap": body[8], "cost": _u16(body, 11),
					"name": _text(body, 13, 76)}
			spec[row].points = _u16(body, 9)
		9:
			var row := int(body[4])
			if body[7] == 0xFF:                              # (one end marker a line: that line's list is complete)
				var line_done := int(body[5])
				if not previews.has(row):
					previews[row] = {}
				previews[row][line_done] = (_preview_new.get(row, {}) as Dictionary).get(line_done, [])
				if _preview_new.has(row):
					(_preview_new[row] as Dictionary).erase(line_done)
			else:
				if not _preview_new.has(row):
					_preview_new[row] = {}
				var line := int(body[5])
				if not _preview_new[row].has(line):
					_preview_new[row][line] = []
				_preview_new[row][line].append({"level": body[6], "kind": body[7], "icon": _u16(body, 8),
						"name": _text(body, 10, 73)})
				return
		10:
			var row := int(body[4])
			var st := []
			for i in 8:
				st.append(_u16(body, 8 + i * 2))
			var res := []
			for i in 9:
				res.append(body[47 + i])
			stats[row] = {"level": body[5], "xp": _u16(body, 6), "stats": st, "health": _u32(body, 24),
					"max_health": _u32(body, 28), "power": _u32(body, 32), "max_power": _u32(body, 36),
					"endurance": _u16(body, 40), "max_endurance": _u16(body, 42), "af": _u16(body, 44),
					"absorb": body[46], "resists": res, "money": _u32(body, 57), "rp": _u16(body, 61)}
			# the 8 stats unbuffed [63..78] and the armour factor's [79..80] (reset day C15, agreed with the server
			# chat): a stat above them is buffed, below debuffed
			if body.size() >= 79:
				var base := []
				for i in 8:
					base.append(_u16(body, 63 + i * 2))
				stats[row]["base"] = base
			if body.size() >= 81:
				stats[row]["base_af"] = _u16(body, 79)
		14:
			var row := int(body[4])
			if body[5] == 0xFF:
				options[row] = _options_new.get(row, [])
				_options_new.erase(row)
			else:
				var names := []
				var at := 13
				for k in int(body[8]):
					var end := at
					while end < body.size() and body[end] != 0:
						end += 1
					names.append(body.slice(at, end).get_string_from_ascii())
					at = end + 1
				# 0.5 (server chat 2026-11-08): after the names, some rows (pet, sub-pet, song, debuff line) carry one u32
				# spell id per name (0 = none) for the hover delve; older rows end at the names
				var ids := []
				if body.size() - at >= 4 * names.size():
					for k in names.size():
						ids.append(_u32(body, at + 4 * k))
				if not _options_new.has(row):
					_options_new[row] = []
				(_options_new[row] as Array).append({"kind": body[5], "slot": body[6], "chosen": body[7], "names": names,
						"used": _u16(body, 9), "cap": _u16(body, 11), "ids": ids})
				return                                       # (the window redraws at the end of the list)
		11:
			var row := int(body[4])
			if not ras.has(row) or (ras[row] as Array).size() != int(body[6]):
				var fresh := []
				fresh.resize(int(body[6]))
				ras[row] = fresh
			(ras[row] as Array)[int(body[5])] = {"index": body[5], "level": body[7], "max": body[8],
					"cost": _u16(body, 9), "points": _u16(body, 11), "icon": _u16(body, 13), "rank": body[15],
					"sub": body[16], "name": _text(body, 17, 80)}
	_changed_soon()


## One redraw a frame (owner 2026-11-07: "my game freezes every time I train a companion, equip them, add or remove an
## item from their inventory"): a /companionui answer is 30 - 80 messages (gear, spec lines, stats texts, the roster)
## and the client reads every waiting message in one frame - each `changed` rebuilt the open companion / hire window
## (20 - 90 ms), 1 - 7 s frozen (perf_live.log "slow packet companion"). Every message is still stored the moment it
## comes; the windows redraw once, at the end of that frame, from all of it.
var _changed_due := false


func _changed_soon() -> void:
	if _changed_due:
		return
	_changed_due = true
	_emit_changed.call_deferred()


func _emit_changed() -> void:
	_changed_due = false
	changed.emit()


## The horn's messages. op 20 opens it (title, realm, n rows to come), op 21 one row - state 0 ready, 1 underway,
## 2 boss respawning, 3 event cooldown, 4 unavailable; seconds left (2 / 3), bots assigned / suggested, event name, the
## server's status line -, op 22 closes, op 23 the result of a sound attempt.
func _horn(op: int, body: PackedByteArray) -> void:
	match op:
		20:
			var n := int(body[4])
			var rows := []
			rows.resize(n)
			horn = {"title": _text(body, 6, 53), "realm": body[5], "rows": rows, "message": horn.get("message", ""),
					"at": Time.get_ticks_msec()}
			horn_opened.emit()
		21:
			var rows: Array = horn.rows
			var i := int(body[4])
			if i >= rows.size():
				rows.resize(i + 1)
			rows[i] = {"index": i, "state": body[5], "seconds": _u32(body, 6), "assigned": _u16(body, 10),
					"needed": _u16(body, 12), "name": _text(body, 14, 61), "status": _text(body, 62, 125)}
			horn.at = Time.get_ticks_msec()
		22:
			horn_closed.emit()
			return
		23:
			horn.message = _text(body, 4, 125)
	horn_changed.emit()


## A /horn command ("list", "sound <index>", "close").
func horn_command(args: String) -> void:
	Server.command("/horn " + args)


## A /companionui command (the server answers with fresh messages).
func command(args: String) -> void:
	if args.begins_with("gear "):
		asked_gear_at = Time.get_ticks_msec()
	Server.command("/companionui " + args)


## The original client's helper windows the server sends alongside (plan 17h): its HouseVault gear window (slots
## 150 and up) and the champion-type trainer windows 200+row (spec) / 230+row (realm abilities). Ours draw the same
## from op 7 - 11, so those are not shown.
static func is_helper_trainer(window_id: int) -> bool:
	return window_id >= 200 and window_id < 240


func is_helper_vault(first_slot: int) -> bool:
	return first_slot >= 150 and Time.get_ticks_msec() - asked_gear_at < 600000


## The name of a roster row (its text slot 16 + 3 r), its "Class level" line and where it is.
func row_name(row: int) -> String:
	# (the server marks the selected row "> Name" - the original client's action bar works on the selected one)
	return String(texts.get(16 + 3 * row, "")).trim_prefix("> ").strip_edges()


func row_class_line(row: int) -> String:
	return String(texts.get(17 + 3 * row, ""))


func row_where(row: int) -> String:
	return String(texts.get(18 + 3 * row, ""))
