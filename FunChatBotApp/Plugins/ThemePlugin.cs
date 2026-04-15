using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Threading.Tasks;
using FunChatBotApp.Services;

namespace FunChatBotApp.Plugins
{
    public class ThemePlugin
    {
        private readonly ThemeService _themeService;

        public ThemePlugin(ThemeService themeService)
        {
            _themeService = themeService;
        }

        [KernelFunction("SwitchApplicationTheme")]
        [Description("Megváltoztatja az alkalmazás kinézetét (témáját) sötét (dark) vagy világos (light) módra. Ezt az eszközt és ágenst hívd meg, ha a felhasználó arra utal, hogy cseréld le a színeket, témát sötétre vagy világosra.")]
        public string SwitchTheme([Description("A kért téma: 'dark' vagy 'light'")] string theme)
        {
            theme = theme.ToLowerInvariant();
            if (theme == "dark" || theme == "light")
            {
                _themeService.SwitchTheme(theme);
                return $"Sikeresen átváltottam a(z) {theme} témára. Válaszolj barátságosan a felhasználónak, hogy megtörtént a váltás.";
            }

            return "Nem sikerült átváltani. Csak 'light' vagy 'dark' opció elfogadott.";
        }
    }
}