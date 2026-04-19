using System.Globalization;
using System.Text;

namespace Spice.Kernels;

/// <summary>
/// Minimal NAIF text-kernel parser for straightforward assignment blocks inside \begindata sections.
/// It is intentionally scoped to the web-data generator's metadata needs rather than the full kernel-pool grammar.
/// </summary>
internal static class TextKernelParser
{
  public sealed record TextKernelAssignment(string Name, string RawValue, IReadOnlyList<string> RawTokens, IReadOnlyList<double> NumericValues)
  {
    public double? FirstNumeric => NumericValues.Count > 0 ? NumericValues[0] : null;
  }

  public sealed record TextKernelDocument(IReadOnlyList<TextKernelAssignment> Assignments, IReadOnlyDictionary<string, TextKernelAssignment> AssignmentMap);

  public static TextKernelDocument Parse(string filePath) {
    using var stream = File.OpenRead(filePath);
    return Parse(stream);
  }

  public static TextKernelDocument Parse(Stream stream) {
    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
    var lines = new List<string>();

    while (!reader.EndOfStream) {
      lines.Add(reader.ReadLine() ?? string.Empty);
    }

    var assignments = new List<TextKernelAssignment>();
    var assignmentMap = new Dictionary<string, TextKernelAssignment>(StringComparer.OrdinalIgnoreCase);
    var inDataBlock = false;

    for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++) {
      var line = lines[lineIndex].Trim();

      if (line.StartsWith(@"\begindata", StringComparison.OrdinalIgnoreCase)) {
        inDataBlock = true;
        continue;
      }

      if (line.StartsWith(@"\begintext", StringComparison.OrdinalIgnoreCase)) {
        inDataBlock = false;
        continue;
      }

      if (!inDataBlock || IsSkippable(line)) {
        continue;
      }

      var equalsIndex = line.IndexOf('=');
      if (equalsIndex < 0) {
        continue;
      }

      var name = line[..equalsIndex].Trim();
      if (name.Length == 0) {
        continue;
      }

      var rawValueBuilder = new StringBuilder(line[(equalsIndex + 1)..].Trim());
      while (rawValueBuilder.Length == 0 || RequiresContinuation(rawValueBuilder.ToString())) {
        lineIndex++;
        if (lineIndex >= lines.Count) {
          throw new InvalidDataException($"Unterminated text-kernel assignment '{name}'.");
        }

        var continuation = lines[lineIndex].Trim();
        if (continuation.StartsWith(@"\begintext", StringComparison.OrdinalIgnoreCase)) {
          throw new InvalidDataException($"Assignment '{name}' reached \\begintext before closing.");
        }

        if (IsSkippable(continuation)) {
          continue;
        }

        if (rawValueBuilder.Length > 0) {
          rawValueBuilder.Append(' ');
        }

        rawValueBuilder.Append(continuation);
      }

      var rawValue = rawValueBuilder.ToString().Trim();
      var tokens = Tokenize(rawValue);
      var numericValues = ParseNumericTokens(tokens);
      var assignment = new TextKernelAssignment(name, rawValue, tokens, numericValues);
      assignments.Add(assignment);
      assignmentMap[name] = assignment;
    }

    return new TextKernelDocument(assignments, assignmentMap);
  }

  static bool IsSkippable(string line) {
    if (string.IsNullOrWhiteSpace(line)) {
      return true;
    }

    return line.StartsWith('\\')
        || line.StartsWith('#')
        || line.StartsWith("//", StringComparison.Ordinal)
        || line.StartsWith("/*", StringComparison.Ordinal)
        || line.StartsWith('*');
  }

  static bool RequiresContinuation(string rawValue) {
    var parenDepth = 0;
    var inQuote = false;

    foreach (var ch in rawValue) {
      if (ch == '\'') {
        inQuote = !inQuote;
        continue;
      }

      if (inQuote) {
        continue;
      }

      if (ch == '(') {
        parenDepth++;
        continue;
      }

      if (ch == ')' && parenDepth > 0) {
        parenDepth--;
      }
    }

    return inQuote || parenDepth > 0;
  }

  static List<string> Tokenize(string rawValue) {
    var value = rawValue.Trim();
    if (value.Length >= 2 && value[0] == '(' && value[^1] == ')') {
      value = value[1..^1];
    }

    var tokens = new List<string>();
    var current = new StringBuilder();
    var inQuote = false;

    foreach (var ch in value) {
      if (ch == '\'') {
        inQuote = !inQuote;
        continue;
      }

      if (!inQuote && (char.IsWhiteSpace(ch) || ch == ',' || ch == '(' || ch == ')')) {
        FlushToken(current, tokens);
        continue;
      }

      current.Append(ch);
    }

    FlushToken(current, tokens);
    return tokens;
  }

  static void FlushToken(StringBuilder current, List<string> tokens) {
    if (current.Length == 0) {
      return;
    }

    tokens.Add(current.ToString());
    current.Clear();
  }

  static List<double> ParseNumericTokens(IReadOnlyList<string> rawTokens) {
    var numeric = new List<double>(rawTokens.Count);

    foreach (var token in rawTokens) {
      var normalized = token.Contains('D') || token.Contains('d')
        ? token.Replace('d', 'E').Replace('D', 'E')
        : token;

      if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) {
        numeric.Add(value);
      }
    }

    return numeric;
  }
}
