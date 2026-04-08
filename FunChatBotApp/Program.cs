using Azure.Identity;
using FunChatBotApp.Components;
using FunChatBotApp.Services;
using FunChatBotApp.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

            builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

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
            });

            app.Run();
        }
    }
}
