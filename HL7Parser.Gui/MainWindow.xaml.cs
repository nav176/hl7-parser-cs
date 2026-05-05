using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using HL7Parser.Core;

namespace HL7Parser.Gui;

public partial class MainWindow : Window
{
    private HL7Message? _parsed;
    private readonly List<(TreeViewItem Item, Brush OrigBg, Brush OrigFg)> _highlighted = new();
    private double _treeZoom = 1.0;

    public MainWindow() => InitializeComponent();

    // ── Browse / Parse ───────────────────────────────────────────────────────

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Open HL7 File",
            Filter = "HL7 Files (*.hl7;*.txt)|*.hl7;*.txt|All Files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
        {
            PathBox.Text       = dlg.FileName;
            ParseBtn.IsEnabled = true;
            ShowStatus("File selected — click Parse to process.", error: false);
        }
    }

    private void OnParse(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        if (string.IsNullOrEmpty(path)) return;
        string raw;
        try { raw = File.ReadAllText(path, System.Text.Encoding.UTF8); }
        catch (FileNotFoundException) { ShowStatus($"File not found: {path}", error: true); return; }
        catch (Exception ex)          { ShowStatus($"Unexpected error: {ex.Message}", error: true); return; }
        ParseAndDisplay(raw, Path.GetFileName(path));
    }

    private void OnParseText(object sender, RoutedEventArgs e)
    {
        var text = MessageInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            ShowStatus("Paste an HL7 message into the text area first.", error: true);
            return;
        }
        ParseAndDisplay(text, "text input");
    }

    private void ParseAndDisplay(string text, string source)
    {
        try { _parsed = Parser.ParseRaw(text); }
        catch (HL7ParseError ex) { ShowStatus($"Parse error: {ex.Message}", error: true); return; }
        catch (Exception ex)     { ShowStatus($"Unexpected error: {ex.Message}", error: true); return; }

        ClearHighlights();
        SearchBox.Text = "";
        PopulateSegments(_parsed);
        PopulateJson(_parsed);
        ExpandBtn.IsEnabled   = true;
        CollapseBtn.IsEnabled = true;
        ShowStatus($"Parsed successfully — {source}.", error: false);
        Tabs.SelectedIndex = 0;
    }

    // ── Tree toolbar ─────────────────────────────────────────────────────────

    private void OnExpandAll(object sender, RoutedEventArgs e)
    {
        foreach (var item in SegTree.Items.OfType<TreeViewItem>())
            item.ExpandSubtree();
    }

    private void OnCollapseAll(object sender, RoutedEventArgs e)
    {
        foreach (var item in SegTree.Items.OfType<TreeViewItem>())
            item.IsExpanded = false;
    }

    // ── Zoom ─────────────────────────────────────────────────────────────────

    private void OnZoomIn(object sender, RoutedEventArgs e)
    {
        _treeZoom = Math.Min(2.0, Math.Round(_treeZoom + 0.1, 1));
        ApplyZoom();
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        _treeZoom = Math.Max(0.5, Math.Round(_treeZoom - 0.1, 1));
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        SegTree.LayoutTransform = new ScaleTransform(_treeZoom, _treeZoom);
        ZoomLabel.Text = $"{(int)Math.Round(_treeZoom * 100)}%";
    }

    // ── Segments tab ─────────────────────────────────────────────────────────

    private void PopulateSegments(HL7Message msg)
    {
        ClearHighlights();
        SegTree.Items.Clear();
        var repCounts = msg.RepeatingSegments.ToDictionary(k => k.Key, v => v.Value.Count);

        TreeViewItem MakeSubNode(string header) => AttachContextMenu(new TreeViewItem
        {
            Header     = header,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x57, 0x5B)),
            FontSize   = 10,
            FontWeight = FontWeights.Normal,
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA)),
        });

        void AddNode(string label, Dictionary<string, object?> seg)
        {
            var segId = label.Length >= 3 ? label[..3] : label;
            var top = AttachContextMenu(new TreeViewItem
            {
                Header     = label,
                IsExpanded = false,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x66, 0xC2)),
                FontWeight = FontWeights.Bold,
            });

            int fieldNum = 0;
            foreach (var (field, value) in seg)
            {
                if (field == "segment_id") continue;
                fieldNum++;
                var fieldLabel = $"{segId}-{fieldNum}  {field}";

                if (value is List<object?> lst)
                {
                    var composite = AttachContextMenu(new TreeViewItem
                    {
                        Header     = $"{fieldLabel}  =  {FlattenList(lst)}",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x21)),
                        FontWeight = FontWeights.Normal,
                        Background = Brushes.White,
                        IsExpanded = false,
                    });
                    bool hasSubLists = lst.Any(x => x is List<object?>);
                    if (hasSubLists)
                    {
                        for (int r = 0; r < lst.Count; r++)
                        {
                            if (lst[r] is List<object?> repComps)
                            {
                                var repVal = string.Join(" ^ ", repComps.Select(c => c?.ToString() ?? "").Where(s => s != ""));
                                var repNode = MakeSubNode($"{segId}-{fieldNum}~{r + 1}  =  {repVal}");
                                for (int c = 0; c < repComps.Count; c++)
                                    if (repComps[c] != null)
                                        repNode.Items.Add(MakeSubNode($"{segId}-{fieldNum}~{r + 1}.{c + 1}  =  {repComps[c]}"));
                                composite.Items.Add(repNode);
                            }
                            else if (lst[r] != null)
                            {
                                composite.Items.Add(MakeSubNode($"{segId}-{fieldNum}~{r + 1}  =  {lst[r]}"));
                            }
                        }
                    }
                    else
                    {
                        for (int c = 0; c < lst.Count; c++)
                            if (lst[c] != null)
                                composite.Items.Add(MakeSubNode($"{segId}-{fieldNum}.{c + 1}  =  {lst[c]}"));
                    }
                    top.Items.Add(composite);
                }
                else
                {
                    top.Items.Add(AttachContextMenu(new TreeViewItem
                    {
                        Header     = $"{fieldLabel}  =  {ValueStr(value)}",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x21)),
                        FontWeight = FontWeights.Normal,
                        Background = Brushes.White,
                    }));
                }
            }
            SegTree.Items.Add(top);
        }

        foreach (var (segId, repIdx) in msg.SegmentOrder)
        {
            if (repIdx is null)
            {
                if (msg.Segments.TryGetValue(segId, out var s)) AddNode(segId, s);
            }
            else
            {
                var insts = msg.RepeatingSegments.TryGetValue(segId, out var il) ? il : null;
                if (insts != null && repIdx < insts.Count)
                {
                    var count = repCounts.GetValueOrDefault(segId, 1);
                    var label = count > 1 ? $"{segId} [{repIdx + 1} of {count}]" : segId;
                    AddNode(label, insts[repIdx.Value]);
                }
            }
        }
    }

    // ── Raw JSON tab ─────────────────────────────────────────────────────────

    private void PopulateJson(HL7Message msg)
        => JsonView.Text = JsonSerializer.Serialize(msg,
               new JsonSerializerOptions { WriteIndented = true });

    // ── Status bar ───────────────────────────────────────────────────────────

    private void ShowStatus(string message, bool error)
    {
        StatusText.Text = message;
        if (error)
        {
            StatusBar.Background  = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xF0));
            StatusBar.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xCD, 0xD2));
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
        }
        else
        {
            StatusBar.Background  = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA));
            StatusBar.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE6, 0xEA));
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x65, 0x67, 0x6B));
        }
    }

    // ── Formatting helpers ────────────────────────────────────────────────────

    private static string ValueStr(object? v) => v switch
    {
        null              => "",
        string s          => s,
        List<object?> lst => FlattenList(lst),
        _                 => v.ToString()!,
    };

    private static string FlattenList(List<object?> lst)
    {
        if (lst.Count == 0) return "";
        bool hasSubLists = lst.Any(x => x is List<object?>);
        return hasSubLists
            ? string.Join(" ~ ", lst.Select(rep => rep is List<object?> comps
                ? string.Join(" ^ ", comps.Select(c => c?.ToString() ?? "").Where(s => s != ""))
                : rep?.ToString() ?? ""))
            : string.Join(" ^ ", lst.Select(c => c?.ToString() ?? "").Where(s => s != ""));
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private static TreeViewItem AttachContextMenu(TreeViewItem item)
    {
        var cm = new ContextMenu();

        var copyVal = new MenuItem { Header = "Copy Value" };
        copyVal.Click += (_, _) =>
        {
            var txt = item.Header?.ToString() ?? "";
            var idx = txt.IndexOf("  =  ", StringComparison.Ordinal);
            Clipboard.SetText(idx >= 0 ? txt[(idx + 5)..].Trim() : txt.Trim());
        };

        var copyLine = new MenuItem { Header = "Copy Full Line" };
        copyLine.Click += (_, _) => Clipboard.SetText((item.Header?.ToString() ?? "").Trim());

        cm.Items.Add(copyVal);
        cm.Items.Add(new Separator());
        cm.Items.Add(copyLine);
        item.ContextMenu = cm;
        return item;
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private void ClearHighlights()
    {
        foreach (var (item, bg, fg) in _highlighted)
        {
            item.Background = bg;
            item.Foreground = fg;
        }
        _highlighted.Clear();
    }

    private void Highlight(TreeViewItem item)
    {
        _highlighted.Add((item, item.Background, item.Foreground));
        item.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0x76));
        item.Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x21));
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        ClearHighlights();
        var query = SearchBox.Text.Trim();
        if (!string.IsNullOrEmpty(query))
            SearchSegTree(query);
    }

    private void OnSearchClear(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        ClearHighlights();
    }

    private void SearchSegTree(string query)
    {
        // PV1.3 or PV1-3 — segment + field number
        var fieldRef = Regex.Match(query, @"^([A-Za-z]{2,3})[.\-](\d+)$");
        if (fieldRef.Success)
        {
            var seg  = fieldRef.Groups[1].Value.ToUpperInvariant();
            var fnum = fieldRef.Groups[2].Value;
            foreach (TreeViewItem top in SegTree.Items)
            {
                var topText = top.Header?.ToString() ?? "";
                if (!topText.StartsWith(seg, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (TreeViewItem child in top.Items)
                {
                    if ((child.Header?.ToString() ?? "").StartsWith($"{seg}-{fnum}  ", StringComparison.Ordinal))
                    {
                        top.IsExpanded = true;
                        Highlight(child);
                    }
                }
            }
            ScrollToFirstMatch();
            return;
        }

        // PV1 — segment ID only
        if (Regex.IsMatch(query, @"^[A-Za-z]{2,3}$"))
        {
            foreach (TreeViewItem top in SegTree.Items)
            {
                if ((top.Header?.ToString() ?? "").StartsWith(query, StringComparison.OrdinalIgnoreCase))
                {
                    top.IsExpanded = true;
                    Highlight(top);
                }
            }
            ScrollToFirstMatch();
            return;
        }

        // Free-text — search field labels and values
        foreach (TreeViewItem top in SegTree.Items)
        {
            foreach (TreeViewItem child in top.Items)
            {
                var txt = child.Header?.ToString() ?? "";
                if (txt.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    top.IsExpanded = true;
                    Highlight(child);
                }
            }
        }
        ScrollToFirstMatch();
    }

    private void ScrollToFirstMatch()
    {
        if (_highlighted.Count == 0) return;
        Dispatcher.InvokeAsync(
            () => _highlighted[0].Item.BringIntoView(),
            System.Windows.Threading.DispatcherPriority.Render);
    }
}
