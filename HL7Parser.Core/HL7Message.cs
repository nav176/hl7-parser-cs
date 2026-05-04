namespace HL7Parser.Core;

public class HL7Message
{
    public string MessageType { get; set; } = "";
    public string? EventType { get; set; }
    public string? MessageControlId { get; set; }
    public string? Version { get; set; }

    /// <summary>Non-repeating segments, keyed by segment ID.</summary>
    public Dictionary<string, Dictionary<string, object?>> Segments { get; set; } = new();

    /// <summary>Repeating segments (NK1, AL1, OBX, …), each holding an ordered list of instances.</summary>
    public Dictionary<string, List<Dictionary<string, object?>>> RepeatingSegments { get; set; } = new();

    /// <summary>Original message segment order. RepeatIndex is null for non-repeating segments.</summary>
    public List<(string SegmentId, int? RepeatIndex)> SegmentOrder { get; set; } = new();
}
