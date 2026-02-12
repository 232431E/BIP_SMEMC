namespace BIP_SMEMC.Services
{
    public class UserPasswordMeta
    {
        public string PlainPassword { get; set; } = string.Empty;
        public bool TwoFactorEnabled { get; set; }
        public string TwoFactorMethod { get; set; } = "email";
        public string TwoFactorSecret { get; set; } = string.Empty;
    }

    public static class UserPasswordMetaCodec
    {
        public static UserPasswordMeta Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new UserPasswordMeta();
            }

            var parts = raw.Split('|');
            var meta = new UserPasswordMeta
            {
                PlainPassword = parts[0]
            };

            foreach (var part in parts.Skip(1))
            {
                var kv = part.Split(':', 2);
                if (kv.Length != 2)
                {
                    continue;
                }

                var key = kv[0].Trim().ToLowerInvariant();
                var val = kv[1].Trim();
                if (key == "2fa_enabled")
                {
                    meta.TwoFactorEnabled = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (key == "2fa_method")
                {
                    meta.TwoFactorMethod = val;
                }
                else if (key == "2fa_secret")
                {
                    meta.TwoFactorSecret = val;
                }
            }

            return meta;
        }

        public static string Serialize(UserPasswordMeta meta)
        {
            return $"{meta.PlainPassword}|2fa_enabled:{(meta.TwoFactorEnabled ? "1" : "0")}|2fa_method:{meta.TwoFactorMethod}|2fa_secret:{meta.TwoFactorSecret}";
        }
    }
}
