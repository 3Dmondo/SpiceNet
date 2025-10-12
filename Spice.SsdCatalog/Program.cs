using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp;
using System.Net; // DecompressionMethods
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

// Crawler for JPL SSD public FTP browser under https://ssd.jpl.nasa.gov/ftp/eph/
// Traverses directory listing pages and extracts metadata about *.bsp ephemeris files.
// Optionally (with --include-testpo) also fetches planetary testpo reference files under planets/ascii/deXXX/.
// For 'planets' and 'satellites' only the 'bsp' subdirectory is traversed for BSP catalog.
// Output (only when catalog content changes unless --force):
//   - docs/SsdCatalog/ssd_catalog.json (BSP files)
//   - docs/SsdCatalog/testpo_catalog.json (TestPo reference files; when flag enabled)
//   - docs/SsdCatalog/SSDCatalog.md (Markdown with standard links; top-level collapsible only)
//   - docs/SsdCatalog/catalog.hash (SHA256 canonical content signature including testpo lines when present)
// Console markers:
//   SSDCATALOG:CHANGED <hash>
//   SSDCATALOG:NO_CHANGE <hash>
// Flags:
//   --stable (default) : hash compare, only write on change
//   --force            : always rewrite files
//   --hash-only        : crawl & output hash marker, no files written
//   --include-testpo   : include testpo catalog & section

const string RootUrl = "https://ssd.jpl.nasa.gov/ftp/eph/";

// ---------------- argument parsing ----------------
bool force = false; bool hashOnly = false; bool includeTestPo = false;
foreach (var a in Environment.GetCommandLineArgs())
  switch (a)
  {
    case "--force": force = true; break;
    case "--hash-only": hashOnly = true; break;
    case "--stable": break; // default
    case "--include-testpo": includeTestPo = true; break;
  }

var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
http.DefaultRequestHeaders.UserAgent.ParseAdd("SpiceNet-SsdCatalogCrawler/1.4 (+https://github.com/3Dmondo/SpiceNet)");

var config = Configuration.Default;
var context = BrowsingContext.New(config);

var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var queue = new Queue<string>();
queue.Enqueue(RootUrl);

var bspEntries = new List<CatalogEntry>();

while (queue.Count > 0)
{
  var url = queue.Dequeue();
  if (!visited.Add(url)) continue;
  try
  {
    Console.WriteLine($"Fetching {url}");
    var html = await http.GetStringAsync(url);
    var doc = await context.OpenAsync(req => req.Content(html));
    var table = doc.QuerySelector("table.ftp-browser");
    if (table is null) continue;
    foreach (var row in table.QuerySelectorAll("tbody > tr"))
    {
      var cells = row.QuerySelectorAll("td");
      if (cells.Length < 4) continue;
      var link = cells[0].QuerySelector("a");
      if (link == null) continue;
      var name = link.TextContent.Trim();
      if (name.Equals("Parent Directory", StringComparison.OrdinalIgnoreCase)) continue;
      var href = link.GetAttribute("href") ?? string.Empty;
      string absolute = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : new Uri(new Uri(url), href).ToString();
      var isDirectoryType = cells[3].TextContent.Trim().Equals("Directory", StringComparison.OrdinalIgnoreCase) || name.EndsWith('/');
      if (isDirectoryType && !absolute.EndsWith('/')) absolute += '/';

      var lastModRaw = cells[1].TextContent.Trim();
      DateTime? lastMod = null;
      if (DateTime.TryParse(lastModRaw, out var dt)) lastMod = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
      var sizeDisplay = cells[2].TextContent.Trim();

      if (isDirectoryType)
      {
        if (IsUnderRoot(absolute))
        {
          var relDir = GetRelativePath(absolute);
          if (ShouldTraverseDirectory(relDir)) queue.Enqueue(absolute);
        }
        continue;
      }

      if (!name.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)) continue;
      var relativePath = GetRelativePath(absolute);
      if (!ShouldIncludeFile(relativePath)) continue;
      bspEntries.Add(new CatalogEntry
      {
        Name = name,
        Url = absolute,
        RelativePath = relativePath,
        LastModified = lastMod,
        SizeDisplay = sizeDisplay,
        SizeBytes = ParseSize(sizeDisplay),
        CollectedUtc = DateTime.UtcNow
      });
    }
  }
  catch (Exception ex)
  {
    Console.Error.WriteLine($"WARN: Failed to process {url}: {ex.Message}");
  }
}

bspEntries.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

List<TestPoEntry> testPoEntries = new();
if (includeTestPo)
{
  // Derive ephemeris numbers from BSP names under planets/bsp/deXXX*.bsp
  var numberRegex = new Regex(@"^de(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  var ephNumbers = bspEntries
    .Where(e => e.RelativePath.StartsWith("planets/bsp/de", StringComparison.OrdinalIgnoreCase))
    .Select(e => numberRegex.Match(e.Name))
    .Where(m => m.Success)
    .Select(m => m.Groups[1].Value)
    .Distinct(StringComparer.Ordinal)
    .OrderBy(s => s, StringComparer.Ordinal)
    .ToList();

  foreach (var num in ephNumbers)
  {
    var asciiDir = $"{RootUrl}planets/ascii/de{num}/";
    try
    {
      Console.WriteLine($"Fetching {asciiDir}");
      var html = await http.GetStringAsync(asciiDir);
      var doc = await context.OpenAsync(req => req.Content(html));
      var table = doc.QuerySelector("table.ftp-browser");
      if (table is null) continue;
      foreach (var row in table.QuerySelectorAll("tbody > tr"))
      {
        var cells = row.QuerySelectorAll("td");
        if (cells.Length < 4) continue;
        var link = cells[0].QuerySelector("a");
        if (link == null) continue;
        var name = link.TextContent.Trim();
        if (!name.Equals($"testpo.{num}", StringComparison.OrdinalIgnoreCase)) continue;
        var href = link.GetAttribute("href") ?? string.Empty;
        string absolute = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : new Uri(new Uri(asciiDir), href).ToString();
        var lastModRaw = cells[1].TextContent.Trim();
        DateTime? lastMod = null;
        if (DateTime.TryParse(lastModRaw, out var dt)) lastMod = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        var sizeDisplay = cells[2].TextContent.Trim();
        testPoEntries.Add(new TestPoEntry
        {
          EphemerisNumber = num,
          Name = name,
          Url = absolute,
          RelativePath = GetRelativePath(absolute),
          LastModified = lastMod,
          SizeDisplay = sizeDisplay,
          SizeBytes = ParseSize(sizeDisplay)
        });
      }
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"WARN: Failed testpo fetch for {num}: {ex.Message}");
    }
  }
}

// Canonical content lines for hashing (include testpo when present)
var canonicalBsp = bspEntries.Select(e => string.Join('|', "BSP", e.RelativePath, e.SizeBytes?.ToString() ?? "-", (e.LastModified?.ToString("yyyy-MM-dd HH:mm") ?? "-")));
var canonicalTestPo = testPoEntries.Select(e => string.Join('|', "TESTPO", e.RelativePath, e.SizeBytes?.ToString() ?? "-", (e.LastModified?.ToString("yyyy-MM-dd HH:mm") ?? "-")));
var canonicalAll = canonicalBsp.Concat(canonicalTestPo).ToArray();
var catalogHash = ComputeSha256(canonicalAll);

const string outDir = "docs/SsdCatalog";
Directory.CreateDirectory(outDir);
string hashFile = Path.Combine(outDir, "catalog.hash");
string jsonFile = Path.Combine(outDir, "ssd_catalog.json");
string testpoFile = Path.Combine(outDir, "testpo_catalog.json");
string mdFile = Path.Combine(outDir, "SSDCatalog.md");

string? previousHash = File.Exists(hashFile) ? File.ReadAllText(hashFile).Trim() : null;
bool changed = force || previousHash is null || !string.Equals(previousHash, catalogHash, StringComparison.OrdinalIgnoreCase);

if (hashOnly)
{
  Console.WriteLine(changed ? $"SSDCATALOG:CHANGED {catalogHash}" : $"SSDCATALOG:NO_CHANGE {catalogHash}");
  return;
}

if (!changed)
{
  Console.WriteLine($"SSDCATALOG:NO_CHANGE {catalogHash}");
  return;
}

// Write BSP catalog JSON
var bspCatalog = new CatalogRoot { Root = RootUrl, GeneratedUtc = DateTime.UtcNow, FileCount = bspEntries.Count, Files = bspEntries };
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
await File.WriteAllTextAsync(jsonFile, JsonSerializer.Serialize(bspCatalog, jsonOptions));

// Write testpo catalog if enabled
if (includeTestPo)
{
  var testpoCatalog = new TestPoCatalogRoot { Root = RootUrl, GeneratedUtc = DateTime.UtcNow, FileCount = testPoEntries.Count, Files = testPoEntries };
  await File.WriteAllTextAsync(testpoFile, JsonSerializer.Serialize(testpoCatalog, jsonOptions));
}

// Build hierarchy for Markdown rendering of BSPs
var rootNode = new DirNode("", null);
foreach (var e in bspEntries)
  rootNode.AddFile(e.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries), 0, e);

var md = new StringBuilder();
md.AppendLine("# JPL SSD BSP Ephemerides Catalog");
md.AppendLine();
md.AppendLine($"Root: {RootUrl}");
md.AppendLine($"Generated (UTC): {bspCatalog.GeneratedUtc:O}");
md.AppendLine($"Total BSP files: {bspCatalog.FileCount}");
if (includeTestPo) md.AppendLine($"Total TestPo files: {testPoEntries.Count}");
md.AppendLine();

// Only top-level directories collapsible; nested shown via <div> lines with &nbsp; indentation.
foreach (var top in rootNode.Directories.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
{
  md.AppendLine("<details>");
  md.Append("  <summary><strong><a href=\"").Append(GetDirectoryUrl(top)).Append("\">").Append(top.Name).Append("/</a></strong></summary>").AppendLine();
  WriteSub(top, 1, md); // depth starts at 1 for children
  md.AppendLine("</details>");
}

if (includeTestPo)
{
  md.AppendLine();
  md.AppendLine("<details>");
  md.AppendLine("  <summary><strong>planet testpo reference files</strong></summary>");
  foreach (var t in testPoEntries.OrderBy(t => int.Parse(t.EphemerisNumber)))
  {
    var ts = t.LastModified?.ToString("yyyy-MM-dd HH:mm") ?? "?";
    md.Append("  <div>").Append(Indent(1)).Append("<a href=\"").Append(t.Url).Append("\">")
      .Append(t.Name).Append("</a> (").Append(t.SizeDisplay).Append(' ').Append(ts).AppendLine(")</div>");
  }
  md.AppendLine("</details>");
}

md.AppendLine();
md.AppendLine("> Automatically generated by Spice.SsdCatalog tool. Do not edit manually.");
await File.WriteAllTextAsync(mdFile, md.ToString());
await File.WriteAllTextAsync(hashFile, catalogHash + Environment.NewLine);

Console.WriteLine($"SSDCATALOG:CHANGED {catalogHash}");
Console.WriteLine($"Catalog written with {bspEntries.Count} BSP files to {outDir} (testpo included={includeTestPo}).");

// ---------------- helper functions & types ----------------
static string ComputeSha256(string[] lines)
{ using var sha = SHA256.Create(); var joined = string.Join('\n', lines); return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(joined))); }
static bool IsUnderRoot(string absolute) => absolute.StartsWith(RootUrl, StringComparison.OrdinalIgnoreCase);
static string GetRelativePath(string absolute) => absolute.StartsWith(RootUrl, StringComparison.OrdinalIgnoreCase) ? absolute[RootUrl.Length..] : absolute;
static bool ShouldTraverseDirectory(string relativeDir)
{ if (string.IsNullOrEmpty(relativeDir)) return true; if (!relativeDir.EndsWith('/')) relativeDir += '/'; if (relativeDir.StartsWith("planets/", StringComparison.OrdinalIgnoreCase)) { if (relativeDir.Equals("planets/", StringComparison.OrdinalIgnoreCase)) return true; return relativeDir.StartsWith("planets/bsp/", StringComparison.OrdinalIgnoreCase); } if (relativeDir.StartsWith("satellites/", StringComparison.OrdinalIgnoreCase)) { if (relativeDir.Equals("satellites/", StringComparison.OrdinalIgnoreCase)) return true; return relativeDir.StartsWith("satellites/bsp/", StringComparison.OrdinalIgnoreCase); } return true; }
static bool ShouldIncludeFile(string relativePath)
{ if (relativePath.StartsWith("planets/", StringComparison.OrdinalIgnoreCase)) return relativePath.StartsWith("planets/bsp/", StringComparison.OrdinalIgnoreCase); if (relativePath.StartsWith("satellites/", StringComparison.OrdinalIgnoreCase)) return relativePath.StartsWith("satellites/bsp/", StringComparison.OrdinalIgnoreCase); return true; }
static long? ParseSize(string sizeDisplay)
{ if (string.IsNullOrWhiteSpace(sizeDisplay)) return null; sizeDisplay = sizeDisplay.Trim(); var unit = sizeDisplay[^1]; if (char.IsLetter(unit)) { if (!double.TryParse(sizeDisplay[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) return null; return unit switch { 'K' or 'k' => (long)(value * 1024), 'M' or 'm' => (long)(value * 1024 * 1024), 'G' or 'g' => (long)(value * 1024 * 1024 * 1024), 'T' or 't' => (long)(value * 1024L * 1024L * 1024L * 1024L), _ => null }; } if (long.TryParse(sizeDisplay, out var bytes)) return bytes; return null; }
static string Indent(int depth) => string.Concat(Enumerable.Repeat("&nbsp;&nbsp;&nbsp;&nbsp;", depth));
static void WriteSub(DirNode dir, int depth, StringBuilder sb)
{
  foreach (var sub in dir.Directories.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
  {
    sb.Append("  <div>").Append(Indent(depth)).Append("<a href=\"").Append(GetDirectoryUrl(sub)).Append("\">")
      .Append(sub.Name).Append("/</a></div>").AppendLine();
    WriteSub(sub, depth + 1, sb);
  }
  foreach (var file in dir.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
  {
    var ts = file.LastModified?.ToString("yyyy-MM-dd HH:mm") ?? "?";
    sb.Append("  <div>").Append(Indent(depth)).Append("<a href=\"").Append(file.Url).Append("\">")
      .Append(file.Name).Append("</a> (").Append(file.SizeDisplay).Append(' ').Append(ts).AppendLine(")</div>");
  }
}
static string GetDirectoryUrl(DirNode node)
{ if (node.Parent == null) return RootUrl; var stack = new Stack<string>(); var cur = node; while (cur.Parent != null) { stack.Push(cur.Name); cur = cur.Parent; } return RootUrl + string.Join('/', stack) + '/'; }

record CatalogRoot { public required string Root { get; init; } public DateTime GeneratedUtc { get; init; } public int FileCount { get; init; } public required List<CatalogEntry> Files { get; init; } }
record CatalogEntry { public required string Name { get; init; } public required string Url { get; init; } public required string RelativePath { get; init; } public DateTime? LastModified { get; init; } public string? SizeDisplay { get; init; } public long? SizeBytes { get; init; } public DateTime CollectedUtc { get; init; } }
record TestPoCatalogRoot { public required string Root { get; init; } public DateTime GeneratedUtc { get; init; } public int FileCount { get; init; } public required List<TestPoEntry> Files { get; init; } }
record TestPoEntry { public required string EphemerisNumber { get; init; } public required string Name { get; init; } public required string Url { get; init; } public required string RelativePath { get; init; } public DateTime? LastModified { get; init; } public string? SizeDisplay { get; init; } public long? SizeBytes { get; init; } }
sealed class DirNode { public string Name { get; } public DirNode? Parent { get; } public Dictionary<string, DirNode> Directories { get; } = new(StringComparer.OrdinalIgnoreCase); public List<CatalogEntry> Files { get; } = new(); public DirNode(string name, DirNode? parent) { Name = name; Parent = parent; } public void AddFile(string[] parts, int index, CatalogEntry entry) { if (index == parts.Length - 1) { Files.Add(entry); return; } var dirName = parts[index]; if (!Directories.TryGetValue(dirName, out var child)) { child = new DirNode(dirName, this); Directories.Add(dirName, child); } child.AddFile(parts, index + 1, entry); } }
