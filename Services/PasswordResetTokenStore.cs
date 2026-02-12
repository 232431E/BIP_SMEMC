namespace BIP_SMEMC.Services
{
    public class PasswordResetTokenStore
    {
        private readonly Dictionary<string, (string Email, DateTime ExpiresAt)> _tokens = new();
        private readonly object _lock = new();

        public string CreateToken(string email, TimeSpan ttl)
        {
            var token = Guid.NewGuid().ToString("N");
            lock (_lock)
            {
                _tokens[token] = (email, DateTime.UtcNow.Add(ttl));
            }
            return token;
        }

        public bool TryConsumeToken(string token, out string email)
        {
            lock (_lock)
            {
                if (_tokens.TryGetValue(token, out var data) && data.ExpiresAt > DateTime.UtcNow)
                {
                    _tokens.Remove(token);
                    email = data.Email;
                    return true;
                }
            }

            email = string.Empty;
            return false;
        }
    }
}
