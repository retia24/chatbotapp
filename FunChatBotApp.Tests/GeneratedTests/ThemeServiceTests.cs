using FunChatBotApp.Services;
using Xunit;

namespace FunChatBotApp.Tests
{
    public class ThemeServiceTests
    {
        [Fact]
        public void SwitchTheme_WithLightTheme_ChangesCurrentThemeAndRaisesEvent()
        {
            // Arrange
            var service = new ThemeService();
            bool eventRaised = false;
            service.OnThemeChanged += () => eventRaised = true;

            // Act
            service.SwitchTheme("light");

            // Assert
            Assert.Equal("light", service.CurrentTheme);
            Assert.True(eventRaised);
        }

        [Fact]
        public void SwitchTheme_WithDarkTheme_ChangesCurrentThemeAndRaisesEvent()
        {
            // Arrange
            var service = new ThemeService();
            bool eventRaised = false;
            service.OnThemeChanged += () => eventRaised = true;

            // Act
            service.SwitchTheme("dark");

            // Assert
            Assert.Equal("dark", service.CurrentTheme);
            Assert.True(eventRaised);
        }

        [Fact]
        public void SwitchTheme_WithInvalidTheme_DoesNotChangeCurrentThemeOrRaiseEvent()
        {
            // Arrange
            var service = new ThemeService();
            string initialTheme = service.CurrentTheme;
            bool eventRaised = false;
            service.OnThemeChanged += () => eventRaised = true;

            // Act
            service.SwitchTheme("blue");

            // Assert
            Assert.Equal(initialTheme, service.CurrentTheme);
            Assert.False(eventRaised);
        }
    }
}