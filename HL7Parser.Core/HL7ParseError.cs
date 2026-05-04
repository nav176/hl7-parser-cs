namespace HL7Parser.Core;

public class HL7ParseError : Exception
{
    public HL7ParseError(string message) : base(message) { }
}
