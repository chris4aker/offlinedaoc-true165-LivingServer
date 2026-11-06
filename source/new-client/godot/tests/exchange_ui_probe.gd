extends Node
## Offline check (owner 2026-11-06, the Realm Exchange's MY LISTINGS): 100 slots as five pages of twenty on a scroll
## bar (no chest pages), a refused drop in red under the list, CLAIM MONEY / NOTHING TO CLAIM, and a chest page past
## the first (from before the 100-slot cap) sent back to the first. A stand-in client records the commands.
##   --skip-login --zone=N --world-mode=1.65 --probe=exchange_ui_probe --out=<dir>


class FakeClient extends Node:
	var commands: Array = []

	func MarketSearch(_f: Dictionary) -> void:
		pass

	func Command(t: String) -> void:
		commands.append(t)

	func SetMarketPrice(_place: int, _copper: int) -> void:
		pass


func _shot(name: String) -> void:
	await get_tree().create_timer(0.4).timeout
	await RenderingServer.frame_post_draw
	var v := get_tree().current_scene
	get_viewport().get_texture().get_image().save_png("%s/%s.png" % [String(v.args.get("out", "user://")), name])


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(5.0).timeout
	var fake := FakeClient.new()
	add_child(fake)
	Server.client = fake
	var ex: ExchangeWindow = v.ui.exchange
	var listed := {}
	for i in [3, 44, 98]:                                     # slots 4, 45, 99: pages 1, 3, 5
		var slot: int = Server.CHEST_FIRST + int(i)
		ItemDb.add_runtime("srvc:%d" % slot, {"id": "srvc:%d" % slot, "name": "Listed Item %d" % (i + 1), "level": 20,
				"icon": 0, "price": 0, "server": true})
		listed[slot] = "srvc:%d" % slot
	Server.listings = listed
	Server.proceeds = 0
	Server.listing_page = 0
	ex.show_window()
	ex.show_tab("listings")
	await get_tree().process_frame
	var ok := true
	var c1: bool = ex._claim.text == "NOTHING TO CLAIM" and ex._claim.disabled and ex._scroll.visible
	ok = ok and c1
	print("[exui] page 1: label '%s', status '%s', claim '%s' disabled %s, scroll visible %s  %s" % [ex._page_label.text,
			ex._status.text, ex._claim.text, ex._claim.disabled, ex._scroll.visible, "OK" if c1 else "WRONG"])
	await _shot("exui_page1")
	ex._scroll.value = 2                                      # the scroll bar to page 3
	await get_tree().process_frame
	var row_ids := []
	for i in ex._rows.size():
		if ex.id_at(i) != "":
			row_ids.append(ex.id_at(i))
	var c2: bool = ex._lpage == 2 and ex._page_label.text == "PAGE 3 OF 5" and row_ids == ["srvc:%d" % (Server.CHEST_FIRST + 44)]
	ok = ok and c2
	print("[exui] scrolled: page %d, label '%s', rows %s, status '%s'  %s" % [ex._lpage + 1, ex._page_label.text,
			row_ids, ex._status.text, "OK" if c2 else "WRONG"])
	await _shot("exui_page3")
	Server.proceeds = 12345
	Server._exchange_text("That item cannot be traded.")       # the broker's refusal of a drop
	await get_tree().process_frame
	var c3: bool = ex._status.text == "THAT ITEM CANNOT BE TRADED." and ex._claim.text == "CLAIM MONEY" and not ex._claim.disabled
	ok = ok and c3
	print("[exui] refused + money waiting: status '%s', proceeds '%s', claim '%s'  %s" % [ex._status.text,
			ex._proceeds.text, ex._claim.text, "OK" if c3 else "WRONG"])
	await _shot("exui_refused")
	Server._exchange_text("The Realm Exchange could not do that: you are too far from the broker.")
	await get_tree().process_frame
	var c5: bool = ex._status.text == "THE REALM EXCHANGE COULD NOT DO THAT: YOU ARE TOO FAR FROM THE BROKER."
	ok = ok and c5
	print("[exui] a refusal with its reason: '%s'  %s" % [ex._status.text, "OK" if c5 else "WRONG"])
	Server.listing_page = 1                                   # left on chest page 2 before the cap
	ex.refresh()
	await get_tree().process_frame
	var c4: bool = fake.commands.has("&whisper previous listing page")
	ok = ok and c4
	print("[exui] chest page 2 -> asked back: %s  %s" % [fake.commands, "OK" if c4 else "WRONG"])
	print("[exui] %s" % ("PASS" if ok else "FAIL"))
	get_tree().quit()
