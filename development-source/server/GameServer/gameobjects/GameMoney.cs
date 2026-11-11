using System.Collections.Generic;

namespace DOL.GS
{
    public class GameMoney: GameStaticItemTimed
    {
        private const string DEFAULT_NAME = "bag of coins";
        private static readonly HashSet<string> _names = [DEFAULT_NAME, "small chest", "large chest", "some copper coins"];

        public long Value { get; set; } // In copper.
        public int Mithril => Money.GetMithril(Value);
        public int Platinum => Money.GetPlatinum(Value);
        public int Gold => Money.GetGold(Value);
        public int Silver => Money.GetSilver(Value);
        public int Copper => Money.GetCopper(Value);

        public GameMoney(long value) : base()
        {
            Level = 0;
            Model = 488;
            Realm = 0;
            Value = value;
            Name = DEFAULT_NAME;
        }

        public GameMoney(long value, GameObject dropper) : this(value)
        {
            X = dropper.X;
            Y = dropper.Y;
            Z = dropper.Z;
            Heading = dropper.Heading;
            CurrentRegion = dropper.CurrentRegion;
        }

        public static bool IsItemMoney(string name)
        {
            return _names.Contains(name);
        }

        public override TryPickUpResult TryAutoPickUp(IGameStaticItemOwner itemOwner)
        {
            lock (_pickUpLock)
            {
                // owner 2026-11-10: a coin already taken (see GameStaticItemTimed._pickedUp) is never paid twice
                if (ObjectState is eObjectState.Deleted || _pickedUp)
                    return TryPickUpResult.DoesNotWant;
                TryPickUpResult result = itemOwner.TryAutoPickUpMoney(this);
                if (result is TryPickUpResult.Success)
                    _pickedUp = true;
                return result;
            }
        }

        public override TryPickUpResult TryPickUp(GamePlayer source, IGameStaticItemOwner itemOwner)
        {
            lock (_pickUpLock)
            {
                if (ObjectState is eObjectState.Deleted || _pickedUp)
                    return TryPickUpResult.DoesNotWant;
                TryPickUpResult result = itemOwner.TryPickUpMoney(source, this);
                if (result is TryPickUpResult.Success)
                    _pickedUp = true;
                return result;
            }
        }
    }
}
