using StatePipes.Common.Internal;

namespace StatePipes.Test.Common
{
    [TestClass]
    public class PasswordHashTest
    {
        [TestMethod]
        public void TestHashing()
        {
            var originalString = "StatePipes123$";
            var hashedString = PasswordHasher.HashPassword(originalString);
            Assert.IsTrue(PasswordHasher.VerifyPassword(hashedString,originalString));
        }
    }
}
