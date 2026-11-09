class_name CharacterSheet
extends RefCounted
## The CHARACTER sheet of the right panel, on the client's charsheet.bmp (192x448; the captions are painted on the
## art, the values go in its boxes - coordinates measured on the bitmap, original pixels). Stats are the client's
## starting stats (PlayerData); the class's own three stats are drawn in the highlight colour as on the client's
## creation screen. Values the game doesn't compute yet (weapon skill, hit points ...) show "-"; nothing made up.

const STAT_ROWS := [158.0, 176.5, 194.5, 212.5]
const LEFT_STATS := ["STR", "CON", "DEX", "QUI"]
const RIGHT_STATS := ["INT", "EMP", "PIE", "CHA"]
const STAT_WORD := {"STR": "STRENGTH", "CON": "CONSTITUTION", "DEX": "DEXTERITY", "QUI": "QUICKNESS",
		"INT": "INTELLIGENCE", "PIE": "PIETY", "EMP": "EMPATHY", "CHA": "CHARISMA"}


# --- the class's main stats (owner 2026-11-09: main stats in gold, and what each stat gives this class on hover) ---
# Primary / secondary / tertiary and the casting stat come from the class data (content/classes/classes.json = the
# server's class definitions); from level 6 the primary rises +1 every level, the secondary every 2 levels and the
# tertiary every 3 (GamePlayer level-up). A 1.65 base class (Fighter, Acolyte ...) has no stat gains of its own: its
# main stats are the ones most of its paths share, plus its casting stat. The effects follow the server's formulas
# (GamePlayer.GetWeaponStat, the evade / parry / block formulas, MaxHealth, MaxMana, SpellHandler damage).

## The painted stat captions of charsheet.bmp (original pixels), left column STR CON DEX QUI, right INT EMP PIE CHA -
## just inside each caption button, ending above the bevel line below it (measured on the 4x art: the line starts
## a quarter pixel under each 14-pixel caption).
const CAPTION_LEFT := Rect2(31, 0, 62, 14)
const CAPTION_RIGHT := Rect2(122, 0, 68, 14)
const CAPTION_TOPS := [159.0, 177.0, 195.0, 213.0]
static var _gold_mat: ShaderMaterial


## A main stat's painted caption in gold (owner 2026-11-09: "by gold I mean this bold stat text"): the caption's own
## pixels drawn again over the art with the light lettering recoloured gold and the dark stone left as it is.
static func _gold_caption(parent: Control, r: Rect2) -> void:
	if _gold_mat == null:
		var sh := Shader.new()
		sh.code = """shader_type canvas_item;
uniform vec3 gold : source_color = vec3(1.0, 0.82, 0.36);
void fragment() {
	vec4 c = texture(TEXTURE, UV);
	float l = max(c.r, max(c.g, c.b));
	float t = smoothstep(0.36, 0.62, l);
	COLOR = vec4(mix(c.rgb, gold * min(l * 1.35, 1.0), t), c.a);
}"""
		_gold_mat = ShaderMaterial.new()
		_gold_mat.shader = sh
	var t := UiArt.picture("charsheet", r)
	t.position = r.position
	t.material = _gold_mat
	parent.add_child(t)


## The player's class entry: by id, checked against the class name the server sent (a promotion, a female title).
static func class_now() -> Dictionary:
	var cid := PlayerData.class_id()
	var e: Dictionary = ClassDb.cls(cid) if cid > 0 else {}
	var want := String(Server.info.get("class_name", "")) if Server.active else ""
	if want == "":
		want = String(PlayerData.character.get("class_name", ""))
	if want != "" and (e.is_empty() or String(e.get("name", "")).to_lower() != want.to_lower()):
		var found := _class_by_name(want)
		if not found.is_empty():
			e = found
	return e


static func _class_by_name(n: String) -> Dictionary:
	ClassDb._load()
	for e in ClassDb._classes.values():
		if String(e.get("name", "")).to_lower() == n.to_lower():
			return e
	for id in (Server.classes.get("classes", {}) as Dictionary):           # female titles (Armswoman ...)
		if String(Server.classes.classes[id].get("female", "")).to_lower() == n.to_lower():
			return ClassDb.cls(int(id))
	return {}


## The class data spells Charisma "CHR"; the sheet uses "CHA".
static func _code(s: String) -> String:
	return "CHA" if s == "CHR" else s


## stat code -> role: PRIMARY / SECONDARY / TERTIARY / PATHS (a base class's shared stat) / CASTING.
static func key_stats(e: Dictionary) -> Dictionary:
	var out := {}
	if e.is_empty():
		return out
	if not bool(e.get("is_base", false)):
		for r in [["primary", "PRIMARY"], ["secondary", "SECONDARY"], ["tertiary", "TERTIARY"]]:
			var s := _code(String(e.get(r[0], "")))
			if s != "" and not out.has(s):
				out[s] = r[1]
	else:
		var paths := _paths(e)
		var count := {}
		for p in paths:
			for k in ["primary", "secondary", "tertiary"]:
				var s := _code(String(p.get(k, "")))
				if s != "":
					count[s] = int(count.get(s, 0)) + 1
		for s in count:
			if int(count[s]) * 2 > paths.size():                       # on more than half of its paths
				out[s] = "PATHS"
		if out.is_empty() and not count.is_empty():
			var best: String = count.keys()[0]
			for s in count:
				if int(count[s]) > int(count[best]):
					best = s
			out[best] = "PATHS"
	var mana := _code(String(e.get("mana_stat", "")))
	if mana != "" and not out.has(mana):
		out[mana] = "CASTING"
	return out


static func _paths(e: Dictionary) -> Array:
	var out := []
	for i in e.get("advanced", []):
		var p := ClassDb.cls(int(i))
		if not p.is_empty():
			out.append(p)
	return out


## The skill lines a class (or any of a base class's paths) trains, lower-cased.
static func _lines_of(e: Dictionary) -> Array:
	var out := []
	for c in [e] + (_paths(e) if bool(e.get("is_base", false)) else []):
		for s in c.get("specs", []):
			var k := String((s as Dictionary).get("spec", "")).to_lower()
			if k != "" and not k.ends_with("career"):
				out.append(k)
	return out


static func _has_line(lines: Array, words: Array) -> bool:
	for l in lines:
		for w in words:
			if String(l).contains(w):
				return true
	return false


## The hover text: the stat, its role for this class, and what it gives this class.
static func stat_tip(code: String, e: Dictionary) -> String:
	var cname := String(e.get("name", "your class"))
	var keys := key_stats(e)
	var base := bool(e.get("is_base", false))
	var lines := _lines_of(e)
	var mana := _code(String(e.get("mana_stat", "")))
	var caster := String(e.get("class_type", "")).to_lower().contains("caster")
	var archer := _has_line(lines, ["longbow", "composite", "recurve", "archery"])
	var crossbow := _has_line(lines, ["crossbow"])
	var out := [String(STAT_WORD.get(code, code))]
	match String(keys.get(code, "")):
		"PRIMARY": out.append("Primary stat of the %s: +1 every level from level 6." % cname)
		"SECONDARY": out.append("Secondary stat of the %s: +1 every 2 levels from level 6." % cname)
		"TERTIARY": out.append("Tertiary stat of the %s: +1 every 3 levels from level 6." % cname)
		"CASTING": out.append("The %s's casting stat." % cname)
		"PATHS": out.append("A main stat for the %s's classes (%s). Stats rise by themselves from level 6, once you choose your class." % [
				cname, ", ".join(_paths(e).map(func(p): return String(p.get("name", ""))))])
		_: out.append("Not a main stat of the %s." % cname)
	match code:
		"STR":
			if cname == "Nightshade":
				out.append("Your spell damage (Nightshade spells are based on Strength).")
			if not caster:
				out.append("Melee damage and weapon skill with slashing, crushing, two-handed and polearm weapons, and half of it with thrusting, flexible and hand-to-hand weapons.")
			out.append("How much you can carry before you are slowed%s." % (" - and so how many arrows" if archer else ""))
		"CON":
			out.append("Your hit points.")
		"DEX":
			if not caster:
				out.append("Damage and weapon skill with thrusting, flexible and hand-to-hand weapons (with Strength), and with staves.")
			else:
				out.append("Damage and weapon skill with a staff.")
			if archer:
				out.append("Damage and accuracy with your bow.")
			if crossbow:
				out.append("Damage and accuracy with a crossbow.")
			if _has_line(lines, ["parry"]):
				out.append("Your chance to parry.")
			if _has_line(lines, ["shield"]):
				out.append("Your chance to block with a shield.")
			out.append("Your chance to evade (with Quickness), once you have Evade.")
			if mana == "DEX" and cname == "Nightshade":          # (archers' DEX "power" has no spells to use in 1.65)
				out.append("Your power pool.")
		"QUI":
			out.append("Attack speed: faster melee swings and bow shots.")
			out.append("Your chance to evade (with Dexterity), once you have Evade.")
			if caster:
				out.append("It does not speed up spell casting in 1.65.")
		_:
			if mana == code:
				out.append("Your power pool and the strength of your spells%s." % (" and songs" if code == "CHA" and _has_line(lines, ["music", "instrument", "song"]) else ""))
			elif base and keys.get(code, "") == "PATHS":
				out.append("The power pool and spells of the paths that cast with it.")
			else:
				out.append("No use to the %s." % cname)
	return "\n".join(out)


static func build(parent: Control) -> void:
	var c := PlayerData.character
	_box(parent, Rect2(22, 47, 24, 14), str(PlayerData.level))
	_box(parent, Rect2(107, 47, 80, 14), str(PlayerData.realm_points))
	var lines := [["race", String(c.get("race", ""))], ["base", String(c.get("class_name", ""))],
			["profession", String(c.get("profession", ""))], ["title", String(c.get("title", ""))]]
	var srv: Dictionary = Server.info if Server.active else {}
	if not srv.is_empty():                                 # the server's own words (SendUpdatePlayer)
		lines = [["race", _word(srv.get("race_name", ""))], ["base", _word(srv.get("base_class", ""))],
				["profession", _word(srv.get("profession", ""))], ["title", _word(srv.get("title", ""))]]
	for i in lines.size():
		var l := UiArt.label(lines[i][1], 8, UiArt.CREAM)
		l.position = Vector2(64, 64 + i * 13)
		l.size = Vector2(122, 12)
		l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		l.clip_text = true
		parent.add_child(l)
	# the server's numbers (owner 2026-10-14: "weapon skill, weapon damage, armor factor, hit points all that needs to
	# work"): SendUpdateWeaponAndArmorStats and the stats packet's hit points; "-" without a server
	var ws: Dictionary = Server.weapon_stats if Server.active else {}
	var hp := Server.max_hp if Server.active and Server.max_hp > 0 else int(srv.get("max_health", 0))
	_box(parent, Rect2(55, 117, 31, 15), str(int(ws.weapon_skill)) if ws.has("weapon_skill") else "-")   # weapon skill
	_box(parent, Rect2(156, 117, 31, 15), str(hp) if hp > 0 else "-")                                    # hit points
	_box(parent, Rect2(55, 135, 31, 14), "%.1f" % float(ws.weapon_damage) if ws.has("weapon_damage") else "-")
	_box(parent, Rect2(156, 135, 31, 14), str(int(ws.armor_factor)) if ws.has("armor_factor") else "-")   # armor factor
	# stats in the client's order STR CON DEX QUI | INT PIE EMP CHA; the sheet lists INT EMP PIE CHA on the right
	var right_index := [4, 6, 5, 7]
	var cls_now := class_now()
	var keys := key_stats(cls_now)
	for i in 4:
		for side in 2:
			var si: int = i if side == 0 else right_index[i]
			var code: String = LEFT_STATS[i] if side == 0 else RIGHT_STATS[i]
			var now := int(PlayerData.effective_stats()[si]) if PlayerData.stats.size() == 8 else 0
			var norm := PlayerData.unmodified_stat(si)
			var v := str(now) if PlayerData.stats.size() == 8 else "-"
			var b := _box(parent, Rect2(5 if side == 0 else 95, STAT_ROWS[i], 24 if side == 0 else 25, 16), v)
			b.tooltip_text = stat_tip(code, cls_now) if not cls_now.is_empty() else code
			b.mouse_filter = Control.MOUSE_FILTER_PASS                 # (so the hover text shows)
			# the painted caption: gold for a main stat (the numbers keep their own colours), and the same hover text
			var cap: Rect2 = CAPTION_LEFT if side == 0 else CAPTION_RIGHT
			var cap_rect := Rect2(cap.position.x, CAPTION_TOPS[i], cap.size.x, cap.size.y)
			if keys.has(code):
				_gold_caption(parent, cap_rect)
			var hover := Control.new()
			hover.position = cap_rect.position
			hover.size = cap_rect.size
			hover.tooltip_text = b.tooltip_text
			hover.mouse_filter = Control.MOUSE_FILTER_PASS
			parent.add_child(hover)
			if Server.active and Server.stat_buffs.size() == 8 and PlayerData.stats.size() == 8:
				# the server's numbers (owner 2026-10-15: "con doesn't properly show up as red text after death ... negative
				# red and green values"): its buffs / debuffs part, and the constitution lost at deaths (CON = index 1)
				var buff := int(Server.stat_buffs[si])
				var lost := Server.con_lost if si == 1 else 0
				if buff < 0 or lost > 0:
					b.add_theme_color_override("font_color", Color(1.0, 0.3, 0.25))
					b.tooltip_text += "\nNOW %d (%s)" % [now, ", ".join(([] if buff >= 0 else ["DEBUFFED %d" % buff]) +
							([] if lost <= 0 else ["%d LOST AT DEATH" % lost]))]
				elif buff > 0:
					b.add_theme_color_override("font_color", Color(0.35, 1.0, 0.35))
					b.tooltip_text += "\nNOW %d (BUFFED +%d)" % [now, buff]
			elif PlayerData.stats.size() == 8 and now < norm:          # lowered (debuffs, constitution lost at death)
				b.add_theme_color_override("font_color", Color(1.0, 0.3, 0.25))
				b.tooltip_text += "\nNOW %d (NORMALLY %d)" % [now, norm]
			elif PlayerData.stats.size() == 8 and now > norm:        # raised by buffs
				b.add_theme_color_override("font_color", Color(0.35, 1.0, 0.35))
				b.tooltip_text += "\nNOW %d (NORMALLY %d)" % [now, norm]
	var info := RichTextLabel.new()
	info.position = Vector2(10, 258)
	info.size = Vector2(158, 148)
	info.bbcode_enabled = true
	info.scroll_active = true
	info.add_theme_font_override("normal_font", UiArt.font(true))
	info.add_theme_font_size_override("normal_font_size", 8)
	info.add_theme_color_override("default_color", UiArt.CREAM)
	info.mouse_filter = Control.MOUSE_FILTER_PASS
	UiArt.themed(info)
	var realm: String = {"alb": "ALBION", "mid": "MIDGARD", "hib": "HIBERNIA"}.get(String(c.get("realm", "")), "-")
	var weapon: String = {"standard": "STANDARD", "twohand": "TWO HANDED", "ranged": "RANGED"}.get(PlayerData.active_weapon, "-")
	var rows := [["REALM", realm]]
	if not srv.is_empty():
		# ADDITIONAL INFO: the rest of SendUpdatePlayer - realm rank (RealmLevel: rank * 10 + level, 1L0 first), its
		# title and the realm skill points, guild and rank, last name, crafting, master level, house - and the resists
		var rl := int(srv.get("realm_level", 0))
		rows.append(["REALM RANK", "%dL%d  %s" % [rl / 10 + 1, rl % 10, _word(srv.get("realm_title", ""))]])
		rows.append(["REALM SKILL POINTS", str(int(srv.get("realm_spec_points", 0)))])
		if _word(srv.get("guild", "")) != "":
			rows.append(["GUILD", (_word(srv.guild) + "  " + _word(srv.get("guild_title", ""))).strip_edges()])
		if _word(srv.get("last_name", "")) != "":
			rows.append(["LAST NAME", _word(srv.last_name)])
		if _word(srv.get("craft_skill", "")) != "":
			rows.append(["CRAFTING", (_word(srv.craft_skill) + "  " + _word(srv.get("craft_title", ""))).strip_edges()])
		if int(srv.get("ml_level", 0)) > 0:
			rows.append(["MASTER LEVEL", "%d  %s" % [int(srv.ml_level), _word(srv.get("ml_title", ""))]])
		if int(srv.get("house", 0)) > 0:
			rows.append(["HOUSE", "LOT %d" % int(srv.house)])
	rows.append(["ACTIVE WEAPON", weapon])
	rows.append(["MONEY", PlayerData.money_text().to_upper()])
	if Server.active and Server.resists.size() >= 9:
		var names := ["CRUSH", "SLASH", "THRUST", "HEAT", "COLD", "MATTER", "BODY", "SPIRIT", "ENERGY"]
		for i in 9:
			rows.append([names[i], "%d%%" % int(Server.resists[i])])
	var gold := UiArt.GOLD.to_html(false)
	info.text = "\n".join(rows.map(func(r): return "[color=#%s]%s[/color]  %s" % [gold, r[0], String(r[1]).to_upper()]))
	parent.add_child(info)


## A server text field: the server writes "None" for an empty one (NullOrEmptyToNone).
static func _word(v) -> String:
	var t := String(v)
	return "" if t == "None" else t


static func _box(parent: Control, r: Rect2, text: String) -> Label:
	var l := UiArt.label(text, 8, UiArt.CREAM)
	l.position = r.position
	l.size = r.size
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.clip_text = true
	l.mouse_filter = Control.MOUSE_FILTER_PASS
	parent.add_child(l)
	return l
