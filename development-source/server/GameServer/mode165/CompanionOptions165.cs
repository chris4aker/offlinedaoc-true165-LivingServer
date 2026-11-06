using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// A hired companion's own choices, set by the player in the new client's Pets / Songs tab and kept in the hire
    /// record until changed (owner 2026-11-05, reset-day S33 / C17): charm on or off (Sorcerer, Minstrel and any class
    /// that has learned a charm; a hired Hunter keeps its wolf), the Cabalist / Enchanter pet, the Bonedancer's sub-pets
    /// (their level total within the commander's 75), and the song or chant held in a fight, standing around and
    /// travelling (Bard, Minstrel, Skald, Paladin). "Automatic" is the bot's own choice. Gamebots ignore all of this.
    ///
    /// Protocol (hire op 14, one per option row, then one with kind 0xFF): [0] row, [1] kind (1 charm, 2 pet, 3 sub-pet,
    /// 4 song), [2] slot, [3] chosen index, [4] number of choices, [5..6] used u16, [7..8] cap u16, [9..] choice names,
    /// each zero-ended. Verbs: charm (row) on|off, pet (row) (i), subpet (row) (slot 1-3) (i), song (row) (slot) (i)
    /// (song slot 1 in a fight, 2 standing around, 3 travelling).
    ///
    /// Kinds 5 and up are generic (client protocol, 2026-11-05): names[0] is the row's caption, the rest the choices,
    /// [3] the chosen index into the choices without the caption; the client sends "option (row) (kind) (slot) (i)".
    /// Each is one class AI choice from the 2026-11-05 class audit (see Generics); choice 0 is always "Automatic".
    /// </summary>
    public static class CompanionOptions165
    {
        public const byte KindCharm = 1, KindPet = 2, KindSubPet = 3, KindSong = 4;
        public const byte KindMainShroom = 19, KindShroomDamage = 20, KindShroomVents = 21, KindShroomAblative = 22,
            KindShroomSpore = 23, KindShroomBurst = 24, KindShroomHeal = 25;
        public const byte KindAggro = 5, KindHealAt = 6, KindDebuff = 7, KindBolts = 8, KindBerserk = 9, KindGuard = 10,
            KindHunterPet = 11, KindStealthOpen = 12, KindHealerRole = 13, KindShamanRole = 14, KindFriarRole = 15,
            KindLifeTransfer = 16, KindAmnesia = 17, KindPerform = 18;
        public static readonly HashSet<string> Verbs = ["charm", "pet", "subpet", "song", "option"];

        /// <summary>A generic AI choice row: its kind, stored key, caption, choices (0 = Automatic) and who gets it.</summary>
        private sealed record Generic(byte Kind, string Key, string Caption, string[] Choices, Func<GameBot, bool> Applies);

        private static readonly Generic[] Generics =
        [
            // melee: taunt styles to hold a monster, or detaunt styles to give it back to the tank
            new(KindAggro, "aggro", "Aggro", ["Automatic", "Hold it (taunt)", "Shed it (detaunt)"],
                bot => bot.StylesTaunt?.Count > 0 || bot.StylesDetaunt?.Count > 0),
            new(KindHealAt, "healat", "Heal at", ["Automatic", "Early (90%)", "Late (50%)"], bot => bot.CanCastHealSpells),
            new(KindDebuff, "debuff", "Debuff before nuking", ["Automatic", "Always", "Never"],
                bot => (bot.HarmfulSpells ?? []).Concat(bot.InstantHarmfulSpells ?? []).Any(BotCasterPriority.IsDebuff)),
            new(KindBolts, "bolts", "Bolts", ["Automatic", "Nukes only"], bot => bot.CanCastBolts),
            new(KindBerserk, "berserk", "Berserk", ["Automatic (hard fights)", "On cooldown", "Off"],
                bot => bot.HasAbility(Abilities.Berserk)),
            new(KindGuard, "guard", "Guard and protect", ["Automatic", "You", "The healer", "Off"],
                bot => bot.HasAbility(Abilities.Guard) || bot.HasAbility(Abilities.Protect) || bot.HasAbility(Abilities.Intercept)),
            new(KindHunterPet, "hunterpet", "Pet", ["Automatic (on)", "Off"],
                bot => (eCharacterClass)bot.CharacterClass.ID == eCharacterClass.Hunter),
            // assassins: hide and open with Backstab / Perforate Artery (BotBrain.StealthOpener)
            new(KindStealthOpen, "stealthopen", "Open from stealth", ["Automatic (on)", "Off"],
                bot => BotPoisonSupply.IsAssassin((eCharacterClass)bot.CharacterClass.ID) &&
                       bot.Styles?.Any(style => style.StealthRequirement) == true),
            // Roles (one stored key "role"; the rows differ by class). Prima / period forums: a Cleric either smites and
            // does crowd control or heals; a Shaman buffs, heals and rezzes from the back; a Friar is the off-healer.
            new(KindHealerRole, "role", "Role", ["Automatic (heal and buff)", "Heal first, attack when nobody is hurt",
                    "Attack, heal only emergencies"],
                bot => (eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Cleric or eCharacterClass.Druid or
                    eCharacterClass.Healer),
            new(KindShamanRole, "role", "Role", ["Automatic (melee and cast)", "Stay back and heal", "Caster, no melee"],
                bot => (eCharacterClass)bot.CharacterClass.ID == eCharacterClass.Shaman),
            new(KindFriarRole, "role", "Role", ["Automatic (fight and heal)", "Off-healer (heals under 40% in a fight)",
                    "Main healer (stays back)"],
                bot => (eCharacterClass)bot.CharacterClass.ID == eCharacterClass.Friar),
            new(KindLifeTransfer, "lifetransfer", "Life transfer", ["Automatic (groupmate under 35%)", "Off"],
                bot => bot.Spells?.Any(spell => spell.SpellType == eSpellType.LifeTransfer) == true),
            new(KindAmnesia, "amnesia", "Amnesia", ["Automatic (give monsters back to my pet)", "Off"],
                bot => bot.Spells?.Any(spell => spell.SpellType == eSpellType.Amnesia) == true),
            // Performers (Prima: a Bard's group role is to prevent downtime - the endurance song in battle, mez every add,
            // heal the Druid; a Minstrel plays a "passive game" of songs and shouts or an "active game" in melee; period
            // forums: the "Battlebard" melee build)
            // Animist shrooms (owner 2026-11-05): which main shroom, and each kind of field shroom on or off - only the
            // kinds this Animist knows are offered (BotAnimistPolicy.KnownKinds)
            new(KindMainShroom, "mainshroom", "Main shroom", ["Automatic", "Damage", "Taunt"],
                bot => BotAnimistPolicy.KnownKinds(bot).Contains("damagemain") && BotAnimistPolicy.KnownKinds(bot).Contains("tauntmain")),
            new(KindShroomDamage, "shroomdamage", "Damage shrooms", ["Automatic (on)", "Off"],
                bot => BotAnimistPolicy.KnownKinds(bot).Contains("damage")),
            new(KindShroomVents, "shroomvents", "Resist vents", ["Automatic (on)", "Off"],
                bot => BotAnimistPolicy.KnownKinds(bot).Contains("vents")),
            new(KindShroomAblative, "shroomablative", "Ablative shrooms", ["Automatic (on)", "Off"],
                bot => BotAnimistPolicy.KnownKinds(bot).Contains("ablative")),
            new(KindShroomSpore, "shroomspore", "Debuff spores", ["Automatic (on)", "Off"],
                bot => BotAnimistPolicy.KnownKinds(bot).Contains("spore")),
            new(KindShroomBurst, "shroomburst", "Shroom burst", ["Automatic (on)", "Off"],
                bot => BotAnimistPolicy.KnownKinds(bot).Contains("burst")),
            new(KindShroomHeal, "shroomheal", "Heal the main shroom", ["Automatic (on)", "Off"],
                bot => BotAnimistPolicy.KnownKinds(bot).Contains("healmain")),
            new(KindPerform, "perform", "Fight", ["Automatic", "Support (stay back: songs, spells, no melee)", "Melee"],
                bot => (eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Bard or eCharacterClass.Minstrel or
                    eCharacterClass.Skald),
        ];

        /// <summary>The player's choice for a generic AI option (0 = Automatic; always 0 for gamebots).</summary>
        public static int Choice(GameBot bot, string key) =>
            Get(bot, key) is { } v && int.TryParse(v, out int i) ? i : 0;

        // ---- storage: "key=value;key=value" in OfflineHiredCompanionRecord.Options

        private static Dictionary<string, string> Read(OfflineHiredCompanionRecord hired)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in (hired?.Options ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0)
                    map[pair[..eq]] = pair[(eq + 1)..];
            }
            return map;
        }

        private static string Get(GameBot bot, string key) =>
            HiredCompanions165.HiredRecord(bot) is { } hired && Read(hired).TryGetValue(key, out string value) ? value : null;

        private static void Set(OfflineHiredCompanionRecord hired, string key, string value)
        {
            Dictionary<string, string> map = Read(hired);
            if (value == null) map.Remove(key); else map[key] = value;
            hired.Options = string.Join(';', map.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
            HiredCompanions165.SaveRecord(hired);
        }

        // ---- what the AI reads

        /// <summary>False only when the player switched a hired companion's charming off.</summary>
        public static bool CharmAllowed(GameBot bot) => Get(bot, "charm") != "0";

        /// <summary>The spell line of the pet the player chose (Cabalist, Enchanter), or null for the bot's own choice.</summary>
        public static string PetLine(GameBot bot) => HasPetChoice(bot) ? Get(bot, "pet") : null;

        /// <summary>
        /// The classes whose pets are a real choice (owner 2026-11-05): the Cabalist's simulacrums and the Enchanter's
        /// Underhill pets each have their own perks, the Theurgist picks an element. A Bonedancer's commander, a
        /// Spiritmaster's or a Necromancer's pet is always the best rank - no choice (a choice stored by the build that
        /// offered one is ignored).
        /// </summary>
        public static bool HasPetChoice(GameBot bot) =>
            bot?.CharacterClass != null &&
            (eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Cabalist or eCharacterClass.Enchanter or eCharacterClass.Theurgist;

        /// <summary>The song or chant type chosen for a situation (1 fight, 2 standing around, 3 travelling), or null.</summary>
        public static eSpellType? Song(GameBot bot, int slot) =>
            Get(bot, $"song{slot}") is { } v && int.TryParse(v, out int type) ? (eSpellType)type : null;

        /// <summary>The Bonedancer's sub-pet plan: per slot null (bot's choice), -1 (none) or a sub-pet type.</summary>
        public static int?[] SubPets(GameBot bot)
        {
            var plan = new int?[3];
            for (int i = 0; i < 3; i++)
                plan[i] = Get(bot, $"sub{i + 1}") is { } v && int.TryParse(v, out int type) ? type : null;
            return plan;
        }

        // ---- what the window offers

        private sealed record Row(byte Kind, byte Slot, int Chosen, List<string> Choices, int Used, int Cap);

        private static bool CanCharm(GameBot bot) => bot.CharacterClass != null &&
            ((eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Sorcerer or eCharacterClass.Minstrel ||
             (eCharacterClass)bot.CharacterClass.ID != eCharacterClass.Hunter && Charm165.LearnedCharm(bot) != null);

        /// <summary>
        /// The pets the player can choose (AI Options pet row; stored as the choice key). Cabalist and Enchanter: every
        /// summon the companion has learned - each is its own pet with its own creature and perks (owner 2026-11-05: Amber,
        /// Ruby, Sapphire... simulacrums; Underhill Friend ... Zealot), keyed "spell:&lt;id&gt;". Theurgist: one choice
        /// per element line (earth, ice, air), keyed by the line.
        /// </summary>
        private static List<(string Line, string Name)> PetLines(GameBot bot)
        {
            if ((eCharacterClass)bot.CharacterClass.ID == eCharacterClass.Theurgist)
                return AutonomousPetSupport.TheurgistSummons(bot)
                    .Where(entry => entry.Line != null)
                    .GroupBy(entry => entry.Line.KeyName)
                    .Select(group => (group.Key, group.OrderByDescending(entry => entry.Spell.Level).First().Spell.Name))
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .ToList();
            return AutonomousPetSupport.MainPetSummons(bot)
                .GroupBy(entry => entry.Spell.ID)
                .Select(group => group.First())
                .OrderBy(entry => entry.Spell.Level).ThenBy(entry => entry.Spell.ID)
                .Select(entry => ($"spell:{entry.Spell.ID}", entry.Spell.Name))
                .ToList();
        }

        private static readonly BdSubPet.SubPetType[] SubPetTypes =
            [BdSubPet.SubPetType.Melee, BdSubPet.SubPetType.Archer, BdSubPet.SubPetType.Caster,
             BdSubPet.SubPetType.Debuffer, BdSubPet.SubPetType.Healer, BdSubPet.SubPetType.Buffer];

        /// <summary>The sub-pet kinds the Bonedancer can summon now: the name of its best rank (the minion spell, e.g.
        /// Bone Guard) and the level that rank comes at.</summary>
        private static List<(BdSubPet.SubPetType Type, int Level, string Name)> SubPetKinds(GameBot bot) =>
            SubPetTypes.Select(type => AutonomousPetSupport.MinionSummons(bot)
                    .Where(entry => (BdSubPet.SubPetType)entry.Spell.DamageType == type)
                    .Select(entry => (Type: type, Level: AutonomousPetSupport.BonedancerSubPetLevel(bot.Level, entry.Spell), entry.Spell.Name))
                    .OrderByDescending(entry => entry.Level).ThenBy(entry => entry.Name)
                    .FirstOrDefault())
                .Where(entry => entry.Level > 0)
                .ToList();

        /// <summary>The songs or chants the companion holds, one per kind (its best rank), in a fixed order.</summary>
        private static List<(eSpellType Type, string Name)> Songs(GameBot bot) =>
            (bot.MiscSpells ?? []).Concat(bot.InstantMiscSpells ?? [])
                .Where(spell => spell != null && spell.Level <= bot.Level && spell.IsPulsing && !spell.IsHarmful &&
                                BotBrain.IsMaintainableClassBuff(spell))
                .GroupBy(spell => spell.SpellType)
                .Select(group => (group.Key, group.OrderByDescending(spell => spell.Level).First().Name))
                .OrderBy(entry => (int)entry.Key)
                .ToList();

        private static bool Performs(GameBot bot) => bot.CharacterClass != null &&
            (eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Bard or eCharacterClass.Minstrel or eCharacterClass.Skald;

        private static int SubPetCapacity(GameBot bot) =>
            bot.ControlledBrain?.Body is CommanderPet commander ? Math.Min(3, commander.SubPetCapacity)
                : bot.Level >= 45 ? 3 : bot.Level >= 30 ? 2 : bot.Level >= 15 ? 1 : 0;

        private static int PlanLevels(int?[] plan, List<(BdSubPet.SubPetType Type, int Level, string Name)> kinds) =>
            plan.Sum(slot => slot is int type && type >= 0 ? kinds.FirstOrDefault(k => (int)k.Type == type).Level : 0);

        private static List<Row> Rows(GameBot bot)
        {
            var rows = new List<Row>();
            if (bot?.CharacterClass == null)
                return rows;
            var cls = (eCharacterClass)bot.CharacterClass.ID;
            if (CanCharm(bot))
                rows.Add(new Row(KindCharm, 0, CharmAllowed(bot) ? 1 : 0, ["Off", "On"], 0, 0));
            if (HasPetChoice(bot))
            {
                var pets = PetLines(bot);
                if (pets.Count > 1)
                {
                    string chosen = PetLine(bot);
                    rows.Add(new Row(KindPet, 0, pets.FindIndex(p => p.Line == chosen) + 1,
                        ["Automatic", .. pets.Select(p => p.Name)], 0, 0));
                }
            }
            if (cls == eCharacterClass.Bonedancer)
            {
                var kinds = SubPetKinds(bot);
                int capacity = SubPetCapacity(bot);
                if (kinds.Count > 0 && capacity > 0)
                {
                    int?[] plan = SubPets(bot);
                    int used = PlanLevels(plan.Take(capacity).ToArray(), kinds);
                    List<string> names = ["Automatic", "None", .. kinds.Select(k => $"{k.Name} ({k.Level})")];
                    for (int slot = 0; slot < capacity; slot++)
                    {
                        int chosen = plan[slot] switch
                        {
                            null => 0,
                            -1 => 1,
                            int type => kinds.FindIndex(k => (int)k.Type == type) is var i && i >= 0 ? i + 2 : 0,
                        };
                        rows.Add(new Row(KindSubPet, (byte)(slot + 1), chosen, names, used, 75));
                    }
                }
            }
            if (Performs(bot) || cls == eCharacterClass.Paladin)
            {
                var songs = Songs(bot);
                if (songs.Count > 0)
                {
                    List<string> names = ["Automatic", .. songs.Select(s => s.Name)];
                    for (int slot = 1; slot <= (Performs(bot) ? 3 : 2); slot++)
                    {
                        int index = Song(bot, slot) is { } type ? songs.FindIndex(s => s.Type == type) + 1 : 0;
                        rows.Add(new Row(KindSong, (byte)slot, Math.Max(0, index), names, 0, 0));
                    }
                }
            }
            foreach (Generic option in Generics)
                if (option.Applies(bot))
                    rows.Add(new Row(option.Kind, 0, Math.Clamp(Choice(bot, option.Key), 0, option.Choices.Length - 1),
                        [option.Caption, .. option.Choices], 0, 0));
            return rows;
        }

        public static void Send(GamePlayer player, int row, GameBot bot)
        {
            foreach (Row option in Rows(bot))
            {
                var body = new List<byte> { (byte)row, option.Kind, option.Slot, (byte)Math.Clamp(option.Chosen, 0, 255),
                    (byte)Math.Min(option.Choices.Count, 255),
                    (byte)option.Used, (byte)(option.Used >> 8), (byte)option.Cap, (byte)(option.Cap >> 8) };
                foreach (string name in option.Choices.Take(255))
                {
                    body.AddRange(Encoding.ASCII.GetBytes(name.Length > 60 ? name[..60] : name));
                    body.Add(0);
                }
                Hire165.Send(player, 14, body.ToArray());
            }
            Hire165.Send(player, 14, [(byte)row, 0xFF]);
        }

        // ---- the verbs

        /// <summary>Applies a choice; null when done, otherwise the problem to show.</summary>
        public static string Handle(GameBot bot, OfflineHiredCompanionRecord hired, string verb, string[] args)
        {
            if (bot == null || hired == null)
                return "Summon them first: they are at the inn.";
            string Arg(int i) => args.Length > i ? args[i] : string.Empty;
            int Int(int i) => int.TryParse(Arg(i), out int v) ? v : -1;
            switch (verb)
            {
                case "charm":
                {
                    if (!CanCharm(bot)) return $"{bot.Name} does not charm.";
                    bool on = Arg(3).Equals("on", StringComparison.OrdinalIgnoreCase) || Arg(3).Equals("yes", StringComparison.OrdinalIgnoreCase);
                    Set(hired, "charm", on ? null : "0");
                    // switched off with a charmed pet: it is let go now
                    if (!on && bot.ControlledBrain?.Body is GameNPC charmed && charmed is not GameSummonedPet)
                        bot.CommandNpcRelease();
                    return null;
                }
                case "pet":
                {
                    var pets = PetLines(bot);
                    int i = Int(3);
                    if (i < 0 || i > pets.Count) return "Choose a pet.";
                    string line = i == 0 ? null : pets[i - 1].Line;
                    if (line == PetLine(bot)) return null;
                    Set(hired, "pet", line);
                    if (line != null && bot.ControlledBrain != null)
                        bot.CommandNpcRelease();   // the chosen pet is summoned in its place
                    return null;
                }
                case "subpet":
                {
                    int slot = Int(3), i = Int(4);
                    var kinds = SubPetKinds(bot);
                    if (slot is < 1 or > 3 || i < 0 || i > kinds.Count + 1) return "Choose a sub-pet.";
                    int?[] plan = SubPets(bot);
                    plan[slot - 1] = i == 0 ? null : i == 1 ? -1 : (int)kinds[i - 2].Type;
                    if (PlanLevels(plan, kinds) > 75) return "Those sub-pets together are above the commander's level 75.";
                    Set(hired, $"sub{slot}", plan[slot - 1]?.ToString());
                    // the commander's minions are let go and summoned again to the plan
                    if (bot.ControlledBrain?.Body is CommanderPet commander)
                        foreach (IControlledBrain minion in commander.ControlledNpcList?.Where(m => m?.Body != null).ToArray() ?? [])
                            minion.Body.Die(null);
                    return null;
                }
                case "option":
                {
                    int kind = Int(3), i = Int(5);
                    Generic option = Generics.FirstOrDefault(g => g.Kind == kind);
                    if (option == null || !option.Applies(bot) || i < 0 || i >= option.Choices.Length)
                        return "Choose an option.";
                    Set(hired, option.Key, i == 0 ? null : i.ToString());
                    // the Hunter's pet switched off: the wolf that is out goes now
                    if (option.Kind == KindHunterPet && i == 1 && bot.ControlledBrain != null)
                        bot.CommandNpcRelease();
                    return null;
                }
                case "song":
                {
                    int slot = Int(3), i = Int(4);
                    var songs = Songs(bot);
                    if (slot is < 1 or > 3 || i < 0 || i > songs.Count) return "Choose a song.";
                    Set(hired, $"song{slot}", i == 0 ? null : ((int)songs[i - 1].Type).ToString());
                    return null;
                }
            }
            return null;
        }
    }
}
