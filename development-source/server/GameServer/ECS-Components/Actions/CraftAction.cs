using System.Collections.Generic;
using System.Reflection;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS
{
    public class CraftAction
    {
        protected static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        public const string CRAFT_QUEUE_LENGTH_PROPERTY = "CraftQueueLength";
        public const string CRAFT_QUEUE_REMAINING_PROPERTY = "CraftQueueRemaining";
        public const string RECIPE_TO_CRAFT_PROPERTY = "RecipeToCraft";

        private GamePlayer _owner;
        private Recipe _recipe;
        private AbstractCraftingSkill _skill;
        private int _craftTime;
        private long _startTick;
        private bool _finishedCraft;
        // Crafting assistant (CraftAssist165): how this craft ended and what it added to the backpack
        private CraftAssistOutcome _assistOutcome;
        private List<(DbInventoryItem Item, int Count)> _assistMade;

        /// <summary>A crafting assistant craft: no craft queue, real materials always, its end reported to CraftAssist165.</summary>
        public bool Assist { get; set; }

        private long EndTime => _startTick + _craftTime;

        public CraftAction(GamePlayer owner, int CraftingTime, Recipe recipe, AbstractCraftingSkill skill)
        {
            _owner = owner;
            _startTick = GameLoop.GameLoopTime;
            _craftTime = CraftingTime * 1000;
            _recipe = recipe;
            _skill = skill;
            owner.craftComponent.CraftState = true;
        }

        public void Tick()
        {
            if (_owner.IsCrowdControlled)
            {
                _assistOutcome = CraftAssistOutcome.Interrupted;
                CleanupCraftAction();
                return;
            }

            if (_owner.CurrentSpellHandler?.Spell.Uninterruptible == false)
            {
                _assistOutcome = CraftAssistOutcome.Interrupted;
                CleanupCraftAction();
                return;
            }

            if (_owner.IsMoving)
            {
                _assistOutcome = CraftAssistOutcome.Interrupted;
                CleanupCraftAction();
                _owner.Out.SendMessage(LanguageMgr.GetTranslation(_owner.Client.Account.Language, "AbstractCraftingSkill.CraftItem.MoveAndInterrupt"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (GameServiceUtils.ShouldTick(EndTime))
            {
                if (Assist)
                    MakeAssistItem();
                else
                    MakeItem();
            }

            if (_finishedCraft)
                CleanupCraftAction();
        }

        public void CleanupCraftAction()
        {
            _owner.craftComponent.CraftAction = null;
            _owner.craftComponent.CraftState = false;
            _finishedCraft = false;
            _owner.Out.SendCloseTimerWindow();
            _owner.TempProperties.RemoveProperty(CRAFT_QUEUE_REMAINING_PROPERTY);

            if (Assist)
            {
                // reported once, after the component is free, so the assistant can start its next craft
                Assist = false;
                CraftAssistOutcome outcome = _assistOutcome == CraftAssistOutcome.None ? CraftAssistOutcome.Interrupted : _assistOutcome;
                CraftAssist165.OnCraftEnded(_owner, _recipe, outcome, _assistMade);
            }
        }

        /// <summary>The player clicked MAKE again during this craft (AbstractCraftingSkill.StopCraftingCurrentItem).</summary>
        public void MarkStoppedByPlayer()
        {
            if (_assistOutcome == CraftAssistOutcome.None)
                _assistOutcome = CraftAssistOutcome.StoppedByPlayer;
        }

        /// <summary>An assistant craft's end: the same chance, materials, product and gains as MakeItem, but never the
        /// /craftmacro queue, and real materials for everyone (a GM without them makes nothing).</summary>
        private void MakeAssistItem()
        {
            Recipe recipe = _recipe;
            AbstractCraftingSkill skill = _skill;
            _finishedCraft = true;

            if (recipe == null || skill == null)
            {
                _assistOutcome = CraftAssistOutcome.MaterialsGone;
                return;
            }

            _owner.Out.SendCloseTimerWindow();

            if (Util.Chance(skill.CalculateChanceToMakeItem(_owner, recipe.Level)))
            {
                if (!skill.RemoveUsedMaterials(_owner, recipe))
                {
                    _owner.Out.SendMessage(LanguageMgr.GetTranslation(_owner.Client.Account.Language, "AbstractCraftingSkill.MakeItem.NotAllMaterials"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    _assistOutcome = CraftAssistOutcome.MaterialsGone;
                    return;
                }

                Dictionary<DbInventoryItem, int> before = CraftAssist165.SnapshotBackpack(_owner);
                skill.BuildCraftedItem(_owner, recipe);
                _assistMade = CraftAssist165.AddedSince(_owner, before);
                skill.GainCraftingSkillPoints(_owner, recipe);
                _assistOutcome = CraftAssistOutcome.Made;
            }
            else
            {
                _owner.Out.SendMessage(LanguageMgr.GetTranslation(_owner.Client.Account.Language, "AbstractCraftingSkill.MakeItem.LoseNoMaterials", recipe.Product.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                _owner.Out.SendPlaySound(eSoundType.Craft, 0x02);
                _assistOutcome = CraftAssistOutcome.Failed;
            }
        }

        protected virtual void MakeItem()
        {
            Recipe recipe = _recipe;
            AbstractCraftingSkill skill = _skill;
            int queue = _owner.TempProperties.GetProperty<int>(CRAFT_QUEUE_LENGTH_PROPERTY);
            int remainingToCraft = _owner.TempProperties.GetProperty<int>(CRAFT_QUEUE_REMAINING_PROPERTY);

            if (_owner == null || recipe == null || skill == null)
            {
                _owner?.Out.SendMessage("Could not find recipe or item to craft!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                log.Error("Crafting.MakeItem: Could not retrieve player, recipe, or raw materials to craft from CraftTimer.");
                return;
            }

            if (queue > 1 && remainingToCraft == 0)
                remainingToCraft = queue;

            _owner.Out.SendCloseTimerWindow();

            if (Util.Chance(skill.CalculateChanceToMakeItem(_owner, recipe.Level)))
            {
                if (!skill.RemoveUsedMaterials(_owner, recipe))
                {
                    _owner.Out.SendMessage(LanguageMgr.GetTranslation(_owner.Client.Account.Language, "AbstractCraftingSkill.MakeItem.NotAllMaterials"), eChatType.CT_System, eChatLoc.CL_SystemWindow);

                    if (_owner.Client.Account.PrivLevel == 1)
                    {
                        CleanupCraftAction();
                        return;
                    }
                }

                _owner.TempProperties.SetProperty(CRAFT_QUEUE_REMAINING_PROPERTY, --remainingToCraft);
                skill.BuildCraftedItem(_owner, recipe);
                skill.GainCraftingSkillPoints(_owner, recipe);
            }
            else
            {
                _owner.Out.SendMessage(LanguageMgr.GetTranslation(_owner.Client.Account.Language, "AbstractCraftingSkill.MakeItem.LoseNoMaterials", recipe.Product.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                _owner.Out.SendPlaySound(eSoundType.Craft, 0x02);
            }

            if (remainingToCraft > 0)
            {
                if (skill.CheckRawMaterials(_owner, recipe))
                {
                    _startTick = GameLoop.GameLoopTime;
                    _owner.Out.SendTimerWindow(LanguageMgr.GetTranslation(_owner.Client.Account.Language, "AbstractCraftingSkill.CraftItem.CurrentlyMaking", recipe.Product.Name), skill.GetCraftingTime(_owner, recipe));
                    _finishedCraft = false;
                    _owner.craftComponent.AddRecipe(recipe);
                }
                else
                    _finishedCraft = true;
            }
            else
            {
                _owner.TempProperties.RemoveProperty(CRAFT_QUEUE_REMAINING_PROPERTY);
                _finishedCraft = true;
            }
        }
    }
}
