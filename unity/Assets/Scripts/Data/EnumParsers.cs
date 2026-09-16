using System;
using System.Collections.Generic;
using VolleyballCore;

namespace VolleyballData
{
    /// <summary>
    /// String-to-enum parsing for CSV-sourced values. Core's Trigger/EffectType/
    /// PlayerRole are real C# enums (unlike Python's plain string constants), so
    /// this mapping step is the price of that type safety -- CSV columns still
    /// store the original lowercase/snake_case strings (e.g. "on_attack",
    /// "pierce_block").
    /// </summary>
    public static class EnumParsers
    {
        private static readonly Dictionary<string, Trigger> TriggerMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["on_serve"] = Trigger.OnServe,
            ["on_receive"] = Trigger.OnReceive,
            ["on_set"] = Trigger.OnSet,
            ["on_quick_set"] = Trigger.OnQuickSet,
            ["on_attack"] = Trigger.OnAttack,
            ["on_block"] = Trigger.OnBlock,
            ["on_block_deflection"] = Trigger.OnBlockDeflection,
            ["on_dig"] = Trigger.OnDig,
            ["on_dig_success"] = Trigger.OnDigSuccess,
            ["on_dig_failure"] = Trigger.OnDigFailure,
            ["on_chase"] = Trigger.OnChase,
            ["on_tip"] = Trigger.OnTip,
            ["on_roster"] = Trigger.OnRoster,
        };

        private static readonly Dictionary<string, EffectType> EffectMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["attack_value_bonus"] = EffectType.AttackValueBonus,
            ["block_value_bonus"] = EffectType.BlockValueBonus,
            ["adjacent_block_bonus"] = EffectType.AdjacentBlockBonus,
            ["pierce_block"] = EffectType.PierceBlock,
            ["set_value_delta"] = EffectType.SetValueDelta,
            ["serve_value_bonus"] = EffectType.ServeValueBonus,
            ["chase_card_bonus"] = EffectType.ChaseCardBonus,
            ["deflect_dig_threshold"] = EffectType.DeflectDigThreshold,
            ["tip_value_bonus"] = EffectType.TipValueBonus,
            ["tip_dig_threshold"] = EffectType.TipDigThreshold,
            ["single_block_only"] = EffectType.SingleBlockOnly,
            ["dig_threshold"] = EffectType.DigThreshold,
            ["wipe_block"] = EffectType.WipeBlock,
            ["no_chase"] = EffectType.NoChase,
            ["roll_shot"] = EffectType.RollShot,
            ["heavy_spin"] = EffectType.HeavySpin,
            ["seam_shot"] = EffectType.SeamShot,
            ["tip_threshold_delta"] = EffectType.TipThresholdDelta,
            ["over_block_bonus"] = EffectType.OverBlockBonus,
            ["hold_card"] = EffectType.HoldCard,
            ["hand_peek"] = EffectType.HandPeek,
            ["exchange_card"] = EffectType.ExchangeCard,
            ["hand_size_mod"] = EffectType.HandSizeMod,
            ["slide_lanes"] = EffectType.SlideLanes,
            ["back_row_pierce"] = EffectType.BackRowPierce,
            ["min_blocker_only"] = EffectType.MinBlockerOnly,
            ["wild_block"] = EffectType.WildBlock,
            ["force_high_block"] = EffectType.ForceHighBlock,
            ["deck_swap_opponent"] = EffectType.DeckSwapOpponent,
            ["wide_spread_bonus"] = EffectType.WideSpreadBonus,
            ["draw_and_add_block"] = EffectType.DrawAndAddBlock,
            ["setter_cover"] = EffectType.SetterCover,
        };

        // Mirrors abilities.py's _ROLE_MAP: lowercase CSV token -> canonical uppercase key.
        private static readonly Dictionary<string, PlayerRole> RoleMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["setter"] = PlayerRole.Setter,
            ["opp"] = PlayerRole.Opp,
            ["mb"] = PlayerRole.Mb,
            ["oh"] = PlayerRole.Oh,
            ["ds"] = PlayerRole.Ds,
            ["libero"] = PlayerRole.Libero,
        };

        public static Trigger ParseTrigger(string value)
        {
            if (TriggerMap.TryGetValue((value ?? "").Trim(), out var trigger))
            {
                return trigger;
            }
            throw new FormatException($"Unknown trigger '{value}'");
        }

        public static EffectType ParseEffect(string value)
        {
            if (EffectMap.TryGetValue((value ?? "").Trim(), out var effect))
            {
                return effect;
            }
            throw new FormatException($"Unknown effect '{value}'");
        }

        public static bool TryParseRole(string value, out PlayerRole role) =>
            RoleMap.TryGetValue((value ?? "").Trim(), out role);

        /// <summary>
        /// Canonical role key used for the "Name::ROLE" disambiguation lookup in
        /// player_cards.csv, mirroring Python's `_ROLE_MAP.get(role_str.lower(),
        /// role_str.upper())` -- falls back to the raw uppercased string for an
        /// unrecognized role rather than throwing (load_player_cards must accept
        /// any role token; only load_roster validates against the real enum).
        /// </summary>
        public static string CanonicalRoleKey(string roleStr)
        {
            string trimmed = (roleStr ?? "").Trim();
            return RoleMap.TryGetValue(trimmed, out var role) ? role.ToString().ToUpperInvariant() : trimmed.ToUpperInvariant();
        }
    }
}
