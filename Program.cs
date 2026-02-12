using BIP_SMEMC.Services;
using BIP_SMEMC.Services.Finance; // Needed for FinancialDataService
using System.Diagnostics;

namespace BIP_SMEMC
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1. Core Framework Services
            builder.Services.AddControllersWithViews().AddNewtonsoftJson();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromHours(4);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            // 2. Register Services (INTEGRATED LIST)
            // ---------------------------------------------------------

            // Auth & Account Services (Fixes your Crash)
            builder.Services.AddSingleton<PasswordResetTokenStore>(); // <--- MISSING LINE FIXED
            builder.Services.AddScoped<AccountService>();
            builder.Services.AddScoped<EmailService>();

            // Finance & Chat Features
            builder.Services.AddScoped<FinanceService>();
            builder.Services.AddScoped<FinanceChatService>();
            builder.Services.AddScoped<FinancialDataService>();
            builder.Services.AddScoped<ProfitImprovementService>();

            // Utilities & Background Tasks
            builder.Services.AddScoped<CategorySeederService>();
            builder.Services.AddScoped<DebtService>();
            builder.Services.AddScoped<PayrollService>();
            builder.Services.AddHttpClient<GeminiService>();
            builder.Services.AddHostedService<NewsBGService>();

            // 3. Register Supabase Client (Strict Mode - No LocalDB)
            builder.Services.AddScoped(provider =>
            {
                var url = builder.Configuration["Supabase:Url"];
                var key = builder.Configuration["Supabase:Key"];

                // Fallback check
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
                {
                    // For debugging, you might want to hardcode them temporarily if config fails
                    // or throw a clear error
                    throw new InvalidOperationException("Supabase URL or Key is missing in appsettings.json");
                }

                var options = new Supabase.SupabaseOptions
                {
                    AutoRefreshToken = true,
                    AutoConnectRealtime = true
                };

                var client = new Supabase.Client(url, key, options);
                client.InitializeAsync().Wait();
                return client;
            });

            var app = builder.Build();

            // 4. Seeder Logic
            using (var scope = app.Services.CreateScope())
            {
                try
                {
                    var seeder = scope.ServiceProvider.GetRequiredService<CategorySeederService>();
                    await seeder.EnsureIndustriesAndRegionsExist();
                    Debug.WriteLine("[STARTUP] Seeder completed successfully.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[STARTUP ERROR] Seeder failed: {ex.Message}");
                }
            }

            // 5. Middleware Pipeline
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            // Session must be before Authorization
            app.UseSession();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}