using Azure.Identity;
using FunChatBotApp.Components;
using FunChatBotApp.Services;

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

            // 2. Szolgáltatások regisztrálása
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Saját service-ek regisztrálása (ezeket a következő lépésben hozzuk létre)
            builder.Services.AddSingleton<CosmosDbService>(); // CosmosDB-hez érdemes Singletont használni
            builder.Services.AddScoped<DocumentService>();
            builder.Services.AddScoped<ChatService>();
            

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseAntiforgery();
            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
