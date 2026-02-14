using BIP_SMEMC.Services;

namespace BIP_SMEMC
{
    public class Program
    {
        public static void Main(string[] args)
        {

            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews().AddNewtonsoftJson();
            // 1. Register your FinanceService (The fix for your error)
            builder.Services.AddScoped<FinanceService>();
            builder.Services.AddScoped<CategorySeederService>();
            builder.Services.AddHttpClient<GeminiService>();
            builder.Services.AddHostedService<NewsBGService>();
            builder.Services.AddScoped<EmailService>();
            builder.Services.AddSingleton<PasswordResetTokenStore>();

            // OptiFlow services
            builder.Services.AddScoped<LearningService>();
            builder.Services.AddScoped<RewardsService>();
            builder.Services.AddScoped<CommunityService>();

            // Session
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromHours(2);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            // 2. Register the Supabase Client (Required for your database connections)
            builder.Services.AddScoped(provider =>
            {
                var url = builder.Configuration["Supabase:Url"];
                var key = builder.Configuration["Supabase:ServiceRoleKey"]
                    ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY")
                    ?? builder.Configuration["Supabase:Key"];
                return new Supabase.Client(url, key);
            });

            var app = builder.Build();

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

            app.UseSession();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
