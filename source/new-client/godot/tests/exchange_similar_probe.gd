extends Node
## Offline check (reset day C16, 2026-11-05, the client-only parts): the listing chest page's x/100 count, and the
## price box's SEARCH SIMILAR - the box closes, BROWSE opens and searches the item's name (a stand-in client records
## the MarketSearch).
##   --skip-login --zone=N --world-mode=1.65 --probe=exchange_similar_probe


class FakeClient extends Node:
	var searches: Array = []
	var commands: Array = []

	func MarketSearch(f: Dictionary) -> void:
		searches.append(f)

	func Command(t: String) -> void:
		commands.append(t)

	func SetMarketPrice(_place: int, _copper: int) -> void:
		pass


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(5.0).timeout
	var fake := FakeClient.new()
	add_child(fake)
	var was = Server.client
	Server.client = fake
	var ex: ExchangeWindow = v.ui.exchange
	var slot := Server.CHEST_FIRST + 3
	ItemDb.add_runtime("srvc:%d" % slot, {"id": "srvc:%d" % slot, "name": "Studded Boots", "level": 20, "icon": 0,
			"price": 0, "server": true})
	Server.listings = {slot: "srvc:%d" % slot}
	ex.show_window()
	ex.show_tab("listings")
	await get_tree().process_frame
	print("[similar] chest label '%s', status '%s'" % [ex._chest_label.text, ex._status.text])
	var ok := ex._chest_label.text.ends_with("1/100")
	ex.ask_price(slot)
	await get_tree().process_frame
	var btn: Button = null
	for b in ex.get_parent().find_children("*", "Button", true, false):
		if (b as Button).text == "SEARCH SIMILAR":
			btn = b
	print("[similar] button found: ", btn != null)
	ok = ok and btn != null
	if btn:
		btn.pressed.emit()
		await get_tree().process_frame
		print("[similar] tab %s, name '%s', searches %s" % [ex.tab, ex._name.text, fake.searches])
		ok = ok and ex.tab == "browse" and fake.searches.size() == 1 and String(fake.searches[0].get("name", "")) == "Studded Boots"
	# sorting: PRICE clicked twice - asc, then desc; the search runs again from page 0
	fake.searches.clear()
	ex.sort_by("price")
	ex.sort_by("price")
	ex.sort_by("level")
	print("[similar] sort whispers %s, searches again %d, level arrow up %s" % [fake.commands, fake.searches.size(),
			ex._arrows["level"].up])
	ok = ok and fake.commands == ["&whisper sort price asc", "&whisper sort price desc", "&whisper sort level asc"] 			and fake.searches.size() == 3 and ex._arrows["level"].visible and not ex._arrows["price"].visible
	# a 24-hour listing priced at 5 gold
	fake.commands.clear()
	ex.show_tab("listings")
	ex.ask_price(slot)
	await get_tree().process_frame
	for b in ex.get_parent().find_children("*", "Button", true, false):
		if (b as Button).text == "24 HOURS":
			(b as Button).pressed.emit()
	for f in ex.get_parent().find_children("*", "LineEdit", true, false):
		if (f as LineEdit).get_parent() and (f as LineEdit).placeholder_text == "0" and (f as LineEdit).position.x < 60:
			(f as LineEdit).text = "5"                                           # (the platinum box's left: gold next)
	var boxes := []
	for f in ex.get_parent().find_children("*", "LineEdit", true, false):
		if (f as LineEdit).placeholder_text == "0" and (f as LineEdit).is_visible_in_tree():
			boxes.append(f)
	boxes.sort_custom(func(a, b): return a.position.x < b.position.x)
	if boxes.size() == 4:
		(boxes[0] as LineEdit).text = ""
		(boxes[1] as LineEdit).text = "5"
	for b in ex.get_parent().find_children("*", "Button", true, false):
		if (b as Button).text == "LIST IT":
			(b as Button).pressed.emit()
	print("[similar] after LIST IT at 24 hours: ", fake.commands)
	ok = ok and fake.commands.has("&whisper duration 3 24")
	# the time left (op 15): slot 3 has 47 h 10 min, slot 4 ran out (0), slot 5 has 35 min
	Server.listings = {slot: "srvc:%d" % slot, slot + 1: "srvc:%d" % slot, slot + 2: "srvc:%d" % slot}
	var b := PackedByteArray([0, 0x48, 1, 15, 3, 3, 0, 0, 4, 0, 0, 5, 35, 0])
	b.encode_u16(6, 47 * 60 + 10)
	var cn := CompanionNet.new()
	add_child(cn)
	cn.handle(b)
	ex.show_tab("listings")
	await get_tree().process_frame
	var times := []
	var prices := []
	for i in 3:
		var r = ex._rows[3 + i]
		times.append(r._quality.text)
		prices.append(r._price.text)
	print("[similar] time left %s, prices %s, header '%s'" % [times, prices, ex._header_quality.text])
	ok = ok and times == ["48H", "-", "35M"] and prices[1] == "NOT FOR SALE" and ex._header_quality.text == "LEFT"
	Server.client = was
	Server.listings = {}
	Server.listing_minutes = {}
	ex.show_tab("browse")
	await get_tree().create_timer(0.3).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(String(v.args.get("out", "user://")) + "/exchange_browse.png")
	print("[similar] RESULT ", "PASS" if ok else "FAIL")
	get_tree().quit()
