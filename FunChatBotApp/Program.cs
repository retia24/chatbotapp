using Azure.Identity;
using FunChatBotApp.Components;
using FunChatBotApp.Services;
using FunChatBotApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

namespace FunChatBotApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1. Key Vault integráció beállítása
            var keyVaultUri = builder.Configuration["KeyVaultUri"];
            if (!string.IsNullOrEmpty(keyVaultUri))
            {
                builder.Configuration.AddAzureKeyVault(
                    new Uri(keyVaultUri),
                    new DefaultAzureCredential());
            }

            // Authentication beállítása (ASP.NET Core Identity & EF Core)
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString));

            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            builder.Services.AddSingleton<IEmailSender<ApplicationUser>, EmailSender>();

            builder.Services.AddAuthentication(options =>
                {
                    options.DefaultScheme = IdentityConstants.ApplicationScheme;
                    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
                })
                .AddIdentityCookies();

            builder.Services.AddAuthorization();
            builder.Services.AddCascadingAuthenticationState();

            // 2. Szolgáltatások regisztrálása
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Saját service-ek regisztrálása (ezeket a következő lépésben hozzuk létre)
            builder.Services.AddSingleton<CosmosDbService>();
            builder.Services.AddScoped<DocumentService>();
            builder.Services.AddScoped<ChatService>();

            // Rate Limiter beállítása
            builder.Services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: partition => new FixedWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            PermitLimit = 100, // Pl. max 100 kérés
                            QueueLimit = 0,
                            Window = TimeSpan.FromMinutes(1)
                        }));

                options.OnRejected = async (context, token) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    await context.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", cancellationToken: token);
                };
            });

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRateLimiter(); // Hozzáadjuk a Rate Limitot a pipeline-ba
            app.UseAntiforgery();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            // Kijelentkezés végpont
            app.MapPost("/Account/Logout", async (SignInManager<ApplicationUser> signInManager, [Microsoft.AspNetCore.Mvc.FromForm] string? returnUrl) =>
            {
                await signInManager.SignOutAsync();
                return Microsoft.AspNetCore.Http.Results.LocalRedirect(returnUrl ?? "/");
            }).DisableAntiforgery(); // Disabling antiforgery for this minimal API endpoint as Blazor Server handles the token implicitly on forms differently.

            app.Run();
        }
    }
}
