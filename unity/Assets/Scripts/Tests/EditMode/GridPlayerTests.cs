using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    public class GridPlayerTests
    {
        [TestCase(PlayerRole.Setter, false)]
        [TestCase(PlayerRole.Opp, true)]
        [TestCase(PlayerRole.Mb, true)]
        [TestCase(PlayerRole.Oh, true)]
        [TestCase(PlayerRole.Ds, true)]
        [TestCase(PlayerRole.Libero, false)]
        public void CanAttackExcludesSetterAndLibero(PlayerRole role, bool expected)
        {
            var player = new GridPlayer(role, 1);
            Assert.AreEqual(expected, player.CanAttack());
        }

        [Test]
        public void CanReceiveServeExcludesOnlySetter()
        {
            Assert.IsFalse(new GridPlayer(PlayerRole.Setter, 1).CanReceiveServe());
            Assert.IsTrue(new GridPlayer(PlayerRole.Libero, 6).CanReceiveServe());
        }

        [Test]
        public void ToStringMatchesPythonRepr()
        {
            var player = new GridPlayer(PlayerRole.Oh, 4);
            Assert.AreEqual("GridPlayer(OH, pos=4)", player.ToString());
        }
    }
}
