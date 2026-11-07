using DOL.AI;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;
using DOL.GS.Scripts.DOL.AI.Brain;

namespace DOL.GS.Scripts
{
    public class BotonidSeedling : GameNPC
    {
        public BotonidSeedling() : base()
        {
        }

        public BotonidSeedling(ABrain defaultBrain) : base(defaultBrain)
        {
        }

        public BotonidSeedling(INpcTemplate template) : base(template)
        {
        }

        public override bool AddToWorld()
        {
            INpcTemplate npcTemplate = NpcTemplateMgr.GetTemplate(60165666);
            LoadTemplate(npcTemplate);

            //seedling: the lure looks like a lower-level monster until the scourgin reveals itself (Prima SI 2002, Domnann
            //'Enhanced Monster AI'; Allakhazam 2003-01-03) - level 3 like the plain seedlings, not the scourgin's 6-9
            Model = 818;
            Size = 9;
            Name = "botonid seedling";
            Level = 3;

            Faction = FactionMgr.GetFactionByID(69);

            BotonidBrain sBrain = new BotonidBrain();
            SetOwnBrain(sBrain);

            //1.30min
            RespawnInterval = 90000;

            base.AddToWorld();
            return true;

            // 818 scaled to 9
        }
    }

    namespace DOL.AI.Brain
    {
        public class BotonidBrain : StandardMobBrain
        {
            public BotonidBrain() : base()
            {
                AggroLevel = 100;
                AggroRange = 500;
            }

            private bool transformed;

            public override int ThinkInterval => 1000;

            public override void Think()
            {
                if (HasAggro)
                {
                    if (!Body.IsWithinRadius(Body.TargetObject, 150)) return;
                    if (transformed) return;
                    // the period message (Allakhazam 2003-01-03), once to the players near; the transform happens once
                    // (it ran inside the loop and flipped back and forth with two or more players near)
                    string who = Body.TargetObject?.Name ?? "you";
                    foreach (GamePlayer player in Body.GetPlayersInRadius(400))
                        player.Out.SendMessage("The lure disappears and the scourgin jumps out at " + who + "!",
                            eChatType.CT_Say,
                            eChatLoc.CL_ChatWindow);
                    Transform(transformed); // scourgin
                    transformed = true;
                }
                else if (!Body.InCombatInLast(30 * 1000) && !HasAggro)
                {
                    if (!transformed) return;
                    Transform(transformed); //seedling
                    transformed = false;
                }

                base.Think();
            }

            private void Transform(bool transformed)
            {
                if (transformed)
                {
                    Body.Size = 9;
                    Body.Model = 818;
                    Body.Name = "botonid seedling";
                    Body.Level = 3;
                    Body.Faction = FactionMgr.GetFactionByID(69);    // Botonids
                }
                else
                {
                    // the scourgin: Prima levels 6-7, the template's size, Predators of Hy Brasil
                    Body.Size = (byte)Util.Random(23, 28);
                    Body.Model = 914;
                    Body.Name = "scourgin";
                    Body.Level = (byte)Util.Random(6, 7);
                    Body.Faction = FactionMgr.GetFactionByID(100);
                }
            }
        }
    }
}