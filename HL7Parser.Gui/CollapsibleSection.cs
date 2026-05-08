using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HL7Parser.Gui;

public class CollapsibleSection : StackPanel
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xDC, 0xE8, 0xF9),  // blue
        Color.FromRgb(0xD8, 0xF0, 0xE2),  // green
        Color.FromRgb(0xFD, 0xF1, 0xD3),  // amber
        Color.FromRgb(0xEC, 0xE0, 0xF8),  // violet
        Color.FromRgb(0xD6, 0xF3, 0xF5),  // teal
        Color.FromRgb(0xFD, 0xDF, 0xE6),  // rose
    ];

    private static int _colourIndex;

    private readonly StackPanel _rows = new();
    private bool _expanded = true;

    public CollapsibleSection(string title)
    {
        Margin = new Thickness(0, 0, 0, 10);

        var accent = Palette[_colourIndex % Palette.Length];
        _colourIndex++;

        var chevron = new TextBlock
        {
            Text              = "▾",
            Margin            = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize          = 11,
        };

        var titleText = new TextBlock
        {
            Text              = title,
            FontWeight        = FontWeights.SemiBold,
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var headerContent = new StackPanel { Orientation = Orientation.Horizontal };
        headerContent.Children.Add(chevron);
        headerContent.Children.Add(titleText);

        var header = new Border
        {
            Background   = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Padding      = new Thickness(10, 6, 10, 6),
            Cursor       = Cursors.Hand,
            Child        = headerContent,
        };

        header.MouseLeftButtonUp += (_, _) =>
        {
            _expanded = !_expanded;
            _rows.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = _expanded ? "▾" : "▸";
        };

        Children.Add(header);

        var rowsBorder = new Border
        {
            Child           = _rows,
            BorderBrush     = new SolidColorBrush(accent),
            BorderThickness = new Thickness(1, 0, 1, 1),
            CornerRadius    = new CornerRadius(0, 0, 6, 6),
            Background      = Res("SurfaceBg"),
        };
        Children.Add(rowsBorder);
    }

    public static void ResetColours() => _colourIndex = 0;

    public void AddRow(string label, string value, int keyWidth = 0)
    {
        _rows.Children.Add(BuildRow(label, value, keyWidth, subItems: null));
    }

    public void AddExpandableRow(string label, string value,
        List<(string SubLabel, string SubValue)> subItems, int keyWidth = 0)
    {
        _rows.Children.Add(BuildRow(label, value, keyWidth, subItems));
    }

    private static UIElement BuildRow(string label, string value, int keyWidth,
        List<(string SubLabel, string SubValue)>? subItems)
    {
        bool hasSubItems = subItems is { Count: > 0 };

        var indicator = new TextBlock
        {
            Text              = hasSubItems ? "▶" : "",
            FontSize          = 9,
            Width             = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground        = Res("MutedFg"),
            Margin            = new Thickness(0, 0, 2, 0),
        };

        var labelText = new TextBlock
        {
            Text         = label,
            Foreground   = Res("MutedFg"),
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 3, 8, 3),
        };

        var valueText = new TextBlock
        {
            Text         = value,
            Foreground   = Res("Fg"),
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 3, 8, 3),
        };

        var mainRow = new Grid { Margin = new Thickness(10, 0, 0, 0) };
        mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });

        if (keyWidth > 0)
        {
            mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keyWidth) });
            mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        else
        {
            mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        Grid.SetColumn(indicator,  0);
        Grid.SetColumn(labelText,  1);
        Grid.SetColumn(valueText,  2);

        mainRow.Children.Add(indicator);
        mainRow.Children.Add(labelText);
        mainRow.Children.Add(valueText);

        var container = new StackPanel();

        var mainBorder = new Border
        {
            Child           = mainRow,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush     = Res("ThemeBorder"),
            Padding         = new Thickness(0, 2, 0, 2),
        };

        if (!hasSubItems)
            return mainBorder;

        var subPanel = new StackPanel
        {
            Visibility  = Visibility.Collapsed,
            Background  = Res("RaisedBg"),
            Margin      = new Thickness(26, 0, 0, 0),
        };

        foreach (var (subLabel, subValue) in subItems!)
        {
            var subLbl = new TextBlock
            {
                Text         = subLabel,
                Foreground   = Res("MutedFg"),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 2, 8, 2),
            };
            var subVal = new TextBlock
            {
                Text         = subValue,
                Foreground   = Res("Fg"),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 2, 8, 2),
            };

            var subGrid = new Grid { Margin = new Thickness(10, 0, 0, 0) };
            if (keyWidth > 0)
            {
                subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keyWidth - 26) });
                subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            else
            {
                subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            Grid.SetColumn(subLbl, 0);
            Grid.SetColumn(subVal, 1);
            subGrid.Children.Add(subLbl);
            subGrid.Children.Add(subVal);

            subPanel.Children.Add(new Border
            {
                Child           = subGrid,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush     = Res("ThemeBorder"),
                Padding         = new Thickness(0, 2, 0, 2),
            });
        }

        mainBorder.Cursor = Cursors.Hand;
        mainBorder.MouseLeftButtonUp += (_, _) =>
        {
            bool open = subPanel.Visibility == Visibility.Visible;
            subPanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            indicator.Text = open ? "▶" : "▼";
        };

        container.Children.Add(mainBorder);
        container.Children.Add(subPanel);
        return container;
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
