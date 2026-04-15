using System;

namespace FunChatBotApp.Services
{
    public class ThemeService
    {
        public string CurrentTheme { get; private set; } = "light";

        public event Action? OnThemeChanged;

        public void SwitchTheme(string theme)
        {
            if (theme == "light" || theme == "dark")
            {
                CurrentTheme = theme;
                OnThemeChanged?.Invoke();
            }
        }
    }
}