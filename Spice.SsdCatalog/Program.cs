using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp;
using System.Net; // DecompressionMethods
using System.Security.Cryptography;
using System.Text;

// Crawler for JPL SSD public FTP browser under https://ssd.jpl.nasa.gov/ftp/eph/
// Traverses directory listing pages and extracts metadata about *.bsp ephemeris files.
// For 'planets' and 'satellites' only the 'bsp' subdirectory is traversed (filters out planets/test-data etc.).
// Output (only when catalog content changes unless --force):
//   - Docs/SsdCatalog/ssd_catalog.json (machine data)
//   - Docs/SsdCatalog/SSDCatalog.md (HTML collapsible tree with directory & file links)
//   - Docs/SsdCatalog/catalog.hash (SHA256 canonical content signature)
// Console markers:
//   SSDCATALOG:CHANGED <hash>
//   SSDCATALOG:NO_CHANGE <hash>
// Exit code always 0 (workflow logic parses marker).
// Flags:
//   --stable (default) : hash compare, only write on change
//   --force            : always rewrite files
//   --hash-only        : crawl & output hash marker, no files written

const string RootUrl = "https://ssd.jpl.nasa.gov/ftp/eph/";

// ---------------- argument parsing ----------------
bool force = false;
bool hashOnly = false;
foreach (var a in Environment.GetCommandLineArgs())
{
  switch (a)
  {
    case "--force": force = true; break;
    case "--hash-only": hashOnly = true; break;
    case "--stable": break; // default
  }
}

var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
http.DefaultRequestHeaders.UserAgent.ParseAdd("SpiceNet-SsdCatalogCrawler/1.3 (+https://github.com/3Dmondo/SpiceNet)");

var config = Configuration.Default;
var context = BrowsingContext.New(config);

var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var queue = new Queue<string>();
queue.Enqueue(RootUrl);

var entries = new List<CatalogEntry>();

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
    if (table is null) continue; // not a directory listing
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
      if (isDirectoryType && !absolute.EndsWith('/')) absolute += '/'; // normalize directory

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
      entries.Add(new CatalogEntry
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

entries.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

// Canonical content lines for hashing
var canonical = entries.Select(e => string.Join('|',
  e.RelativePath,
  e.SizeBytes?.ToString() ?? "-",
  (e.LastModified?.ToString("yyyy-MM-dd HH:mm") ?? "-")
)).ToArray();
var catalogHash = ComputeSha256(canonical);

const string outDir = "Docs/SsdCatalog";
Directory.CreateDirectory(outDir);
string hashFile = Path.Combine(outDir, "catalog.hash");
string jsonFile = Path.Combine(outDir, "ssd_catalog.json");
string mdFile = Path.Combine(outDir, "SSDCatalog.md");

string? previousHash = null;
if (File.Exists(hashFile))
{
  previousHash = (await File.ReadAllTextAsync(hashFile)).Trim();
  if (previousHash.Length == 0) previousHash = null;
}

bool changed = force || previousHash is null || !string.Equals(previousHash, catalogHash, StringComparison.OrdinalIgnoreCase);

if (hashOnly)
{
  Console.WriteLine(changed ? $"SSDCATALOG:CHANGED {catalogHash}" : $"SSDCATALOG:NO_CHANGE {catalogHash}");
  return; // do not write any files
}

if (!changed)
{
  Console.WriteLine($"SSDCATALOG:NO_CHANGE {catalogHash}");
  return; // stable output; keep existing timestamp/time
}

// Changed -> write files
var catalog = new CatalogRoot
{
  Root = RootUrl,
  GeneratedUtc = DateTime.UtcNow,
  FileCount = entries.Count,
  Files = entries
};
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
await File.WriteAllTextAsync(jsonFile, JsonSerializer.Serialize(catalog, jsonOptions));

// Build hierarchy for HTML rendering
var rootNode = new DirNode("", null);
foreach (var e in entries)
  rootNode.AddFile(e.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries), 0, e);
var maxDepth = rootNode.GetMaxDepth();

var md = new StringBuilder();
md.AppendLine("# JPL SSD BSP Ephemerides Catalog");
md.AppendLine();
md.AppendLine($"Root: {RootUrl}");
md.AppendLine($"Generated (UTC): {catalog.GeneratedUtc:O}");
md.AppendLine($"Total BSP files: {catalog.FileCount}");
md.AppendLine();
md.AppendLine("<style>");
for (int d = 1; d <= maxDepth + 3; d++)
  md.AppendLine($".indent{d} {{ margin-left: {d * 20}px; }}");
md.AppendLine("details > summary { cursor: pointer; }");
md.AppendLine("details { padding: 2px 0; }");
md.AppendLine("</style>");
md.AppendLine();
foreach (var dir in rootNode.Directories.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
  RenderDirectory(dir, md, depth:0, open:false);
md.AppendLine();
md.AppendLine("> Automatically generated by Spice.SsdCatalog tool. Do not edit manually.");
await File.WriteAllTextAsync(mdFile, md.ToString());
await File.WriteAllTextAsync(hashFile, catalogHash + Environment.NewLine);

Console.WriteLine($"SSDCATALOG:CHANGED {catalogHash}");
Console.WriteLine($"Catalog written with {entries.Count} BSP files to {outDir}.");

// ---------------- helpers ----------------
static string ComputeSha256(string[] lines)
{
  using var sha = SHA256.Create();
  var joined = string.Join('\n', lines);
  var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(joined));
  return Convert.ToHexString(bytes); // upper-case hex
}

static bool IsUnderRoot(string absolute) => absolute.StartsWith(RootUrl, StringComparison.OrdinalIgnoreCase);
static string GetRelativePath(string absolute) => absolute.StartsWith(RootUrl, StringComparison.OrdinalIgnoreCase) ? absolute[RootUrl.Length..] : absolute;

static bool ShouldTraverseDirectory(string relativeDir)
{
  if (string.IsNullOrEmpty(relativeDir)) return true; // root
  if (!relativeDir.EndsWith('/')) relativeDir += '/';
  if (relativeDir.StartsWith("planets/", StringComparison.OrdinalIgnoreCase))
  { if (relativeDir.Equals("planets/", StringComparison.OrdinalIgnoreCase)) return true; return relativeDir.StartsWith("planets/bsp/", StringComparison.OrdinalIgnoreCase); }
  if (relativeDir.StartsWith("satellites/", StringComparison.OrdinalIgnoreCase))
  { if (relativeDir.Equals("satellites/", StringComparison.OrdinalIgnoreCase)) return true; return relativeDir.StartsWith("satellites/bsp/", StringComparison.OrdinalIgnoreCase); }
  return true;
}

static bool ShouldIncludeFile(string relativePath)
{
  if (relativePath.StartsWith("planets/", StringComparison.OrdinalIgnoreCase)) return relativePath.StartsWith("planets/bsp/", StringComparison.OrdinalIgnoreCase);
  if (relativePath.StartsWith("satellites/", StringComparison.OrdinalIgnoreCase)) return relativePath.StartsWith("satellites/bsp/", StringComparison.OrdinalIgnoreCase);
  return true;
}

static long? ParseSize(string sizeDisplay)
{
  if (string.IsNullOrWhiteSpace(sizeDisplay)) return null;
  sizeDisplay = sizeDisplay.Trim();
  var unit = sizeDisplay[^1];
  if (char.IsLetter(unit))
  {
    if (!double.TryParse(sizeDisplay[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) return null;
    return unit switch
    {
      'K' or 'k' => (long)(value * 1024),
      'M' or 'm' => (long)(value * 1024 * 1024),
      'G' or 'g' => (long)(value * 1024 * 1024 * 1024),
      'T' or 't' => (long)(value * 1024L * 1024L * 1024L * 1024L),
      _ => null
    };
  }
  if (long.TryParse(sizeDisplay, out var bytes)) return bytes;
  return null;
}

static void RenderDirectory(DirNode dir, StringBuilder sb, int depth, bool open)
{
  var dirUrl = GetDirectoryUrl(dir);
  var openAttr = open ? " open" : string.Empty;
  var indentClass = depth == 0 ? string.Empty : $" class=\"indent{depth}\"";
  sb.Append("<details").Append(openAttr).AppendLine(">");
  sb.Append("  <summary").Append(indentClass).Append("><strong><a href=\"").Append(dirUrl).Append("\">").Append(dir.Name).Append("/</a></strong></summary>").AppendLine();
  foreach (var sub in dir.Directories.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
    RenderDirectory(sub, sb, depth + 1, open:false);
  foreach (var file in dir.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
  {
    var ts = file.LastModified?.ToString("yyyy-MM-dd HH:mm") ?? "?";
    sb.Append("  <div class=\"indent").Append(depth + 1).Append("\"><a href=\"")
      .Append(file.Url).Append("\">").Append(file.Name).Append("</a> (")
      .Append(file.SizeDisplay).Append(' ').Append(ts).AppendLine(")</div>");
  }
  sb.AppendLine("</details>");
}

static string GetDirectoryUrl(DirNode node)
{
  if (node.Parent == null) return RootUrl; // synthetic root
  var stack = new Stack<string>();
  var cur = node;
  while (cur.Parent != null)
  { stack.Push(cur.Name); cur = cur.Parent; }
  return RootUrl + string.Join('/', stack) + '/';
}

record CatalogRoot
{
  public required string Root { get; init; }
  public DateTime GeneratedUtc { get; init; }
  public int FileCount { get; init; }
  public required List<CatalogEntry> Files { get; init; }
}

record CatalogEntry
{
  public required string Name { get; init; }
  public required string Url { get; init; }
  public required string RelativePath { get; init; }
  public DateTime? LastModified { get; init; }
  public string? SizeDisplay { get; init; }
  public long? SizeBytes { get; init; }
  public DateTime CollectedUtc { get; init; }
}

sealed class DirNode
{
  public string Name { get; }
  public DirNode? Parent { get; }
  public Dictionary<string, DirNode> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
  public List<CatalogEntry> Files { get; } = new();

  public DirNode(string name, DirNode? parent) { Name = name; Parent = parent; }

  public void AddFile(string[] parts, int index, CatalogEntry entry)
  {
    if (index == parts.Length - 1) { Files.Add(entry); return; }
    var dirName = parts[index];
    if (!Directories.TryGetValue(dirName, out var child))
    {
      child = new DirNode(dirName, this);
      Directories.Add(dirName, child);
    }
    child.AddFile(parts, index + 1, entry);
  }

  public int GetMaxDepth(int depth = 0)
  {
    var max = depth;
    foreach (var d in Directories.Values)
    {
      var sub = d.GetMaxDepth(depth + 1);
      if (sub > max) max = sub;
    }
    return max;
  }
}
