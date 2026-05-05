using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HL7Parser.Gui;

/// <summary>Card-style section with a click-to-expand/collapse header and animated body.</summary>
public class CollapsibleSection : UserControl
{
    private static readonly (string Accent, string Bg)[] Colours =
    {
        ("#1877F2", "#D8EAFD"), ("#2E7D32", "#E8F5E9"),
        ("#6A1B9A", "#F3E5F5"), ("#E65100", "#FBE9E7"),
        ("#00695C", "#E0F2F1"), ("#C62828", "#FFEBEE"),
        ("#283593", "#E8EAF6"), ("#F57F17", "#FFFDE7"),
    };
    private static int _colourIndex;

    public static void ResetColours() => _colourIndex = 0;

    private readonly Button       _header;
    private readonly StackPanel   _body;
    private readonly Grid         _bodyClip;
    private bool                  _expanded;

    public CollapsibleSection(string title, bool startCollapsed = false)
    {
        _expanded = !startCollapsed;
        var (accent, bg) = Colours[_colourIndex++ % Colours.Length];
        var accentBrush  = (Brush)new BrushConverter().ConvertFrom(accent)!;
        var bgBrush      = (Brush)new BrushConverter().ConvertFrom(bg)!;

        // ── Card border ──────────────────────────────────────────────────
        var card = new Border
        {
            CornerRadius     = new CornerRadius(8),
            BorderBrush      = new SolidColorBrush(Color.FromRgb(0xE4, 0xE6, 0xEA)),
            BorderThickness  = new Thickness(1),
            Background       = Brushes.White,
            Margin           = new Thickness(0, 0, 0, 6),
        };

        var cardStack = new StackPanel();
        card.Child = cardStack;

        // ── Header button ────────────────────────────────────────────────
        _header = new Button
        {
            Height              = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background          = bgBrush,
            Foreground          = accentBrush,
            BorderThickness     = new Thickness(0),
            FontWeight          = FontWeights.Bold,
            FontSize            = 11,
            Padding             = new Thickness(12, 0, 12, 0),
            Cursor              = System.Windows.Input.Cursors.Hand,
        };
        _header.Click += OnHeaderClick;
        UpdateLabel(title);
        cardStack.Children.Add(_header);

        // ── Body (clipped for animation) ─────────────────────────────────
        _bodyClip = new Grid { ClipToBounds = true };

        _body = new StackPanel
        {
            Background = Brushes.White,
            Margin     = new Thickness(16, 10, 16, 12),
        };
        _bodyClip.Children.Add(_body);
        cardStack.Children.Add(_bodyClip);

        if (startCollapsed)
            _body.Visibility = Visibility.Collapsed;

        Content = card;
    }

    private void UpdateLabel(string title)
    {
        string arrow = _expanded ? "▼" : "▶";
        _header.Content = $"  {arrow}   {title.ToUpperInvariant()}";
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        // Re-derive title from button text (strip arrow prefix)
        var txt   = _header.Content?.ToString() ?? "";
        var bare  = txt.TrimStart().TrimStart('▼', '▶').Trim();
        UpdateLabel(bare.ToLowerInvariant()); // label was already upper

        if (_expanded)
        {
            _body.Visibility = Visibility.Visible;
            AnimateHeight(0, _body.DesiredSize.Height == 0
                ? _body.ActualHeight > 0 ? _body.ActualHeight : double.NaN
                : _body.DesiredSize.Height);
        }
        else
        {
            AnimateHeight(_bodyClip.ActualHeight, 0, onComplete: () => _body.Visibility = Visibility.Collapsed);
        }
    }

    private void AnimateHeight(double from, double to, Action? onComplete = null)
    {
        if (double.IsNaN(to))
        {
            // Can't animate to NaN – just show
            _bodyClip.MaxHeight = double.PositiveInfinity;
            return;
        }

        var anim = new DoubleAnimation
        {
            From           = from,
            To             = to,
            Duration       = TimeSpan.FromMilliseconds(to > from ? 180 : 150),
            EasingFunction = to > from
                ? new CubicEase { EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        if (onComplete != null)
            anim.Completed += (_, _) => onComplete();

        _bodyClip.BeginAnimation(MaxHeightProperty, anim);
    }

    /// <summary>Add a labelled key-value row to the section body.</summary>
    public void AddRow(string label, string value, bool bold = false, int keyWidth = 160)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keyWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyLabel = new TextBlock
        {
            Text                = $"{label}:",
            Foreground          = new SolidColorBrush(Color.FromRgb(0x65, 0x67, 0x6B)),
            FontSize            = 11,
            TextAlignment       = TextAlignment.Right,
            Margin              = new Thickness(0, 0, 10, 0),
            VerticalAlignment   = VerticalAlignment.Top,
            FontWeight          = bold ? FontWeights.Bold : FontWeights.Normal,
        };
        Grid.SetColumn(keyLabel, 0);

        var valLabel = new TextBlock
        {
            Text          = string.IsNullOrEmpty(value) ? "—" : value,
            Foreground    = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x21)),
            TextWrapping  = TextWrapping.Wrap,
            FontWeight    = bold ? FontWeights.Bold : FontWeights.Normal,
        };
        Grid.SetColumn(valLabel, 1);

        row.Children.Add(keyLabel);
        row.Children.Add(valLabel);
        _body.Children.Add(row);
    }

    /// <summary>Add a key-value row that expands to reveal per-component sub-rows on click.</summary>
    public void AddExpandableRow(string label, string value, List<(string SubLabel, string SubValue)> subItems,
                                 bool bold = false, int keyWidth = 160)
    {
        bool isExpanded = false;
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keyWidth) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.Cursor = System.Windows.Input.Cursors.Hand;

        var keyLabel = new TextBlock
        {
            Text              = $"{label}:",
            Foreground        = new SolidColorBrush(Color.FromRgb(0x65, 0x67, 0x6B)),
            FontSize          = 11,
            TextAlignment     = TextAlignment.Right,
            Margin            = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight        = bold ? FontWeights.Bold : FontWeights.Normal,
        };
        Grid.SetColumn(keyLabel, 0);

        var valLabel = new TextBlock
        {
            Text              = string.IsNullOrEmpty(value) ? "—" : value,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x21)),
            TextWrapping      = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight        = bold ? FontWeights.Bold : FontWeights.Normal,
        };
        Grid.SetColumn(valLabel, 1);

        var arrow = new TextBlock
        {
            Text              = "▶",
            FontSize          = 9,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x65, 0x67, 0x6B)),
            Margin            = new Thickness(6, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(arrow, 2);

        mainGrid.Children.Add(keyLabel);
        mainGrid.Children.Add(valLabel);
        mainGrid.Children.Add(arrow);

        var subPanel = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin     = new Thickness(keyWidth + 10, 2, 0, 4),
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA)),
        };

        foreach (var (sl, sv) in subItems)
        {
            var subGrid = new Grid { Margin = new Thickness(4, 1, 4, 1) };
            subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var subKey = new TextBlock
            {
                Text       = $"{sl}:  ",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8D, 0x91)),
                FontSize   = 10,
            };
            Grid.SetColumn(subKey, 0);

            var subVal = new TextBlock
            {
                Text         = string.IsNullOrEmpty(sv) ? "—" : sv,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x3A, 0x3B, 0x3C)),
                FontSize     = 10,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(subVal, 1);

            subGrid.Children.Add(subKey);
            subGrid.Children.Add(subVal);
            subPanel.Children.Add(subGrid);
        }

        mainGrid.MouseLeftButtonUp += (_, _) =>
        {
            isExpanded      = !isExpanded;
            arrow.Text      = isExpanded ? "▼" : "▶";
            subPanel.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        };

        outer.Children.Add(mainGrid);
        outer.Children.Add(subPanel);
        _body.Children.Add(outer);
    }
}
