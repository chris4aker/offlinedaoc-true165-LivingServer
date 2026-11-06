extends Node
## Offline check of listing on the Realm Exchange (owner 2026-11-06: "error message when i drag and drop no popup
## options and it completely deleted my melodic lute"), a stand-in client recording what is sent:
##   1. companion gear just used (the old ten-minute slot filter): your listing chest (window 5, slots 150+) still shows
##      - the three listings the owner's window never got (a vest, a vambrace, the lute)
##   2. a backpack item dropped on a row: MoveItem at once (the broker holds the slots); the server's plain update puts
##      it in the chest; the price box comes up; LIST IT sends the price and the duration
##   3. the companion GIVE ITEMS window (HouseVault 4, slots 150+) right after a companion gear click: still not shown
##   4. a vault opened since (the server's moves go there): a drop moves nothing until the broker's chest is back
##   5. the broker's "that slot already holds a listing": the chest asked for again
##   --skip-login --zone=N --world-mode=1.65 --probe=exchange_drop_probe


class FakeClient extends Node:
	var calls: Array = []

	func MoveItem(from: int, to: int, count: int) -> void:
		calls.append("MoveItem %d->%d x%d" % [from, to, count])

	func Command(t: String) -> void:
		calls.append(t)

	func SetMarketPrice(place: int, copper: int) -> void:
		calls.append("SetMarketPrice %d %d" % [place, copper])

	func MarketSearch(_f: Dictionary) -> void:
		pass

	func Interact(oid: int, _x: int, _y: int) -> void:
		calls.append("Interact %d" % oid)


func _item(slot: int, name: String, model: int) -> Dictionary:
	return {"slot": slot, "name": name, "model": model, "level": 20, "value1": 0, "value2": 0, "hand": 0,
			"type_damage": 0, "object_type": 36, "weight": 10, "condition": 100, "durability": 100, "quality": 100,
			"bonus": 0, "extension": 0, "effect": 0}


func _price_box(ex: Control) -> Control:
	for c in ex.get_parent().get_children():
		if c is ColorRect:
			for b in c.find_children("*", "Button", true, false):
				if (b as Button).text == "LIST IT":
					return c
	return null


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(5.0).timeout
	var fake := FakeClient.new()
	add_child(fake)
	var was_active: bool = Server.active
	var was_mode: String = Server.world_mode
	Server.client = fake
	Server.active = true                                         # (PlayerData.move's server path)
	Server.world_mode = "1.65"
	var ex: ExchangeWindow = v.ui.exchange
	var ok := true
	# 1. companion gear used a moment ago; the broker greets, the listing chest opens with what the owner had in it
	CompanionNet.asked_gear_at = Time.get_ticks_msec()
	Server._exchange_text("Welcome to the Realm Exchange. These are real items sold by inhabitants of your realm.")
	Server.on_inventory({"window": 5, "items": [_item(150, "bronze studded vest", 1), _item(157, "Lion Embossed Vambraces", 2),
			_item(158, "Melodic Lute", 3)]})
	await get_tree().process_frame
	var names := []
	for s in Server.listings:
		names.append(String(ItemDb.item(String(Server.listings[s])).get("name", "")))
	var c1: bool = Server.listings.size() == 3 and ex.visible and ex.tab == "listings" and Server.chest_owner == "exchange"
	ok = ok and c1
	print("[drop] 1. chest after companion gear: %s, window shown %s on %s, slots owner %s  %s" % [names, ex.visible,
			ex.tab, Server.chest_owner, "OK" if c1 else "WRONG"])
	# 2. a backpack item dropped on row 3 (slot 152)
	var bag := 40
	PlayerData.inventory[bag] = {"id": "srv:%d" % bag, "count": 1}
	ItemDb.add_runtime("srv:%d" % bag, {"id": "srv:%d" % bag, "name": "Lion Embossed Vambraces", "level": 20, "icon": 0,
			"server": true})
	var data := {"kind": "item", "slot": bag, "id": "srv:%d" % bag, "icon": 0, "name": "Lion Embossed Vambraces"}
	fake.calls.clear()
	ex._rows[2]._drop_data(Vector2.ONE, data)
	var sent := fake.calls.duplicate()
	PlayerData.inventory.erase(bag)                              # (the server's update: the bag slot empty ...)
	Server.on_inventory({"window": 0, "items": [_item(152, "Lion Embossed Vambraces", 2)]})   # ... the chest slot filled
	await get_tree().process_frame
	await get_tree().process_frame
	var box := _price_box(ex)
	var c2a: bool = sent == ["MoveItem 40->152 x1"] and Server.listings.has(152) and box != null
	ok = ok and c2a
	print("[drop] 2. drop on row 3: sent %s, listed %s, price box %s  %s" % [sent, Server.listings.has(152), box != null,
			"OK" if c2a else "WRONG"])
	if box:
		var fields := box.find_children("*", "LineEdit", true, false)
		(fields[1] as LineEdit).text = "2"                       # 2 gold (platinum, gold, silver, copper)
		fake.calls.clear()
		for b in box.find_children("*", "Button", true, false):
			if (b as Button).text == "LIST IT":
				(b as Button).pressed.emit()
		await get_tree().process_frame
		var c2b: bool = fake.calls == ["SetMarketPrice 2 20000", "&whisper duration 2 48"]
		ok = ok and c2b
		print("[drop] 2. LIST IT at 2 gold: %s  %s" % [fake.calls, "OK" if c2b else "WRONG"])
	# 3. the companion GIVE ITEMS window right after a companion gear click: not shown, the chest untouched
	CompanionNet.asked_gear_at = Time.get_ticks_msec()
	var before := Server.listings.size()
	var vault_opened := [false]
	var hook := func(_t, _f, _n): vault_opened[0] = true
	Server.vault_open.connect(hook)
	Server.on_inventory({"window": 4, "items": [_item(150, "Companion Sword", 9)]})
	Server.vault_open.disconnect(hook)
	var c3: bool = Server.listings.size() == before and not vault_opened[0]
	ok = ok and c3
	print("[drop] 3. companion GIVE ITEMS window: listings %d -> %d, a vault window opened %s  %s" % [before,
			Server.listings.size(), vault_opened[0], "OK" if c3 else "WRONG"])
	# 4. a vault opened since: nothing moves until the broker's chest comes back
	Server.chest_owner = "vault"
	PlayerData.inventory[bag] = {"id": "srv:%d" % bag, "count": 1}
	fake.calls.clear()
	ex._rows[5]._drop_data(Vector2.ONE, data)
	await get_tree().create_timer(0.6).timeout
	var waiting := fake.calls.duplicate()
	Server.on_inventory({"window": 5, "items": [_item(150, "bronze studded vest", 1), _item(152, "Lion Embossed Vambraces", 2),
			_item(157, "Lion Embossed Vambraces", 2), _item(158, "Melodic Lute", 3)]})
	await get_tree().create_timer(0.5).timeout
	var c4: bool = waiting == ["&whisper manage my listings"] and fake.calls == ["&whisper manage my listings", "MoveItem 40->155 x1"]
	ok = ok and c4
	print("[drop] 4. vault opened since: before the chest %s, after %s  %s" % [waiting, fake.calls, "OK" if c4 else "WRONG"])
	# 5. "that slot already holds a listing": the chest asked for again
	fake.calls.clear()
	Server._exchange_text("The Realm Exchange could not do that: that slot already holds a listing; drop it on an empty one.")
	await get_tree().process_frame
	var c5: bool = fake.calls == ["&whisper manage my listings"]
	ok = ok and c5
	print("[drop] 5. 'already holds a listing': %s  %s" % [fake.calls, "OK" if c5 else "WRONG"])
	print("[drop] %s" % ("PASS" if ok else "FAIL"))
	Server.active = was_active
	Server.world_mode = was_mode
	get_tree().quit()
