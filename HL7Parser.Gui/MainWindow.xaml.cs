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
            PathBox.Text      = dlg.FileName;
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
        PopulateSummary(_parsed);
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

    // ── Summary tab ──────────────────────────────────────────────────────────

    private void PopulateSummary(HL7Message msg)
    {
        SummaryPanel.Children.Clear();
        CollapsibleSection.ResetColours();

        var segs = msg.Segments;
        var rep  = msg.RepeatingSegments;

        // Message
        var sec = AddSection("Message");
        sec.AddRow("Message Type",        msg.MessageType);
        sec.AddRow("Event Type",          Extractors.GetEventType(msg) ?? "");
        sec.AddRow("Control ID",          msg.MessageControlId ?? "");
        sec.AddRow("HL7 Version",         msg.Version ?? "");
        if (segs.TryGetValue("MSH", out var msh))
        {
            sec.AddRow("Sending Application", FirstComponent(msh.GetValueOrDefault("sending_application")));
            sec.AddRow("Sending Facility",    FirstComponent(msh.GetValueOrDefault("sending_facility")));
            sec.AddRow("Message DateTime",    FieldStr(msh, "datetime"));
        }

        // Patient
        if (segs.TryGetValue("PID", out var pid))
        {
            sec = AddSection("Patient");
            sec.AddRow("Patient ID (MRN)", Extractors.GetPatientId(msg) ?? "", bold: true);
            sec.AddRow("Patient Name",     FormatName(Extractors.GetPatientName(msg)), bold: true);
            sec.AddRow("Date of Birth",    FieldStr(pid, "datetime_of_birth"));
            sec.AddRow("Sex",              FieldStr(pid, "administrative_sex"));
            sec.AddRow("Address",          FormatAddress(pid.GetValueOrDefault("patient_address")));
            sec.AddRow("Phone (Home)",     FormatPhone(pid.GetValueOrDefault("phone_number_home")));
            sec.AddRow("Account Number",   FirstComponent(pid.GetValueOrDefault("patient_account_number")));
        }

        // Visit
        if (segs.TryGetValue("PV1", out var pv1))
        {
            sec = AddSection("Visit");
            sec.AddRow("Patient Class",          FieldStr(pv1, "patient_class"));
            sec.AddRow("Location",               FormatLocation(Extractors.GetPatientLocation(msg)));
            sec.AddRow("Admission Type",         FieldStr(pv1, "admission_type"));
            sec.AddRow("Attending MD",           FormatDoctor(Extractors.GetAttendingDoctor(msg)));
            sec.AddRow("Referring MD",           FormatDoctor(Extractors.GetReferringDoctor(msg)));
            sec.AddRow("Admit DateTime",         FieldStr(pv1, "admit_datetime"));
            sec.AddRow("Discharge DateTime",     FieldStr(pv1, "discharge_datetime"));
            sec.AddRow("Discharge Disposition",  FieldStr(pv1, "discharge_disposition"));
        }

        // Clinical Summary
        var allergies    = rep.TryGetValue("AL1",  out var a)  ? a  : new();
        var diagnoses    = rep.TryGetValue("DG1",  out var d)  ? d  : new();
        var observations = rep.TryGetValue("OBX",  out var o)  ? o  : new();
        var nok          = rep.TryGetValue("NK1",  out var nk) ? nk : new();

        if (allergies.Count > 0 || diagnoses.Count > 0 || observations.Count > 0 || nok.Count > 0)
        {
            sec = AddSection("Clinical Summary");
            if (allergies.Count > 0)
            {
                var codes = allergies.Select(al => CodedText(al, "allergen_code")).Where(s => s != "");
                sec.AddRow($"Allergies ({allergies.Count})", string.Join(", ", codes));
            }
            if (diagnoses.Count > 0)
            {
                var descs = diagnoses.Select(dg => CodedText(dg, "diagnosis_code")).Where(s => s != "");
                sec.AddRow($"Diagnoses ({diagnoses.Count})", string.Join("; ", descs));
            }
            if (observations.Count > 0)
                sec.AddRow("Observations", $"{observations.Count} recorded");
            if (nok.Count > 0)
            {
                var names = nok.Select(nk1 => NkName(nk1)).Where(s => s != "");
                sec.AddRow($"Next of Kin ({nok.Count})", string.Join(", ", names));
            }
        }

        // Acknowledgment
        if (segs.TryGetValue("MSA", out var msa))
        {
            sec = AddSection("Acknowledgment");
            var ackCode = FieldStr(msa, "acknowledgment_code");
            var ackLabel = ackCode switch
            {
                "AA" => "Accept (AA)", "AE" => "Application Error (AE)",
                "AR" => "Reject (AR)", _ => ackCode,
            };
            sec.AddRow("Ack Code",             ackLabel);
            sec.AddRow("Original Control ID",  FieldStr(msa, "message_control_id"));
            var msgText = FieldStr(msa, "text_message");
            if (!string.IsNullOrEmpty(msgText))
                sec.AddRow("Message", msgText);
        }

        // Orders
        var orcs = rep.TryGetValue("ORC", out var orcList) ? orcList : new();
        var obrs = rep.TryGetValue("OBR", out var obrList) ? obrList : new();
        var ntes = rep.TryGetValue("NTE", out var nteList) ? nteList : new();
        if (orcs.Count > 0 || obrs.Count > 0)
        {
            sec = AddSection($"Orders ({obrs.Count})");
            var svcs = obrs.Select(ob => CodedText(ob, "universal_service_id")).Where(s => s != "");
            if (svcs.Any()) sec.AddRow("Services", string.Join(" | ", svcs));

            var statuses = obrs.Select(ob => FieldStr(ob, "result_status"))
                               .Where(s => s != "Unknown" && s != "")
                               .Distinct();
            var statusMap = new Dictionary<string, string>
            {
                ["P"] = "Preliminary", ["F"] = "Final",    ["I"] = "In Process",
                ["R"] = "Results stored", ["C"] = "Corrected", ["X"] = "No results available",
            };
            if (statuses.Any())
                sec.AddRow("Result Status", string.Join(", ",
                    statuses.Select(s => statusMap.TryGetValue(s, out var lbl) ? lbl : s)));
            if (ntes.Count > 0)
            {
                var comments = ntes.Select(n => FieldStr(n, "comment")).Where(s => s != "Unknown" && s != "").Take(3);
                sec.AddRow($"Notes ({ntes.Count})", string.Join(" | ", comments));
            }
        }

        // Appointment
        var aisList = rep.TryGetValue("AIS", out var aisL) ? aisL : new();
        var aipList = rep.TryGetValue("AIP", out var aipL) ? aipL : new();
        if ((segs.TryGetValue("SCH", out var sch) && sch.Count > 0) || aisList.Count > 0)
        {
            sec = AddSection("Appointment");
            if (sch != null)
            {
                sec.AddRow("Placer Appt ID", FirstComponent(sch.GetValueOrDefault("placer_appointment_id")));
                sec.AddRow("Filler Appt ID", FirstComponent(sch.GetValueOrDefault("filler_appointment_id")));
                sec.AddRow("Reason",         CodedText(sch, "appointment_reason"));
                sec.AddRow("Type",           CodedText(sch, "appointment_type"));
                var dur = FieldStr(sch, "appointment_duration");
                if (dur != "Unknown" && dur != "")
                    sec.AddRow("Duration", $"{dur} {CodedText(sch, "appointment_duration_units")}".Trim());
            }
            if (aisList.Count > 0)
            {
                var svcs2 = aisList.Select(ai => CodedText(ai, "universal_service_id")).Where(s => s != "");
                sec.AddRow($"Services ({aisList.Count})", string.Join(" | ", svcs2));
            }
            if (aipList.Count > 0)
            {
                var providers = aipList.Select(ai => AipPersonnel(ai)).Where(s => s != "");
                sec.AddRow($"Personnel ({aipList.Count})", string.Join(", ", providers));
            }
        }

        // Financial
        var ft1List = rep.TryGetValue("FT1", out var ft1L) ? ft1L : new();
        var pr1List = rep.TryGetValue("PR1", out var pr1L) ? pr1L : new();
        if (ft1List.Count > 0 || pr1List.Count > 0)
        {
            sec = AddSection("Financial Charges");
            if (ft1List.Count > 0)
            {
                double total = 0;
                var codes2 = new List<string>();
                foreach (var ft in ft1List)
                {
                    if (ft.TryGetValue("transaction_amount_extended", out var amt) && amt != null)
                        if (double.TryParse(FirstComponent(amt), out var d2)) total += d2;
                    codes2.Add(CodedText(ft, "transaction_code"));
                }
                sec.AddRow($"Charges ({ft1List.Count})", string.Join(" | ", codes2.Where(s => s != "")));
                if (total > 0) sec.AddRow("Total Amount", $"${total:N2}", bold: true);
            }
            if (pr1List.Count > 0)
            {
                var procs = pr1List.Select(pr => CodedText(pr, "procedure_code")).Where(s => s != "");
                sec.AddRow($"Procedures ({pr1List.Count})", string.Join(" | ", procs));
            }
        }

        AddSegmentSections(msg);
    }

    private void AddSegmentSections(HL7Message msg)
    {
        foreach (var (segId, repIdx) in msg.SegmentOrder)
        {
            Dictionary<string, object?> seg;
            string label;

            if (repIdx is null)
            {
                if (!msg.Segments.TryGetValue(segId, out seg!)) continue;
                label = segId;
            }
            else
            {
                if (!msg.RepeatingSegments.TryGetValue(segId, out var insts) || repIdx >= insts.Count) continue;
                var count = insts.Count;
                label = count > 1 ? $"{segId} [{repIdx + 1} of {count}]" : segId;
                seg = insts[repIdx.Value];
            }

            var sec = new CollapsibleSection(label, startCollapsed: true);
            SummaryPanel.Children.Add(sec);

            int fieldNum = 0;
            foreach (var (field, value) in seg)
            {
                if (field == "segment_id") continue;
                fieldNum++;
                var rowLabel = $"{segId}-{fieldNum}  {field}";
                var subItems = BuildSubItems(value, $"{segId}-{fieldNum}");
                if (subItems != null)
                    sec.AddExpandableRow(rowLabel, ValueStr(value), subItems, keyWidth: 220);
                else
                    sec.AddRow(rowLabel, ValueStr(value), keyWidth: 220);
            }
        }
    }

    private static List<(string SubLabel, string SubValue)>? BuildSubItems(object? value, string fieldRef = "")
    {
        if (value is not List<object?> lst || lst.Count == 0) return null;
        bool hasSubLists = lst.Any(x => x is List<object?>);
        var items = new List<(string, string)>();
        if (hasSubLists)
        {
            for (int r = 0; r < lst.Count; r++)
            {
                if (lst[r] is List<object?> repComps)
                    for (int c = 0; c < repComps.Count; c++)
                        items.Add(($"{fieldRef}~{r + 1}.{c + 1}", repComps[c]?.ToString() ?? ""));
                else if (lst[r] != null)
                    items.Add(($"{fieldRef}~{r + 1}", lst[r]!.ToString()!));
            }
        }
        else
        {
            for (int c = 0; c < lst.Count; c++)
                items.Add(($"{fieldRef}.{c + 1}", lst[c]?.ToString() ?? ""));
        }
        return items.Count > 0 ? items : null;
    }

    private CollapsibleSection AddSection(string title)
    {
        var sec = new CollapsibleSection(title);
        SummaryPanel.Children.Add(sec);
        return sec;
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

    // ── Status bar ────────────────────────────────────────────────────────────

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

    private static string FieldStr(Dictionary<string, object?> seg, string key)
        => seg.TryGetValue(key, out var v) && v != null ? ValueStr(v) : "";

    private static string ValueStr(object? v) => v switch
    {
        null              => "",
        string s          => s,
        List<object?> lst => FlattenList(lst),
        _                 => v.ToString()!,
    };

    // Renders a parsed list without JSON. Top-level is components (^ separated);
    // if any element is itself a list the outer list is repetitions (~ separated).
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

    // Returns the first component of a composite field (or the value itself if it's a plain string).
    private static string FirstComponent(object? raw) => raw switch
    {
        null              => "",
        string s          => s,
        List<object?> lst => lst.Count > 0 ? (lst[0]?.ToString() ?? "") : "",
        _                 => raw.ToString()!,
    };

    private static string CodedText(Dictionary<string, object?> seg, string key)
    {
        if (!seg.TryGetValue(key, out var raw)) return "";
        // resolve repetitions: if the outer list contains sub-lists, take the first repetition
        if (raw is List<object?> outer && outer.Count > 0 && outer[0] is List<object?> firstRep)
            raw = firstRep;
        if (raw is List<object?> comps)
            return (comps.Count > 1 ? comps[1] : comps.Count > 0 ? comps[0] : null)?.ToString() ?? "";
        return raw?.ToString() ?? "";
    }

    // XAD: [0]=street(SAD), [1]=other designation, [2]=city, [3]=state, [4]=zip, [5]=country
    private static string FormatAddress(object? raw)
    {
        if (raw == null) return "";
        List<object?>? comps = null;
        if (raw is List<object?> outer)
            comps = outer.Count > 0 && outer[0] is List<object?> firstRep ? firstRep : outer;

        if (comps == null) return raw.ToString() ?? "";

        var streetRaw = comps.Count > 0 ? comps[0] : null;
        var street    = streetRaw is List<object?> sad ? sad[0]?.ToString() : streetRaw?.ToString();
        var other     = comps.Count > 1 ? comps[1]?.ToString() : null;
        var city      = comps.Count > 2 ? comps[2]?.ToString() : null;
        var state     = comps.Count > 3 ? comps[3]?.ToString() : null;
        var zip       = comps.Count > 4 ? comps[4]?.ToString() : null;
        var country   = comps.Count > 5 ? comps[5]?.ToString() : null;

        var line1        = Join(", ", street, other);
        var cityState    = Join(", ", city, state);
        var cityStateZip = Join(" ",  cityState, zip);
        return Join(", ", line1, cityStateZip, country);
    }

    // XTN: [0]=legacy number, [4]=country code, [5]=area code, [6]=local number, [7]=extension
    private static string FormatPhone(object? raw)
    {
        if (raw == null) return "";
        List<object?>? comps = null;
        if (raw is List<object?> outer)
            comps = outer.Count > 0 && outer[0] is List<object?> firstRep ? firstRep : outer;

        if (comps == null) return raw.ToString() ?? "";

        var legacy    = comps.Count > 0  ? comps[0]?.ToString()  : null;
        var areaCode  = comps.Count > 5  ? comps[5]?.ToString()  : null;
        var localNum  = comps.Count > 6  ? comps[6]?.ToString()  : null;
        var extension = comps.Count > 7  ? comps[7]?.ToString()  : null;

        if (!string.IsNullOrEmpty(areaCode) && !string.IsNullOrEmpty(localNum))
        {
            var num = $"({areaCode}) {localNum}";
            return string.IsNullOrEmpty(extension) ? num : $"{num} x{extension}";
        }
        return legacy ?? "";
    }

    private static string Join(string sep, params string?[] parts)
        => string.Join(sep, parts.Where(p => !string.IsNullOrEmpty(p)));

    private static string FormatName(Dictionary<string, string?> n)
    {
        var parts = new[] { n["prefix"], n["given"], n["middle"], n["family"], n["suffix"] };
        var result = string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
        return string.IsNullOrEmpty(result) ? "—" : result;
    }

    private static string FormatLocation(Dictionary<string, string?> loc)
    {
        var parts = new[] { loc["point_of_care"], loc["room"], loc["bed"] };
        var result = string.Join("/", parts.Where(p => !string.IsNullOrEmpty(p)));
        var facility = loc["facility"];
        if (!string.IsNullOrEmpty(facility))
            result = string.IsNullOrEmpty(result) ? facility : $"{result} ({facility})";
        return string.IsNullOrEmpty(result) ? "—" : result;
    }

    private static string FormatDoctor(Dictionary<string, string?> doc)
    {
        var name = string.Join(" ", new[] { doc["given"], doc["family"] }
            .Where(p => !string.IsNullOrEmpty(p)));
        var id = doc["id"];
        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name)) return $"{name} [{id}]";
        return string.IsNullOrEmpty(name) ? (id ?? "—") : name;
    }

    private static string NkName(Dictionary<string, object?> nk)
    {
        if (!nk.TryGetValue("name", out var raw)) return "";
        List<object?>? comps = null;
        if (raw is List<object?> outer)
            comps = outer.Count > 0 && outer[0] is List<object?> firstRep ? firstRep : outer;
        if (comps == null) return raw?.ToString() ?? "";
        var given  = comps.Count > 1 ? comps[1]?.ToString() : null;
        var family = comps.Count > 0 ? comps[0]?.ToString() : null;
        return Join(" ", given, family);
    }

    private static string AipPersonnel(Dictionary<string, object?> aip)
    {
        if (!aip.TryGetValue("personnel_resource_id", out var raw)) return "";
        List<object?>? comps = null;
        if (raw is List<object?> outer)
            comps = outer.Count > 0 && outer[0] is List<object?> firstRep ? firstRep : outer;
        if (comps == null) return raw?.ToString() ?? "";
        var given  = comps.Count > 2 ? comps[2]?.ToString() : null;
        var family = comps.Count > 1 ? comps[1]?.ToString() : null;
        var id     = comps.Count > 0 ? comps[0]?.ToString() : null;
        var name   = Join(" ", given, family);
        return string.IsNullOrEmpty(name) ? (id ?? "") : name;
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
