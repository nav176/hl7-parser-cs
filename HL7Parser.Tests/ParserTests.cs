using HL7Parser.Core;

namespace HL7Parser.Tests;

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class Fixture
{
    public static string Load(string name)
    {
        var path = Path.Combine("fixtures", name);
        return File.ReadAllText(path, System.Text.Encoding.UTF8);
    }

    public static HL7Message Parse(string name) => Parser.ParseRaw(Load(name));
}

// ── Delimiters ────────────────────────────────────────────────────────────────

public class TestDelimiters
{
    private const string Msh = "MSH|^~\\&|SEND|FAC|RECV|FAC2|20240101120000||ADT^A01|MSG001|P|2.5";

    [Fact] public void DefaultFieldSep()     => Assert.Equal('|',  Delimiters.FromMsh(Msh).Field);
    [Fact] public void DefaultComponent()    => Assert.Equal('^',  Delimiters.FromMsh(Msh).Component);
    [Fact] public void DefaultRepetition()   => Assert.Equal('~',  Delimiters.FromMsh(Msh).Repetition);
    [Fact] public void DefaultEscape()       => Assert.Equal('\\', Delimiters.FromMsh(Msh).Escape);
    [Fact] public void DefaultSubcomponent() => Assert.Equal('&',  Delimiters.FromMsh(Msh).Subcomponent);

    [Fact]
    public void ShortMshThrows()
        => Assert.Throws<HL7ParseError>(() => Delimiters.FromMsh("MSH|^~"));

    [Fact]
    public void NonMshThrows()
        => Assert.Throws<HL7ParseError>(() => Delimiters.FromMsh("PID|^~\\&|foo"));
}

// ── ParseRaw ──────────────────────────────────────────────────────────────────

public class TestParseRaw
{
    private const string SimpleA01 =
        "MSH|^~\\&|SEND|FAC|RECV|FAC2|20240101120000||ADT^A01|MSG001|P|2.5\r" +
        "EVN|A01|20240101120000\r" +
        "PID|1||MRN123456|||DOE^JOHN^MICHAEL|||||||||||||\r" +
        "PV1|1|I|3 WEST^301^A^HOSPITAL||||||||||||||||\r" +
        "AL1|1|DA|PENICILLIN^Penicillin^L|SV|HIVES/ANAPHYLAXIS";

    [Fact] public void MessageType()  => Assert.Equal("ADT^A01", Parser.ParseRaw(SimpleA01).MessageType);
    [Fact] public void EventType()    => Assert.Equal("A01",     Parser.ParseRaw(SimpleA01).EventType);
    [Fact] public void ControlId()    => Assert.Equal("MSG001",  Parser.ParseRaw(SimpleA01).MessageControlId);
    [Fact] public void Version()      => Assert.Equal("2.5",     Parser.ParseRaw(SimpleA01).Version);

    [Fact]
    public void SegmentsPresent()
    {
        var msg = Parser.ParseRaw(SimpleA01);
        Assert.True(msg.Segments.ContainsKey("MSH"));
        Assert.True(msg.Segments.ContainsKey("EVN"));
        Assert.True(msg.Segments.ContainsKey("PID"));
        Assert.True(msg.Segments.ContainsKey("PV1"));
    }

    [Fact]
    public void Al1InRepeating()
    {
        var msg = Parser.ParseRaw(SimpleA01);
        Assert.True(msg.RepeatingSegments.ContainsKey("AL1"));
        Assert.Single(msg.RepeatingSegments["AL1"]);
    }

    [Fact]
    public void LfLineEndings()
    {
        var lf = SimpleA01.Replace("\r", "\n");
        Assert.Equal("ADT^A01", Parser.ParseRaw(lf).MessageType);
    }

    [Fact]
    public void CrlfLineEndings()
    {
        var crlf = SimpleA01.Replace("\r", "\r\n");
        Assert.Equal("ADT^A01", Parser.ParseRaw(crlf).MessageType);
    }

    [Fact]
    public void EmptyMessageThrows()
        => Assert.Throws<HL7ParseError>(() => Parser.ParseRaw(""));

    [Fact]
    public void MissingMshThrows()
        => Assert.Throws<HL7ParseError>(() => Parser.ParseRaw("PID|1||MRN\r"));

    [Fact]
    public void BomIsStripped()
        => Assert.Equal("ADT^A01", Parser.ParseRaw('\uFEFF' + SimpleA01).MessageType);
}

// ── Segment field names ───────────────────────────────────────────────────────

public class TestSegmentFields
{
    private static readonly HL7Message Msg = Parser.ParseRaw(
        "MSH|^~\\&|SEND|FAC|RECV|FAC2|20240101||ADT^A01|MSG001|P|2.5\r" +
        "PID|1||MRN999|||DOE^JANE");

    [Fact]
    public void MshFieldSeparator()
        => Assert.Equal("|", Msg.Segments["MSH"]["field_separator"]?.ToString());

    [Fact]
    public void MshSendingApplication()
        => Assert.Equal("SEND", Msg.Segments["MSH"]["sending_application"]?.ToString());

    [Fact]
    public void PidSegmentId()
        => Assert.Equal("PID", Msg.Segments["PID"]["segment_id"]?.ToString());

    [Fact]
    public void UnknownSegmentUsesGenericKeys()
    {
        var msg = Parser.ParseRaw(
            "MSH|^~\\&|A|B|C|D|20240101||ADT^A01|X|P|2.5\r" +
            "ZZZ|custom_value");
        Assert.True(msg.Segments.ContainsKey("ZZZ"));
        Assert.True(msg.Segments["ZZZ"].ContainsKey("field_1"));
    }
}

// ── Component / repetition parsing ───────────────────────────────────────────

public class TestComponentParsing
{
    private static readonly HL7Message A01 = Fixture.Parse("a01_admit.hl7");

    [Fact]
    public void PatientNameComponents()
    {
        var name = A01.Segments["PID"]["patient_name"];
        Assert.IsType<List<object?>>(name);
        var comps = (List<object?>)name!;
        Assert.Equal("DOE",    comps[0]?.ToString());
        Assert.Equal("JOHN",   comps[1]?.ToString());
        Assert.Equal("MICHAEL",comps[2]?.ToString());
    }

    [Fact]
    public void Pv1LocationComponents()
    {
        var loc = A01.Segments["PV1"]["assigned_patient_location"];
        var comps = Assert.IsType<List<object?>>(loc);
        // fixture: 3 WEST^301^A^GENERAL_HOSPITAL
        Assert.Equal("3 WEST", comps[0]?.ToString());
        Assert.Equal("301",    comps[1]?.ToString());
        Assert.Equal("A",      comps[2]?.ToString());
    }

    [Fact]
    public void EmptyFieldIsNull()
    {
        var d = new Delimiters();
        Assert.Null(Parser.ParseFieldValue("", d));
    }
}

// ── Extractor functions ───────────────────────────────────────────────────────

public class TestExtractors
{
    // Mirrors the Python test A01_TEXT constant exactly (note: PID-5 needs only one empty field before it)
    private const string A01Text =
        "MSH|^~\\&|SEND|FAC|RECV|RFAC|20240101120000||ADT^A01|CTL001|P|2.5\r" +
        "EVN|A01|20240101120000\r" +
        "PID|1||MRN001^^^FAC^MR||SMITH^JANE^M||19900601|F\r" +
        "PV1|1|I|ICU^101^A^FAC||E||DOC001^JONES^BOB^DR^MD";

    private static readonly HL7Message A01 = Parser.ParseRaw(A01Text);

    [Fact] public void GetPatientId()
        => Assert.Equal("MRN001", Extractors.GetPatientId(A01));

    [Fact] public void GetPatientName()
    {
        var n = Extractors.GetPatientName(A01);
        Assert.Equal("SMITH", n["family"]);
        Assert.Equal("JANE",  n["given"]);
        Assert.Equal("M",     n["middle"]);
    }

    [Fact] public void GetEventType()
        => Assert.Equal("A01", Extractors.GetEventType(A01));

    [Fact] public void GetPatientLocation()
    {
        var loc = Extractors.GetPatientLocation(A01);
        Assert.Equal("ICU", loc["point_of_care"]);
        Assert.Equal("101", loc["room"]);
        Assert.Equal("A",   loc["bed"]);
    }

    [Fact] public void GetAttendingDoctor()
    {
        var doc = Extractors.GetAttendingDoctor(A01);
        Assert.Equal("DOC001", doc["id"]);
        Assert.Equal("JONES",  doc["family"]);
        Assert.Equal("BOB",    doc["given"]);
    }
}

// ── File-based parsing ────────────────────────────────────────────────────────

public class TestParseFile
{
    [Fact] public void A01File() => Assert.Equal("ADT^A01", Fixture.Parse("a01_admit.hl7").MessageType);
    [Fact] public void A03File() => Assert.Equal("ADT^A03", Fixture.Parse("a03_discharge.hl7").MessageType);

    [Fact] public void A01Allergies()
    {
        var msg = Fixture.Parse("a01_admit.hl7");
        Assert.True(msg.RepeatingSegments.TryGetValue("AL1", out var al1));
        Assert.Equal(2, al1!.Count);
    }

    [Fact] public void A01Diagnoses()
    {
        var msg = Fixture.Parse("a01_admit.hl7");
        Assert.True(msg.RepeatingSegments.TryGetValue("DG1", out var dg1));
        Assert.Equal(2, dg1!.Count);
    }

    [Fact] public void A01Observations()
    {
        var msg = Fixture.Parse("a01_admit.hl7");
        Assert.True(msg.RepeatingSegments.TryGetValue("OBX", out var obx));
        Assert.Equal(2, obx!.Count);
    }

    [Fact] public void FileNotFoundThrows()
        => Assert.Throws<FileNotFoundException>(() =>
               Parser.ParseRaw(File.ReadAllText("fixtures/nonexistent.hl7")));
}

// ── A08 Update ────────────────────────────────────────────────────────────────

public class TestA08Update
{
    private static readonly HL7Message Msg = Fixture.Parse("a08_update.hl7");

    [Fact] public void MessageType() => Assert.Equal("ADT^A08", Msg.MessageType);
    [Fact] public void EventType()   => Assert.Equal("A08",     Extractors.GetEventType(Msg));
    [Fact] public void PatientId()   => Assert.NotNull(Extractors.GetPatientId(Msg));

    [Fact] public void InsuranceSegmentsPresent()
        => Assert.True(Msg.RepeatingSegments.ContainsKey("IN1"));

    [Fact] public void In1NotInSingleSegments()
        => Assert.False(Msg.Segments.ContainsKey("IN1"));
}

// ── A16 Pending Admit ─────────────────────────────────────────────────────────

public class TestA16PendingAdmit
{
    private static readonly HL7Message Msg = Fixture.Parse("a16_pending_admit.hl7");

    [Fact] public void MessageType() => Assert.Equal("ADT^A16", Msg.MessageType);
    [Fact] public void PatientId()   => Assert.NotNull(Extractors.GetPatientId(Msg));
}

// ── A28 Add Person ────────────────────────────────────────────────────────────

public class TestA28AddPerson
{
    private static readonly HL7Message Msg = Fixture.Parse("adt_a28_add_person.hl7");

    [Fact] public void MessageType() => Assert.Equal("ADT^A28", Msg.MessageType);
    [Fact] public void PatientId()   => Assert.NotNull(Extractors.GetPatientId(Msg));
}

// ── SIU Appointment ───────────────────────────────────────────────────────────

public class TestSIUAppointment
{
    private static readonly HL7Message Msg = Fixture.Parse("siu_s12_appointment.hl7");

    [Fact] public void MessageType()   => Assert.Equal("SIU^S12", Msg.MessageType);
    [Fact] public void SchPresent()    => Assert.True(Msg.Segments.ContainsKey("SCH"));
    [Fact] public void AisPresent()    => Assert.True(Msg.RepeatingSegments.ContainsKey("AIS"));
    [Fact] public void AipPresent()    => Assert.True(Msg.RepeatingSegments.ContainsKey("AIP"));
}

// ── DFT Financial ─────────────────────────────────────────────────────────────

public class TestDFTFinancial
{
    private static readonly HL7Message Msg = Fixture.Parse("dft_p03_charges.hl7");

    [Fact] public void MessageType() => Assert.Equal("DFT^P03", Msg.MessageType);
    [Fact] public void Ft1Present()  => Assert.True(Msg.RepeatingSegments.ContainsKey("FT1"));
    [Fact] public void Pr1Present()  => Assert.True(Msg.RepeatingSegments.ContainsKey("PR1"));
}

// ── ORM Order ────────────────────────────────────────────────────────────────

public class TestORMOrder
{
    private static readonly HL7Message Msg = Fixture.Parse("orm_o01_order.hl7");

    [Fact] public void MessageType() => Assert.Equal("ORM^O01", Msg.MessageType);
    [Fact] public void OrcPresent()  => Assert.True(Msg.RepeatingSegments.ContainsKey("ORC"));
    [Fact] public void ObrPresent()  => Assert.True(Msg.RepeatingSegments.ContainsKey("OBR"));
}

// ── ORP Order Response ────────────────────────────────────────────────────────

public class TestORPResponse
{
    private static readonly HL7Message Msg = Fixture.Parse("orp_o10_response.hl7");

    [Fact] public void MessageType() => Assert.Equal("ORP^O10", Msg.MessageType);
    [Fact] public void MsaPresent()  => Assert.True(Msg.Segments.ContainsKey("MSA"));
}

// ── RDE Pharmacy Order ────────────────────────────────────────────────────────

public class TestRDEPharmacyOrder
{
    private static readonly HL7Message Msg = Fixture.Parse("rde_o11_rx_order.hl7");

    [Fact] public void MessageType() => Assert.Equal("RDE^O11", Msg.MessageType);
    [Fact] public void RxoPresent()  => Assert.True(Msg.RepeatingSegments.ContainsKey("RXO"));
    [Fact] public void RxePresent()  => Assert.True(Msg.RepeatingSegments.ContainsKey("RXE"));
    [Fact] public void RxrPresent()  => Assert.True(Msg.RepeatingSegments.ContainsKey("RXR"));
}

// ── RAS Administration ────────────────────────────────────────────────────────

public class TestRASAdministration
{
    private static readonly HL7Message Msg = Fixture.Parse("ras_o17_admin.hl7");

    [Fact] public void MessageType() => Assert.Equal("RAS^O17", Msg.MessageType);
    [Fact] public void RxaPresent()  => Assert.True(Msg.RepeatingSegments.ContainsKey("RXA"));
}

// ── ORU Lab Results ───────────────────────────────────────────────────────────

public class TestORULabResults
{
    private static readonly HL7Message Msg = Fixture.Parse("oru_r01_lab.hl7");

    [Fact] public void MessageType()  => Assert.Equal("ORU^R01", Msg.MessageType);
    [Fact] public void ObxPresent()   => Assert.True(Msg.RepeatingSegments.ContainsKey("OBX"));
    [Fact] public void SpmPresent()   => Assert.True(Msg.RepeatingSegments.ContainsKey("SPM"));
    [Fact] public void SacPresent()   => Assert.True(Msg.RepeatingSegments.ContainsKey("SAC"));
}

// ── Segment order ─────────────────────────────────────────────────────────────

public class TestSegmentOrder
{
    [Fact]
    public void OrderPreservesMessageStructure()
    {
        var msg = Fixture.Parse("a01_admit.hl7");
        var ids = msg.SegmentOrder.Select(t => t.SegmentId).ToList();
        Assert.Equal("MSH", ids[0]);
        Assert.Equal("EVN", ids[1]);
        Assert.Equal("PID", ids[2]);
    }
}
