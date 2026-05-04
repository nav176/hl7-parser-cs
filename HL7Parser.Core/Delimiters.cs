namespace HL7Parser.Core;

public class Delimiters
{
    public char Field { get; }
    public char Component { get; }
    public char Repetition { get; }
    public char Escape { get; }
    public char Subcomponent { get; }

    public Delimiters(char field = '|', char component = '^', char repetition = '~',
                      char escape = '\\', char subcomponent = '&')
    {
        Field = field;
        Component = component;
        Repetition = repetition;
        Escape = escape;
        Subcomponent = subcomponent;
    }

    public static Delimiters FromMsh(string mshLine)
    {
        if (!mshLine.StartsWith("MSH"))
            throw new HL7ParseError("First segment must be MSH");
        if (mshLine.Length < 8)
            throw new HL7ParseError("MSH segment is too short to contain encoding characters");

        char fieldSep = mshLine[3];
        string encoding = mshLine.Substring(4, 4);
        if (encoding.Length < 4)
            throw new HL7ParseError($"MSH encoding characters are incomplete: {encoding}");

        return new Delimiters(fieldSep, encoding[0], encoding[1], encoding[2], encoding[3]);
    }
}
