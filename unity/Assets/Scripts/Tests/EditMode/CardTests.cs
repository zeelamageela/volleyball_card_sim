using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    public class CardTests
    {
        [Test]
        public void ToStringFormatsValueAndColorLetter()
        {
            Assert.AreEqual("7R", new Card(7, CardColor.Red).ToString());
            Assert.AreEqual("10B", new Card(10, CardColor.Black).ToString());
        }

        [Test]
        public void EqualityIsByValue()
        {
            var a = new Card(5, CardColor.Red);
            var b = new Card(5, CardColor.Red);
            var c = new Card(5, CardColor.Black);

            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
            Assert.AreNotEqual(a, c);
            Assert.IsTrue(a != c);
        }
    }
}
