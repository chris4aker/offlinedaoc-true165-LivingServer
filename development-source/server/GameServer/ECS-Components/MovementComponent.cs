using System.Numerics;
using System.Threading;

namespace DOL.GS
{
    public class MovementComponent : IServiceObject
    {
        private const int SUBZONE_RELOCATION_CHECK_INTERVAL = 2000; // Subzone update is forced by OnPositionUpdate, so this doesn't need to be very frequent.

        protected Vector3 _ownerPosition;
        private bool _relocationCheckPending;
        private long _nextRelocationCheckTick;
        private int _turningDisabledCount;

        public GameLiving Owner { get; }
        private short _currentSpeed;
        public short CurrentSpeed
        {
            get => _currentSpeed;
            set
            {
                if (value != 0 || _currentSpeed != 0)
                    LastWalkTick = GameLoop.GameLoopTime;
                _currentSpeed = value;
            }
        }
        public short MaxSpeedBase { get; set; } // Currently unused for players.
        public long LastMovementTick { get; private set; }
        /// <summary>
        /// Owner 2026-11-10: the last tick the owner walked or stopped walking. Unlike LastMovementTick, turning on the
        /// spot does not count (bot party upkeep settles on this, BotTravelCastHold).
        /// </summary>
        public long LastWalkTick { get; private set; }
        public virtual short MaxSpeed => (short) Owner.GetModified(eProperty.MaxSpeed);
        public bool IsMoving => CurrentSpeed != 0;
        public bool IsTurningDisabled => Interlocked.CompareExchange(ref _turningDisabledCount, 0, 0) > 0 && !Owner.effectListComponent.ContainsEffectForEffectType(eEffect.SpeedOfSound);
        public ServiceObjectId ServiceObjectId { get; } = new(ServiceObjectType.MovementComponent);

        protected MovementComponent(GameLiving owner)
        {
            Owner = owner;
        }

        public static MovementComponent Create(GameLiving living)
        {
            if (living is GameNPC npc)
                return new NpcMovementComponent(npc);
            else if (living is GamePlayer player)
                return new PlayerMovementComponent(player);
            else
                return new MovementComponent(living);
        }

        public virtual void Tick()
        {
            if (Owner.ObjectState is not GameObject.eObjectState.Active)
            {
                RemoveFromServiceObjectStore();
                return;
            }

            TickInternal();
        }

        protected virtual void TickInternal()
        {
            if (!_relocationCheckPending && !GameServiceUtils.ShouldTick(_nextRelocationCheckTick))
                return;

            Owner.SubZoneObject.CheckForRelocation();
            _nextRelocationCheckTick = GameLoop.GameLoopTime + SUBZONE_RELOCATION_CHECK_INTERVAL;
            _relocationCheckPending = false;
        }

        public virtual void OnPositionUpdate()
        {
            _relocationCheckPending = true;
            LastMovementTick = GameLoop.GameLoopTime;
        }

        public virtual void DisableTurning(bool add)
        {
            if (add)
                Interlocked.Increment(ref _turningDisabledCount);
            else
                Interlocked.Decrement(ref _turningDisabledCount);
        }

        protected virtual void UpdatePosition() { }

        protected void AddToServiceObjectStore()
        {
            ServiceObjectStore.Add(this);
        }

        protected void RemoveFromServiceObjectStore()
        {
            ServiceObjectStore.Remove(this);
        }
    }
}
