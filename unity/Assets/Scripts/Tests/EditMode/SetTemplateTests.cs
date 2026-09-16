using System.Linq;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>Ported from tests/test_game.py's TestSetterTemplates.</summary>
    public class SetTemplateTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void QuicksetTierIsFrontRowOnlyTwoLanes(int setValue)
        {
            var t = SetTemplate.Universal[setValue];
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, t.FrontLanes);
            Assert.IsEmpty(t.BackLanes);
            Assert.AreEqual(2, t.MaxAttackers);
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void MidTierIsAnyMixTwoLanes(int setValue)
        {
            var t = SetTemplate.Universal[setValue];
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, t.FrontLanes);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, t.BackLanes);
            Assert.AreEqual(2, t.MaxAttackers);
        }

        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        public void HighTierIsAnyMixThreeLanes(int setValue)
        {
            var t = SetTemplate.Universal[setValue];
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, t.FrontLanes);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, t.BackLanes);
            Assert.AreEqual(3, t.MaxAttackers);
        }

        [Test]
        public void UniversalTemplateCoversAllTenValues()
        {
            for (int v = 1; v <= 10; v++)
            {
                Assert.IsTrue(SetTemplate.Universal.ContainsKey(v), $"missing value {v}");
            }
        }

        [Test]
        public void BrokenPlayTemplateCoversAllTenValues()
        {
            for (int v = 1; v <= 10; v++)
            {
                Assert.IsTrue(SetTemplate.BrokenPlay.ContainsKey(v), $"missing value {v}");
            }
        }
    }
}
