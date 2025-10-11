// CSPICE Port Reference: N/A (original managed design)
namespace Spice.Kernels;

/// <summary>
/// Parses a minimal meta-kernel listing kernel file paths inside a KERNELS_TO_LOAD assignment block.
/// Example:
///   \begindata
///   KERNELS_TO_LOAD = ( 'a.tls'
///                       "subdir/ephem.bsp" )
/// Relative paths are resolved against the directory of the meta-kernel source path provided to <see cref="Parse(Stream,string)"/>.
/// Comments (lines beginning with \\ # // /* *) and blank lines are ignored. Simplified grammar for MVP tests.
/// </summary>
internal static class MetaKernelParser
{
  internal static MetaKernel Parse(Stream stream, string sourcePath)
  {
    if (!stream.CanRead) throw new ArgumentException("Stream must be readable", nameof(stream));
    var dir = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory();

    using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, leaveOpen:true);
    var lines = new List<string>();
    while (!reader.EndOfStream) lines.Add(reader.ReadLine() ?? string.Empty);

    var kernels = new List<string>();

    for (int i=0;i<lines.Count;i++)
    {
      var line = lines[i].Trim();
      if (IsSkippable(line)) continue;
      if (!line.StartsWith("KERNELS_TO_LOAD", StringComparison.OrdinalIgnoreCase)) continue;
      int eq = line.IndexOf('=');
      if (eq < 0) throw new InvalidDataException("Missing '=' in KERNELS_TO_LOAD line");
      var after = line[(eq+1)..].Trim();
      var blockBuilder = new System.Text.StringBuilder();
      if (after.StartsWith('(')) blockBuilder.Append(after[1..]);
      // accumulate until ')'
      while (!blockBuilder.ToString().Contains(')'))
      {
        i++;
        if (i >= lines.Count) throw new InvalidDataException("Unterminated KERNELS_TO_LOAD block");
        var l = lines[i].Trim();
        if (IsSkippable(l)) continue;
        blockBuilder.Append(' ').Append(l);
      }
      var block = blockBuilder.ToString();
      int close = block.IndexOf(')');
      if (close >= 0) block = block[..close];
      foreach (var path in ExtractQuoted(block))
      {
        var resolved = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(dir, path));
        kernels.Add(resolved);
      }
    }

    if (kernels.Count == 0) throw new InvalidDataException("No KERNELS_TO_LOAD entries found");
    return new MetaKernel(kernels);
  }

  static bool IsSkippable(string line)
  {
    if (string.IsNullOrWhiteSpace(line)) return true;
    if (line.StartsWith("\\") || line.StartsWith("#") || line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("*")) return true;
    if (line.StartsWith("\begindata", StringComparison.OrdinalIgnoreCase)) return true;
    return false;
  }

  static IEnumerable<string> ExtractQuoted(string block)
  {
    var list = new List<string>();
    int i=0;
    while (i < block.Length)
    {
      char c = block[i];
      if (c == '\'' || c == '"')
      {
        char quote = c;
        int start = ++i;
        while (i < block.Length && block[i] != quote) i++;
        if (i >= block.Length) throw new InvalidDataException("Unterminated quoted path in meta-kernel");
        var raw = block[start..i];
        if (!string.IsNullOrWhiteSpace(raw)) list.Add(raw.Trim());
        i++; // skip closing quote
      }
      else i++;
    }
    return list;
  }
}

/// <summary>
/// Meta kernel model holding absolute kernel file paths.
/// </summary>
internal sealed record MetaKernel(IReadOnlyList<string> KernelPaths);

/// <summary>
/// Registry tracking loaded kernel paths (and optionally parsed kernel objects in later phases).
/// </summary>
internal sealed class KernelRegistry
{
  readonly List<string> _paths = new();
  internal IReadOnlyList<string> KernelPaths => _paths;

  internal void AddKernelPaths(IEnumerable<string> paths)
  {
    foreach (var p in paths)
    {
      var full = Path.GetFullPath(p);
      if (!_paths.Contains(full, StringComparer.OrdinalIgnoreCase))
        _paths.Add(full);
    }
  }

  internal void AddMetaKernel(MetaKernel meta) => AddKernelPaths(meta.KernelPaths);
}
