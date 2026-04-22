using System;
using FunChatBotApp.Services;
using Moq;
using Xunit;

namespace FunChatBotApp.Tests.Services
{
    public class ThemeServiceTests
    {
        [Fact]
        public void CurrentTheme_DefaultValue_IsLight()
        {
            // Arrange
            var service = new ThemeService();

            // Act
            var currentTheme = service.CurrentTheme;

            // Assert
            Assert.Equal("light", currentTheme);
        }

        [Fact]
        public void SwitchTheme_WithDark_UpdatesCurrentThemeAndRaisesEvent()
        {
            // Arrange
            var service = new ThemeService();
            var handlerMock = new Mock<Action>();
            service.OnThemeChanged += handlerMock.Object;

            // Act
            service.SwitchTheme("dark");

            // Assert
            Assert.Equal("dark", service.CurrentTheme);
            handlerMock.Verify(h => h(), Times.Once);
        }

        [Fact]
        public void SwitchTheme_WithLight_UpdatesCurrentThemeAndRaisesEvent()
        {
            // Arrange
            var service = new ThemeService();
            service.SwitchTheme("dark");
            var handlerMock = new Mock<Action>();
            service.OnThemeChanged += handlerMock.Object;

            // Act
            service.SwitchTheme("light");

            // Assert
            Assert.Equal("light", service.CurrentTheme);
            handlerMock.Verify(h => h(), Times.Once);
        }

        [Fact]
        public void SwitchTheme_WithInvalidTheme_DoesNotUpdateThemeAndDoesNotRaiseEvent()
        {
            // Arrange
            var service = new ThemeService();
            var initialTheme = service.CurrentTheme;
            var handlerMock = new Mock<Action>();
            service.OnThemeChanged += handlerMock.Object;

            // Act
            service.SwitchTheme("blue");

            // Assert
            Assert.Equal(initialTheme, service.CurrentTheme);
            handlerMock.Verify(h => h(), Times.Never);
        }

        [Fact]
        public void SwitchTheme_WithCaseMismatchedTheme_DoesNotUpdateThemeAndDoesNotRaiseEvent()
        {
            // Arrange
            var service = new ThemeService();
            var handlerMock = new Mock<Action>();
            service.OnThemeChanged += handlerMock.Object;

            // Act
            service.SwitchTheme("Dark");

            // Assert
            Assert.Equal("light", service.CurrentTheme);
            handlerMock.Verify(h => h(), Times.Never);
        }
    }
}