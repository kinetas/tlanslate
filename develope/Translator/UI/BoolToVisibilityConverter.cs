using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Translator.UI;

/// <summary>
/// bool → Visibility 변환기.
/// true → Visible, false → Collapsed
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    /// <remarks>
    /// parameter가 "Inverse" 문자열이면 논리가 반전됩니다 (false → Visible, true → Collapsed).
    /// int/ActiveRegionsCount 등 숫자를 받을 경우 0이면 false, 그 외 true로 처리합니다.
    /// </remarks>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value switch
        {
            bool b => b,
            int i => i > 0,
            _ => false
        };

        bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        if (inverse) boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool result = value is Visibility visibility && visibility == Visibility.Visible;
        bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        return inverse ? !result : result;
    }
}
