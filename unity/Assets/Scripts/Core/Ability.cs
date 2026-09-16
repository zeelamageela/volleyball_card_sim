using System;
using System.Collections.Generic;

namespace VolleyballCore
{
    /// <summary>Ported from src/abilities.py's Trigger constants class.</summary>
    public enum Trigger
    {
        OnServe,
        OnReceive,
        OnSet,
        OnQuickSet,
        OnAttack,
        OnBlock,
        OnBlockDeflection,
        OnDig,
        OnDigSuccess,
        OnDigFailure,
        OnChase,
        OnTip,
        OnRoster, // persistent team-level passive
    }

    /// <summary>Ported from src/abilities.py's EffectType constants class.</summary>
    public enum EffectType
    {
        AttackValueBonus,
        BlockValueBonus,
        AdjacentBlockBonus,
        PierceBlock,
        SetValueDelta,
        ServeValueBonus,
        ChaseCardBonus,
        DeflectDigThreshold,
        TipValueBonus,
        TipDigThreshold,
        SingleBlockOnly,
        DigThreshold,
        WipeBlock,
        NoChase,
        RollShot,
        HeavySpin,
        SeamShot,
        TipThresholdDelta,
        OverBlockBonus,
        HoldCard,
        HandPeek,
        ExchangeCard,
        HandSizeMod,
        SlideLanes,
        BackRowPierce,
        MinBlockerOnly,
        WildBlock,
        ForceHighBlock,
        DeckSwapOpponent,
        WideSpreadBonus,
        DrawAndAddBlock,
        SetterCover,
    }

    /// <summary>Ported from src/abilities.py's Ability dataclass.</summary>
    public sealed class Ability
    {
        public string AbilityName { get; }
        public Trigger Trigger { get; }

        /// <summary>e.g. "attack_card_value"; empty = always fires.</summary>
        public string ConditionField { get; }

        /// <summary>e.g. "5" or ">=7"; empty = always fires.</summary>
        public string ConditionValue { get; }

        public EffectType Effect { get; }
        public int EffectValue { get; }
        public bool IsActive { get; }
        public string Description { get; }

        public Ability(
            string abilityName,
            Trigger trigger,
            string conditionField,
            string conditionValue,
            EffectType effect,
            int effectValue,
            bool isActive = false,
            string description = "")
        {
            AbilityName = abilityName;
            Trigger = trigger;
            ConditionField = conditionField ?? "";
            ConditionValue = conditionValue ?? "";
            Effect = effect;
            EffectValue = effectValue;
            IsActive = isActive;
            Description = description ?? "";
        }

        /// <summary>
        /// Return true if this ability's condition is satisfied by context.
        /// ConditionValue supports comparison prefixes for numeric fields:
        ///   &gt;=N, &lt;=N, &gt;N, &lt;N, or plain equality (N). String fields
        /// (e.g. dig_type) always use case-insensitive equality.
        /// </summary>
        public bool ConditionMatches(IReadOnlyDictionary<string, object> context)
        {
            if (string.IsNullOrEmpty(ConditionField))
            {
                return true;
            }
            if (context == null || !context.TryGetValue(ConditionField, out object raw) || raw == null)
            {
                return false;
            }

            string cond = ConditionValue.Trim();
            if (int.TryParse(Convert.ToString(raw), out int val))
            {
                if (cond.StartsWith(">="))
                {
                    return int.TryParse(cond.Substring(2), out int n) && val >= n;
                }
                if (cond.StartsWith("<="))
                {
                    return int.TryParse(cond.Substring(2), out int n) && val <= n;
                }
                if (cond.StartsWith(">"))
                {
                    return int.TryParse(cond.Substring(1), out int n) && val > n;
                }
                if (cond.StartsWith("<"))
                {
                    return int.TryParse(cond.Substring(1), out int n) && val < n;
                }
                return int.TryParse(cond, out int eq) && val == eq;
            }
            return string.Equals(Convert.ToString(raw), ConditionValue, StringComparison.OrdinalIgnoreCase);
        }
    }
}
