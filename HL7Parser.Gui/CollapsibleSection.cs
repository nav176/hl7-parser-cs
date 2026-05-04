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
    private bool                  _expanded = true;

    public CollapsibleSection(string title)
    {
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
    public void AddRow(string label, string value, bool bold = false)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
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
}
