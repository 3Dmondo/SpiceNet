// Original design: Utility for extracting DAF (SPK) comment area and lightweight symbol/style assignments.
// References: spc.req (comment area), daf.req (record structure), symbols.req (conceptual inspiration only).
using System.Text;
using System.Text.RegularExpressions;
using Spice.IO;

namespace Spice.Kernels;

/// <summary>
/// Provides methods to read the comment area of a DAF-based kernel (e.g. SPK) and
/// heuristically parse simple KEY = value[ value2 ...] style assignments (“symbols”).
/// Parsing is intentionally lightweight; it does NOT build a full SPICE kernel pool.
/// </summary>
public static class DafCommentUtility
{
  static readonly Regex Assignment = new("^([A-Z0-9_]+)\\s*=\\s*(.+)$", RegexOptions.Compiled);

  public sealed record DafSymbol(string Name, IReadOnlyList<string> RawValues, IReadOnlyList<double> NumericValues)
  {
    public override string ToString()
    {
      var nums = NumericValues.Count > 0 ? $" [{string.Join(' ', NumericValues)}]" : string.Empty;
      return $"{Name} = {string.Join(' ', RawValues)}{nums}";
    }
  }

  /// <summary>Reads all comment lines and parsed symbols from the specified DAF (SPK) file.</summary>
  public static (string[] Comments, IReadOnlyList<DafSymbol> Symbols) Extract(string filePath)
  {
    using var fs = File.OpenRead(filePath);
    using var daf = FullDafReader.Open(fs, leaveOpen: false);
    var comments = daf.ReadComments();
    var symbols = new List<DafSymbol>();

    foreach (var line in comments)
    {
      var m = Assignment.Match(line.Trim());
      if (!m.Success) continue;
      string name = m.Groups[1].Value.Trim();
      string rhs = m.Groups[2].Value.Trim();
      if (name.Length == 0 || rhs.Length == 0) continue;
      // Tokenize on comma or whitespace
      var rawTokens = rhs.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      var rawList = (IReadOnlyList<string>)rawTokens.ToList();
      var numeric = new List<double>();
      foreach (var tok in rawTokens)
      {
        string norm = tok.Contains('D') || tok.Contains('d') ? tok.Replace('d', 'E').Replace('D', 'E') : tok;
        if (double.TryParse(norm, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv))
          numeric.Add(dv);
      }
      symbols.Add(new DafSymbol(name, rawList, numeric));
    }
    return (comments, symbols);
  }
}
