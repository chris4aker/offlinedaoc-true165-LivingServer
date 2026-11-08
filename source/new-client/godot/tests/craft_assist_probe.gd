extends Node
## Offline check (owner 2026-11-08: the crafting assistant - set a skill goal, it crafts / buys / sells, estimates the
## cost and time, a baseline cost against the current estimate): hand-built 'H' ops 40 / 41 / 42 through
## CompanionNet.handle, a fake server recording the panel's /craftassist commands.
##   A  op 42 over two packets + the end packet: rows only after the end, one "recipes" signal
##   B  op 41 with a negative net cost (a profit) and skill rows     C  op 40: fields, baseline vs current colours
##   D  the panel's commands (status on open, estimate, start, stop)  E  option off: greyed, nothing sent
##   F  the skill drop-down per trade                                 G  ops 40-42 don't redraw the companion windows
##   --skip-login --zone=N --world-mode=1.65 --probe=craft_assist_probe

const Net := preload("res://game/net/craft_assist_net.gd")
const AssistPanel := preload("res://game/ui/craft_assist_panel.gd")


class FakeClient extends Node:
	var sent: Array = []

	func Command(t: String) -> void:
		sent.append(t)


class FakeWindow extends Control:
	var trade := 11


var _kinds: Array = []


static func _body(op: int) -> PackedByteArray:
	var b := PackedByteArray()
	b.resize(128)
	b[1] = 0x48
	b[2] = 1
	b[3] = op
	return b


static func _rows(b: PackedByteArray, at: int, rows: Array) -> void:
	b[at - 1] = rows.size()
	for k in rows.size():
		var i: int = at + k * 9
		b[i] = rows[k][0]
		b.encode_u16(i + 1, rows[k][1])
		b.encode_u16(i + 3, rows[k][2])
		b.encode_u32(i + 5, rows[k][3])


static func _recipe_packet(trade: int, first: int, k: int) -> PackedByteArray:
	var b := _body(42)
	b[4] = trade
	b[5] = k
	for n in k:
		var i := 8 + n * 18
		var id := first + n
		b.encode_u16(i, id)
		b.encode_u16(i + 2, 700 + n)
		b.encode_u32(i + 4, 1000 * id)
		b.encode_u32(i + 8, 300 * id)
		b.encode_u16(i + 12, 670 if n % 2 == 0 else 0)
		b[i + 14] = 1 | 2
		b[i + 15] = 1
		b.encode_u16(i + 16, 9 + n)
	return b


static func _status_packet(state: int, reason: int, baseline: int, current: int) -> PackedByteArray:
	var b := _body(40)
	b[4] = state
	b[5] = reason
	b[6] = 11
	b[7] = 2                                             # auto-sell only
	b.encode_u16(8, 784)
	b.encode_u16(10, 740)
	b.encode_u16(12, 37)
	b.encode_u16(14, 6)
	b.encode_u32(16, 52000)
	b.encode_u32(20, 18000)
	b.encode_u16(24, 5083)
	b.encode_s32(26, baseline)
	b.encode_s32(30, current)
	b.encode_u32(34, 7260)
	b.encode_u32(38, 600)
	_rows(b, 43, [[11, 740, 784, 7260], [7, 675, 690, 3000]])
	return b


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(3.0).timeout
	var was_on := Net.enabled()
	var fc := FakeClient.new()
	Server.client = fc
	add_child(fc)
	Server.craft_skills = {11: {"points": 734, "name": "Tailoring"}, 7: {"points": 675, "name": "Leathercrafting"},
			8: {"points": 788, "name": "Clothworking"}, 6: {"points": 300, "name": "Metalworking"}}
	Server.craft_assist_changed.connect(func(k: String): _kinds.append(k))
	var net := CompanionNet.new()
	add_child(net)
	var redraws := [0]
	net.changed.connect(func(): redraws[0] += 1)
	var ok := true

	# A: op 42 accumulation
	net.handle(_recipe_packet(11, 100, 6))
	net.handle(_recipe_packet(11, 106, 2))
	var before_end: bool = _kinds.is_empty() and Net.recipe_info.is_empty()
	net.handle(_recipe_packet(11, 0, 0))
	var r: Dictionary = Net.recipe_info.get(101, {})
	var good_a: bool = before_end and _kinds == ["recipes"] and Net.recipe_info.size() == 8 and r.get("level") == 701 \
			and r.get("cost") == 101000 and r.get("sell") == 30300 and r.get("sec_min") == 0 and r.get("trains") == 3 \
			and r.get("status") == 1 and r.get("secs") == 10 and r.get("trade") == 11 \
			and int(Net.recipe_info[106].sec_min) == 670 and Net.trained_skills(3) == [8, 7]
	ok = ok and good_a
	print("[craftassist] A: op 42 rows %d after the end (none before %s), signals %s, row 101 %s  %s" % [
			Net.recipe_info.size(), before_end, _kinds, r, "OK" if good_a else "WRONG"])

	# B: op 41
	_kinds.clear()
	var e := _body(41)
	e[4] = 11
	e[5] = 1
	e.encode_u16(8, 734)
	e.encode_u16(10, 784)
	e.encode_u32(12, 312)
	e.encode_u32(16, 18720)
	e.encode_u32(20, 250000)
	e.encode_u32(24, 262345)
	e.encode_s32(28, -12345)
	e.encode_u32(32, 99000)
	e.encode_u16(36, 5083)
	_rows(e, 39, [[11, 734, 784, 18720], [7, 675, 701, 9000]])
	net.handle(e)
	var es := Net.estimate
	var good_b: bool = _kinds == ["estimate"] and es.target == 11 and es.feasible == true and es.reason == 0 \
			and es.from == 734 and es.goal == 784 and es.crafts == 312 and es.secs == 18720 and es.cost == 250000 \
			and es.resale == 262345 and es.net == -12345 and es.money == 99000 and es.recipe == 5083 \
			and es.skills.size() == 2 and es.skills[1] == {"skill": 7, "now": 675, "projected": 701, "eta": 9000}
	ok = ok and good_b
	print("[craftassist] B: op 41 net %d, crafts %d, skills %s, signals %s  %s" % [es.get("net"), es.get("crafts"),
			es.get("skills"), _kinds, "OK" if good_b else "WRONG"])

	# C: op 40 fields
	_kinds.clear()
	net.handle(_status_packet(1, 0, 100000, 90000))
	var st := Net.status
	var good_c: bool = _kinds == ["status"] and st.state == 1 and st.reason == 0 and st.target == 11 \
			and st.auto_buy == false and st.auto_sell == true and st.goal == 784 and st.now == 740 and st.crafts == 37 \
			and st.gained == 6 and st.spent == 52000 and st.earned == 18000 and st.recipe == 5083 \
			and st.baseline == 100000 and st.current == 90000 and st.secs_left == 7260 and st.elapsed == 600 \
			and st.skills.size() == 2 and st.skills[0].eta == 7260 and Net.running()
	ok = ok and good_c
	print("[craftassist] C: op 40 %s  %s" % [st, "OK" if good_c else "WRONG"])
	net.handle(_status_packet(3, 7, 0, 0))               # (stopped: the panel starts idle-ish below)

	# D / E: the panel
	Net.set_enabled(false)
	fc.sent.clear()
	var w := FakeWindow.new()
	w.size = Vector2(320, 448)
	v.ui.root.add_child(w)
	var p := AssistPanel.new()
	p.position = Vector2(10, 60)
	p.size = Vector2(300, 380)
	w.add_child(p)
	p.setup(w)
	await get_tree().process_frame
	var greyed: bool = p._skill.disabled and not p._goal.editable and p._estimate.disabled and p._start.disabled \
			and p._buy.disabled and p._sell.disabled and p._minus.disabled and p._note.visible
	p.ask_estimate()
	p.start()
	var good_e: bool = greyed and fc.sent.is_empty()
	ok = ok and good_e
	print("[craftassist] E: option off - greyed %s, note '%s', sent %s  %s" % [greyed, p._note.text, fc.sent,
			"OK" if good_e else "WRONG"])
	Net.set_enabled(true)
	p._asked_at = -100000
	p.refresh()
	p._on_shown()
	var live: bool = not p._skill.disabled and p._goal.editable and not p._estimate.disabled and not p._start.disabled \
			and p._stop.disabled and not p._note.visible
	var open_sent: Array = fc.sent.duplicate()
	fc.sent.clear()
	p.ask_estimate()
	p._buy.button_pressed = false
	p.start()
	net.handle(_status_packet(1, 0, 100000, 90000))      # the run started: STOP lights, the rest greys
	await get_tree().process_frame
	var running_ui: bool = not p._stop.disabled and p._start.disabled and p._skill.disabled
	var cur_green: bool = p._current.get_theme_color("font_color") == UiArt.GREEN and p._current.text.contains("LESS") \
			and p._baseline.text.contains("BASELINE") and p._info.get_parsed_text().contains("Making: woolen cloth gloves")
	var green_text: String = p._current.text
	net.handle(_status_packet(1, 0, 100000, 120000))
	var cur_red: bool = p._current.get_theme_color("font_color") == UiArt.RED and p._current.text.contains("MORE")
	var red_text: String = p._current.text
	p.stop()
	var want := ["&craftassist estimate 11 784 1", "&craftassist start 11 784 0 1", "&craftassist stop"]
	var good_d: bool = live and open_sent == ["&craftassist status"] and fc.sent == want and running_ui and cur_green and cur_red
	ok = ok and good_d
	print("[craftassist] D: option on - live %s, on open %s, buttons %s, running ui %s" % [live, open_sent, fc.sent, running_ui])
	print("[craftassist]    green '%s' %s / red '%s' %s  %s" % [green_text, cur_green, red_text, cur_red,
			"OK" if good_d else "WRONG"])
	print("[craftassist]    status text: %s | %s" % [p._head.text, p._info.get_parsed_text().replace("\n", " / ")])
	print("[craftassist]    table: %s" % p._table.get_parsed_text().replace("\n", " "))
	net.handle(_status_packet(3, 7, 0, 0))
	net.handle(e)
	p.view = "estimate"
	p.refresh()
	print("[craftassist]    estimate text: %s | %s" % [p._head.text, p._info.get_parsed_text().replace("\n", " / ")])

	# F: the skill drop-down per trade
	var f11: Array = AssistPanel.skills_for(11)
	var f1: Array = AssistPanel.skills_for(1)
	var f4: Array = AssistPanel.skills_for(4)
	var f12: Array = AssistPanel.skills_for(12)
	w.trade = 1
	p._rebuild_skills(false)
	var good_f: bool = f11.slice(0, 3) == [11, 7, 8] and f1[0] == 1 and f1.has(6) and f1.has(14) and f4.has(10) and f12.has(14) \
			and p.targets == f1
	ok = ok and good_f
	print("[craftassist] F: tailoring %s, weaponcraft %s, alchemy %s, fletching %s, goal after switch %s  %s" % [
			f11, f1, f4, f12, p._goal.text, "OK" if good_f else "WRONG"])

	# G
	var good_g: bool = redraws[0] == 0
	ok = ok and good_g
	print("[craftassist] G: companion redraws %d  %s" % [redraws[0], "OK" if good_g else "WRONG"])
	Net.set_enabled(was_on)
	print("[craftassist] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
