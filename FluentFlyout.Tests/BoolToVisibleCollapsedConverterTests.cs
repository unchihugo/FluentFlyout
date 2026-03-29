using FluentFlyoutWPF.Classes.Utils;
using System.Windows;

namespace FluentFlyout.Tests;

public class BoolToVisibleCollapsedConverterTests
{
    [Fact]
    public void Convert_BoolTrue_ReturnsTrueValue()
    {
        // Arrange
        var converter = new BoolToVisibleCollapsedConverter();
        converter.TrueValue = Visibility.Visible;
        converter.FalseValue = Visibility.Collapsed;

        // Act
        var result = converter.Convert(true, typeof(Visibility), null, null);

        // Assert
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_BoolFalse_ReturnsFalseValue()
    {
        // Arrange
        var converter = new BoolToVisibleCollapsedConverter();
        converter.TrueValue = Visibility.Visible;
        converter.FalseValue = Visibility.Collapsed;

        // Act
        var result = converter.Convert(false, typeof(Visibility), null, null);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_Null_ReturnsFalseValue()
    {
        // Arrange
        var converter = new BoolToVisibleCollapsedConverter();
        converter.FalseValue = Visibility.Hidden;

        // Act
        var result = converter.Convert(null, typeof(Visibility), null, null);

        // Assert
        Assert.Equal(Visibility.Hidden, result);
    }

    [Fact]
    public void Convert_NonBool_ReturnsFalseValue()
    {
        // Arrange
        var converter = new BoolToVisibleCollapsedConverter();
        converter.FalseValue = Visibility.Collapsed;

        // Act
        var result = converter.Convert("not a bool", typeof(Visibility), null, null);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void ConvertBack_VisibilityTrueValue_ReturnsTrue()
    {
        // Arrange
        var converter = new BoolToVisibleCollapsedConverter();
        converter.TrueValue = Visibility.Visible;

        // Act
        var result = converter.ConvertBack(Visibility.Visible, typeof(bool), null, null);

        // Assert
        Assert.True((bool)result);
    }

    [Fact]
    public void ConvertBack_VisibilityFalseValue_ReturnsFalse()
    {
        // Arrange
        var converter = new BoolToVisibleCollapsedConverter();
        converter.TrueValue = Visibility.Visible;

        // Act
        var result = converter.ConvertBack(Visibility.Collapsed, typeof(bool), null, null);

        // Assert
        Assert.False((bool)result);
    }

    [Fact]
    public void ConvertBack_NonVisibility_ReturnsFalse()
    {
        // Arrange
        var converter = new BoolToVisibleCollapsedConverter();

        // Act
        var result = converter.ConvertBack("not visibility", typeof(bool), null, null);

        // Assert
        Assert.False((bool)result);
    }
}
