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
    private readonly List<(Border Item, Brush OrigBg)> _summaryHighlights = new();
    private double _treeZoom          = 1.0;
    private int    _matchIndex        = -1;
    private int    _summaryMatchIndex = -1;
    private bool   _hideEmpty         = false;
    private bool   _summaryHideEmpty  = false;
    private bool   _darkMode          = false;
    private bool   _statusIsError     = false;

    public MainWindow()
    {
        InitializeComponent();
        SegTree.AddHandler(TreeViewItem.SelectedEvent,   new RoutedEventHandler(OnTreeNodeSelected));
        SegTree.AddHandler(TreeViewItem.UnselectedEvent, new RoutedEventHandler(OnTreeNodeUnselected));
    }

    private static void OnTreeNodeSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem tvi)
        {
            tvi.Tag = tvi.Foreground;
            tvi.Foreground = Brushes.White;
        }
    }

    private static void OnTreeNodeUnselected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem tvi && tvi.Tag is Brush orig)
            tvi.Foreground = orig;
    }

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
        ClearSummaryHighlights();
        SearchBox.Text        = "";
        SummarySearchBox.Text = "";
        PopulateSummary(_parsed);
        PopulateSegments(_parsed);
        PopulateJson(_parsed);
        if (_hideEmpty)        ApplyHideEmpty();
        if (_summaryHideEmpty) ApplySummaryHideEmpty();
        ExpandBtn.IsEnabled        = true;
        CollapseBtn.IsEnabled      = true;
        HideEmptyBtn.IsEnabled     = true;
        SummaryExpandBtn.IsEnabled    = true;
        SummaryCollapseBtn.IsEnabled  = true;
        SummaryHideEmptyBtn.IsEnabled = true;
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

    private void OnToggleHideEmpty(object sender, RoutedEventArgs e)
    {
        _hideEmpty = !_hideEmpty;
        HideEmptyBtn.Content = _hideEmpty ? "Show Empty" : "Hide Empty";
        ApplyHideEmpty();
    }

    // ── Dark mode ─────────────────────────────────────────────────────────────

    private void OnToggleDarkMode(object sender, RoutedEventArgs e)
    {
        _darkMode = !_darkMode;
        var src = _darkMode ? "Themes/Dark.xaml" : "Themes/Light.xaml";
        var dict = new ResourceDictionary { Source = new Uri(src, UriKind.Relative) };
        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(dict);

        DarkModeIcon.Text   = _darkMode ? "" : "";  // Sun : Moon
        DarkModeBtn.ToolTip = _darkMode ? "Switch to light mode" : "Switch to dark mode";

        ClearSummaryHighlights();
        SummarySearchBox.Text = "";

        // Update tree item colors in-place — preserves expand/collapse state and scroll position
        RefreshTreeColors();

        // Re-apply status bar with new theme colors
        ShowStatus(StatusText.Text, _statusIsError);
    }

    // Walk tree items and repaint them without rebuilding the tree structure.
    private void RefreshTreeColors()
    {
        // Clear search highlights first — their stored originals would be stale after recolor
        ClearHighlights();
        SearchBox.Text = "";

        foreach (TreeViewItem segNode in SegTree.Items)
        {
            // Segment header stays brand blue — skip it, just process children
            foreach (TreeViewItem fieldNode in segNode.Items.OfType<TreeViewItem>())
            {
                fieldNode.Background = Res("SurfaceBg");
                fieldNode.Foreground = Res("Fg");
                RefreshSubNodes(fieldNode);
            }
        }
    }

    private static void RefreshSubNodes(TreeViewItem parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            child.Background = Res("RaisedBg");
            child.Foreground = Res("SubNodeFg");
            RefreshSubNodes(child);
        }
    }

    // ── Hide empty fields ─────────────────────────────────────────────────────

    private static bool IsEmptyField(TreeViewItem item)
    {
        var txt = item.Header?.ToString() ?? "";
        var idx = txt.IndexOf("  =  ", StringComparison.Ordinal);
        if (idx < 0) return false;
        return string.IsNullOrWhiteSpace(txt[(idx + 5)..]);
    }

    private void ApplyHideEmpty()
    {
        foreach (TreeViewItem top in SegTree.Items)
            foreach (TreeViewItem child in top.Items.OfType<TreeViewItem>())
                SetItemVisibility(child);
    }

    private bool SetItemVisibility(TreeViewItem item)
    {
        bool anyChildVisible = false;
        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            if (SetItemVisibility(child)) anyChildVisible = true;

        bool hide = _hideEmpty && IsEmptyField(item) && !anyChildVisible;
        item.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        return !hide;
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

    // ── Summary tab ──────────────────────────────────────────────────────────

    private void PopulateSummary(HL7Message msg)
    {
        SummaryPanel.Children.Clear();
        CollapsibleSection.ResetColours();

        foreach (var (segId, repIdx) in msg.SegmentOrder)
        {
            Dictionary<string, object?>? seg = null;
            string blockLabel;

            if (repIdx is null)
            {
                msg.Segments.TryGetValue(segId, out seg);
                blockLabel = segId;
            }
            else
            {
                if (msg.RepeatingSegments.TryGetValue(segId, out var insts) && repIdx < insts.Count)
                {
                    seg = insts[repIdx.Value];
                    var count = msg.RepeatingSegments[segId].Count;
                    blockLabel = count > 1 ? $"{segId} [{repIdx + 1} of {count}]" : segId;
                }
                else blockLabel = segId;
            }

            if (seg == null) continue;

            var sec = AddSection(blockLabel);

            foreach (var (field, val) in seg)
            {
                if (field == "segment_id") continue;
                int num = GetFieldNumber(segId, field);
                var rowLabel = num > 0 ? $"{segId}-{num}  {field}" : field;

                if (val is List<object?> list && list.Count > 0)
                {
                    var subItems = GetSubItems(list);
                    if (subItems.Count > 0)
                        sec.AddExpandableRow(rowLabel, ValueStr(val), subItems, keyWidth: 180);
                    else
                        sec.AddRow(rowLabel, ValueStr(val), keyWidth: 180);
                }
                else
                {
                    sec.AddRow(rowLabel, ValueStr(val), keyWidth: 180);
                }
            }
        }
    }

    private CollapsibleSection AddSection(string title)
    {
        var sec = new CollapsibleSection(title);
        SummaryPanel.Children.Add(sec);
        return sec;
    }

    private static int GetFieldNumber(string segId, string fieldName)
    {
        if (!SegmentFields.Fields.TryGetValue(segId, out var names)) return 0;
        var idx = Array.IndexOf(names, fieldName);
        return idx > 0 ? idx : 0;
    }

    private static List<(string SubLabel, string SubValue)> GetSubItems(List<object?> list)
    {
        var result = new List<(string, string)>();
        bool hasReps = list.Any(x => x is List<object?>);
        if (hasReps)
        {
            for (int r = 0; r < list.Count; r++)
            {
                var rep = list[r];
                var repStr = rep is List<object?> comps
                    ? string.Join(" ^ ", comps.Select(c => c?.ToString() ?? "").Where(s => s != ""))
                    : rep?.ToString() ?? "";
                if (!string.IsNullOrEmpty(repStr))
                    result.Add(($"Rep {r + 1}", repStr));
            }
        }
        else
        {
            for (int c = 0; c < list.Count; c++)
            {
                var cv = list[c]?.ToString();
                if (!string.IsNullOrEmpty(cv))
                    result.Add(($".{c + 1}", cv));
            }
        }
        return result;
    }

    // ── Summary toolbar ──────────────────────────────────────────────────────

    private void OnSummaryExpandAll(object sender, RoutedEventArgs e)
    {
        foreach (var sec in SummaryPanel.Children.OfType<CollapsibleSection>())
            sec.SetExpanded(true);
    }

    private void OnSummaryCollapseAll(object sender, RoutedEventArgs e)
    {
        foreach (var sec in SummaryPanel.Children.OfType<CollapsibleSection>())
            sec.SetExpanded(false);
    }

    private void OnSummaryToggleHideEmpty(object sender, RoutedEventArgs e)
    {
        _summaryHideEmpty = !_summaryHideEmpty;
        SummaryHideEmptyBtn.Content = _summaryHideEmpty ? "Show Empty" : "Hide Empty";
        ApplySummaryHideEmpty();
    }

    private void ApplySummaryHideEmpty()
    {
        foreach (var sec in SummaryPanel.Children.OfType<CollapsibleSection>())
        {
            bool allHidden = sec.ApplyHideEmpty(_summaryHideEmpty);
            sec.Visibility = allHidden ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void OnSummarySearchChanged(object sender, TextChangedEventArgs e)
    {
        ClearSummaryHighlights();
        var query = SummarySearchBox.Text.Trim();
        if (!string.IsNullOrEmpty(query))
            SearchSummary(query);
    }

    private void OnSummaryFindNext(object sender, RoutedEventArgs e) => AdvanceSummaryMatch(+1);
    private void OnSummaryFindPrev(object sender, RoutedEventArgs e) => AdvanceSummaryMatch(-1);

    private void AdvanceSummaryMatch(int delta)
    {
        if (_summaryHighlights.Count == 0) return;
        ScrollToSummaryMatch((_summaryMatchIndex + delta + _summaryHighlights.Count) % _summaryHighlights.Count);
    }

    private void SearchSummary(string query)
    {
        foreach (var sec in SummaryPanel.Children.OfType<CollapsibleSection>())
        {
            foreach (var border in sec.FindRows(query))
            {
                _summaryHighlights.Add((border, border.Background ?? Brushes.Transparent));
                border.Background = Res("MatchBg");
            }
        }
        if (_summaryHighlights.Count > 0)
            ScrollToSummaryMatch(0);
    }

    private void ClearSummaryHighlights()
    {
        foreach (var (item, origBg) in _summaryHighlights)
            item.Background = origBg;
        _summaryHighlights.Clear();
        _summaryMatchIndex = -1;
    }

    private void ScrollToSummaryMatch(int idx)
    {
        if (_summaryHighlights.Count == 0) return;

        if (_summaryMatchIndex >= 0 && _summaryMatchIndex < _summaryHighlights.Count)
            _summaryHighlights[_summaryMatchIndex].Item.Background = Res("MatchBg");

        _summaryMatchIndex = idx;
        var cur = _summaryHighlights[_summaryMatchIndex].Item;
        cur.Background = Res("MatchActiveBg");

        Dispatcher.InvokeAsync(
            () => cur.BringIntoView(),
            System.Windows.Threading.DispatcherPriority.Render);
    }

    // ── Segments tab ─────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private void PopulateSegments(HL7Message msg)
    {
        ClearHighlights();
        SegTree.Items.Clear();
        var repCounts = msg.RepeatingSegments.ToDictionary(k => k.Key, v => v.Value.Count);

        TreeViewItem MakeSubNode(string header) => AttachContextMenu(new TreeViewItem
        {
            Header     = header,
            Foreground = Res("SubNodeFg"),
            FontWeight = FontWeights.Normal,
            Background = Res("RaisedBg"),
        });

        void AddNode(int lineNum, string label, Dictionary<string, object?> seg)
        {
            var segId = label.Length >= 3 ? label[..3] : label;
            var top = AttachContextMenu(new TreeViewItem
            {
                Header     = $"{lineNum}  {label}",
                IsExpanded = false,
                Foreground = Res("Fg"),
                Background = Res("SurfaceBg"),
                FontWeight = FontWeights.SemiBold,
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
                        Foreground = Res("Fg"),
                        FontWeight = FontWeights.Normal,
                        Background = Res("SurfaceBg"),
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
                                {
                                    if (repComps[c] == null) continue;
                                    var cn = ComponentFields.Get(segId, fieldNum, c + 1);
                                    var lbl = cn != null
                                        ? $"{segId}-{fieldNum}~{r + 1}.{c + 1}  {cn}  =  {repComps[c]}"
                                        : $"{segId}-{fieldNum}~{r + 1}.{c + 1}  =  {repComps[c]}";
                                    repNode.Items.Add(MakeSubNode(lbl));
                                }
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
                        {
                            if (lst[c] == null) continue;
                            var cn = ComponentFields.Get(segId, fieldNum, c + 1);
                            var lbl = cn != null
                                ? $"{segId}-{fieldNum}.{c + 1}  {cn}  =  {lst[c]}"
                                : $"{segId}-{fieldNum}.{c + 1}  =  {lst[c]}";
                            composite.Items.Add(MakeSubNode(lbl));
                        }
                    }
                    top.Items.Add(composite);
                }
                else
                {
                    top.Items.Add(AttachContextMenu(new TreeViewItem
                    {
                        Header     = $"{fieldLabel}  =  {ValueStr(value)}",
                        Foreground = Res("Fg"),
                        FontWeight = FontWeights.Normal,
                        Background = Res("SurfaceBg"),
                    }));
                }
            }
            SegTree.Items.Add(top);
        }

        int lineNum = 0;
        foreach (var (segId, repIdx) in msg.SegmentOrder)
        {
            lineNum++;
            if (repIdx is null)
            {
                if (msg.Segments.TryGetValue(segId, out var s)) AddNode(lineNum, segId, s);
            }
            else
            {
                var insts = msg.RepeatingSegments.TryGetValue(segId, out var il) ? il : null;
                if (insts != null && repIdx < insts.Count)
                {
                    var count = repCounts.GetValueOrDefault(segId, 1);
                    var label = count > 1 ? $"{segId} [{repIdx + 1} of {count}]" : segId;
                    AddNode(lineNum, label, insts[repIdx.Value]);
                }
            }
        }
    }

    // ── Raw JSON tab ─────────────────────────────────────────────────────────

    private void PopulateJson(HL7Message msg)
        => JsonView.Text = JsonSerializer.Serialize(msg, JsonOpts);

    // ── Status bar ───────────────────────────────────────────────────────────

    private void ShowStatus(string message, bool error)
    {
        _statusIsError        = error;
        StatusText.Text       = message;
        StatusBar.Background  = Res(error ? "StatusErrBg"     : "StatusOkBg");
        StatusBar.BorderBrush = Res(error ? "StatusErrBorder" : "ThemeBorder");
        StatusText.Foreground = Res(error ? "StatusErrFg"     : "StatusOkFg");
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
        _matchIndex = -1;
    }

    private void Highlight(TreeViewItem item)
    {
        _highlighted.Add((item, item.Background, item.Foreground));
        item.Background = Res("MatchBg");
        item.Foreground = Res("MatchFg");
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        ClearHighlights();
        var query = SearchBox.Text.Trim();
        if (!string.IsNullOrEmpty(query))
            SearchSegTree(query);
    }

    private void OnFindNext(object sender, RoutedEventArgs e) => AdvanceMatch(+1);
    private void OnFindPrev(object sender, RoutedEventArgs e) => AdvanceMatch(-1);

    private void AdvanceMatch(int delta)
    {
        if (_highlighted.Count == 0) return;
        ScrollToMatch((_matchIndex + delta + _highlighted.Count) % _highlighted.Count);
    }

    // Segment headers are formatted as "{lineNum}  {segId}…" — strip the leading "N  " prefix.
    private static string SegIdFromHeader(TreeViewItem item)
    {
        var h = item.Header?.ToString() ?? "";
        var i = h.IndexOf("  ", StringComparison.Ordinal);
        return i >= 0 ? h[(i + 2)..] : h;
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
                if (!SegIdFromHeader(top).StartsWith(seg, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (TreeViewItem child in top.Items)
                {
                    if ((child.Header?.ToString() ?? "").StartsWith($"{seg}-{fnum}  ", StringComparison.Ordinal))
                    {
                        top.IsExpanded = true;
                        Highlight(child);
                    }
                }
            }
            ScrollToMatch(0);
            return;
        }

        // PV1 — segment ID only
        if (Regex.IsMatch(query, @"^[A-Za-z]{2,3}$"))
        {
            foreach (TreeViewItem top in SegTree.Items)
            {
                if (SegIdFromHeader(top).StartsWith(query, StringComparison.OrdinalIgnoreCase))
                {
                    top.IsExpanded = true;
                    Highlight(top);
                }
            }
            ScrollToMatch(0);
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
        ScrollToMatch(0);
    }

    private void ScrollToMatch(int idx)
    {
        if (_highlighted.Count == 0) return;

        // Restore previous current match back to plain highlight
        if (_matchIndex >= 0 && _matchIndex < _highlighted.Count)
        {
            _highlighted[_matchIndex].Item.Background = Res("MatchBg");
            _highlighted[_matchIndex].Item.Foreground = Res("MatchFg");
        }

        _matchIndex = idx;
        var cur = _highlighted[_matchIndex].Item;
        cur.Background = Res("MatchActiveBg");
        cur.Foreground = Res("MatchActiveFg");

        Dispatcher.InvokeAsync(
            () => cur.BringIntoView(),
            System.Windows.Threading.DispatcherPriority.Render);
    }
}
