using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>Ported from tests/test_game.py's TestResolveAttack (two-tier rule).</summary>
    public class AttackResolutionTests
    {
        [Test]
        public void KillWhenAttackGreaterThanBlock()
        {
            Assert.AreEqual(AttackOutcomeType.Kill, AttackResolution.ResolveAttack(7, 5));
        }

        [Test]
        public void KillOnUnblockedLane()
        {
            Assert.AreEqual(AttackOutcomeType.Kill, AttackResolution.ResolveAttack(1, 0));
        }

        [Test]
        public void KillExactlyGreater()
        {
            Assert.AreEqual(AttackOutcomeType.Kill, AttackResolution.ResolveAttack(10, 9));
        }

        [Test]
        public void DeflectOnExactTie()
        {
            Assert.AreEqual(AttackOutcomeType.Deflect, AttackResolution.ResolveAttack(5, 5));
        }

        [Test]
        public void StuffedWhenBlockHigherByOne()
        {
            Assert.AreEqual(AttackOutcomeType.Stuffed, AttackResolution.ResolveAttack(6, 7));
        }

        [Test]
        public void StuffedDiffLarge()
        {
            Assert.AreEqual(AttackOutcomeType.Stuffed, AttackResolution.ResolveAttack(1, 10));
        }

        [Test]
        public void GetDigDefenderRoleEvenIsAlwaysLibero()
        {
            Assert.AreEqual(PlayerRole.Libero, AttackResolution.GetDigDefenderRole(1, 6));
            Assert.AreEqual(PlayerRole.Libero, AttackResolution.GetDigDefenderRole(2, 10));
            Assert.AreEqual(PlayerRole.Libero, AttackResolution.GetDigDefenderRole(3, 8));
        }

        [Test]
        public void GetDigDefenderRoleOddSplitsByLane()
        {
            Assert.AreEqual(PlayerRole.Setter, AttackResolution.GetDigDefenderRole(1, 7));
            Assert.AreEqual(PlayerRole.Setter, AttackResolution.GetDigDefenderRole(2, 7));
            Assert.AreEqual(PlayerRole.Ds, AttackResolution.GetDigDefenderRole(3, 7));
        }
    }
}
