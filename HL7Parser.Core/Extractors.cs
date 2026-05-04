namespace HL7Parser.Core;

public static class Extractors
{
    private static readonly Dictionary<string, string?> EmptyName = new()
    {
        ["family"] = null, ["given"] = null, ["middle"] = null,
        ["suffix"] = null, ["prefix"] = null,
    };

    private static readonly Dictionary<string, string?> EmptyLocation = new()
    {
        ["point_of_care"] = null, ["room"] = null, ["bed"] = null, ["facility"] = null,
    };

    private static readonly Dictionary<string, string?> EmptyDoctor = new()
    {
        ["id"] = null, ["family"] = null, ["given"] = null,
    };

    /// <summary>Return the primary patient MRN from PID-3.</summary>
    public static string? GetPatientId(HL7Message msg)
    {
        if (!msg.Segments.TryGetValue("PID", out var pid)) return null;
        if (!pid.TryGetValue("patient_identifier_list", out var raw)) return null;

        if (raw is List<object?> list)
        {
            var first = list.Count > 0 ? list[0] : null;
            if (first is List<object?> cx) return cx.Count > 0 ? cx[0]?.ToString() : null;
            return first?.ToString();
        }
        return raw?.ToString();
    }

    /// <summary>Return patient name components from PID-5 (XPN).</summary>
    public static Dictionary<string, string?> GetPatientName(HL7Message msg)
    {
        if (!msg.Segments.TryGetValue("PID", out var pid)) return EmptyName;
        if (!pid.TryGetValue("patient_name", out var nameObj)) return EmptyName;

        List<object?>? name = null;

        if (nameObj is List<object?> outer && outer.Count > 0 && outer[0] is List<object?> inner)
            name = inner;                         // multiple repetitions – take first
        else if (nameObj is List<object?> single)
            name = single;                        // single repetition split into components

        if (name != null)
        {
            return new Dictionary<string, string?>
            {
                ["family"] = name.Count > 0 ? name[0]?.ToString() : null,
                ["given"]  = name.Count > 1 ? name[1]?.ToString() : null,
                ["middle"] = name.Count > 2 ? name[2]?.ToString() : null,
                ["suffix"] = name.Count > 3 ? name[3]?.ToString() : null,
                ["prefix"] = name.Count > 4 ? name[4]?.ToString() : null,
            };
        }
        return new Dictionary<string, string?>
        {
            ["family"] = nameObj?.ToString(), ["given"] = null,
            ["middle"] = null, ["suffix"] = null, ["prefix"] = null,
        };
    }

    /// <summary>Return the event type code from EVN-1 or MSH-9.2.</summary>
    public static string? GetEventType(HL7Message msg)
    {
        if (msg.Segments.TryGetValue("EVN", out var evn) &&
            evn.TryGetValue("event_type_code", out var code) && code != null)
            return code.ToString();
        return msg.EventType;
    }

    /// <summary>Return the assigned patient location from PV1-3.</summary>
    public static Dictionary<string, string?> GetPatientLocation(HL7Message msg)
    {
        if (!msg.Segments.TryGetValue("PV1", out var pv1)) return EmptyLocation;
        if (!pv1.TryGetValue("assigned_patient_location", out var raw)) return EmptyLocation;

        if (raw is List<object?> loc)
        {
            return new Dictionary<string, string?>
            {
                ["point_of_care"] = loc.Count > 0 ? loc[0]?.ToString() : null,
                ["room"]          = loc.Count > 1 ? loc[1]?.ToString() : null,
                ["bed"]           = loc.Count > 2 ? loc[2]?.ToString() : null,
                ["facility"]      = loc.Count > 3 ? loc[3]?.ToString() : null,
            };
        }
        return new Dictionary<string, string?>
        {
            ["point_of_care"] = raw?.ToString(), ["room"] = null,
            ["bed"] = null, ["facility"] = null,
        };
    }

    /// <summary>Return the attending doctor from PV1-7 (XCN).</summary>
    public static Dictionary<string, string?> GetAttendingDoctor(HL7Message msg)
        => GetXcnFromPv1(msg, "attending_doctor");

    /// <summary>Return the referring doctor from PV1-8 (XCN).</summary>
    public static Dictionary<string, string?> GetReferringDoctor(HL7Message msg)
        => GetXcnFromPv1(msg, "referring_doctor");

    private static Dictionary<string, string?> GetXcnFromPv1(HL7Message msg, string field)
    {
        if (!msg.Segments.TryGetValue("PV1", out var pv1)) return EmptyDoctor;
        if (!pv1.TryGetValue(field, out var raw)) return EmptyDoctor;

        List<object?>? doc = null;
        if (raw is List<object?> outer && outer.Count > 0 && outer[0] is List<object?> inner)
            doc = inner;
        else if (raw is List<object?> single)
            doc = single;

        if (doc != null)
        {
            return new Dictionary<string, string?>
            {
                ["id"]     = doc.Count > 0 ? doc[0]?.ToString() : null,
                ["family"] = doc.Count > 1 ? doc[1]?.ToString() : null,
                ["given"]  = doc.Count > 2 ? doc[2]?.ToString() : null,
            };
        }
        return new Dictionary<string, string?>
        {
            ["id"] = raw?.ToString(), ["family"] = null, ["given"] = null,
        };
    }
}
