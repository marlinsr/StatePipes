using Microsoft.AspNetCore.Identity;

namespace StatePipes.Common.Internal
{
    internal class PasswordHasher
    {
        internal class DummyUser { }
        private static readonly DummyUser _user = new();
        private static readonly Microsoft.AspNetCore.Identity.PasswordHasher<DummyUser> _passwordHasher = new();
        public static string HashPassword(string password)
        {
            return _passwordHasher.HashPassword(_user, password);
        }
        public static bool VerifyPassword(string hashedPassword, string password)
        {
            var verificationResults = _passwordHasher.VerifyHashedPassword(_user, hashedPassword, password);
            return verificationResults == PasswordVerificationResult.Success;
        }
    }
}
