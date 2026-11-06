using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.Styles;
using DOL.Language;

namespace DOL.GS
{
    public class PlayerAttackAction : AttackAction
    {
        private GamePlayer _playerOwner;

        public PlayerAttackAction(GamePlayer owner) : base(owner)
        {
            _playerOwner = owner;
        }

        public override void OnAimInterrupt(GameObject attacker)
        {
            string attackTypeMsg;

            if (_playerOwner.ActiveWeapon != null && _playerOwner.ActiveWeapon.Object_Type == (int)eObjectType.Thrown)
                attackTypeMsg = LanguageMgr.GetTranslation(_playerOwner.Client.Account.Language, "GamePlayer.Attack.Type.Throw");
            else
                attackTypeMsg = LanguageMgr.GetTranslation(_playerOwner.Client.Account.Language, "GamePlayer.Attack.Type.Shot");

            if (attacker is GameNPC npcAttacker)
                _playerOwner.Out.SendMessage(LanguageMgr.GetTranslation(_playerOwner.Client.Account.Language, "GamePlayer.Attack.Interrupted", attacker.GetName(0, true, _playerOwner.Client.Account.Language, npcAttacker), attackTypeMsg), eChatType.CT_YouHit, eChatLoc.CL_SystemWindow);
            else
                _playerOwner.Out.SendMessage(LanguageMgr.GetTranslation(_playerOwner.Client.Account.Language, "GamePlayer.Attack.Interrupted", attacker.GetName(0, true), attackTypeMsg), eChatType.CT_YouHit, eChatLoc.CL_SystemWindow);
        }

        public const long HeldBackMessageMilliseconds = 1500;
        private long _nextHeldBackMessage;
        private AttackHeldBack? _lastHeldBack;

        /// <summary>
        /// True 1.65 Mode (owner 2026-11-06: the player drew a weapon next to a monster and nothing happened, no message):
        /// say why no swing comes - stunned, mesmerized, engaging, casting a spell, no target - in the system window, at
        /// most every 1.5 seconds for the same reason (as "too far away" and "not in view" are).
        /// </summary>
        protected override void OnAttackHeldBack(AttackHeldBack reason)
        {
            if (!Classic165Mode.IsOn || _playerOwner.Client?.Account == null ||
                !ShouldSayHeldBack(reason, _lastHeldBack, GameLoop.GameLoopTime, _nextHeldBackMessage))
                return;
            _lastHeldBack = reason;
            _nextHeldBackMessage = GameLoop.GameLoopTime + HeldBackMessageMilliseconds;
            string language = _playerOwner.Client.Account.Language;
            string message = reason switch
            {
                AttackHeldBack.Stunned => LanguageMgr.GetTranslation(language, "GamePlayer.StartAttack.CantAttackStunned"),
                AttackHeldBack.Mezzed => LanguageMgr.GetTranslation(language, "GamePlayer.StartAttack.CantAttackmesmerized"),
                AttackHeldBack.Casting => "You can't attack while casting a spell!",
                AttackHeldBack.Engaging => "You can't attack while you are engaging your target!",
                _ => LanguageMgr.GetTranslation(language, "GamePlayer.Attack.NeedTarget"),
            };
            _playerOwner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        /// <summary>Pure rule: a held-back message goes out for a new reason, or the same one after 1.5 seconds.</summary>
        public static bool ShouldSayHeldBack(AttackHeldBack reason, AttackHeldBack? last, long now, long next) =>
            reason != last || now >= next;

        protected override bool PrepareMeleeAttack()
        {
            _combatStyle = StyleComponent.GetStyleToUse();

            // Check if we still have enough endurance for the style and update `_weapon` if needed.
            if (_combatStyle != null)
            {
                DbInventoryItem styleWeapon = (eObjectType) _combatStyle.WeaponTypeRequirement is eObjectType.Shield ? _leftWeapon : _weapon;

                if (styleWeapon != null && !StyleProcessor.CheckEnduranceCost(_playerOwner, styleWeapon, _combatStyle))
                    _combatStyle = null;
                else
                    _weapon = styleWeapon;
            }

            return base.PrepareMeleeAttack();
        }

        protected override bool PrepareRangedAttack()
        {
            if (base.PrepareRangedAttack())
            {
                // This is also done in weaponAction.Execute(), but we must unstealth immediately if the call is delayed.
                if (_ticksToTarget > 0)
                    _playerOwner.Stealth(false);

                return true;
            }

            return false;
        }

        protected override void PerformRangedAttack()
        {
            _playerOwner.rangeAttackComponent.RemoveEnduranceAndAmmoOnShot();
            base.PerformRangedAttack();

            if (_playerOwner.rangeAttackComponent.Ammo.Count == 0)
                _playerOwner.rangeAttackComponent.UpdateAmmo(_playerOwner.ActiveWeapon);
        }

        protected override bool FinalizeMeleeAttack()
        {
            if (base.FinalizeMeleeAttack())
            {
                if (_playerOwner.UseDetailedCombatLog)
                    _playerOwner.Out.SendMessage($"Attack Speed: {_interval / 1000.0}s", eChatType.CT_DamageAdd, eChatLoc.CL_SystemWindow);

                StyleComponent.NextCombatStyle = null;
                StyleComponent.NextCombatBackupStyle = null;
                return true;
            }

            return false;
        }

        protected override bool FinalizeRangedAttack()
        {
            bool stopAttack = false;

            if (_playerOwner.rangeAttackComponent.RangedAttackState is not eRangedAttackState.AimFireReload)
                stopAttack = true;
            else if (_playerOwner.Endurance < RangeAttackComponent.DEFAULT_ENDURANCE_COST)
            {
                stopAttack = true;
                _playerOwner.Out.SendMessage(LanguageMgr.GetTranslation(_playerOwner.Client.Account.Language, "GamePlayer.StartAttack.TiredUse", _weapon.Name), eChatType.CT_YouHit, eChatLoc.CL_SystemWindow);
            }
            else
            {
                DbInventoryItem ammo = _playerOwner.rangeAttackComponent.Ammo;

                if (ammo == null || ammo.Count == 0)
                    stopAttack = true;
            }

            if (stopAttack)
            {
                _playerOwner.rangeAttackComponent.RangedAttackState = eRangedAttackState.None;
                AttackComponent.StopAttack();
                return false;
            }

            return base.FinalizeRangedAttack();
        }
    }
}
