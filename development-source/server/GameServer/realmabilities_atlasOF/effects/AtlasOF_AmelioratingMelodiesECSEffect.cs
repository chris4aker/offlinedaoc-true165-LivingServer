using System;
using System.Collections.Generic;
using DOL.GS.PacketHandler;

namespace DOL.GS.Effects
{
    public class AmelioratingMelodiesECSEffect : ECSGameAbilityEffect
    {
        private const int RANGE = 1500;
        private int _heal;

        public AmelioratingMelodiesECSEffect(in ECSGameEffectInitParams initParams) : base(initParams)
        {
            EffectType = eEffect.AmelioratingMelodies;
            PulseFreq = 1500; // 1.5s. Effect lasts 30s so that is 20 ticks.
            NextTick = GameLoop.GameLoopTime;
            _heal = (int) Effectiveness; // Effectiveness value is used as a heal value per tick.
        }

        public override ushort Icon => 4250;
        public override string Name => "Ameliorating Melodies";
        public override bool HasPositiveEffect => true;

        // A gamebot's melody (True 1.65 bot realm abilities) heals every living group member in range, bots and players.
        private void PulseForBot(GameBot bot)
        {
            IEnumerable<GameLiving> members = bot.Group != null ? bot.Group.GetMembersInTheGroup() : new GameLiving[] { bot };
            foreach (GameLiving member in members)
            {
                if (!member.IsAlive || member.Health >= member.MaxHealth || !bot.IsWithinRadius(member, RANGE))
                    continue;
                int heal = Math.Min(_heal, member.MaxHealth - member.Health);
                heal = member.ChangeHealth(bot, eHealthChangeType.Regenerate, heal);
                (member as GamePlayer)?.Out.SendMessage($"{bot.Name} 's Ameliorating Melodies heals you for {heal} hit points.", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
            }
        }

        public override void OnEffectPulse()
        {
            if (OwnerPlayer == null)
            {
                if (Owner is GameBot bot)
                    PulseForBot(bot);
                return;
            }

            List<GamePlayer> playersToHeal;

            // OF AM works on the caster as well, unlike NF AM.
            if (OwnerPlayer.Group == null)
            {
                playersToHeal = GameLoop.GetListForTick<GamePlayer>();
                playersToHeal.Add(OwnerPlayer);
            }
            else
                playersToHeal = OwnerPlayer.Group.GetPlayersInTheGroup();

            foreach (GamePlayer player in playersToHeal)
            {
                if ((player.Health < player.MaxHealth) && OwnerPlayer.IsWithinRadius(player, RANGE) && player.IsAlive)
                {
                    int heal = _heal;

                    if (player.Health + heal > player.MaxHealth)
                        heal = player.MaxHealth - player.Health;

                    heal = player.ChangeHealth(OwnerPlayer, eHealthChangeType.Regenerate, heal);
                    OwnerPlayer.Out.SendMessage($"Your Ameliorating Melodies heal {player.Name} for {heal} hit points.", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
                    player.Out.SendMessage($"{OwnerPlayer.Name} 's Ameliorating Melodies heals you for {heal} hit points.", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
                }
            }
        }
    }
}
