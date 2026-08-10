using System.Windows.Input;
using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class KeyBindingFormatterTests
    {
        [Fact]
        public void ToStorageString_NoModifiers_IsJustKeyName()
        {
            Assert.Equal("Up", KeyBindingFormatter.ToStorageString(Key.Up, ModifierKeys.None));
        }

        [Fact]
        public void ToStorageString_WithModifiers_OrdersControlShiftAltWindows()
        {
            var modifiers = ModifierKeys.Windows | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Control;
            Assert.Equal("Control+Shift+Alt+Windows+J", KeyBindingFormatter.ToStorageString(Key.J, modifiers));
        }

        [Fact]
        public void ToDisplayString_UsesFriendlyModifierNames()
        {
            var modifiers = ModifierKeys.Control | ModifierKeys.Shift;
            Assert.Equal("Ctrl+Shift+Del", KeyBindingFormatter.ToDisplayString(Key.Delete, modifiers));
        }

        [Theory]
        [InlineData(Key.Up, "Up Arrow")]
        [InlineData(Key.Down, "Down Arrow")]
        [InlineData(Key.Left, "Left Arrow")]
        [InlineData(Key.Right, "Right Arrow")]
        [InlineData(Key.Back, "Backspace")]
        [InlineData(Key.Delete, "Del")]
        [InlineData(Key.Return, "Enter")]
        [InlineData(Key.D4, "4")]
        [InlineData(Key.Space, "Space")]
        public void ToDisplayString_FriendlyKeyNames(Key key, string expected)
        {
            Assert.Equal(expected, KeyBindingFormatter.ToDisplayString(key, ModifierKeys.None));
        }

        [Fact]
        public void ToDisplayString_FromStorageString_RoundTrips()
        {
            var storage = KeyBindingFormatter.ToStorageString(Key.End, ModifierKeys.Control);
            Assert.Equal("Ctrl+End", KeyBindingFormatter.ToDisplayString(storage));
        }

        [Fact]
        public void ToDisplayString_FromUnparsableStorageString_ReturnsInputUnchanged()
        {
            Assert.Equal("NotAKey", KeyBindingFormatter.ToDisplayString("NotAKey"));
        }

        [Fact]
        public void TryParse_CanonicalStorageString_Succeeds()
        {
            var ok = KeyBindingFormatter.TryParse("Control+Shift+J", out var key, out var modifiers);

            Assert.True(ok);
            Assert.Equal(Key.J, key);
            Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, modifiers);
        }

        [Theory]
        [InlineData("Up Arrow", Key.Up)]
        [InlineData("Down Arrow", Key.Down)]
        [InlineData("Left Arrow", Key.Left)]
        [InlineData("Right Arrow", Key.Right)]
        [InlineData("Backspace", Key.Back)]
        [InlineData("Del", Key.Delete)]
        [InlineData("Enter", Key.Return)]
        [InlineData("Return", Key.Return)]
        [InlineData("Esc", Key.Escape)]
        [InlineData("4", Key.D4)]
        public void TryParse_FriendlyAlias_Succeeds(string alias, Key expected)
        {
            var ok = KeyBindingFormatter.TryParse(alias, out var key, out _);

            Assert.True(ok);
            Assert.Equal(expected, key);
        }

        [Fact]
        public void TryParse_IsCaseInsensitive()
        {
            var ok = KeyBindingFormatter.TryParse("ctrl+del", out var key, out var modifiers);

            Assert.True(ok);
            Assert.Equal(Key.Delete, key);
            Assert.Equal(ModifierKeys.Control, modifiers);
        }

        [Fact]
        public void TryParse_CtrlAlias_MapsToControlModifier()
        {
            var ok = KeyBindingFormatter.TryParse("Ctrl+A", out _, out var modifiers);
            Assert.True(ok);
            Assert.Equal(ModifierKeys.Control, modifiers);
        }

        [Fact]
        public void TryParse_WinAlias_MapsToWindowsModifier()
        {
            var ok = KeyBindingFormatter.TryParse("Win+D", out _, out var modifiers);
            Assert.True(ok);
            Assert.Equal(ModifierKeys.Windows, modifiers);
        }

        [Fact]
        public void TryParse_EmptyString_Fails()
        {
            var ok = KeyBindingFormatter.TryParse(string.Empty, out _, out _);
            Assert.False(ok);
        }

        [Fact]
        public void TryParse_UnknownKeyName_Fails()
        {
            var ok = KeyBindingFormatter.TryParse("NotARealKey", out _, out _);
            Assert.False(ok);
        }

        [Fact]
        public void ToStorageString_And_TryParse_RoundTripAllModifierCombinations()
        {
            foreach (ModifierKeys modifiers in new[]
            {
                ModifierKeys.None, ModifierKeys.Control, ModifierKeys.Shift, ModifierKeys.Alt, ModifierKeys.Windows,
                ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows
            })
            {
                var storage = KeyBindingFormatter.ToStorageString(Key.F5, modifiers);
                var ok = KeyBindingFormatter.TryParse(storage, out var key, out var parsedModifiers);

                Assert.True(ok);
                Assert.Equal(Key.F5, key);
                Assert.Equal(modifiers, parsedModifiers);
            }
        }
    }
}
