using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DualSound.Contracts.Enums;

namespace DualSound.App.Converters;

/// <summary>
/// Converts boolean to Visibility, with optional invert parameter.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        var invert = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);

        if (invert)
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

/// <summary>
/// Converts AudioDeviceState to modern status indicator colors.
/// </summary>
public sealed class DeviceStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(16, 185, 129));      // Emerald 500
    private static readonly SolidColorBrush UnpluggedBrush = new(Color.FromRgb(245, 158, 11));   // Amber 500
    private static readonly SolidColorBrush DisabledBrush = new(Color.FromRgb(107, 114, 128));   // Gray 500
    private static readonly SolidColorBrush NotPresentBrush = new(Color.FromRgb(75, 85, 99));    // Gray 600

    static DeviceStateToBrushConverter()
    {
        ActiveBrush.Freeze();
        UnpluggedBrush.Freeze();
        DisabledBrush.Freeze();
        NotPresentBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AudioDeviceState state)
        {
            return state switch
            {
                AudioDeviceState.Active => ActiveBrush,
                AudioDeviceState.Unplugged => UnpluggedBrush,
                AudioDeviceState.Disabled => DisabledBrush,
                AudioDeviceState.NotPresent => NotPresentBrush,
                _ => NotPresentBrush
            };
        }

        return NotPresentBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts AudioDeviceType to distinctive badge accent colors.
/// </summary>
public sealed class DeviceTypeToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush HdmiBrush = new(Color.FromRgb(6, 182, 212));       // Cyan 500
    private static readonly SolidColorBrush AnalogBrush = new(Color.FromRgb(59, 130, 246));    // Blue 500
    private static readonly SolidColorBrush UsbBrush = new(Color.FromRgb(168, 85, 247));       // Purple 500
    private static readonly SolidColorBrush BluetoothBrush = new(Color.FromRgb(99, 102, 241)); // Indigo 500
    private static readonly SolidColorBrush VirtualBrush = new(Color.FromRgb(234, 179, 8));    // Yellow 500
    private static readonly SolidColorBrush UnknownBrush = new(Color.FromRgb(148, 163, 184));  // Slate 400

    static DeviceTypeToBrushConverter()
    {
        HdmiBrush.Freeze();
        AnalogBrush.Freeze();
        UsbBrush.Freeze();
        BluetoothBrush.Freeze();
        VirtualBrush.Freeze();
        UnknownBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AudioDeviceType type)
        {
            return type switch
            {
                AudioDeviceType.HdmiDisplayPort => HdmiBrush,
                AudioDeviceType.LineOut35mm or AudioDeviceType.Headphones or AudioDeviceType.Speakers => AnalogBrush,
                AudioDeviceType.UsbAudio => UsbBrush,
                AudioDeviceType.BluetoothAudio => BluetoothBrush,
                AudioDeviceType.VirtualAudio => VirtualBrush,
                _ => UnknownBrush
            };
        }

        return UnknownBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Collapses element when string is null or whitespace.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        var hasText = !string.IsNullOrWhiteSpace(text);
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Resolves a resource key string to a StreamGeometry or Geometry resource.
/// </summary>
public sealed class StringToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current != null)
        {
            return Application.Current.TryFindResource(key) as Geometry;
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
