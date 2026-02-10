using BIP_SMEMC.Services;
using System.Diagnostics;

namespace BIP_SMEMC
{
    public class Program
    {
        public static async Task Main(string[] args)
        {

            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews().AddNewtonsoftJson();
            // 1. Register your FinanceService (The fix for your error)
            builder.Services.AddScoped<FinanceService>();
            builder.Services.AddScoped<CategorySeederService>();
            builder.Services.AddHttpClient<GeminiService>();
            builder.Services.AddHostedService<NewsBGService>();

            // 2. Register the Supabase Client (Required for your database connections)
            builder.Services.AddScoped(provider =>
            {
                var url = builder.Configuration["Supabase:Url"];
                var key = builder.Configuration["Supabase:Key"];
                return new Supabase.Client(url, key);
            });

            var app = builder.Build();
            // --- FIX: EXPLICITLY RUN SEEDER ON STARTUP ---
            using (var scope = app.Services.CreateScope())
            {
                try
                {
                    var seeder = scope.ServiceProvider.GetRequiredService<CategorySeederService>();
                    // This ensures industries/regions are populated before any controller runs
                    await seeder.EnsureIndustriesAndRegionsExist();
                    Debug.WriteLine("[STARTUP] Seeder completed successfully.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[STARTUP ERROR] Seeder failed: {ex.Message}");
                }
            }
            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
