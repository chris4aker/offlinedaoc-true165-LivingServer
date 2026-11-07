extends Node
## Offline check (owner 2026-11-07 via the server chat: level 0 vendor trash read white in the bag, grey on hover): an
## item's name in a list (backpack, vault, merchant - ItemTooltip.name_color) in the same con colour as its hover box's
## name line (con_color), level 0 included - grey for a level 0 item from player level 3 up.
##   --skip-login --zone=N --world-mode=1.65 --probe=item_name_grey_probe


func _ready() -> void:
	await get_tree().create_timer(2.0).timeout
	var ok := true
	var grey := ItemTooltip.CON[-3] as Color
	var was := PlayerData.level
	for pl in [1, 2, 3, 10, 20, 35, 50]:
		PlayerData.level = pl
		for il in [0, 1, 5, 20, 50]:
			var it := {"id": "x", "name": "Jewel Studded Circlet", "level": il}
			var list := ItemTooltip.name_color(it, false)
			var hover := ItemTooltip.con_color(il, pl)
			var good: bool = list == hover and (il != 0 or pl < 3 or list == grey)
			ok = ok and good
			if not good or il == 0:
				print("[itemgrey] player %d, item level %d: list %s, hover %s  %s" % [pl, il, list, hover, "OK" if good else "WRONG"])
	PlayerData.level = was
	var empty := ItemTooltip.name_color({}, false) == UiArt.CREAM
	print("[itemgrey] empty slot cream: %s" % ("OK" if empty else "WRONG"))
	print("[itemgrey] %s" % ("PASS" if ok and empty else "FAIL"))
	get_tree().quit()
