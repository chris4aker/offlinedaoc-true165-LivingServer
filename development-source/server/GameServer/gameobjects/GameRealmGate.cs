using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode: a realm's door that belongs to no keep. Its Door row carries the owning realm (1-3; every other door
    /// is realm 6 "Door"), set by the content pass (tools\claude-version\true165\build_gates165.py). Two kinds, the period
    /// rules (owner 2026-11-06, second pass):
    /// <list type="bullet">
    /// <item><b>Milegate</b> ("gate fort": Hadrian's Wall, Odin's Gate, Emain Macha). ANYONE opens it, any realm, by a click;
    /// it shuts by itself after a few seconds. Bots of every realm walk through it shut or open, without opening it
    /// (owner 2026-11-06: bots got stuck opening and closing doors; they walk through doors), and none stops to break it. It can still be attacked, broken and
    /// repaired (Warcry 1.52, 2002-03-19: "The Albion mile fort gates can now be attacked, broken, and repaired"; Freddyshouse
    /// 2003-04-21: "waiting for stealthers to open the doors"; 2002-12-13: the doors "were always open"): health of a level 1
    /// frontier keep door, spells have no effect, /repair by its realm, 5% per 30 minutes with no enemy near; broken it stands
    /// open until repaired above 15%. It is openable by interaction, so the navmesh never shuts it: pathing passes it for
    /// every realm.</item>
    /// <item><b>Border keep door</b> (Castle Sauvage, Snowdonia Fortress, Svasud, Vindsaul, Druim Ligen, Druim Cain). It cannot
    /// be damaged at all and only its realm opens it (Allakhazam 2002-01-12: "can't open the frontier keep door of a border
    /// keep if you aren't a member"; the inside border door "has no health"). The navmesh registers it, so while shut its
    /// polygons are BlockingDoor: its realm's bots, companions and pets walk through, every other actor and planner treats
    /// it as a wall (Pathfinder.BlocksActor).</item>
    /// </list>
    /// A server restart brings a gate back whole and shut (period: a server reset repaired doors); its health is not saved.
    /// </summary>
    public class GameRealmGate : GameDoorBase
    {
        /// <summary>A level 1 frontier keep door's base level (GameKeep BaseLevel 50): health and armor as that door.</summary>
        public const int BaseLevel = 50;
        /// <summary>At or below this health a milegate stands broken open (GameKeepDoor's close threshold).</summary>
        public const int CloseThresholdPercent = 15;
        public const int StaysOpenMilliseconds = 5_000;
        public const int RepairIntervalMilliseconds = 30 * 60 * 1000;
        public const int EnemiesNearRadius = 3_000;

        private static readonly ConcurrentDictionary<ushort, GameRealmGate[]> ByRegion = new();
        private static readonly ConcurrentDictionary<ushort, GameRealmGate[]> OffMeshByRegion = new();

        /// <summary>A walk this close to a gate off the navmesh (2D, within <see cref="CrossingHeight"/>) runs through it.</summary>
        public const float CrossingRadius = 140;
        public const float CrossingHeight = 260;
        /// <summary>The navmesh box a registered gate (a border keep door) takes its door polygons from
        /// (LocalPathfindingMgr.RegisterDoor): half its passage and some. Border keep doors are up to 619 wide (BuildNav
        /// DoorSize); a keep's doors stand 1,300 and more apart.</summary>
        public const float DoorwayHalfWidth = 320;

        private CloseAction _closeAction;
        private byte _lastHealthPercent;

        /// <summary>The three realms that can own a gate.</summary>
        public static bool IsOwnerRealm(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;

        /// <summary>Which Door rows become gates: True 1.65 Mode only, outside every keep, owned by a realm.</summary>
        public static bool LoadsAsGate(bool classic165, bool inKeepArea, eRealm rowRealm) =>
            classic165 && !inKeepArea && IsOwnerRealm(rowRealm);

        /// <summary>A border keep's door: one of the border keep fixtures GameDoor knows (its door 01, 02, 03 or 04).</summary>
        public static bool IsBorderKeepFixture(int doorId) => GameDoor.IsBorderKeepDoor(doorId / 100 * 100 + 1);

        /// <summary>Whoever may walk through: a milegate anyone (it opens for all); a border keep door its realm, or anyone
        /// while it stands open.</summary>
        public static bool MayPass(eRealm actor, eRealm gate, eDoorState state, bool borderKeep) =>
            !borderKeep || state == eDoorState.Open || IsOwnerRealm(gate) && actor == gate;

        /// <summary>Who may open it by hand: a milegate anyone; a border keep door its realm only.</summary>
        public static bool MayOpen(eRealm actor, eRealm gate, bool borderKeep) =>
            !borderKeep || IsOwnerRealm(gate) && actor == gate;

        /// <summary>Who may damage it: an enemy of a standing milegate; nobody a border keep door.</summary>
        public static bool MayBreak(eRealm actor, eRealm gate, bool alive, bool borderKeep) =>
            !borderKeep && alive && IsOwnerRealm(actor) && IsOwnerRealm(gate) && actor != gate;

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

        /// <summary>
        /// The region's border keep doors that have no door polygons on the navmesh (RegisterDoor found none). The mesh cannot
        /// shut them, so the mover checks its walk against them (Pathfinder.MarkGatesOffMesh). Milegates are never here: they
        /// open for everyone and are not registered.
        /// </summary>
        public static GameRealmGate[] OffMeshInRegion(ushort regionId) =>
            OffMeshByRegion.GetOrAdd(regionId, static id =>
            {
                var gates = new List<GameRealmGate>();
                foreach (GameRealmGate gate in InRegion(id))
                    if (DoorMgr.IsUnregisteredInNavmesh(gate.DoorId))
                        gates.Add(gate);
                return gates.ToArray();
            });

        public static void ForgetRegions()
        {
            ByRegion.Clear();
            OffMeshByRegion.Clear();
        }

        /// <summary>
        /// Where a walk from <paramref name="a"/> to <paramref name="b"/> must stop for a gate at <paramref name="door"/>:
        /// the fraction of the segment where it comes within <see cref="CrossingRadius"/> of the gate (2D, at a height
        /// within <see cref="CrossingHeight"/>), in front of it. False when the segment does not run through the gate.
        /// </summary>
        public static bool TryCrossing(System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 door, out float stop)
        {
            stop = 0;
            System.Numerics.Vector2 a2 = new(a.X, a.Y), ab = new(b.X - a.X, b.Y - a.Y), point = new(door.X, door.Y);
            float length2 = ab.LengthSquared();
            float t = length2 < 1 ? 0 : Math.Clamp(System.Numerics.Vector2.Dot(point - a2, ab) / length2, 0, 1);
            float distance2 = System.Numerics.Vector2.DistanceSquared(a2 + ab * t, point);
            float z = a.Z + (b.Z - a.Z) * t;
            if (distance2 > CrossingRadius * CrossingRadius || Math.Abs(z - door.Z) > CrossingHeight)
                return false;
            float back = length2 < 1 ? 0 : MathF.Sqrt(CrossingRadius * CrossingRadius - distance2) / MathF.Sqrt(length2);
            stop = Math.Max(0, t - back);
            return true;
        }

        public bool IsBorderKeep => IsBorderKeepFixture(DoorId);
        public bool IsMilegate => !IsBorderKeep;
        public bool MayPass(GameLiving actor) => actor != null && MayPass(actor.Realm, Realm, State, IsBorderKeep);
        public bool MayPass(eRealm realm) => MayPass(realm, Realm, State, IsBorderKeep);
        public bool MayOpen(GameLiving actor) => actor != null && MayOpen(actor.Realm, Realm, IsBorderKeep);
        public bool MayBreak(GameLiving actor) => actor != null && MayBreak(actor.Realm, Realm, IsAlive, IsBorderKeep);
        /// <summary>A milegate broken open (a border keep door never is).</summary>
        public bool IsBroken => IsMilegate && IsBrokenPercent(HealthPercent);

        /// <summary>A milegate opens for everyone (not registered on the navmesh, never a blocker); a border keep door does not.</summary>
        public override bool CanBeOpenedViaInteraction => IsMilegate;
        public override bool IsAttackableDoor => IsMilegate;
        public override int MaxHealth => GateMaxHealth(Properties.KEEP_DOORS_BASE_HEALTH);
        public override int RealmPointsValue => 0;
        public override long ExperienceValue => 0;
        public override string Name => IsBorderKeep ? "Keep Door" : "Milegate";

        public override string GetName(int article, bool firstLetterUppercase) => "the " + base.GetName(article, firstLetterUppercase);

        /// <summary>A border keep door has no health (as a postern): nothing can attack it.</summary>
        public override int Health
        {
            get => IsBorderKeep ? 0 : m_health;
            set
            {
                if (IsBorderKeep)
                    return;

                m_health = Math.Clamp(value, 0, MaxHealth);

                if (m_health > 0 && m_health < MaxHealth)
                    StartHealthRegeneration();

                // Repaired above the threshold: it shuts again (unless someone has it open just now).
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

        /// <summary>Opens a milegate for anyone; a border keep door for its realm (or a GM, or the server itself) only.</summary>
        public override void Open(GameLiving opener = null)
        {
            if (opener != null && !MayOpen(opener) && !(opener is GamePlayer { Client.Account.PrivLevel: > 1 }))
            {
                (opener as GamePlayer)?.Out.SendMessage($"{GetName(0, true)} is shut; it belongs to an enemy realm.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            lock (_stateLock)
            {
                _closeAction ??= new(this);
                // A border keep door's timer is not extended (GameDoor: players could keep it open).
                if (!(IsBorderKeep && _closeAction.IsAlive))
                    _closeAction.Start(StaysOpenMilliseconds);
            }

            SetState(eDoorState.Open);
        }

        /// <summary>Shuts it, unless it is a broken milegate (that stands open until it is repaired) or the closer may not
        /// open it either (an enemy at a border keep door).</summary>
        public override void Close(GameLiving closer = null)
        {
            if (IsBroken || closer != null && !MayOpen(closer) && !(closer is GamePlayer { Client.Account.PrivLevel: > 1 }))
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
            if (IsBorderKeep || damageAmount + criticalAmount <= 0 || !IsAlive)
                return;

            base.TakeDamage(source, damageType, damageAmount, criticalAmount);

            if (_lastHealthPercent != HealthPercent)
            {
                _lastHealthPercent = HealthPercent;

                foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    ClientService.UpdateObjectForPlayer(player, this);
            }
        }

        /// <summary>Damage as a level 1 frontier keep door takes it (GameKeepDoor.ModifyAttack, keep door toughness); none to
        /// a border keep door.</summary>
        public override void ModifyAttack(AttackData attackData)
        {
            if (attackData.DamageType is eDamageType.GM)
                return;

            if (IsBorderKeep || attackData.Attacker is GameNPC && !Properties.DOORS_ALLOWPETATTACK &&
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
            if (IsBorderKeep)
                return;

            Health = Math.Min(Health + Math.Max(0, amount), MaxHealth);
            BroadcastDoorStatus();
        }

        public override void StartHealthRegeneration()
        {
            if (IsBorderKeep || m_healthRegenerationTimer == null || m_healthRegenerationTimer.IsAlive || Health >= MaxHealth)
                return;

            m_healthRegenerationTimer.Start(RepairIntervalMilliseconds);
        }

        protected override int HealthRegenerationTimerCallback(ECSGameTimer timer)
        {
            if (IsBorderKeep || HealthPercent >= 100)
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
            list.Add(player.Realm == Realm ? $"You select the {Name}. It belongs to your realm."
                : IsBorderKeep ? $"You select the {Name}. It belongs to an enemy realm!"
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
