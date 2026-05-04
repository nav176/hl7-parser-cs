using System.Text.Json;
using System.Text.Json.Serialization;
using HL7Parser.Core;

// ── Argument parsing ─────────────────────────────────────────────────────────
string? filePath   = null;
bool pretty        = false;
bool summary       = false;
string? segFilter  = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--pretty":   pretty    = true;  break;
        case "--summary":  summary   = true;  break;
        case "--segment":
            if (i + 1 < args.Length) segFilter = args[++i].ToUpperInvariant();
            break;
        default:
            if (!args[i].StartsWith('-')) filePath = args[i];
            break;
    }
}

// ── Read input ────────────────────────────────────────────────────────────────
string raw;
try
{
    if (filePath != null)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"hl7-parse: file not found: {filePath}");
            return 1;
        }
        raw = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
    }
    else
    {
        if (Console.IsInputRedirected == false)
            Console.Error.WriteLine("hl7-parse: reading from stdin (use Ctrl-Z / Ctrl-D to end input)");
        raw = Console.In.ReadToEnd();
    }
}
catch (IOException ex)
{
    Console.Error.WriteLine($"hl7-parse: I/O error: {ex.Message}");
    return 1;
}

// ── Parse ────────────────────────────────────────────────────────────────────
HL7Message parsed;
try
{
    parsed = Parser.ParseRaw(raw);
}
catch (HL7ParseError ex)
{
    Console.Error.WriteLine($"hl7-parse: parse error: {ex.Message}");
    return 1;
}

// ── Output ───────────────────────────────────────────────────────────────────
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented        = pretty,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
};

if (summary)
{
    PrintSummary(parsed);
    return 0;
}

if (segFilter != null)
{
    object? segData =
        parsed.Segments.TryGetValue(segFilter, out var seg) ? seg :
        parsed.RepeatingSegments.TryGetValue(segFilter, out var repSegs) ? repSegs :
        null;

    if (segData == null)
    {
        Console.Error.WriteLine($"hl7-parse: segment '{segFilter}' not found in message");
        return 1;
    }
    Console.WriteLine(JsonSerializer.Serialize(segData, jsonOptions));
    return 0;
}

Console.WriteLine(JsonSerializer.Serialize(parsed, jsonOptions));
return 0;

// ── Summary helper ────────────────────────────────────────────────────────────
static void PrintSummary(HL7Message msg)
{
    var segs = msg.Segments;
    var rep  = msg.RepeatingSegments;

    var name    = Extractors.GetPatientName(msg);
    var fullName = string.Join(" ", new[]
    {
        name["prefix"], name["given"], name["middle"], name["family"], name["suffix"]
    }.Where(s => !string.IsNullOrEmpty(s)));

    var loc    = Extractors.GetPatientLocation(msg);
    var locStr = string.Join("/", new[]
    {
        loc["point_of_care"], loc["room"], loc["bed"]
    }.Where(s => !string.IsNullOrEmpty(s)));

    var doc    = Extractors.GetAttendingDoctor(msg);
    var docStr = string.Join(" ", new[] { doc["given"], doc["family"] }
        .Where(s => !string.IsNullOrEmpty(s)));

    segs.TryGetValue("PID", out var pid);
    segs.TryGetValue("PV1", out var pv1);

    var lines = new[]
    {
        $"Message Type    : {msg.MessageType}",
        $"Event Type      : {Extractors.GetEventType(msg) ?? "Unknown"}",
        $"Control ID      : {msg.MessageControlId ?? "Unknown"}",
        $"HL7 Version     : {msg.Version ?? "Unknown"}",
        $"Patient ID      : {Extractors.GetPatientId(msg) ?? "Unknown"}",
        $"Patient Name    : {(string.IsNullOrEmpty(fullName) ? "Unknown" : fullName)}",
        $"DOB             : {GetField(pid, "datetime_of_birth")}",
        $"Sex             : {GetField(pid, "administrative_sex")}",
        $"Patient Class   : {GetField(pv1, "patient_class")}",
        $"Location        : {(string.IsNullOrEmpty(locStr) ? "Unknown" : locStr)}",
        $"Attending MD    : {(string.IsNullOrEmpty(docStr) ? "Unknown" : docStr)}",
        $"Admit DateTime  : {GetField(pv1, "admit_datetime")}",
        $"Discharge DT    : {GetField(pv1, "discharge_datetime")}",
    };

    foreach (var line in lines) Console.WriteLine(line);

    if (rep.TryGetValue("AL1", out var al1) && al1.Count > 0)
        Console.WriteLine($"Allergies       : {al1.Count} recorded");
    if (rep.TryGetValue("DG1", out var dg1) && dg1.Count > 0)
        Console.WriteLine($"Diagnoses       : {dg1.Count} recorded");
}

static string GetField(Dictionary<string, object?>? seg, string key)
    => seg?.TryGetValue(key, out var v) == true && v != null ? v.ToString()! : "Unknown";
