namespace HL7Parser.Core;

/// <summary>Component names for composite HL7 fields, keyed by (segment ID, 1-based field number).</summary>
public static class ComponentFields
{
    private static readonly string[] Xpn = ["family_name", "given_name", "second_name", "suffix", "prefix", "degree", "name_type_code", "name_representation_code"];
    private static readonly string[] Xcn = ["id", "family_name", "given_name", "second_name", "suffix", "prefix", "degree", "source_table", "assigning_authority", "name_type_code", "identifier_check_digit", "check_digit_scheme", "identifier_type_code", "assigning_facility"];
    private static readonly string[] Cx  = ["id", "check_digit", "check_digit_scheme", "assigning_authority", "identifier_type_code", "assigning_facility", "effective_date", "expiration_date"];
    private static readonly string[] Xad = ["street_address", "other_designation", "city", "state", "zip", "country", "address_type", "other_geographic_designation"];
    private static readonly string[] Xtn = ["telephone_number", "use_code", "equipment_type", "email_address", "country_code", "area_code", "local_number", "extension"];
    private static readonly string[] Pl  = ["point_of_care", "room", "bed", "facility", "location_status", "person_location_type", "building", "floor"];
    private static readonly string[] Cwe = ["identifier", "text", "coding_system", "alt_identifier", "alt_text", "alt_coding_system"];
    private static readonly string[] Xon = ["organization_name", "name_type_code", "id_number", "check_digit", "check_digit_scheme", "assigning_authority", "identifier_type_code", "assigning_facility"];
    private static readonly string[] Ei  = ["entity_identifier", "namespace_id", "universal_id", "universal_id_type"];
    private static readonly string[] Hd  = ["namespace_id", "universal_id", "universal_id_type"];
    private static readonly string[] Msg = ["message_code", "trigger_event", "message_structure"];

    private static readonly Dictionary<(string, int), string[]> Map = new()
    {
        // MSH
        [("MSH", 3)] = Hd,  [("MSH", 4)] = Hd,  [("MSH", 5)] = Hd,  [("MSH", 6)] = Hd,
        [("MSH", 9)] = Msg,

        // EVN
        [("EVN", 5)] = Cwe, [("EVN", 6)] = Xcn,

        // PID
        [("PID",  3)] = Cx,  [("PID",  4)] = Cx,  [("PID",  5)] = Xpn, [("PID",  6)] = Xpn,
        [("PID",  9)] = Xpn, [("PID", 10)] = Cwe, [("PID", 11)] = Xad, [("PID", 13)] = Xtn,
        [("PID", 14)] = Xtn, [("PID", 15)] = Cwe, [("PID", 16)] = Cwe, [("PID", 17)] = Cwe,
        [("PID", 18)] = Cx,

        // PD1
        [("PD1", 3)] = Xon, [("PD1", 4)] = Xcn,

        // PV1
        [("PV1",  3)] = Pl,  [("PV1",  5)] = Cx,  [("PV1",  6)] = Pl,  [("PV1",  7)] = Xcn,
        [("PV1",  8)] = Xcn, [("PV1",  9)] = Xcn, [("PV1", 10)] = Cwe, [("PV1", 11)] = Pl,
        [("PV1", 14)] = Cwe, [("PV1", 17)] = Xcn, [("PV1", 19)] = Cx,  [("PV1", 36)] = Cwe,
        [("PV1", 37)] = Pl,  [("PV1", 42)] = Pl,

        // NK1
        [("NK1",  2)] = Xpn, [("NK1",  3)] = Cwe, [("NK1",  4)] = Xad, [("NK1",  5)] = Xtn,
        [("NK1",  6)] = Xtn, [("NK1",  7)] = Cwe, [("NK1", 12)] = Cx,  [("NK1", 13)] = Xon,
        [("NK1", 30)] = Xpn, [("NK1", 31)] = Xtn, [("NK1", 32)] = Xad, [("NK1", 33)] = Cx,

        // AL1 / IAM
        [("AL1", 3)] = Cwe, [("AL1", 4)] = Cwe,
        [("IAM", 3)] = Cwe, [("IAM", 4)] = Cwe,

        // DG1
        [("DG1",  3)] = Cwe, [("DG1",  7)] = Cwe, [("DG1", 16)] = Xcn,

        // OBX
        [("OBX",  3)] = Cwe, [("OBX",  6)] = Cwe, [("OBX", 15)] = Xcn,
        [("OBX", 16)] = Xcn, [("OBX", 17)] = Cwe,

        // IN1
        [("IN1",  2)] = Cwe, [("IN1",  3)] = Cx,  [("IN1",  4)] = Xon, [("IN1",  5)] = Xad,
        [("IN1",  6)] = Xpn, [("IN1",  7)] = Xtn, [("IN1", 10)] = Cx,  [("IN1", 11)] = Xon,
        [("IN1", 16)] = Xpn, [("IN1", 17)] = Cwe, [("IN1", 19)] = Xad, [("IN1", 30)] = Xcn,
        [("IN1", 49)] = Cx,

        // ORC
        [("ORC",  2)] = Ei,  [("ORC",  3)] = Ei,  [("ORC",  4)] = Ei,  [("ORC", 10)] = Xcn,
        [("ORC", 11)] = Xcn, [("ORC", 12)] = Xcn, [("ORC", 13)] = Pl,  [("ORC", 14)] = Xtn,
        [("ORC", 16)] = Cwe, [("ORC", 17)] = Xon, [("ORC", 19)] = Xcn, [("ORC", 20)] = Cwe,
        [("ORC", 21)] = Xon, [("ORC", 22)] = Xad, [("ORC", 23)] = Xtn, [("ORC", 24)] = Xad,

        // OBR
        [("OBR",  2)] = Ei,  [("OBR",  3)] = Ei,  [("OBR",  4)] = Cwe, [("OBR", 10)] = Xcn,
        [("OBR", 16)] = Xcn, [("OBR", 17)] = Xtn, [("OBR", 28)] = Xcn, [("OBR", 29)] = Ei,
        [("OBR", 32)] = Xcn, [("OBR", 44)] = Cwe,

        // RXO, RXE, RXA, RXR
        [("RXO",  1)] = Cwe, [("RXO",  4)] = Cwe, [("RXO",  5)] = Cwe, [("RXO", 10)] = Cwe,
        [("RXO", 12)] = Cwe,
        [("RXE",  2)] = Cwe, [("RXE",  5)] = Cwe, [("RXE",  6)] = Cwe,
        [("RXA",  5)] = Cwe, [("RXA",  7)] = Cwe, [("RXA",  8)] = Cwe, [("RXA", 10)] = Xcn,
        [("RXA", 11)] = Pl,  [("RXA", 14)] = Cwe, [("RXA", 18)] = Cwe,
        [("RXR",  1)] = Cwe, [("RXR",  2)] = Cwe, [("RXR",  3)] = Cwe, [("RXR",  4)] = Cwe,

        // SCH
        [("SCH",  1)] = Ei,  [("SCH",  2)] = Ei,  [("SCH",  7)] = Cwe, [("SCH",  8)] = Cwe,
        [("SCH", 10)] = Cwe, [("SCH", 12)] = Xcn, [("SCH", 13)] = Xtn, [("SCH", 14)] = Xad,
        [("SCH", 16)] = Xcn, [("SCH", 17)] = Xtn, [("SCH", 18)] = Xad, [("SCH", 20)] = Xcn,
        [("SCH", 21)] = Xtn, [("SCH", 23)] = Ei,  [("SCH", 24)] = Ei,  [("SCH", 26)] = Ei,
        [("SCH", 27)] = Ei,

        // AIS, AIP
        [("AIS", 3)] = Cwe, [("AIP", 3)] = Xcn,

        // FT1
        [("FT1",  6)] = Cwe, [("FT1",  7)] = Cwe, [("FT1", 13)] = Cwe, [("FT1", 14)] = Cwe,
        [("FT1", 16)] = Pl,  [("FT1", 19)] = Cwe, [("FT1", 20)] = Xcn, [("FT1", 23)] = Ei,
        [("FT1", 24)] = Xcn, [("FT1", 25)] = Cwe,

        // PR1
        [("PR1",  3)] = Cwe, [("PR1",  8)] = Xcn, [("PR1", 11)] = Xcn, [("PR1", 12)] = Xcn,
        [("PR1", 13)] = Cwe, [("PR1", 15)] = Cwe,

        // SPM
        [("SPM",  4)] = Cwe, [("SPM",  7)] = Cwe, [("SPM",  8)] = Cwe,

        // MSA / ERR
        [("MSA", 6)] = Cwe, [("ERR", 3)] = Cwe, [("ERR", 5)] = Cwe,

        // GT1
        [("GT1",  2)] = Cx,  [("GT1",  3)] = Xpn, [("GT1",  4)] = Xpn, [("GT1",  5)] = Xad,
        [("GT1",  6)] = Xtn, [("GT1",  7)] = Xtn, [("GT1", 16)] = Xpn, [("GT1", 17)] = Xad,
        [("GT1", 18)] = Xtn, [("GT1", 21)] = Xon,
    };

    /// <summary>Returns the component name for the given (segId, 1-based fieldNum, 1-based compNum), or null if unknown.</summary>
    public static string? Get(string segId, int fieldNum, int compNum)
    {
        if (!Map.TryGetValue((segId, fieldNum), out var names)) return null;
        var idx = compNum - 1;
        return idx >= 0 && idx < names.Length ? names[idx] : null;
    }
}
