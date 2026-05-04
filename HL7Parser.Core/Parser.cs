namespace HL7Parser.Core;

public static class Parser
{
    private static readonly HashSet<string> Repeating = new(StringComparer.Ordinal)
    {
        "NK1", "AL1", "DG1", "OBX", "IN1", "IN2", "IN3", "GT1",
        "ROL", "DB1", "IAM",
        "PRD", "CTD", "CON", "VAR",
        "AIS", "AIP", "AIL", "AIG",
        "FT1", "PR1", "GP2",
        "ORC", "OBR", "NTE",
        "RXO", "RXE", "RXA", "RXR", "RXD", "RXG", "TQ1", "TQ2",
        "SPM", "SAC",
        "LOC", "LDP", "LCH", "LCC",
        "ERR", "SFT", "PRT",
    };

    /// <summary>
    /// Parse a single field value. Returns null, string, List&lt;object?&gt; (components),
    /// or List&lt;object?&gt; of (string|List&lt;object?&gt;) (repetitions).
    /// </summary>
    public static object? ParseFieldValue(string raw, Delimiters d)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        var reps = raw.Split(d.Repetition);
        var parsedReps = new List<object?>(reps.Length);

        foreach (var rep in reps)
        {
            var comps = rep.Split(d.Component);
            if (comps.Length == 1)
                parsedReps.Add(comps[0].Length > 0 ? comps[0] : null);
            else
                parsedReps.Add(comps.Select(c => c.Length > 0 ? (object?)c : null).ToList());
        }

        return parsedReps.Count == 1 ? parsedReps[0] : parsedReps;
    }

    /// <summary>Parse one segment line into a named-field dictionary.</summary>
    public static Dictionary<string, object?> ParseSegment(string line, Delimiters d)
    {
        if (line.Length < 3)
            return new Dictionary<string, object?> { ["segment_id"] = line };

        var segId = line[..3];
        SegmentFields.Fields.TryGetValue(segId, out var defs);
        var result = new Dictionary<string, object?>();

        if (segId == "MSH")
        {
            // line[3..] = "|^~\&|field3|field4|..."
            // split by field sep → ["", "^~\&", field3, field4, ...]
            var rawFields = line[3..].Split(d.Field);
            result["segment_id"]    = "MSH";
            result["field_separator"] = d.Field.ToString();
            result["encoding_chars"]  = rawFields.Length > 1 ? rawFields[1] : "";

            // rawFields[2] → MSH-3 → defs index 3
            for (int i = 2; i < rawFields.Length; i++)
            {
                int idx  = i + 1;
                var name = defs != null && idx < defs.Length ? defs[idx] : $"field_{idx}";
                result[name] = ParseFieldValue(rawFields[i], d);
            }
        }
        else
        {
            var rawFields = line.Split(d.Field);
            for (int i = 0; i < rawFields.Length; i++)
            {
                var name = defs != null && i < defs.Length ? defs[i] : $"field_{i}";
                result[name] = i == 0 ? (object?)rawFields[i] : ParseFieldValue(rawFields[i], d);
            }
        }

        return result;
    }

    /// <summary>Parse a raw HL7 message string into a structured <see cref="HL7Message"/>.</summary>
    public static HL7Message ParseRaw(string text)
    {
        // Strip UTF-8/UTF-16 BOM (U+FEFF) if present
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];

        // Normalise line endings – HL7 spec uses CR, accept LF and CRLF too
        text = text.Replace("\r\n", "\r").Replace('\n', '\r').Trim('\r');

        var lines = text.Split('\r')
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToArray();

        if (lines.Length == 0)
            throw new HL7ParseError("Empty message");
        if (!lines[0].StartsWith("MSH", StringComparison.Ordinal))
            throw new HL7ParseError("Message must start with MSH segment");

        var delimiters = Delimiters.FromMsh(lines[0]);

        var segments    = new Dictionary<string, Dictionary<string, object?>>();
        var repeating   = new Dictionary<string, List<Dictionary<string, object?>>>();
        var segmentOrder = new List<(string, int?)>();

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;
            var segId  = line.Length >= 3 ? line[..3] : line;
            var parsed = ParseSegment(line, delimiters);

            if (Repeating.Contains(segId))
            {
                if (!repeating.TryGetValue(segId, out var instances))
                {
                    instances = new List<Dictionary<string, object?>>();
                    repeating[segId] = instances;
                }
                segmentOrder.Add((segId, instances.Count));
                instances.Add(parsed);
            }
            else
            {
                segments[segId] = parsed;
                segmentOrder.Add((segId, null));
            }
        }

        var msh = segments.TryGetValue("MSH", out var m) ? m : new();
        msh.TryGetValue("message_type", out var rawMsgType);

        string messageType;
        string? eventType;

        if (rawMsgType is List<object?> comps)
        {
            messageType = string.Join("^", comps.Select(c => c?.ToString() ?? ""));
            eventType   = comps.Count > 1 ? comps[1]?.ToString() : null;
        }
        else
        {
            messageType = rawMsgType?.ToString() ?? "";
            eventType   = null;
        }

        msh.TryGetValue("message_control_id", out var cid);
        msh.TryGetValue("version_id",          out var ver);

        return new HL7Message
        {
            MessageType      = messageType,
            EventType        = eventType,
            MessageControlId = cid?.ToString(),
            Version          = ver?.ToString(),
            Segments         = segments,
            RepeatingSegments = repeating,
            SegmentOrder     = segmentOrder,
        };
    }
}
