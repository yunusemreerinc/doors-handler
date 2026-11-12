using System.Globalization;
using System.Windows.Data;
using DoorsHandler.Core;

namespace DoorsHandler.Desktop.Converters;

/// <summary>Shows a <see cref="DoorsItemType"/> with a short Turkish label.</summary>
public sealed class ItemTypeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DoorsItemType.Folder => "📁 Klasör",
        DoorsItemType.Project => "📦 Proje",
        DoorsItemType.Formal => "📄 Formal",
        DoorsItemType.Link => "🔗 Link",
        DoorsItemType.Descriptive => "📝 Descriptive",
        _ => "? Bilinmiyor",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
