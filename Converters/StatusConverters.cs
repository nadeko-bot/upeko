using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using upeko.Services;

namespace upeko.Converters;

public class StatusToDotColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MainActivityState status)
        {
            return status switch
            {
                MainActivityState.Running => new SolidColorBrush(Color.Parse("#12b886")),
                MainActivityState.Runnable => new SolidColorBrush(Color.Parse("#0070f3")),
                MainActivityState.Updatable => new SolidColorBrush(Color.Parse("#f5a623")),
                MainActivityState.Downloadable => new SolidColorBrush(Color.Parse("#999999")),
                _ => new SolidColorBrush(Color.Parse("#999999"))
            };
        }
        return new SolidColorBrush(Color.Parse("#999999"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StatusToPillBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MainActivityState status)
        {
            return status switch
            {
                MainActivityState.Running => new SolidColorBrush(Color.Parse("#12b886"), 0.15),
                MainActivityState.Runnable => new SolidColorBrush(Color.Parse("#0070f3"), 0.1),
                MainActivityState.Updatable => new SolidColorBrush(Color.Parse("#f5a623"), 0.1),
                MainActivityState.Downloadable => new SolidColorBrush(Color.Parse("#999999"), 0.1),
                _ => new SolidColorBrush(Color.Parse("#999999"), 0.1)
            };
        }
        return new SolidColorBrush(Color.Parse("#999999"), 0.1);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StatusToPillForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MainActivityState status)
        {
            return status switch
            {
                MainActivityState.Running => new SolidColorBrush(Color.Parse("#12b886")),
                MainActivityState.Runnable => new SolidColorBrush(Color.Parse("#0070f3")),
                MainActivityState.Updatable => new SolidColorBrush(Color.Parse("#f5a623")),
                MainActivityState.Downloadable => new SolidColorBrush(Color.Parse("#999999")),
                _ => new SolidColorBrush(Color.Parse("#999999"))
            };
        }
        return new SolidColorBrush(Color.Parse("#999999"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var loc = LocalizationService.Instance;
        if (value is MainActivityState status)
        {
            return status switch
            {
                MainActivityState.Running => loc["Status_Running"],
                MainActivityState.Runnable => loc["Status_Ready"],
                MainActivityState.Updatable => loc["Status_UpdateAvailable"],
                MainActivityState.Downloadable => loc["Status_NotDownloaded"],
                _ => loc["Status_Unknown"]
            };
        }
        return loc["Status_Unknown"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? 1.0 : 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class DepStatusToDotColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status switch
            {
                "installed" => new SolidColorBrush(Color.Parse("#50e3c2")),
                "missing" => new SolidColorBrush(Color.Parse("#f5a623")),
                "checking" => new SolidColorBrush(Color.Parse("#0070f3")),
                _ => new SolidColorBrush(Color.Parse("#999999"))
            };
        }
        return new SolidColorBrush(Color.Parse("#999999"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class NullToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() ?? (parameter?.ToString() ?? "\u2014");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}
