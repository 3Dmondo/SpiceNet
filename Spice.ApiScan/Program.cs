using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

// Simple public API inventory tool (Phase 0 Prompt 26)
// Scans referenced runtime assemblies (Spice.*) excluding test/benchmark/scan utilities
// and emits a deterministic JSON description of the public surface.

var assemblies = AppDomain.CurrentDomain.GetAssemblies()
  .Where(a => a.GetName().Name is { } n && n.StartsWith("Spice.") &&
              !n.EndsWith("Tests") && !n.EndsWith("Benchmarks") && !n.EndsWith("ApiScan"))
  .ToList();

// Ensure referenced assemblies are loaded by touching a known type from each project if needed
void Touch<T>() { _ = typeof(T); }
// Touch known root types (best-effort; ignore if not present)
try { Touch<Spice.Core.Vector3d>(); } catch {};
try { Touch<Spice.IO.FullDafReader>(); } catch {};
try { Touch<Spice.Kernels.SpkKernel>(); } catch {};
try { Touch<Spice.Ephemeris.EphemerisService>(); } catch {};

assemblies = AppDomain.CurrentDomain.GetAssemblies()
  .Where(a => a.GetName().Name is { } n && n.StartsWith("Spice.") &&
              !n.EndsWith("Tests") && !n.EndsWith("Benchmarks") && !n.EndsWith("ApiScan"))
  .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
  .ToList();

var model = new List<AssemblyModel>();
foreach (var asm in assemblies)
{
  var asmModel = new AssemblyModel { Name = asm.GetName().Name! };
  var types = asm.GetTypes()
    .Where(t => (t.IsPublic || t.IsNestedPublic) && !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
    .OrderBy(t => t.Namespace).ThenBy(t => t.Name, StringComparer.Ordinal);

  foreach (var t in types)
  {
    var typeModel = new TypeModel
    {
      Namespace = t.Namespace ?? string.Empty,
      Name = t.Name,
      Kind = t switch
      {
        { IsInterface: true } => "interface",
        { IsEnum: true } => "enum",
        { IsValueType: true } => "struct",
        _ => "class"
      }
    };

    // Public instance + static members (declared only)
    var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    foreach (var m in t.GetMethods(flags))
    {
      if (m.IsSpecialName) continue; // skip property/event accessors & operators for simplicity
      typeModel.Members.Add($"method {Signature(m)}");
    }
    foreach (var p in t.GetProperties(flags))
    {
      typeModel.Members.Add($"property {p.PropertyType.Name} {p.Name} {(p.CanRead ? "get;" : string.Empty)}{(p.CanWrite ? " set;" : string.Empty)}");
    }
    foreach (var f in t.GetFields(flags))
    {
      typeModel.Members.Add($"field {f.FieldType.Name} {f.Name}");
    }
    foreach (var e in t.GetEvents(flags))
    {
      typeModel.Members.Add($"event {e.EventHandlerType?.Name} {e.Name}");
    }

    if (typeModel.Members.Count == 0)
      typeModel.Members.Add("(no public members declared)");

    asmModel.Types.Add(typeModel);
  }
  model.Add(asmModel);
}

var options = new JsonSerializerOptions
{
  WriteIndented = true,
  DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts");
Directory.CreateDirectory(outputDir);
var outputPath = Path.GetFullPath(Path.Combine(outputDir, "api-scan.json"));
File.WriteAllText(outputPath, JsonSerializer.Serialize(model, options));
Console.WriteLine($"Public API scan written to: {outputPath}");

static string Signature(MethodInfo m)
{
  var ps = m.GetParameters();
  var paramSig = string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}"));
  return $"{m.ReturnType.Name} {m.Name}({paramSig})";
}

sealed class AssemblyModel
{
  public string Name { get; set; } = string.Empty;
  public List<TypeModel> Types { get; } = new();
}

sealed class TypeModel
{
  public string Namespace { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Kind { get; set; } = string.Empty;
  public List<string> Members { get; } = new();
}
