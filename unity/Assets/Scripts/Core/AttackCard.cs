namespace VolleyballCore
{
    /// <summary>Ported from src/game.py's AttackCard dataclass.</summary>
    public sealed class AttackCard
    {
        public Card Card { get; }
        public AttackPosition Position { get; }

        /// <summary>True = drawn from deck face-down; value hidden during block/lane decisions.</summary>
        public bool Blind { get; }

        public AttackCard(Card card, AttackPosition position, bool blind = false)
        {
            Card = card;
            Position = position;
            Blind = blind;
        }

        public bool IsBackRow() => Position == AttackPosition.Back;
    }
}
