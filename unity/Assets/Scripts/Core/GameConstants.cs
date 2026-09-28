namespace VolleyballCore
{
    /// <summary>Shared constants ported from the Python engine's module-level values.</summary>
    public static class GameConstants
    {
        // src/cards.py: HAND_SIZE
        public const int HandSize = 5;

        // src/game.py: POINTS_TO_WIN
        public const int PointsToWin = 15;

        // src/game.py: MAX_EXCHANGES -- safety cap to prevent infinite rallies
        public const int MaxExchanges = 200;

        // src/game.py: TIP_THRESHOLD -- any (effective) attack card <= this may be
        // declared a tip, front row only.
        public const int TipThreshold = 5;
    }
}
