extends Node
## Offline check (owner 2026-11-06: "dragging the item onto the realm exchange slots still doesn't do anything"): a
## backpack item dropped on a MY LISTINGS row - what the client sends (a stand-in client records MoveItem and the
## broker whispers): with the broker on its listings the move goes at once; with the broker still on its market
## search (BROWSE opened first) the client asks for its listings first, then moves; a tab clicked tells the broker.
##   --skip-login --zone=N --world-mode=1.65 --probe=exchange_drop_probe


class FakeClient extends Node:
	var calls: Array = []

	func MoveItem(from: int, to: int, count: int) -> void:
		calls.append("MoveItem %d->%d x%d" % [from, to, count])

	func Command(t: String) -> void:
		calls.append(t)

	func MarketSearch(_f: Dictionary) -> void:
		pass

	func SetMarketPrice(_place: int, _copper: int) -> void:
		pass


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(5.0).timeout
	var fake := FakeClient.new()
	add_child(fake)
	var was_active: bool = Server.active
	Server.client = fake
	Server.active = true                                         # (PlayerData.move's server path)
	var bag := 40                                                # the first backpack slot
	PlayerData.inventory[bag] = {"id": "srv:%d" % bag, "count": 1}
	ItemDb.add_runtime("srv:%d" % bag, {"id": "srv:%d" % bag, "name": "Lion Embossed Vambraces", "level": 20, "icon": 0,
			"server": true})
	Server.listings = {}
	var ex: ExchangeWindow = v.ui.exchange
	ex.show_window()
	ex.show_tab("listings")
	await get_tree().process_frame
	var data := {"kind": "item", "slot": bag, "id": "srv:%d" % bag, "icon": 0, "name": "Lion Embossed Vambraces"}
	var ok := true
	# 1. the broker on its listings: the move at once
	Server.exchange_mode = "listings"
	ex._rows[2]._drop_data(Vector2.ONE, data)
	var c1: bool = fake.calls == ["MoveItem 40->152 x1"] and ex._ask_price == 152
	ok = ok and c1
	print("[drop] broker on listings, drop on row 3: %s, price asked for %d  %s" % [fake.calls, ex._ask_price,
			"OK" if c1 else "WRONG"])
	# 2. the broker still on its market search: listings first, then the move
	fake.calls.clear()
	ex._ask_price = -1
	Server.exchange_mode = "browse"
	ex._rows[4]._drop_data(Vector2.ONE, data)
	var before := fake.calls.duplicate()
	await get_tree().create_timer(1.2).timeout
	var c2: bool = before == ["&whisper manage my listings"] and fake.calls == ["&whisper manage my listings", "MoveItem 40->154 x1"]
	ok = ok and c2
	print("[drop] broker on browse, drop on row 5: at once %s, after 1.2 s %s  %s" % [before, fake.calls,
			"OK" if c2 else "WRONG"])
	# 3. the tabs tell the broker
	fake.calls.clear()
	Server.exchange_mode = "listings"
	ex._tab_browse.pressed.emit()
	Server.exchange_mode = "browse"
	ex._tab_listings.pressed.emit()
	var c3: bool = fake.calls == ["&whisper browse the exchange", "&whisper manage my listings"] and ex.tab == "listings"
	ok = ok and c3
	print("[drop] tabs clicked: %s, tab %s  %s" % [fake.calls, ex.tab, "OK" if c3 else "WRONG"])
	print("[drop] %s" % ("PASS" if ok else "FAIL"))
	Server.active = was_active
	get_tree().quit()
