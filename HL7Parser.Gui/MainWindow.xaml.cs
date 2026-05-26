using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using HL7Parser.Core;
using Microsoft.Win32;

namespace HL7Parser.Gui;

public partial class MainWindow : Window
{
    private HL7Message? _parsed;
    private string[]    _parsedLines = [];
    private readonly List<(TreeViewItem Item, Brush OrigBg, Brush OrigFg)> _highlighted = new();
    private double _treeZoom      = 1.0;
    private int    _matchIndex    = -1;
    private bool   _hideEmpty     = false;
    private bool   _darkMode      = false;
    private bool   _statusIsError = false;

    public MainWindow()
    {
        InitializeComponent();
        SegTree.AddHandler(TreeViewItem.SelectedEvent,   new RoutedEventHandler(OnTreeNodeSelected));
        SegTree.AddHandler(TreeViewItem.UnselectedEvent, new RoutedEventHandler(OnTreeNodeUnselected));
    }

    private void OnTreeNodeSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem tvi) return;
        tvi.Tag = tvi.Foreground;
        tvi.Foreground = Brushes.White;
        HighlightInEditor(tvi);
    }

    private static void OnTreeNodeUnselected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem tvi && tvi.Tag is Brush orig)
            tvi.Foreground = orig;
    }

    // ── Parse ────────────────────────────────────────────────────────────────

    private void OnLoadFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Open HL7 File",
            Filter = "HL7 Files (*.hl7;*.txt)|*.hl7;*.txt|All Files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            MessageInputBox.Text = File.ReadAllText(dlg.FileName);
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not read file: {ex.Message}", error: true);
        }
    }

    private void OnParseText(object sender, RoutedEventArgs e)
    {
        var text = MessageInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            ShowStatus("Paste an HL7 message into the text area first.", error: true);
            return;
        }
        ParseAndDisplay(text);
    }

    private void ParseAndDisplay(string text)
    {
        try { _parsed = Parser.ParseRaw(text); }
        catch (HL7ParseError ex) { ShowStatus($"Parse error: {ex.Message}", error: true); return; }
        catch (Exception ex)     { ShowStatus($"Unexpected error: {ex.Message}", error: true); return; }

        _parsedLines = text
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        var treeState = CaptureTreeState();

        ClearHighlights();
        SearchBox.Text = "";
        PopulateSegments(_parsed);
        PopulateJson(_parsed);

        RestoreTreeState(treeState);

        if (_hideEmpty) ApplyHideEmpty();
        ExpandBtn.IsEnabled    = true;
        CollapseBtn.IsEnabled  = true;
        HideEmptyBtn.IsEnabled = true;
        ShowStatus($"Parsed — {_parsed.SegmentOrder.Count} segments.", error: false);
        Tabs.SelectedIndex = 0;
    }

    private Dictionary<string, HashSet<string>> CaptureTreeState()
    {
        var state = new Dictionary<string, HashSet<string>>();
        foreach (TreeViewItem segNode in SegTree.Items)
        {
            if (!segNode.IsExpanded) continue;
            var segLabel = StripLineNumber(segNode.Header?.ToString() ?? "");
            var expandedFields = new HashSet<string>();
            foreach (TreeViewItem fieldNode in segNode.Items.OfType<TreeViewItem>())
            {
                if (fieldNode.IsExpanded)
                    expandedFields.Add(fieldNode.Header?.ToString() ?? "");
            }
            state[segLabel] = expandedFields;
        }
        return state;
    }

    private void RestoreTreeState(Dictionary<string, HashSet<string>> state)
    {
        foreach (TreeViewItem segNode in SegTree.Items)
        {
            var segLabel = StripLineNumber(segNode.Header?.ToString() ?? "");
            if (!state.TryGetValue(segLabel, out var expandedFields)) continue;
            segNode.IsExpanded = true;
            foreach (TreeViewItem fieldNode in segNode.Items.OfType<TreeViewItem>())
            {
                if (expandedFields.Contains(fieldNode.Header?.ToString() ?? ""))
                    fieldNode.IsExpanded = true;
            }
        }
    }

    private static string StripLineNumber(string header)
    {
        var m = Regex.Match(header, @"^\d+\s{2}(.+)$");
        return m.Success ? m.Groups[1].Value : header;
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

        DarkModeIcon.Text   = _darkMode ? "" : "";  // Sun : Moon
        DarkModeBtn.ToolTip = _darkMode ? "Switch to light mode" : "Switch to dark mode";

        // Update tree item colors in-place — preserves expand/collapse state and scroll position
        RefreshTreeColors();

        // Re-apply status bar with new theme colors
        ShowStatus(StatusText.Text, _statusIsError);
    }

    private void RefreshTreeColors()
    {
        ClearHighlights();
        SearchBox.Text = "";

        foreach (TreeViewItem segNode in SegTree.Items)
        {
            segNode.Background = Res("SurfaceBg");
            segNode.Foreground = Res("Fg");
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

    // ── Editor highlight on tree selection ───────────────────────────────────

    private void HighlightInEditor(TreeViewItem tvi)
    {
        if (_parsedLines.Length == 0) return;

        var header = tvi.Header?.ToString() ?? "";

        // Segment root node: "N  SEGID..."
        var segRootMatch = Regex.Match(header, @"^(\d+)\s{2}");
        if (segRootMatch.Success)
        {
            int segIdx = int.Parse(segRootMatch.Groups[1].Value) - 1;
            var editorLine = FindEditorLine(segIdx);
            if (editorLine < 0) return;
            var docLine = MessageInputBox.Document.GetLineByNumber(editorLine);
            MessageInputBox.Select(docLine.Offset, docLine.Length);
            MessageInputBox.ScrollToLine(editorLine);
            return;
        }

        // Field / sub-field node: "SEGID-N..."
        var fieldMatch = Regex.Match(header, @"^([A-Z0-9]{2,3})-(\d+)");
        if (!fieldMatch.Success) return;

        var segId    = fieldMatch.Groups[1].Value;
        int fieldNum = int.Parse(fieldMatch.Groups[2].Value);

        var root      = GetSegmentRoot(tvi);
        var rootMatch = Regex.Match(root.Header?.ToString() ?? "", @"^(\d+)\s{2}");
        if (!rootMatch.Success) return;

        int segIdx2    = int.Parse(rootMatch.Groups[1].Value) - 1;
        int editorLine2 = FindEditorLine(segIdx2);
        if (editorLine2 < 0) return;

        var docLine2  = MessageInputBox.Document.GetLineByNumber(editorLine2);
        var lineText  = MessageInputBox.Document.GetText(docLine2.Offset, docLine2.Length);

        var (relOff, len) = GetFieldSpan(segId, fieldNum, lineText);
        if (relOff < 0) return;

        MessageInputBox.Select(docLine2.Offset + relOff, len);
        MessageInputBox.ScrollToLine(editorLine2);
    }

    private int FindEditorLine(int segIdx)
    {
        if (segIdx < 0 || segIdx >= _parsedLines.Length) return -1;
        var target = _parsedLines[segIdx].Trim();
        for (int i = 1; i <= MessageInputBox.Document.LineCount; i++)
        {
            var dl   = MessageInputBox.Document.GetLineByNumber(i);
            var text = MessageInputBox.Document.GetText(dl.Offset, dl.Length).Trim();
            if (text == target) return i;
        }
        return -1;
    }

    // Returns (offset, length) of the given field within a raw HL7 line.
    private static (int Offset, int Length) GetFieldSpan(string segId, int fieldNum, string line)
    {
        // MSH-1 is the literal | separator at position 3
        if (segId == "MSH" && fieldNum == 1)
            return (3, 1);

        // For MSH, fields in the split array are at index = fieldNum-1;
        // for all other segments, at index = fieldNum.
        int pipeIdx = segId == "MSH" ? fieldNum - 1 : fieldNum;

        var parts = line.Split('|');
        if (pipeIdx >= parts.Length) return (-1, 0);

        int offset = 0;
        for (int i = 0; i < pipeIdx; i++)
            offset += parts[i].Length + 1; // +1 for the | separator

        return (offset, parts[pipeIdx].Length);
    }

    private static TreeViewItem GetSegmentRoot(TreeViewItem item)
    {
        var current = item;
        while (current.Parent is TreeViewItem parent)
            current = parent;
        return current;
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
