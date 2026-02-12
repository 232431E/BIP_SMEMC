using BIP_SMEMC.Models;
using BIP_SMEMC.Services;
using DocumentFormat.OpenXml.Drawing.Charts;
using Microsoft.AspNetCore.Mvc;
using OtpNet;
using System;
using System.Text.RegularExpressions;

namespace BIP_SMEMC.Controllers
{
    public class AccountController : Controller
    {
        private const string AdminEmail = "admin@nyp.sg";
        private const string AdminPassword = "Admin123!";

        private readonly Supabase.Client _supabase;
        private readonly EmailService _emailService;
        private readonly PasswordResetTokenStore _resetTokenStore;

        public AccountController(Supabase.Client supabase, EmailService emailService, PasswordResetTokenStore resetTokenStore)
        {
            _supabase = supabase;
            _emailService = emailService;
            _resetTokenStore = resetTokenStore;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (!string.IsNullOrWhiteSpace(HttpContext.Session.GetString("UserEmail")))
            {
                return RedirectToAction("Index", "Dashboard");
            }


            return View(new LoginViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var email = model.Email.Trim().ToLower();
            var user = (await _supabase.From<UserModel>().Where(x => x.Email == email).Get()).Models.FirstOrDefault();
            if (user != null)
            {
                var meta = UserPasswordMetaCodec.Parse(user.PasswordHash);
                if (!string.Equals(model.Password, meta.PlainPassword, StringComparison.Ordinal))
                {
                    ViewBag.ErrorMessage = "Invalid email or password.";
                    return View(model);
                }

                if (meta.TwoFactorEnabled)
                {
                    HttpContext.Session.SetString("PendingUserEmail", user.Email);
                    HttpContext.Session.SetString("PendingUserRole", IsAdminUser(user) ? "Admin" : "User");
                    HttpContext.Session.SetString("Pending2FAMethod", meta.TwoFactorMethod);
                    HttpContext.Session.SetString("Pending2FASecret", meta.TwoFactorSecret ?? string.Empty);

                    if (string.Equals(meta.TwoFactorMethod, "email", StringComparison.OrdinalIgnoreCase))
                    {
                        var code = Random.Shared.Next(100000, 999999).ToString();
                        HttpContext.Session.SetString("Pending2FAEmailCode", code);
                        HttpContext.Session.SetString("Pending2FAEmailCodeExpiry", DateTime.UtcNow.AddMinutes(5).ToString("O"));
                        await _emailService.SendTwoFactorCodeAsync(user.Email, code);
                    }

                    return RedirectToAction(nameof(VerifyTwoFactor));
                }

                HttpContext.Session.SetString("UserEmail", user.Email);
                HttpContext.Session.SetString("UserRole", IsAdminUser(user) ? "Admin" : "User");
                return IsAdminUser(user)
                    ? RedirectToAction("Index", "Admin")
                    : RedirectToAction("Index", "Dashboard");
            }

            if (string.Equals(email, AdminEmail, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(model.Password, AdminPassword, StringComparison.Ordinal))
            {
                HttpContext.Session.SetString("UserEmail", AdminEmail);
                HttpContext.Session.SetString("UserRole", "Admin");
                return RedirectToAction("Index", "Admin");
            }

            ViewBag.ErrorMessage = "Invalid email or password.";
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Signup()
        {
            var industries = await _supabase.From<IndustryModel>().Order("name", Postgrest.Constants.Ordering.Ascending).Get();
            ViewBag.Industries = industries.Models.Select(x => x.Name).ToList();
            return View(new SignupViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Signup(SignupViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var industries = await _supabase.From<IndustryModel>().Order("name", Postgrest.Constants.Ordering.Ascending).Get();
                ViewBag.Industries = industries.Models.Select(x => x.Name).ToList();
                return View(model);
            }

            if (!PasswordIsStrong(model.Password))
            {
                ModelState.AddModelError(nameof(model.Password), "Password must include upper, lower, and digit.");
                return View(model);
            }

            var email = model.Email.Trim().ToLower();
            if (string.Equals(email, AdminEmail, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.Email), "This email is reserved.");
                return View(model);
            }

            // --- FIX: Check Supabase for valid industry ---
            var industryCheck = await _supabase
                .From<IndustryModel>()
                .Where(x => x.Name == model.Industry)
                .Get();

            if (!industryCheck.Models.Any())
            {
                ModelState.AddModelError(nameof(model.Industry), "Please select a valid industry.");
                // OPTIONAL: Reload industries for the view dropdown if you are passing them via ViewBag
                return View(model);
            }

            var existing = (await _supabase.From<UserModel>().Where(x => x.Email == email).Get()).Models.FirstOrDefault();
            if (existing != null)
            {
                ModelState.AddModelError(nameof(model.Email), "Email already exists.");
                return View(model);
            }

            var passwordMeta = UserPasswordMetaCodec.Serialize(new UserPasswordMeta
            {
                PlainPassword = model.Password,
                TwoFactorEnabled = false,
                TwoFactorMethod = "email",
                TwoFactorSecret = string.Empty
            });

            await _supabase.From<UserModel>().Insert(new UserModel
            {
                Email = email,
                PasswordHash = passwordMeta,
                FullName = model.Username.Trim(),
                Industries = new List<string> { model.Industry },
                Role = "User"
            });

            TempData["SuccessMessage"] = "Signup successful. Please login.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View(new ForgotPasswordViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var email = model.Email.Trim().ToLower();
            var user = (await _supabase.From<UserModel>().Where(x => x.Email == email).Get()).Models.FirstOrDefault();
            if (user != null)
            {
                var token = _resetTokenStore.CreateToken(email, TimeSpan.FromMinutes(30));
                var link = $"{Request.Scheme}://{Request.Host}/Account/ResetPassword?token={token}";
                await _emailService.SendPasswordResetEmailAsync(email, link);
            }

            TempData["SuccessMessage"] = "An email reset link was sent.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult ResetPassword(string? token)
        {
            var vm = new ResetPasswordViewModel
            {
                AccessToken = token ?? string.Empty
            };
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!PasswordIsStrong(model.NewPassword))
            {
                ModelState.AddModelError(nameof(model.NewPassword), "Password must include upper, lower, and digit.");
                return View(model);
            }

            if (!_resetTokenStore.TryConsumeToken(model.AccessToken, out var email))
            {
                ModelState.AddModelError(string.Empty, "Reset token is invalid or expired.");
                return View(model);
            }

            var user = (await _supabase.From<UserModel>().Where(x => x.Email == email).Get()).Models.FirstOrDefault();
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "User not found.");
                return View(model);
            }

            var meta = UserPasswordMetaCodec.Parse(user.PasswordHash);
            meta.PlainPassword = model.NewPassword;
            var updatedUser = new UserModel
            {
                Email = user.Email,
                FullName = user.FullName,
                Industries = user.Industries ?? new List<string> { "Other" },
                Role = user.Role,
                PasswordHash = UserPasswordMetaCodec.Serialize(meta)
            };
            await ReplaceUser(user.Email, updatedUser);

            TempData["SuccessMessage"] = "Password reset successful.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult VerifyTwoFactor()
        {
            if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("PendingUserEmail")))
            {
                return RedirectToAction(nameof(Login));
            }

            ViewBag.Method = HttpContext.Session.GetString("Pending2FAMethod") ?? "email";
            return View(new TwoFactorVerifyViewModel());
        }

        [HttpPost]
        public IActionResult VerifyTwoFactor(TwoFactorVerifyViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Method = HttpContext.Session.GetString("Pending2FAMethod") ?? "email";
                return View(model);
            }

            var email = HttpContext.Session.GetString("PendingUserEmail");
            var role = HttpContext.Session.GetString("PendingUserRole") ?? "User";
            var method = HttpContext.Session.GetString("Pending2FAMethod") ?? "email";

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction(nameof(Login));
            }

            if (string.Equals(method, "authenticator", StringComparison.OrdinalIgnoreCase))
            {
                var secret = HttpContext.Session.GetString("Pending2FASecret") ?? string.Empty;
                var totp = new Totp(Base32Encoding.ToBytes(secret));
                if (!totp.VerifyTotp(model.Code, out _, new VerificationWindow(1, 1)))
                {
                    ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
                    ViewBag.Method = method;
                    return View(model);
                }
            }
            else
            {
                var savedCode = HttpContext.Session.GetString("Pending2FAEmailCode");
                var expiryRaw = HttpContext.Session.GetString("Pending2FAEmailCodeExpiry");
                var expiry = DateTime.TryParse(expiryRaw, out var d) ? d : DateTime.MinValue;
                if (!string.Equals(savedCode, model.Code, StringComparison.Ordinal) || DateTime.UtcNow > expiry)
                {
                    ModelState.AddModelError(string.Empty, "Invalid or expired email code.");
                    ViewBag.Method = method;
                    return View(model);
                }
            }

            HttpContext.Session.SetString("UserEmail", email);
            HttpContext.Session.SetString("UserRole", role);
            ClearPendingTwoFactorState();
            return RedirectToAction("Index", "Dashboard");
        }

        [RequireAppAuth]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var email = HttpContext.Session.GetString("UserEmail")!;
            var role = HttpContext.Session.GetString("UserRole") ?? "User";
            var user = (await _supabase.From<UserModel>().Where(x => x.Email == email).Get()).Models.FirstOrDefault();
            var industries = await _supabase.From<IndustryModel>().Order("name", Postgrest.Constants.Ordering.Ascending).Get();
            ViewBag.Industries = industries.Models.Select(x => x.Name).ToList();

            var model = new ProfileViewModel
            {
                Email = email,
                Role = role,
                Industry = user?.Industries?.FirstOrDefault() ?? "Other"
            };

            return View(model);
        }

        [RequireAppAuth]
        [HttpPost]
        public async Task<IActionResult> Profile(ProfileViewModel model)
        {
            var email = HttpContext.Session.GetString("UserEmail")!;
            var role = HttpContext.Session.GetString("UserRole") ?? "User";

            // --- FIX: Check Supabase for valid industry ---
            var industryCheck = await _supabase
                .From<IndustryModel>()
                .Where(x => x.Name == model.Industry)
                .Get();

            if (!industryCheck.Models.Any())
            {
                ModelState.AddModelError(nameof(model.Industry), "Please select a valid industry.");
            }

            var newPassword = model.NewPassword?.Trim() ?? string.Empty;
            var confirmPassword = model.ConfirmPassword?.Trim() ?? string.Empty;
            var wantsPasswordChange = !string.IsNullOrWhiteSpace(newPassword) || !string.IsNullOrWhiteSpace(confirmPassword);

            if (wantsPasswordChange && (string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmPassword)))
            {
                ModelState.AddModelError(string.Empty, "Enter both new password and confirm password, or leave both blank.");
            }
            else if (wantsPasswordChange && !string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                ModelState.AddModelError(string.Empty, "Passwords do not match.");
            }
            else if (wantsPasswordChange && !PasswordIsStrong(newPassword))
            {
                ModelState.AddModelError(nameof(model.NewPassword), "Password must include upper, lower, and digit.");
            }

            model.Email = email;
            model.Role = role;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = (await _supabase.From<UserModel>().Where(x => x.Email == email).Get()).Models.FirstOrDefault();

            if (user == null)
            {
                var fallbackPassword = string.Equals(email, AdminEmail, StringComparison.OrdinalIgnoreCase)
                    ? AdminPassword
                    : "Temp1234";

                user = new UserModel
                {
                    Email = email,
                    FullName = email.Split('@')[0],
                    Role = role,
                    Industries = new List<string> { model.Industry },
                    PasswordHash = UserPasswordMetaCodec.Serialize(new UserPasswordMeta
                    {
                        PlainPassword = fallbackPassword,
                        TwoFactorEnabled = false,
                        TwoFactorMethod = "email",
                        TwoFactorSecret = string.Empty
                    })
                };
                await _supabase.From<UserModel>().Insert(user);
            }
            else if (IsAdminUser(user) && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                HttpContext.Session.SetString("UserRole", "Admin");
                role = "Admin";
            }

            var meta = UserPasswordMetaCodec.Parse(user.PasswordHash);
            if (wantsPasswordChange)
            {
                meta.PlainPassword = newPassword;
            }

            var updatedUser = new UserModel
            {
                Email = user.Email,
                FullName = user.FullName,
                Industries = new List<string> { model.Industry },
                Role = user.Role,
                PasswordHash = UserPasswordMetaCodec.Serialize(meta)
            };
            await ReplaceUser(user.Email, updatedUser);

            TempData["SuccessMessage"] = "Profile Updated Successfully";
            return RedirectToAction(nameof(Profile));
        }

        [RequireAppAuth]
        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            var email = HttpContext.Session.GetString("UserEmail")!;
            var role = HttpContext.Session.GetString("UserRole") ?? "User";
            var user = (await _supabase.From<UserModel>().Where(x => x.Email == email).Get()).Models.FirstOrDefault();
            if (user == null)
            {
                user = new UserModel
                {
                    Email = email,
                    FullName = email.Split('@')[0],
                    Role = role,
                    Industries = new List<string> { "Other" },
                    PasswordHash = UserPasswordMetaCodec.Serialize(new UserPasswordMeta
                    {
                        PlainPassword = string.Equals(email, AdminEmail, StringComparison.OrdinalIgnoreCase) ? AdminPassword : "Temp1234",
                        TwoFactorEnabled = false,
                        TwoFactorMethod = "email",
                        TwoFactorSecret = string.Empty
                    })
                };
                await _supabase.From<UserModel>().Insert(user);
            }

            var meta = UserPasswordMetaCodec.Parse(user.PasswordHash);
            var pendingSecret = HttpContext.Session.GetString("PendingAuthenticatorSecret");
            var secretForUri = string.IsNullOrWhiteSpace(pendingSecret) ? meta.TwoFactorSecret : pendingSecret;
            var uri = string.IsNullOrWhiteSpace(secretForUri) ? null : BuildOtpAuthUri(user.Email, secretForUri);

            return View(new AccountSettingsViewModel
            {
                Email = user.Email,
                Role = user.Role,
                Industry = user.Industries?.FirstOrDefault() ?? "",
                IsTwoFactorEnabled = meta.TwoFactorEnabled,
                TwoFactorMethod = meta.TwoFactorMethod,
                AuthenticatorSecret = secretForUri,
                AuthenticatorUri = uri
            });
        }

        [RequireAppAuth]
        [HttpPost]
        public async Task<IActionResult> EnableEmail2Fa()
        {
            var email = HttpContext.Session.GetString("UserEmail")!;
            var user = (await _supabase.From<UserModel>().Where(x => x.Email == email).Get()).Models.First();
            var meta = UserPasswordMetaCodec.Parse(user.PasswordHash);
            meta.TwoFactorEnabled = true;
            meta.TwoFactorMethod = "email";
            meta.TwoFactorSecret = string.Empty;

            await ReplaceUser(email, new UserModel
            {
                Email = user.Email,
                FullName = user.FullName,
                Industries = user.Industries ?? new List<string> { "Other" },
                Role = user.Role,
                PasswordHash = UserPasswordMetaCodec.Serialize(meta)
            });

            return RedirectToAction(nameof(Settings));
        }

        [RequireAppAuth]
        [HttpPost]
        public IActionResult StartAuthenticatorSetup()
        {
            var secretBytes = KeyGeneration.GenerateRandomKey(20);
            var base32Secret = Base32Encoding.ToString(secretBytes);
            HttpContext.Session.SetString("PendingAuthenticatorSecret", base32Secret);
            return RedirectToAction(nameof(Settings));
        }

        [RequireAppAuth]
        [HttpPost]
        public async Task<IActionResult> ConfirmAuthenticatorSetup(string code)
        {
            var pendingSecret = HttpContext.Session.GetString("PendingAuthenticatorSecret");
            if (string.IsNullOrWhiteSpace(pendingSecret))
            {
                TempData["ErrorMessage"] = "No authenticator setup in progress.";
                return RedirectToAction(nameof(Settings));
            }

            var totp = new Totp(Base32Encoding.ToBytes(pendingSecret));
            if (!totp.VerifyTotp(code ?? string.Empty, out _, new VerificationWindow(1, 1)))
            {
                TempData["ErrorMessage"] = "Invalid authenticator code.";
                return RedirectToAction(nameof(Settings));
            }

            var email = HttpContext.Session.GetString("UserEmail")!;
            var user = (await _supabase.From<UserModel>().Where(x => x.Email == email).Get()).Models.First();
            var meta = UserPasswordMetaCodec.Parse(user.PasswordHash);
            meta.TwoFactorEnabled = true;
            meta.TwoFactorMethod = "authenticator";
            meta.TwoFactorSecret = pendingSecret;

            await ReplaceUser(email, new UserModel
            {
                Email = user.Email,
                FullName = user.FullName,
                Industries = user.Industries ?? new List<string> { "Other" },
                Role = user.Role,
                PasswordHash = UserPasswordMetaCodec.Serialize(meta)
            });

            HttpContext.Session.Remove("PendingAuthenticatorSecret");
            return RedirectToAction(nameof(Settings));
        }

        [RequireAppAuth]
        [HttpPost]
        public async Task<IActionResult> Disable2Fa()
        {
            var email = HttpContext.Session.GetString("UserEmail")!;
            var user = (await _supabase.From<UserModel>().Where(x => x.Email == email).Get()).Models.First();
            var meta = UserPasswordMetaCodec.Parse(user.PasswordHash);
            meta.TwoFactorEnabled = false;
            meta.TwoFactorMethod = "email";
            meta.TwoFactorSecret = string.Empty;

            await ReplaceUser(email, new UserModel
            {
                Email = user.Email,
                FullName = user.FullName,
                Industries = user.Industries ?? new List<string> { "Other" },
                Role = user.Role,
                PasswordHash = UserPasswordMetaCodec.Serialize(meta)
            });

            return RedirectToAction(nameof(Settings));
        }

        [HttpPost]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }

        [RequireAppAuth]
        [HttpPost]
        public async Task<IActionResult> DeleteMyAccount()
        {
            var email = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction(nameof(Login));
            }

            if (string.Equals(email, AdminEmail, StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Admin account cannot be deleted from this page.";
                return RedirectToAction(nameof(Settings));
            }

            await _supabase.From<UserModel>().Where(x => x.Email == email).Delete();
            HttpContext.Session.Clear();
            TempData["SuccessMessage"] = "Your account has been deleted.";
            return RedirectToAction(nameof(Login));
        }

        private static bool PasswordIsStrong(string password)
        {
            return !string.IsNullOrWhiteSpace(password) &&
                   password.Length >= 6 &&
                   Regex.IsMatch(password, @"[A-Z]") &&
                   Regex.IsMatch(password, @"[a-z]") &&
                   Regex.IsMatch(password, @"\d");
        }

        private static string BuildOtpAuthUri(string email, string base32Secret)
        {
            var issuer = Uri.EscapeDataString("SME Finance Assistant");
            var label = Uri.EscapeDataString($"SME Finance Assistant:{email}");
            return $"otpauth://totp/{label}?secret={base32Secret}&issuer={issuer}&digits=6&period=30";
        }

        private static bool IsAdminUser(UserModel user)
        {
            return string.Equals(user.Email, AdminEmail, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ReplaceUser(string email, UserModel updatedUser)
        {
            if (string.IsNullOrEmpty(updatedUser.Email))
            {
                updatedUser.Email = email; // Force set it if missing
            }

            await _supabase.From<UserModel>().Where(x => x.Email == email).Delete();
            await _supabase.From<UserModel>().Insert(updatedUser);
        }

        private void ClearPendingTwoFactorState()
        {
            HttpContext.Session.Remove("PendingUserEmail");
            HttpContext.Session.Remove("PendingUserRole");
            HttpContext.Session.Remove("Pending2FAMethod");
            HttpContext.Session.Remove("Pending2FASecret");
            HttpContext.Session.Remove("Pending2FAEmailCode");
            HttpContext.Session.Remove("Pending2FAEmailCodeExpiry");
        }
    }
}
