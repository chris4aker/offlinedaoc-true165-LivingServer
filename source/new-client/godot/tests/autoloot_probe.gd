extends Node
## Offline check (owner 2026-11-05: an AUTO LOOT option, default off): the setting starts off; at a world entry the
## client tells the server "/autoloot off quiet" (the server keeps it per session only), and "/autoloot on quiet"
## when it is switched on. A stand-in client records what would be sent.
##   --skip-login --zone=N --world-mode=1.65 --probe=autoloot_probe


class FakeClient extends Node:
	var sent: Array = []

	func Command(t: String) -> void:
		sent.append(t)

	func PlayerInit() -> void:
		sent.append("PlayerInit")

	func GameOpen() -> void:
		sent.append("GameOpen")


func _ready() -> void:
	await get_tree().create_timer(4.0).timeout
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)
	if cfg.has_section_key("player", "autoloot"):
		cfg.erase_section_key("player", "autoloot")         # (a fresh install: no setting yet)
		cfg.save(UiArt.settings_path)
	var fake := FakeClient.new()
	add_child(fake)
	var was_client = Server.client
	var was_active := Server.active
	var was_playing := Server.playing
	Server.client = fake
	Server.active = true
	Server.playing = false
	var default_on := Loot.autoloot()
	Server.zone_ready()                                       # a world entry
	var at_entry := fake.sent.duplicate()
	fake.sent.clear()
	Loot.set_autoloot(true)
	var on_change := fake.sent.duplicate()
	var saved_on := Loot.autoloot()
	fake.sent.clear()
	Server.playing = false
	Server.zone_ready()                                       # the next entry repeats it
	var next_entry := fake.sent.duplicate()
	Loot.set_autoloot(false)
	Server.client = was_client
	Server.active = was_active
	Server.playing = was_playing
	print("[autoloot] default on: %s; entry sent %s; switched on sent %s (saved %s); next entry %s" % [default_on,
			at_entry, on_change, saved_on, next_entry])
	var ok: bool = not default_on and at_entry == ["PlayerInit", "GameOpen", "&autoloot off quiet"] \
			and on_change == ["&autoloot on quiet"] and saved_on and next_entry.has("&autoloot on quiet")
	print("[autoloot] RESULT ", "PASS" if ok else "FAIL")
	get_tree().quit()
