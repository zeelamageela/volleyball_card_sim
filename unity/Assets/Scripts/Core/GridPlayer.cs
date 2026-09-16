namespace VolleyballCore
{
    /// <summary>Ported from src/players.py's GridPlayer dataclass.</summary>
    public readonly struct GridPlayer
    {
        public PlayerRole Role { get; }
        public int Position { get; } // volleyball position 1-6

        public GridPlayer(PlayerRole role, int position)
        {
            Role = role;
            Position = position;
        }

        public bool CanAttack() => Role != PlayerRole.Libero && Role != PlayerRole.Setter;
        public bool CanReceiveServe() => Role != PlayerRole.Setter;
        public bool IsFrontRow() => Role.IsFrontRow();
        public bool IsBackRow() => Role.IsBackRow();

        public override string ToString() => $"GridPlayer({Role.DisplayName()}, pos={Position})";
    }
}
