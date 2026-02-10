using BIP_SMEMC.Models;
using System.Diagnostics;

namespace BIP_SMEMC.Services
{
    public class AccountService
    {
        private readonly Supabase.Client _supabase;

        public AccountService(Supabase.Client supabase)
        {
            _supabase = supabase;
        }
        public async Task UpdateUserPreferences(string email, List<string> industries, List<string> regions)
        {
            await _supabase
                .From<UserModel>()
                .Where(u => u.Email == email)
                .Update(new UserModel
                {
                    Industries = industries,
                    Regions = regions
                });
            Debug.WriteLine($"[DB] Preferences updated for {email}");
        }
    }
}
