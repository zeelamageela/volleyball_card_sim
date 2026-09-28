using System;

namespace VolleyballCore
{
    /// <summary>Ported from src/cards.py's Card (a frozen dataclass) -- Python's string
    /// color ("red"/"black") becomes an enum here since nothing in the engine branches
    /// on color for gameplay purposes; it's cosmetic/display only, same as in Python.</summary>
    public enum CardColor
    {
        Red,
        Black,
    }

    /// <summary>Immutable value card, 1-10. Ported from src/cards.py's Card.</summary>
    public readonly struct Card : IEquatable<Card>
    {
        public int Value { get; }
        public CardColor Color { get; }

        public Card(int value, CardColor color)
        {
            Value = value;
            Color = color;
        }

        public override string ToString() =>
            $"{Value}{(Color == CardColor.Red ? "R" : "B")}";

        public bool Equals(Card other) => Value == other.Value && Color == other.Color;
        public override bool Equals(object obj) => obj is Card other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Value, Color);

        public static bool operator ==(Card left, Card right) => left.Equals(right);
        public static bool operator !=(Card left, Card right) => !left.Equals(right);
    }
}
