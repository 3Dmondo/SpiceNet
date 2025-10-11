// CSPICE Port Reference: N/A (original managed design)
using System.Text;
using Spice.Core;

namespace Spice.Kernels;

/// <summary>
/// Minimal leap second text kernel (LSK) parser for synthetic / trimmed test kernels.
/// Supports parsing lines defining the cumulative TAI-UTC offset via a DELTET/DELTA_AT assignment block:
///   DELTET/DELTA_AT = ( 32, @1999-JAN-01
///                       33, @2006-JAN-01 )
/// Dates are interpreted as UTC 00:00:00 of the specified day. Offsets are cumulative seconds (TAI-UTC) becoming
/// effective at that UTC boundary (inclusive).
/// Ignored content: blank lines, lines beginning with '\\', '#', '/*', '*/', '//' as simple comment handling, and other assignments.
/// This parser is intentionally strict and minimal for MVP test usage; it does not fully implement NAIF LSK grammar.
/// </summary>
internal static class LskParser
{
  static readonly Dictionary<string,int> MonthMap = new(StringComparer.OrdinalIgnoreCase)
  {
    ["JAN"] = 1,["FEB"] = 2,["MAR"] = 3,["APR"] = 4,["MAY"] = 5,["JUN"] = 6,
    ["JUL"] = 7,["AUG"] = 8,["SEP"] = 9,["OCT"] = 10,["NOV"] = 11,["DEC"] = 12
  };

  internal static LskKernel Parse(Stream stream)
  {
    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen:true);
    var lines = new List<string>();
    while (!reader.EndOfStream) lines.Add(reader.ReadLine() ?? string.Empty);

    var entries = new List<LeapSecondEntry>();

    for (int i=0;i<lines.Count;i++)
    {
      var line = lines[i].Trim();
      if (IsSkippable(line)) continue;
      if (!line.StartsWith("DELTET/DELTA_AT", StringComparison.OrdinalIgnoreCase)) continue;

      int eq = line.IndexOf('=');
      if (eq < 0) throw new InvalidDataException("Missing '=' in DELTET/DELTA_AT line");
      var afterEq = line[(eq+1)..].Trim();
      var blockBuilder = new StringBuilder();

      if (afterEq.StartsWith('('))
        blockBuilder.Append(afterEq[1..]);

      while (!blockBuilder.ToString().Contains(')'))
      {
        i++;
        if (i >= lines.Count) throw new InvalidDataException("Unterminated DELTET/DELTA_AT block");
        var l = lines[i].Trim();
        if (IsSkippable(l)) continue;
        blockBuilder.Append(' ').Append(l);
      }

      var block = blockBuilder.ToString();
      int closeIdx = block.IndexOf(')');
      if (closeIdx >= 0) block = block[..closeIdx];

      var rawTokens = block.Replace('\t',' ').Split(new[]{' ',','}, StringSplitOptions.RemoveEmptyEntries);
      for (int t = 0; t < rawTokens.Length; t++)
      {
        if (!double.TryParse(rawTokens[t], out var offset))
          throw new InvalidDataException($"Expected numeric TAI-UTC offset token near '{rawTokens[t]}'");
        t++;
        if (t >= rawTokens.Length) throw new InvalidDataException("Missing date token after offset");
        var dateToken = rawTokens[t];
        if (!dateToken.StartsWith('@')) throw new InvalidDataException($"Date token must start with '@' (got '{dateToken}')");
        var date = ParseDate(dateToken[1..]);
        entries.Add(new LeapSecondEntry(date, offset));
      }
    }

    if (entries.Count == 0)
      throw new InvalidDataException("No DELTET/DELTA_AT entries found in LSK");

    return LskKernel.FromEntries(entries);
  }

  static bool IsSkippable(string line)
  {
    if (string.IsNullOrWhiteSpace(line)) return true;
    if (line.StartsWith("\\") || line.StartsWith("#") || line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("*")) return true;
    if (line.StartsWith("\begindata", StringComparison.OrdinalIgnoreCase)) return true;
    return false;
  }

  static DateTimeOffset ParseDate(string token)
  {
    var parts = token.Split('-');
    if (parts.Length != 3) throw new InvalidDataException($"Invalid date format '{token}'");
    if (!int.TryParse(parts[0], out var year)) throw new InvalidDataException($"Invalid year in '{token}'");
    if (!MonthMap.TryGetValue(parts[1], out var month)) throw new InvalidDataException($"Invalid month in '{token}'");
    if (!int.TryParse(parts[2], out var day)) throw new InvalidDataException($"Invalid day in '{token}'");
    return new DateTimeOffset(year, month, day, 0,0,0, TimeSpan.Zero);
  }
}
