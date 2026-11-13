using System.Globalization;
using System.Windows.Data;
using DoorsHandler.Core;

namespace DoorsHandler.Desktop.Converters;

/// <summary>Shows a <see cref="DoorsItemType"/> with a short label.</summary>
public sealed class ItemTypeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DoorsItemType.Folder => "📁 Folder",
        DoorsItemType.Project => "📦 Project",
        DoorsItemType.Formal => "📄 Formal",
        DoorsItemType.Link => "🔗 Link",
        DoorsItemType.Descriptive => "📝 Descriptive",
        _ => "? Unknown",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
