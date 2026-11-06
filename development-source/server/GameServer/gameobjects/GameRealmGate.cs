using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode (owner 2026-11-06: "bot navmeshes respect the gate fort gates and can go thru them but enemy factions
    /// have to attack the doors first if they want through"): a frontier milegate ("gate fort": Hadrian's Wall, Odin's
    /// Gate, Emain Macha) or border keep door that belongs to no keep. Its Door row carries the owning realm (1-3; every
    /// other door is realm 6 "Door"), set by the content pass (tools\claude-version\true165\build_gates165.py).
    /// <list type="bullet">
    /// <item>Its own realm passes: a player's DoorRequest opens it for a few seconds (as any door), bots and pets of the
    /// realm walk through it (Pathfinder), its state unchanged.</item>
    /// <item>Enemies cannot open it. They break it like a keep door (melee, arrows, rams, trebuchets): health of a level 1
    /// frontier keep door (keep_doors_base_health x base level 50), its armor too; spells have no effect, as on keep doors.</item>
    /// <item>Broken, it stands open (passable for everyone) until it is repaired above 15% (/repair by its realm, or the
    /// keep doors' 5% per 30 minutes while no enemy is near), then shuts again. Period: Warcry 1.52 (2002-03-19) "The
    /// Albion mile fort gates can now be attacked, broken, and repaired"; broken keep doors stayed open until repaired.</item>
    /// <item>The navmesh registers it (it cannot be opened by interaction), so a shut gate is a BlockingDoor polygon: the
    /// door state is the same for every realm; who may pass is decided per actor (<see cref="MayPass(eRealm, eRealm, eDoorState)"/>).</item>
    /// </list>
    /// A server restart brings it back whole and shut (period: a server reset repaired doors); its health is not saved.
    /// </summary>
    public class GameRealmGate : GameDoorBase
    {
        /// <summary>A level 1 frontier keep door's base level (GameKeep BaseLevel 50): health and armor as that door.</summary>
        public const int BaseLevel = 50;
        /// <summary>At or below this health a gate stands broken open (GameKeepDoor's close threshold).</summary>
        public const int CloseThresholdPercent = 15;
        public const int StaysOpenMilliseconds = 5_000;
        public const int RepairIntervalMilliseconds = 30 * 60 * 1000;
        public const int EnemiesNearRadius = 3_000;

        private static readonly ConcurrentDictionary<ushort, GameRealmGate[]> ByRegion = new();

        private CloseAction _closeAction;
        private byte _lastHealthPercent;

        /// <summary>The three realms that can own a gate.</summary>
        public static bool IsOwnerRealm(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;

        /// <summary>Which Door rows become gates: True 1.65 Mode only, outside every keep, owned by a realm.</summary>
        public static bool LoadsAsGate(bool classic165, bool inKeepArea, eRealm rowRealm) =>
            classic165 && !inKeepArea && IsOwnerRealm(rowRealm);

        /// <summary>Whoever may walk through: anyone while it stands open (opened or broken), else its own realm only.</summary>
        public static bool MayPass(eRealm actor, eRealm gate, eDoorState state) =>
            state == eDoorState.Open || IsOwnerRealm(gate) && actor == gate;

        /// <summary>An enemy of the gate who may break it: another of the three realms, while it still stands shut.</summary>
        public static bool MayBreak(eRealm actor, eRealm gate, bool alive, eDoorState state) =>
            alive && state == eDoorState.Closed && IsOwnerRealm(actor) && IsOwnerRealm(gate) && actor != gate;

        public static bool IsBrokenPercent(int healthPercent) => healthPercent <= CloseThresholdPercent;
        public static int GateMaxHealth(int keepDoorBaseHealth) => BaseLevel * keepDoorBaseHealth;
        /// <summary>ArmorFactorCalculator's keep door armor for a level 1 GameKeep of base level 50.</summary>
        public static int GateArmorFactor => (int)(BaseLevel * 1.1 * 24);

        /// <summary>The gates of a region (a handful; built once from the door list and reset when a gate registers).</summary>
        public static GameRealmGate[] InRegion(ushort regionId) =>
            ByRegion.GetOrAdd(regionId, static id =>
            {
                var gates = new List<GameRealmGate>();
                foreach (GameDoorBase door in DoorMgr.Snapshot())
                    if (door is GameRealmGate gate && gate.CurrentRegionID == id)
                        gates.Add(gate);
                return gates.ToArray();
            });

        public static void ForgetRegions() => ByRegion.Clear();

        public bool MayPass(GameLiving actor) => actor != null && MayPass(actor.Realm, Realm, State);
        public bool MayBreak(GameLiving actor) => actor != null && MayBreak(actor.Realm, Realm, IsAlive, State);
        public bool IsBroken => IsBrokenPercent(HealthPercent);

        public override bool CanBeOpenedViaInteraction => false;
        public override bool IsAttackableDoor => true;
        public override int MaxHealth => GateMaxHealth(Properties.KEEP_DOORS_BASE_HEALTH);
        public override int RealmPointsValue => 0;
        public override long ExperienceValue => 0;
        public override string Name => GameDoor.IsBorderKeepDoor(DoorId) ? "Keep Door" : "Milegate";

        public override string GetName(int article, bool firstLetterUppercase) => "the " + base.GetName(article, firstLetterUppercase);

        public override int Health
        {
            get => m_health;
            set
            {
                m_health = Math.Clamp(value, 0, MaxHealth);

                if (m_health > 0 && m_health < MaxHealth)
                    StartHealthRegeneration();

                // Repaired above the threshold: it shuts again (unless its own realm has it open just now).
                if (State == eDoorState.Open && !IsBroken && _closeAction?.IsAlive != true && ObjectState == eObjectState.Active)
                    SetState(eDoorState.Closed);
            }
        }

        public override void LoadFromDatabase(DOL.Database.DataObject obj)
        {
            base.LoadFromDatabase(obj);
            m_health = MaxHealth;
            _lastHealthPercent = HealthPercent;
            State = eDoorState.Closed;
        }

        // Its state is runtime only (see the class summary); GM door edits keep their own rows.
        public override void SaveIntoDatabase() { }

        /// <summary>Opens for its own realm (or a GM, or the server itself); an enemy is refused.</summary>
        public override void Open(GameLiving opener = null)
        {
            if (opener != null && !MayPass(opener) && !(opener is GamePlayer { Client.Account.PrivLevel: > 1 }))
            {
                (opener as GamePlayer)?.Out.SendMessage($"{GetName(0, true)} is shut; it belongs to an enemy realm.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            lock (_stateLock)
            {
                _closeAction ??= new(this);
                // A border keep door's timer is not extended (GameDoor: players could keep it open).
                if (!(GameDoor.IsBorderKeepDoor(DoorId) && _closeAction.IsAlive))
                    _closeAction.Start(StaysOpenMilliseconds);
            }

            SetState(eDoorState.Open);
        }

        /// <summary>Shuts it, unless it is broken: a broken gate stands open until it is repaired.</summary>
        public override void Close(GameLiving closer = null)
        {
            if (IsBroken)
                return;

            _closeAction?.Stop();
            SetState(eDoorState.Closed);
        }

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;

            if (State == eDoorState.Closed)
                Open(player);

            return true;
        }

        public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount)
        {
            if (damageAmount + criticalAmount <= 0 || !IsAlive)
                return;

            base.TakeDamage(source, damageType, damageAmount, criticalAmount);

            if (_lastHealthPercent != HealthPercent)
            {
                _lastHealthPercent = HealthPercent;

                foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    ClientService.UpdateObjectForPlayer(player, this);
            }
        }

        /// <summary>Damage as a level 1 frontier keep door takes it (GameKeepDoor.ModifyAttack, keep door toughness).</summary>
        public override void ModifyAttack(AttackData attackData)
        {
            if (attackData.DamageType is eDamageType.GM)
                return;

            if (attackData.Attacker is GameNPC && !Properties.DOORS_ALLOWPETATTACK &&
                (attackData.Attacker as GameNPC).Brain is AI.Brain.IControlledBrain { } brain && brain.GetPlayerOwner() != null)
            {
                attackData.AttackResult = eAttackResult.NotAllowed_ServerRules;
                attackData.Damage = attackData.StyleDamage = attackData.CriticalDamage = 0;
                return;
            }

            attackData.Damage = AdjustedDamage(attackData.Damage, Properties.SET_KEEP_DOOR_TOUGHNESS);
            attackData.StyleDamage = AdjustedDamage(attackData.StyleDamage, Properties.SET_KEEP_DOOR_TOUGHNESS);
            attackData.CriticalDamage = AdjustedDamage(attackData.CriticalDamage, Properties.SET_KEEP_DOOR_TOUGHNESS);
        }

        /// <summary>GameKeepDoor's damage rule at keep level 1.</summary>
        public static int AdjustedDamage(int damage, int toughness) => (damage - damage * 5 / 100) * toughness / 100;

        public override void Die(GameObject killer)
        {
            base.Die(killer);

            foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.INFO_DISTANCE))
                player.Out.SendMessage($"The {Name} is broken!", eChatType.CT_System, eChatLoc.CL_SystemWindow);

            _closeAction?.Stop();
            SetState(eDoorState.Open);
        }

        /// <summary>Repaired by its realm (/repair) or its slow mending: health back, shut again above the threshold.</summary>
        public void Repair(int amount)
        {
            Health = Math.Min(Health + Math.Max(0, amount), MaxHealth);
            BroadcastDoorStatus();
        }

        public override void StartHealthRegeneration()
        {
            if (m_healthRegenerationTimer == null || m_healthRegenerationTimer.IsAlive || Health >= MaxHealth)
                return;

            m_healthRegenerationTimer.Start(RepairIntervalMilliseconds);
        }

        protected override int HealthRegenerationTimerCallback(ECSGameTimer timer)
        {
            if (HealthPercent >= 100)
                return 0;

            // As keep doors (GameKeepDoor): 5% per 30 minutes, never while enemies are at it.
            if (!InCombat && !EnemiesNear())
                Repair(MaxHealth / 100 * 5);

            return RepairIntervalMilliseconds;
        }

        private bool EnemiesNear()
        {
            foreach (GamePlayer player in GetPlayersInRadius(EnemiesNearRadius))
                if (player.IsAlive && player.Realm != Realm && IsOwnerRealm(player.Realm) &&
                    (ePrivLevel)player.Client.Account.PrivLevel == ePrivLevel.Player)
                    return true;
            foreach (GameNPC npc in GetNPCsInRadius(EnemiesNearRadius))
                if (npc is GameBot bot && bot.IsAlive && bot.Realm != Realm && IsOwnerRealm(bot.Realm))
                    return true;
            return false;
        }

        public override IList GetExamineMessages(GamePlayer player)
        {
            IList list = base.GetExamineMessages(player);
            list.Add(player.Realm == Realm
                ? $"You select the {Name}. It belongs to your realm."
                : $"You select the {Name}. It belongs to an enemy realm and can be attacked!");
            return list;
        }

        public void BroadcastDoorStatus()
        {
            foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                ClientService.UpdateObjectForPlayer(player, this);
                player.Out.SendDoorState(CurrentRegion, this);
            }
        }

        private void SetState(eDoorState state) => State = state;

        private sealed class CloseAction : ECSGameTimerWrapperBase
        {
            public CloseAction(GameRealmGate gate) : base(gate) { }

            protected override int OnTick(ECSGameTimer timer)
            {
                var gate = (GameRealmGate)timer.Owner;
                if (!gate.IsBroken)
                    gate.SetState(eDoorState.Closed);
                return 0;
            }
        }
    }
}
