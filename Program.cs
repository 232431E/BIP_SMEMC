using BIP_SMEMC.Services;
using BIP_SMEMC.Services.Finance;
using System.Diagnostics;

namespace BIP_SMEMC
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // --- CORE SERVICES ---
            builder.Services.AddControllersWithViews().AddNewtonsoftJson();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddMemoryCache();

            // FIX A: Add the global HttpClient factory (Required for NewsBGService)
            builder.Services.AddHttpClient();

            // --- SESSION ---
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromHours(2);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            // --- SUPABASE (Scoped) ---
            builder.Services.AddScoped(provider =>
            {
                var url = builder.Configuration["Supabase:Url"];
                var key = builder.Configuration["Supabase:Key"];
                var options = new Supabase.SupabaseOptions { AutoRefreshToken = true, AutoConnectRealtime = true };
                return new Supabase.Client(url, key, options);
            });

            // --- APP SERVICES ---
            builder.Services.AddSingleton<PasswordResetTokenStore>();
            builder.Services.AddScoped<AccountService>();
            builder.Services.AddScoped<EmailService>();
            builder.Services.AddScoped<FinanceService>();
            builder.Services.AddScoped<FinancialDataService>();
            builder.Services.AddScoped<FinanceChatService>();
            builder.Services.AddScoped<ProfitImprovementService>();
            builder.Services.AddScoped<DebtService>();
            builder.Services.AddScoped<PayrollService>();
            builder.Services.AddScoped<CategorySeederService>();
            builder.Services.AddScoped<RewardsService>(); // <--- ADD/UNCOMMENT THIS LINE
            builder.Services.AddScoped<LearningService>(); builder.Services.AddScoped<CommunityService>();

            // FIX B: Register GeminiService as a standard Scoped service 
            // DO NOT use AddHttpClient<GeminiService> anymore because it no longer takes HttpClient in constructor
            builder.Services.AddScoped<GeminiService>();

            // --- BACKGROUND TASKS ---
            builder.Services.AddHostedService<NewsBGService>();

            var app = builder.Build();

            // --- SEEDER LOGIC ---
            using (var scope = app.Services.CreateScope())
            {
                try
                {
                    var seeder = scope.ServiceProvider.GetRequiredService<CategorySeederService>();
                    await seeder.EnsureIndustriesAndRegionsExist();
                }
                catch (Exception ex) { Debug.WriteLine($"Seeder failed: {ex.Message}"); }
            }

            // --- PIPELINE ---
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();
            app.UseAuthorization();
            app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}