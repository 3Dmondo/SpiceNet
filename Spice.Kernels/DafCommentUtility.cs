// Original design: Utility for extracting DAF (SPK) comment area and lightweight symbol/style assignments.
// References: spc.req (comment area), daf.req (record structure), symbols.req (conceptual inspiration only).
using System.Text.RegularExpressions;
using Spice.IO;

namespace Spice.Kernels;

/// <summary>
/// Provides methods to read the comment area of a DAF-based kernel (e.g. SPK) and
/// heuristically parse simple KEY = value[ value2 ...] or KEY value1 value2 ... style assignments ("symbols").
/// Parsing is intentionally lightweight; it does NOT build a full SPICE kernel pool.
/// </summary>
public static class DafCommentUtility
{
  // Matches KEY = rhs (entire line after = captured)
  static readonly Regex AssignmentEquals = new("^([A-Z0-9_]+)\\s*=\\s*(.+)$", RegexOptions.Compiled);
  // Matches KEY <num> [other tokens...] (first contiguous numeric column after spaces)
  static readonly Regex AssignmentColumns = new(
    "^([A-Z0-9_]+)\\s+([-+]?\\d+(?:\\.\\d*)?(?:[DEde][+-]?\\d+)?)(?:\\s+(.+))?$",
    RegexOptions.Compiled);

  public sealed record DafSymbol(string Name, IReadOnlyList<string> RawValues, IReadOnlyList<double> NumericValues)
  {
    public double? FirstNumeric => NumericValues.Count > 0 ? NumericValues[0] : null;
    public override string ToString()
    {
      var nums = NumericValues.Count > 0 ? $" [{string.Join(' ', NumericValues)}]" : string.Empty;
      return $"{Name} = {string.Join(' ', RawValues)}{nums}";
    }
  }

  /// <summary>Reads all comment lines and parsed symbols from the specified DAF (SPK) file.</summary>
  public static (string[] Comments, IReadOnlyList<DafSymbol> Symbols, Dictionary<string, DafSymbol> SymbolMap) Extract(string filePath)
  {
    using var fs = File.OpenRead(filePath);
    using var daf = FullDafReader.Open(fs, leaveOpen: false);
    var comments = daf.ReadComments();
    var symbols = new List<DafSymbol>();
    var map = new Dictionary<string, DafSymbol>(StringComparer.OrdinalIgnoreCase);

    foreach (var rawLine in comments)
    {
      var line = rawLine.Trim();
      if (line.Length == 0) continue;

      DafSymbol? sym = null;

      // Style 1: KEY = values
      var mEq = AssignmentEquals.Match(line);
      if (mEq.Success)
      {
        var name = mEq.Groups[1].Value.Trim();
        var rhs = mEq.Groups[2].Value.Trim();
        if (name.Length == 0 || rhs.Length == 0) continue;
        var rawTokens = Tokenize(rhs);
        var numeric = ParseNumericTokens(rawTokens);
        sym = new DafSymbol(name, rawTokens, numeric);
      }
      else
      {
        // Style 2: KEY value1 value2 ... (columns). We accept only if KEY not purely numeric and value1 numeric.
        var mCols = AssignmentColumns.Match(line);
        if (mCols.Success)
        {
          var name = mCols.Groups[1].Value.Trim();
          var firstVal = mCols.Groups[2].Value.Trim();
          var rest = mCols.Groups[3].Success ? mCols.Groups[3].Value.Trim() : string.Empty;
          var rhs = rest.Length > 0 ? firstVal + " " + rest : firstVal;
          var rawTokens = Tokenize(rhs);
          var numeric = ParseNumericTokens(rawTokens);
          if (numeric.Count > 0) sym = new DafSymbol(name, rawTokens, numeric);
        }
      }

      if (sym is not null)
      {
        symbols.Add(sym);
        map[sym.Name] = sym; // last wins (consistent with kernel pool overwrite precedence philosophy)
      }
    }
    return (comments, symbols, map);
  }

  /// <summary>Try get numeric constant value (first numeric token) with given symbol name.</summary>
  public static bool TryGetConstant(string filePath, string name, out double value)
  {
    var (_, _, map) = Extract(filePath);
    if (map.TryGetValue(name, out var sym) && sym.FirstNumeric is double v)
    {
      value = v; return true;
    }
    value = default; return false;
  }

  static List<string> Tokenize(string rhs)
    => rhs.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

  static List<double> ParseNumericTokens(List<string> rawTokens)
  {
    var numeric = new List<double>(rawTokens.Count);
    foreach (var tok in rawTokens)
    {
      var norm = tok.Contains('D') || tok.Contains('d') ? tok.Replace('d', 'E').Replace('D', 'E') : tok;
      if (double.TryParse(norm, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv))
        numeric.Add(dv);
    }
    return numeric;
  }
}
