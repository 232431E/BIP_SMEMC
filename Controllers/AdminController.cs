using BIP_SMEMC.Models;
using BIP_SMEMC.Services;
using Microsoft.AspNetCore.Mvc;

namespace BIP_SMEMC.Controllers
{
    [RequireAdmin]
    public class AdminController : Controller
    {
        private const string AdminEmail = "admin@nyp.sg";
        private readonly Supabase.Client _supabase;

        public AdminController(Supabase.Client supabase)
        {
            _supabase = supabase;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // 1. Fetch Users
            var users = (await _supabase.From<UserModel>().Get()).Models.OrderBy(x => x.Email).ToList();

            // 2. Fetch Industries for the Dropdowns
            var industries = await _supabase.From<IndustryModel>()
                .Order("name", Postgrest.Constants.Ordering.Ascending)
                .Get();

            // Pass to View via ViewBag
            ViewBag.Industries = industries.Models.Select(x => x.Name).ToList();

            return View(new AdminUsersViewModel { Users = users });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUser(string originalEmail, string email, string role, string industry, string fullName)
        {
            if (string.IsNullOrWhiteSpace(originalEmail)) return RedirectToAction(nameof(Index));

            var originalEmailKey = originalEmail.Trim().ToLower();
            var source = (await _supabase.From<UserModel>().Where(x => x.Email == originalEmailKey).Get()).Models.FirstOrDefault();

            if (source == null) return RedirectToAction(nameof(Index));

            var newEmail = (email ?? originalEmail).Trim().ToLower();
            var safeRole = string.Equals(newEmail, AdminEmail, StringComparison.OrdinalIgnoreCase)
                ? "Admin"
                : (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "User");

            // 3. Validate Industry against DB
            var industryExists = (await _supabase.From<IndustryModel>().Where(x => x.Name == industry).Get()).Models.Any();
            var safeIndustry = industryExists ? industry.Trim() : (source.Industries?.FirstOrDefault() ?? "Other");

            var updated = new UserModel
            {
                Email = newEmail,
                FullName = string.IsNullOrWhiteSpace(fullName) ? source.FullName : fullName.Trim(),
                Role = safeRole,
                Industries = new List<string> { safeIndustry },
                PasswordHash = source.PasswordHash
            };

            await _supabase.From<UserModel>().Where(x => x.Email == originalEmailKey).Delete();
            await _supabase.From<UserModel>().Insert(updated);

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUser(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return RedirectToAction(nameof(Index));
            if (string.Equals(email.Trim(), AdminEmail, StringComparison.OrdinalIgnoreCase)) return RedirectToAction(nameof(Index));

            await _supabase.From<UserModel>().Where(x => x.Email == email.Trim().ToLower()).Delete();
            return RedirectToAction(nameof(Index));
        }
    }
}