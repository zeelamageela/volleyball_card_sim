using System.Collections.Generic;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>Ported behavior checks for src/abilities.py's Ability.condition_matches.</summary>
    public class AbilityTests
    {
        private static Ability Make(string conditionField, string conditionValue) =>
            new("Test", Trigger.OnAttack, conditionField, conditionValue, EffectType.AttackValueBonus, 1);

        [Test]
        public void EmptyConditionFieldAlwaysMatches()
        {
            var ability = Make("", "");
            Assert.IsTrue(ability.ConditionMatches(new Dictionary<string, object>()));
            Assert.IsTrue(ability.ConditionMatches(null));
        }

        [Test]
        public void GreaterOrEqualOperator()
        {
            var ability = Make("attack_card_value", ">=7");
            Assert.IsTrue(ability.ConditionMatches(Ctx(7)));
            Assert.IsTrue(ability.ConditionMatches(Ctx(10)));
            Assert.IsFalse(ability.ConditionMatches(Ctx(6)));
        }

        [Test]
        public void LessOrEqualOperator()
        {
            var ability = Make("attack_card_value", "<=3");
            Assert.IsTrue(ability.ConditionMatches(Ctx(3)));
            Assert.IsFalse(ability.ConditionMatches(Ctx(4)));
        }

        [Test]
        public void GreaterThanOperator()
        {
            var ability = Make("attack_card_value", ">6");
            Assert.IsTrue(ability.ConditionMatches(Ctx(7)));
            Assert.IsFalse(ability.ConditionMatches(Ctx(6)));
        }

        [Test]
        public void LessThanOperator()
        {
            var ability = Make("attack_card_value", "<4");
            Assert.IsTrue(ability.ConditionMatches(Ctx(3)));
            Assert.IsFalse(ability.ConditionMatches(Ctx(4)));
        }

        [Test]
        public void PlainEqualityOperator()
        {
            var ability = Make("attack_card_value", "5");
            Assert.IsTrue(ability.ConditionMatches(Ctx(5)));
            Assert.IsFalse(ability.ConditionMatches(Ctx(6)));
        }

        [Test]
        public void MissingContextFieldFailsToMatch()
        {
            var ability = Make("attack_card_value", ">=5");
            Assert.IsFalse(ability.ConditionMatches(new Dictionary<string, object>()));
        }

        [Test]
        public void StringFieldUsesCaseInsensitiveEquality()
        {
            var ability = Make("dig_type", "tip");
            Assert.IsTrue(ability.ConditionMatches(new Dictionary<string, object> { ["dig_type"] = "tip" }));
            Assert.IsTrue(ability.ConditionMatches(new Dictionary<string, object> { ["dig_type"] = "TIP" }));
            Assert.IsFalse(ability.ConditionMatches(new Dictionary<string, object> { ["dig_type"] = "normal" }));
        }

        private static Dictionary<string, object> Ctx(int attackCardValue) =>
            new() { ["attack_card_value"] = attackCardValue };
    }
}
