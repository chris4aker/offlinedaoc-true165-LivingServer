using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Generates era-valid identities for autonomous Classic + Shrouded Isles bots.
    /// This class never creates or spawns a bot; it only produces an identity when
    /// the population controller is explicitly asked to create one.
    /// </summary>
    public static class AutonomousBotIdentityGenerator
    {
        public sealed record Identity(string Name, eRealm Realm, eGender Gender, eCharacterClass CharacterClass, eRace Race);

        private sealed record ClassChoice(eCharacterClass CharacterClass, params eRace[] Races);

        private static readonly IReadOnlyDictionary<eRealm, ClassChoice[]> Choices =
            new Dictionary<eRealm, ClassChoice[]>
            {
                [eRealm.Albion] =
                [
                    new(eCharacterClass.Paladin, eRace.Avalonian, eRace.Briton, eRace.Highlander, eRace.Saracen),
                    new(eCharacterClass.Armsman, eRace.Avalonian, eRace.Briton, eRace.Highlander, eRace.Inconnu, eRace.Saracen),
                    new(eCharacterClass.Scout, eRace.Briton, eRace.Highlander, eRace.Inconnu, eRace.Saracen),
                    new(eCharacterClass.Minstrel, eRace.Briton, eRace.Highlander, eRace.Saracen),
                    new(eCharacterClass.Theurgist, eRace.Avalonian, eRace.Briton),
                    new(eCharacterClass.Cleric, eRace.Avalonian, eRace.Briton, eRace.Highlander),
                    new(eCharacterClass.Wizard, eRace.Avalonian, eRace.Briton),
                    new(eCharacterClass.Sorcerer, eRace.Avalonian, eRace.Briton, eRace.Inconnu, eRace.Saracen),
                    new(eCharacterClass.Infiltrator, eRace.Briton, eRace.Inconnu, eRace.Saracen),
                    new(eCharacterClass.Friar, eRace.Briton),
                    new(eCharacterClass.Mercenary, eRace.Avalonian, eRace.Briton, eRace.Highlander, eRace.Inconnu, eRace.Saracen),
                    new(eCharacterClass.Necromancer, eRace.Briton, eRace.Inconnu, eRace.Saracen),
                    new(eCharacterClass.Cabalist, eRace.Avalonian, eRace.Briton, eRace.Inconnu, eRace.Saracen),
                    new(eCharacterClass.Reaver, eRace.Briton, eRace.Inconnu, eRace.Saracen),
                ],
                [eRealm.Midgard] =
                [
                    new(eCharacterClass.Thane, eRace.Dwarf, eRace.Norseman, eRace.Troll),
                    new(eCharacterClass.Warrior, eRace.Dwarf, eRace.Kobold, eRace.Norseman, eRace.Troll, eRace.Valkyn),
                    new(eCharacterClass.Shadowblade, eRace.Kobold, eRace.Norseman, eRace.Valkyn),
                    new(eCharacterClass.Skald, eRace.Dwarf, eRace.Kobold, eRace.Norseman, eRace.Troll),
                    new(eCharacterClass.Hunter, eRace.Dwarf, eRace.Kobold, eRace.Norseman, eRace.Valkyn),
                    new(eCharacterClass.Healer, eRace.Dwarf, eRace.Norseman),
                    new(eCharacterClass.Spiritmaster, eRace.Kobold, eRace.Norseman),
                    new(eCharacterClass.Shaman, eRace.Kobold, eRace.Troll),
                    new(eCharacterClass.Runemaster, eRace.Dwarf, eRace.Kobold, eRace.Norseman),
                    new(eCharacterClass.Bonedancer, eRace.Kobold, eRace.Troll, eRace.Valkyn),
                    new(eCharacterClass.Berserker, eRace.Dwarf, eRace.Norseman, eRace.Troll, eRace.Valkyn),
                    new(eCharacterClass.Savage, eRace.Dwarf, eRace.Kobold, eRace.Norseman, eRace.Troll, eRace.Valkyn),
                ],
                [eRealm.Hibernia] =
                [
                    new(eCharacterClass.Eldritch, eRace.Elf, eRace.Lurikeen),
                    new(eCharacterClass.Enchanter, eRace.Elf, eRace.Lurikeen),
                    new(eCharacterClass.Mentalist, eRace.Celt, eRace.Elf, eRace.Lurikeen),
                    new(eCharacterClass.Blademaster, eRace.Celt, eRace.Elf, eRace.Firbolg),
                    new(eCharacterClass.Hero, eRace.Celt, eRace.Firbolg, eRace.Lurikeen, eRace.Sylvan),
                    new(eCharacterClass.Champion, eRace.Celt, eRace.Elf, eRace.Lurikeen),
                    new(eCharacterClass.Warden, eRace.Celt, eRace.Firbolg, eRace.Sylvan),
                    new(eCharacterClass.Druid, eRace.Celt, eRace.Firbolg, eRace.Sylvan),
                    new(eCharacterClass.Bard, eRace.Celt, eRace.Firbolg),
                    new(eCharacterClass.Nightshade, eRace.Elf, eRace.Lurikeen),
                    new(eCharacterClass.Ranger, eRace.Celt, eRace.Elf, eRace.Lurikeen),
                    new(eCharacterClass.Animist, eRace.Celt, eRace.Firbolg, eRace.Sylvan),
                    new(eCharacterClass.Valewalker, eRace.Celt, eRace.Firbolg, eRace.Sylvan),
                    // (0.38: the Sluaghbinder is discontinued, owner 2026-10-30.)
                ],
            };

        public static Identity Generate(eRealm realm, eGender gender, ISet<string> reservedNames = null, Random random = null)
        {
            if (!Choices.TryGetValue(realm, out ClassChoice[] realmChoices))
                throw new ArgumentOutOfRangeException(nameof(realm), "Autonomous bots must belong to Albion, Midgard, or Hibernia.");
            if (gender is not eGender.Male and not eGender.Female)
                throw new ArgumentOutOfRangeException(nameof(gender), "Autonomous bots must have a male or female identity.");

            random ??= Random.Shared;
            ClassChoice classChoice = realmChoices[random.Next(realmChoices.Length)];
            eRace race = classChoice.Races[random.Next(classChoice.Races.Length)];
            string name = GenerateUniqueName(realm, race, gender, reservedNames, random);
            return new Identity(name, realm, gender, classChoice.CharacterClass, race);
        }

        public static Identity GenerateForClass(
            eRealm realm,
            eGender gender,
            eCharacterClass characterClass,
            ISet<string> reservedNames = null,
            Random random = null)
        {
            if (!Choices.TryGetValue(realm, out ClassChoice[] realmChoices))
                throw new ArgumentOutOfRangeException(nameof(realm));
            ClassChoice choice = realmChoices.FirstOrDefault(entry => entry.CharacterClass == characterClass);
            if (choice == null)
                throw new ArgumentException($"{characterClass} is not a Classic + SI {GlobalConstants.RealmToName(realm)} class.", nameof(characterClass));
            if (gender is not eGender.Male and not eGender.Female)
                throw new ArgumentOutOfRangeException(nameof(gender));

            random ??= Random.Shared;
            eRace race = choice.Races[random.Next(choice.Races.Length)];
            return new Identity(
                GenerateUniqueName(realm, race, gender, reservedNames, random),
                realm,
                gender,
                characterClass,
                race);
        }

        /// <summary>A new world bot: era class and race, and a fresh unique name claimed in offline_bot_names
        /// (BotNameService). False when the name table is not ready or busy; try again later. Call off the game loop.</summary>
        public static bool TryGenerateClaimed(eRealm realm, eGender gender, long botId, out Identity identity, Random random = null)
        {
            identity = null;
            if (!Choices.TryGetValue(realm, out ClassChoice[] realmChoices) || gender is not eGender.Male and not eGender.Female)
                return false;
            random ??= Random.Shared;
            ClassChoice classChoice = realmChoices[random.Next(realmChoices.Length)];
            eRace race = classChoice.Races[random.Next(classChoice.Races.Length)];
            if (!BotNameService.TryClaimNewBotName(realm, race, gender, botId, out string name))
                return false;
            identity = new Identity(name, realm, gender, classChoice.CharacterClass, race);
            return true;
        }

        public static IReadOnlyCollection<eCharacterClass> GetEraClasses(eRealm realm) =>
            Choices.TryGetValue(realm, out ClassChoice[] choices)
                ? choices.Select(choice => choice.CharacterClass).ToArray()
                : Array.Empty<eCharacterClass>();

        public static IReadOnlyCollection<eRace> GetEligibleRaces(eCharacterClass characterClass) =>
            Choices.Values.SelectMany(value => value)
                .FirstOrDefault(choice => choice.CharacterClass == characterClass)?.Races ?? Array.Empty<eRace>();

        // Names come from the race-styled generator (BotNameService, botnames165.json). They are not claimed here:
        // callers that keep a bot for good claim its name through BotNameService. Without a reserved set, names already
        // worn by a player or bot are skipped (the /tg helpers).
        private static string GenerateUniqueName(eRealm realm, eRace race, eGender gender, ISet<string> reservedNames, Random random)
        {
            BotNameGenerator generator = BotNameService.Generator
                ?? throw new InvalidOperationException("The bot name tables are missing.");
            bool Used(string candidate) => reservedNames != null ? reservedNames.Contains(candidate) : BotNameService.IsNameTaken(candidate);
            string name = generator.RandomName(race, gender, random, Used, attempts: reservedNames != null ? 2048 : 40);
            if (name == null)
            {
                // this race is crowded in the reserved set: any other race of the realm
                foreach (eRace other in BotNameGenerator.StyleRaces.Where(r => r != race && BotNameGenerator.RealmOf(r) == realm))
                {
                    name = generator.RandomName(other, gender, random, Used);
                    if (name != null)
                        break;
                }
            }
            if (name == null)
                throw new InvalidOperationException($"Unable to produce another unique {GlobalConstants.RealmToName(realm)} {gender} bot name.");
            reservedNames?.Add(name);
            return name;
        }
    }
}
