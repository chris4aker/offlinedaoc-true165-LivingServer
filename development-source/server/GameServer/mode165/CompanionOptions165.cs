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
    /// </summary>
    public static class CompanionOptions165
    {
        public const byte KindCharm = 1, KindPet = 2, KindSubPet = 3, KindSong = 4;
        public static readonly HashSet<string> Verbs = ["charm", "pet", "subpet", "song"];

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
        public static string PetLine(GameBot bot) => Get(bot, "pet");

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

        /// <summary>The pets the companion can summon, one per spell line, by that line's best learned summon.</summary>
        private static List<(string Line, string Name)> PetLines(GameBot bot) =>
            AutonomousPetSupport.MainPetSummons(bot)
                .Where(entry => entry.Line != null)
                .GroupBy(entry => entry.Line.KeyName)
                .Select(group => (group.Key, group.OrderByDescending(entry => entry.Spell.Level).First().Spell.Name))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToList();

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
            if (cls is eCharacterClass.Cabalist or eCharacterClass.Enchanter)
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
