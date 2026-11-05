using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using upeko.ViewModels;

namespace upeko.Converters;

internal static class Palette
{
    public static readonly Color Running = Color.Parse("#12b886");
    public static readonly Color Ready = Color.Parse("#0070f3");
    public static readonly Color Warning = Color.Parse("#f5a623");
    public static readonly Color Error = Color.Parse("#e5484d");
    public static readonly Color Neutral = Color.Parse("#999999");

    public static Color ForState(object? value) => value switch
    {
        MainActivityState.Running => Running,
        MainActivityState.Runnable => Ready,
        MainActivityState.Updatable => Warning,
        _ => Neutral
    };
}

public class StatusToDotColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(Palette.ForState(value));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StatusToPillBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(Palette.ForState(value), value is MainActivityState.Running ? 0.15 : 0.1);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StatusToPillForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(Palette.ForState(value));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class DepStatusToDotColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(value switch
        {
            "installed" => Palette.Running,
            "missing" => Palette.Warning,
            "checking" => Palette.Ready,
            _ => Palette.Neutral
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ToastKindToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(value switch
        {
            ToastKind.Success => Palette.Running,
            ToastKind.Error => Palette.Error,
            _ => Palette.Ready
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
