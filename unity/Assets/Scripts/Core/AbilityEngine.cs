using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VolleyballCore
{
    /// <summary>
    /// Resolves passive ability effects for one team during a game. Ported from
    /// src/abilities.py's AbilityEngine.
    ///
    /// Keyed by PlayerRole so Rally can look up the right player for each phase
    /// without any string comparisons in the hot path.
    /// </summary>
    public sealed class AbilityEngine : IAbilityEngine
    {
        private readonly Dictionary<PlayerRole, PlayerCard> _cards;
        private int _pendingSetDelta;
        private int _pendingMbAttackBonus;
        private int _pendingTipThresholdDelta;
        private int _currentHandSize = 5; // updated per attack phase
        private readonly List<string> _events = new();

        public bool Verbose { get; set; }

        public AbilityEngine(Dictionary<PlayerRole, PlayerCard> cards) => _cards = cards;

        /// <summary>Reset per-game state. Call at the start of each new game.</summary>
        public void Reset()
        {
            _pendingSetDelta = 0;
            _pendingMbAttackBonus = 0;
            _pendingTipThresholdDelta = 0;
        }

        /// <summary>Return and clear all queued ability-trigger messages.</summary>
        public List<string> DrainEvents()
        {
            var events = new List<string>(_events);
            _events.Clear();
            return events;
        }

        private void Log(string msg) => _events.Add(msg);

        private void LogFired(string playerName, string abilityName, string desc) =>
            Log($"  * [{playerName}] {abilityName}: {desc}");

        // -- Phase queries --

        public int ServeValueBonus() =>
            Sum(PlayerRole.Setter, Trigger.OnServe, EffectType.ServeValueBonus, null);

        public int AttackValueBonus(PlayerRole role, int attackCardValue) =>
            Sum(role, Trigger.OnAttack, EffectType.AttackValueBonus, new Dictionary<string, object>
            {
                ["attack_card_value"] = attackCardValue,
                ["hand_size"] = _currentHandSize,
            });

        public bool PierceBlock(PlayerRole role, int attackCardValue) =>
            HasMatchingAbility(role, Trigger.OnAttack, EffectType.PierceBlock,
                new Dictionary<string, object> { ["attack_card_value"] = attackCardValue },
                "pierce block (block ignored)");

        public int BlockValueBonus(PlayerRole role) =>
            Sum(role, Trigger.OnBlock, EffectType.BlockValueBonus, null);

        /// <summary>Bonus added to the two lanes adjacent to the MB when MB is blocking.</summary>
        public int AdjacentBlockBonus(bool mbIsBlocking) =>
            mbIsBlocking ? Sum(PlayerRole.Mb, Trigger.OnBlock, EffectType.AdjacentBlockBonus, null) : 0;

        /// <summary>Call after a successful normal dig. Accumulates any pending set_value_delta
        /// so the next SET phase can consume it.</summary>
        public void RecordDigSuccess(PlayerRole role, DigType digType)
        {
            int delta = Sum(role, Trigger.OnDigSuccess, EffectType.SetValueDelta,
                new Dictionary<string, object> { ["dig_type"] = DigTypeString(digType) });
            _pendingSetDelta += delta;
        }

        public int ConsumeSetDelta()
        {
            int delta = _pendingSetDelta;
            _pendingSetDelta = 0;
            return delta;
        }

        public int OnSetBonus(int setCardValue = 0) =>
            Sum(PlayerRole.Setter, Trigger.OnSet, EffectType.SetValueDelta,
                new Dictionary<string, object> { ["set_card_value"] = setCardValue });

        /// <summary>Store MB quick-set attack bonus when MB is in the eligible lanes.</summary>
        public void ActivateQuickSet()
        {
            int bonus = Sum(PlayerRole.Mb, Trigger.OnQuickSet, EffectType.AttackValueBonus, null);
            _pendingMbAttackBonus += bonus;
        }

        public int ConsumeMbAttackBonus()
        {
            int b = _pendingMbAttackBonus;
            _pendingMbAttackBonus = 0;
            return b;
        }

        /// <summary>Flat bonus added to chase running total (on_chase, Libero + DS). Chase
        /// only ever happens on a failed serve reception now -- a dig never chases.</summary>
        public int ChaseBonus() =>
            Sum(PlayerRole.Libero, Trigger.OnChase, EffectType.ChaseCardBonus, null)
            + Sum(PlayerRole.Ds, Trigger.OnChase, EffectType.ChaseCardBonus, null);

        /// <summary>MB blocker makes deflections harder(+) or easier(-) for the attacker to
        /// control. Applied as a penalty to the attacker's dig target.</summary>
        public int DeflectDigThreshold() =>
            Sum(PlayerRole.Mb, Trigger.OnBlockDeflection, EffectType.DeflectDigThreshold, null);

        /// <summary>Raw ability value, same as every other Sum() hook here -- but Rally.cs's
        /// ResolveTip *subtracts* this from the effective tip value, not adds, since a tip
        /// is checked backwards from a hit (lower is the attacker's advantage: harder to
        /// block, harder to dig). A positive CSV value here still means "good for the
        /// attacker," it's just applied as a reduction at the call site.</summary>
        public int TipValueBonus(PlayerRole role) =>
            Sum(role, Trigger.OnTip, EffectType.TipValueBonus, null);

        /// <summary>Same inversion as TipValueBonus -- Rally.cs's ResolveTip subtracts this
        /// from the defender's effective dig value (lower is the defender's advantage when
        /// digging a tip), so a positive CSV value still reads as "good for the
        /// defender."</summary>
        public int TipDigThreshold(PlayerRole role) =>
            Sum(role, Trigger.OnTip, EffectType.TipDigThreshold, null);

        public bool SingleBlockOnly(PlayerRole role, int attackCardValue) =>
            HasMatchingAbility(role, Trigger.OnAttack, EffectType.SingleBlockOnly,
                new Dictionary<string, object> { ["attack_card_value"] = attackCardValue },
                "single block only (double block ignored)");

        /// <summary>Bonus added to the effective attack value for dig comparison only.
        /// Positive = attack is harder to dig.</summary>
        public int AttackDigThreshold(PlayerRole role) =>
            Sum(role, Trigger.OnAttack, EffectType.DigThreshold, null);

        public bool WipeBlock(PlayerRole role, int attackCardValue) =>
            HasMatchingAbility(role, Trigger.OnAttack, EffectType.WipeBlock,
                new Dictionary<string, object> { ["attack_card_value"] = attackCardValue },
                "wipe off block (instant point)");

        public bool RollShot(PlayerRole role, int attackCardValue) =>
            HasMatchingAbility(role, Trigger.OnAttack, EffectType.RollShot,
                new Dictionary<string, object> { ["attack_card_value"] = attackCardValue },
                "roll shot (goes over block, dug like tip)");

        public bool HeavySpin(PlayerRole role, int attackCardValue) =>
            HasMatchingAbility(role, Trigger.OnAttack, EffectType.HeavySpin,
                new Dictionary<string, object> { ["attack_card_value"] = attackCardValue },
                "heavy spin (block ignored, no chase)");

        public bool SeamShot(PlayerRole role, int attackCardValue) =>
            HasMatchingAbility(role, Trigger.OnAttack, EffectType.SeamShot,
                new Dictionary<string, object> { ["attack_card_value"] = attackCardValue },
                "seam shot (deflect = instant win)");

        public bool SlideLanes(PlayerRole role, int attackCardValue) =>
            HasMatchingAbility(role, Trigger.OnAttack, EffectType.SlideLanes,
                new Dictionary<string, object> { ["attack_card_value"] = attackCardValue },
                "slide lanes eligible");

        public bool BackRowPierce(PlayerRole role, int attackCardValue) =>
            HasMatchingAbility(role, Trigger.OnAttack, EffectType.BackRowPierce,
                new Dictionary<string, object> { ["attack_card_value"] = attackCardValue },
                "back-row pierce (block ignored)");

        public bool MinBlockerOnly(PlayerRole role, int attackCardValue) =>
            HasMatchingAbility(role, Trigger.OnAttack, EffectType.MinBlockerOnly,
                new Dictionary<string, object> { ["attack_card_value"] = attackCardValue },
                "min blocker only (worst block card counts)");

        /// <summary>Threshold for wild block (0 if not available). Cards &lt;= threshold
        /// can block any lane.</summary>
        public int WildBlockThreshold(PlayerRole role) =>
            FirstMatchingEffectValue(role, Trigger.OnBlock, EffectType.WildBlock, null);

        /// <summary>N when player has wide_spread_bonus N: fires when |card1-card2| &gt;= N,
        /// adding +N to block.</summary>
        public int WideSpreadBonus(PlayerRole role) =>
            FirstMatchingEffectValue(role, Trigger.OnBlock, EffectType.WideSpreadBonus, null);

        /// <summary>Threshold for force_high_block (0 if not active). Blocks &lt;= threshold
        /// are ignored.</summary>
        public int ForceHighBlockThreshold(PlayerRole role, int attackCardValue) =>
            FirstMatchingEffectValue(role, Trigger.OnAttack, EffectType.ForceHighBlock,
                new Dictionary<string, object> { ["attack_card_value"] = attackCardValue });

        /// <summary>True if this dig success triggers opponent hand disruption.</summary>
        public bool DeckSwapOpponentOnDig(DigType digType)
        {
            var ctx = new Dictionary<string, object> { ["dig_type"] = DigTypeString(digType) };
            foreach (var card in _cards.Values)
            {
                foreach (var a in card.Abilities)
                {
                    if (a.Effect == EffectType.DeckSwapOpponent && a.Trigger == Trigger.OnDigSuccess
                        && !a.IsActive && a.ConditionMatches(ctx))
                    {
                        if (Verbose)
                        {
                            LogFired(card.PlayerName, a.AbilityName, "deck swap (opponent discards hand)");
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Call after the setter plays their card. Accumulates tip_threshold_delta.</summary>
        public void ActivateTipThreshold(int setCardValue)
        {
            int delta = Sum(PlayerRole.Setter, Trigger.OnSet, EffectType.TipThresholdDelta,
                new Dictionary<string, object> { ["set_card_value"] = setCardValue });
            _pendingTipThresholdDelta += delta;
        }

        public int ConsumeTipThresholdDelta()
        {
            int delta = _pendingTipThresholdDelta;
            _pendingTipThresholdDelta = 0;
            return delta;
        }

        /// <summary>Bonus added to the defender's effective dig card value. Positive = dig
        /// target is reduced by this much (easier to dig).</summary>
        public int DefenderDigThreshold(PlayerRole role) =>
            Sum(role, Trigger.OnDig, EffectType.DigThreshold, null);

        /// <summary>
        /// Minimum dig card value needed for an adjacent player (Libero or DS) to
        /// intercept a dig that would otherwise go to the Setter. 0 if no such ability
        /// is present. When multiple players have the ability, the lowest threshold wins.
        /// </summary>
        public int SetterCoverThreshold()
        {
            var thresholds = new List<int>();
            foreach (var role in new[] { PlayerRole.Libero, PlayerRole.Ds })
            {
                if (!_cards.TryGetValue(role, out var card))
                {
                    continue;
                }
                foreach (var a in card.Abilities)
                {
                    if (a.Effect == EffectType.SetterCover && a.Trigger == Trigger.OnDig && !a.IsActive)
                    {
                        thresholds.Add(a.EffectValue);
                        if (Verbose)
                        {
                            LogFired(card.PlayerName, a.AbilityName, $"setter cover active (threshold={a.EffectValue})");
                        }
                    }
                }
            }
            return thresholds.Count > 0 ? thresholds.Min() : 0;
        }

        /// <summary>Number of cards to draw from deck and add to block (0 if none).</summary>
        public int DrawAndAddBlock(PlayerRole role)
        {
            if (!_cards.TryGetValue(role, out var card))
            {
                return 0;
            }
            foreach (var a in card.Abilities)
            {
                if (a.Effect == EffectType.DrawAndAddBlock && a.Trigger == Trigger.OnBlock && !a.IsActive)
                {
                    if (Verbose)
                    {
                        LogFired(card.PlayerName, a.AbilityName, $"draw {a.EffectValue} card(s) and add to block");
                    }
                    return a.EffectValue;
                }
            }
            return 0;
        }

        /// <summary>Update current attacker hand size for hand-exhaustion condition checks.</summary>
        public void SetHandSize(int n) => _currentHandSize = n;

        /// <summary>Bonus to attack value when the lane was double-blocked.</summary>
        public int OverBlockBonus(PlayerRole role, int attackCardValue, bool isDoubleBlocked)
        {
            if (!isDoubleBlocked)
            {
                return 0;
            }
            return Sum(role, Trigger.OnAttack, EffectType.OverBlockBonus, new Dictionary<string, object>
            {
                ["attack_card_value"] = attackCardValue,
                ["hand_size"] = _currentHandSize,
            });
        }

        public bool HoldCardCheck(PlayerRole role, int attackCardValue) =>
            HasMatchingAbility(role, Trigger.OnAttack, EffectType.HoldCard, new Dictionary<string, object>
            {
                ["attack_card_value"] = attackCardValue,
                ["hand_size"] = _currentHandSize,
            }, "hold card (attack card returned to hand)");

        /// <summary>Cards to peek from top of deck before committing attack cards. Dormant --
        /// not yet consumed anywhere (matches Python's "Future: requires strategy AI support").</summary>
        public int HandPeekCount()
        {
            int total = 0;
            foreach (var role in new[] { PlayerRole.Oh, PlayerRole.Mb, PlayerRole.Opp })
            {
                total += Sum(role, Trigger.OnAttack, EffectType.HandPeek,
                    new Dictionary<string, object> { ["hand_size"] = _currentHandSize });
            }
            return total;
        }

        /// <summary>True if any attacking player on this team has the exchange_card ability.</summary>
        public bool ExchangeCardEligible()
        {
            foreach (var role in new[] { PlayerRole.Oh, PlayerRole.Mb, PlayerRole.Opp })
            {
                if (!_cards.TryGetValue(role, out var card))
                {
                    continue;
                }
                if (card.Abilities.Any(a => a.Effect == EffectType.ExchangeCard && a.Trigger == Trigger.OnAttack && !a.IsActive))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Net change to HandSize for this team (scans all on_roster abilities).</summary>
        public int HandSizeModifier() =>
            _cards.Values
                .SelectMany(pc => pc.Abilities)
                .Where(a => a.Effect == EffectType.HandSizeMod && a.Trigger == Trigger.OnRoster && !a.IsActive)
                .Sum(a => a.EffectValue);

        // -- Internal helpers --

        private static string DigTypeString(DigType digType) => digType == DigType.Tip ? "tip" : "normal";

        private int Sum(PlayerRole role, Trigger trigger, EffectType effect, IReadOnlyDictionary<string, object> context)
        {
            if (!_cards.TryGetValue(role, out var card))
            {
                return 0;
            }
            int total = 0;
            foreach (var a in card.Abilities)
            {
                if (a.Trigger == trigger && a.Effect == effect && !a.IsActive && a.ConditionMatches(context))
                {
                    total += a.EffectValue;
                    if (Verbose && a.EffectValue != 0)
                    {
                        string sign = a.EffectValue > 0 ? $"+{a.EffectValue}" : a.EffectValue.ToString();
                        Log($"  * [{card.PlayerName}] {a.AbilityName}: {sign} {EffectDisplayName(effect)}");
                    }
                }
            }
            return total;
        }

        private bool HasMatchingAbility(
            PlayerRole role, Trigger trigger, EffectType effect,
            IReadOnlyDictionary<string, object> context, string logDescription)
        {
            if (!_cards.TryGetValue(role, out var card))
            {
                return false;
            }
            foreach (var a in card.Abilities)
            {
                if (a.Effect == effect && a.Trigger == trigger && !a.IsActive && a.ConditionMatches(context))
                {
                    if (Verbose)
                    {
                        LogFired(card.PlayerName, a.AbilityName, logDescription);
                    }
                    return true;
                }
            }
            return false;
        }

        private int FirstMatchingEffectValue(
            PlayerRole role, Trigger trigger, EffectType effect, IReadOnlyDictionary<string, object> context)
        {
            if (!_cards.TryGetValue(role, out var card))
            {
                return 0;
            }
            foreach (var a in card.Abilities)
            {
                if (a.Effect == effect && a.Trigger == trigger && !a.IsActive
                    && (context == null || a.ConditionMatches(context)))
                {
                    return a.EffectValue;
                }
            }
            return 0;
        }

        private static string EffectDisplayName(EffectType effect)
        {
            string name = effect.ToString();
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c) && i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
