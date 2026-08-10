using System.Globalization;
using System.Windows;
using MaterialDesignThemes.Wpf;
using RouteJumper.Converters;
using RouteJumper.Models;
using Xunit;

namespace RouteJumper.Tests.Converters
{
    public class AutoPilotLabelConverterTests
    {
        private readonly AutoPilotLabelConverter _converter = new();

        [Fact]
        public void Convert_True_ReturnsStop()
        {
            Assert.Equal("Stop", _converter.Convert(true, typeof(string), null, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_False_ReturnsAutoPilot()
        {
            Assert.Equal("Auto Pilot", _converter.Convert(false, typeof(string), null, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_NonBool_ReturnsAutoPilot()
        {
            Assert.Equal("Auto Pilot", _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ConvertBack_Throws()
        {
            Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(null, typeof(bool), null, CultureInfo.InvariantCulture));
        }
    }

    public class BoolToHiddenVisibilityConverterTests
    {
        private readonly BoolToHiddenVisibilityConverter _converter = new();

        [Fact]
        public void Convert_True_ReturnsVisible() =>
            Assert.Equal(Visibility.Visible, _converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_False_ReturnsHidden() =>
            Assert.Equal(Visibility.Hidden, _converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_NonBool_ReturnsHidden() =>
            Assert.Equal(Visibility.Hidden, _converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture));
    }

    public class BoolToVisibilityConverterTests
    {
        private readonly BoolToVisibilityConverter _converter = new();

        [Fact]
        public void Convert_True_ReturnsVisible() =>
            Assert.Equal(Visibility.Visible, _converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_False_ReturnsCollapsed() =>
            Assert.Equal(Visibility.Collapsed, _converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_TrueWithInvertParameter_ReturnsCollapsed() =>
            Assert.Equal(Visibility.Collapsed, _converter.Convert(true, typeof(Visibility), "Invert", CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_FalseWithInvertParameter_ReturnsVisible() =>
            Assert.Equal(Visibility.Visible, _converter.Convert(false, typeof(Visibility), "Invert", CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_InvertParameterIsCaseInsensitive() =>
            Assert.Equal(Visibility.Collapsed, _converter.Convert(true, typeof(Visibility), "invert", CultureInfo.InvariantCulture));
    }

    public class StringToVisibilityConverterTests
    {
        private readonly StringToVisibilityConverter _converter = new();

        [Fact]
        public void Convert_NonEmptyString_ReturnsVisible() =>
            Assert.Equal(Visibility.Visible, _converter.Convert("text", typeof(Visibility), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_EmptyString_ReturnsCollapsed() =>
            Assert.Equal(Visibility.Collapsed, _converter.Convert(string.Empty, typeof(Visibility), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_Null_ReturnsCollapsed() =>
            Assert.Equal(Visibility.Collapsed, _converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture));
    }

    public class RowIconToVisibilityConverterTests
    {
        private readonly RowIconToVisibilityConverter _converter = new();

        [Theory]
        [InlineData(RowIcon.InProgress)]
        [InlineData(RowIcon.Complete)]
        public void Convert_NonNoneIcon_ReturnsVisible(RowIcon icon) =>
            Assert.Equal(Visibility.Visible, _converter.Convert(icon, typeof(Visibility), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_NoneIcon_ReturnsHidden() =>
            Assert.Equal(Visibility.Hidden, _converter.Convert(RowIcon.None, typeof(Visibility), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_NonRowIconValue_ReturnsHidden() =>
            Assert.Equal(Visibility.Hidden, _converter.Convert("not a RowIcon", typeof(Visibility), null, CultureInfo.InvariantCulture));
    }

    public class ReferenceEqualsConverterTests
    {
        private readonly ReferenceEqualsConverter _converter = new();

        [Fact]
        public void Convert_SameReference_ReturnsTrue()
        {
            var obj = new object();
            Assert.Equal(true, _converter.Convert(new[] { obj, obj }, typeof(bool), null, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_DifferentReferences_ReturnsFalse()
        {
            Assert.Equal(false, _converter.Convert(new[] { new object(), new object() }, typeof(bool), null, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_FirstValueNull_ReturnsFalse()
        {
            Assert.Equal(false, _converter.Convert(new object?[] { null, null }, typeof(bool), null, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Convert_WrongArity_ReturnsFalse()
        {
            Assert.Equal(false, _converter.Convert(new object?[] { new object() }, typeof(bool), null, CultureInfo.InvariantCulture));
        }
    }

    public class IconStatusToPackIconKindConverterTests
    {
        private readonly IconStatusToPackIconKindConverter _converter = new();

        [Fact]
        public void Convert_CompleteIcon_ReturnsCheck() =>
            Assert.Equal(PackIconKind.Check, _converter.Convert(new object?[] { RowIcon.Complete, string.Empty }, typeof(PackIconKind), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_InProgressPlotted_ReturnsHourglass() =>
            Assert.Equal(PackIconKind.Hourglass, _converter.Convert(new object?[] { RowIcon.InProgress, "Plotted" }, typeof(PackIconKind), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_InProgressJumping_ReturnsRocketLaunch() =>
            Assert.Equal(PackIconKind.RocketLaunch, _converter.Convert(new object?[] { RowIcon.InProgress, "Jumping" }, typeof(PackIconKind), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_InProgressOtherStatus_ReturnsPlay() =>
            Assert.Equal(PackIconKind.Play, _converter.Convert(new object?[] { RowIcon.InProgress, "Cooldown" }, typeof(PackIconKind), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_InProgressBlankStatus_ReturnsPlay() =>
            Assert.Equal(PackIconKind.Play, _converter.Convert(new object?[] { RowIcon.InProgress, string.Empty }, typeof(PackIconKind), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_NoneIcon_ReturnsPlay() =>
            Assert.Equal(PackIconKind.Play, _converter.Convert(new object?[] { RowIcon.None, string.Empty }, typeof(PackIconKind), null, CultureInfo.InvariantCulture));

        [Fact]
        public void Convert_EmptyValues_ReturnsPlay() =>
            Assert.Equal(PackIconKind.Play, _converter.Convert(Array.Empty<object?>(), typeof(PackIconKind), null, CultureInfo.InvariantCulture));
    }
}
