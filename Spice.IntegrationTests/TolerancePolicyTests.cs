using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using Spice.Core;

namespace Spice.IntegrationTests;

public class TolerancePolicyTests
{
  [Fact]
  public void TolerancePolicy_ModernHighFidelity()
  {
    var t = TolerancePolicy.Get(440, hasAuConstant: true);
    t.Strict.ShouldBeTrue();
    t.PositionAu.ShouldBe(2e-14);
    t.VelocityAuPerDay.ShouldBe(3e-17);
  }

  [Fact]
  public void TolerancePolicy_Legacy()
  {
    var t = TolerancePolicy.Get(410, hasAuConstant: true);
    t.Strict.ShouldBeFalse();
    t.PositionAu.ShouldBe(6e-14);
    t.VelocityAuPerDay.ShouldBe(5e-14);
  }

  [Fact]
  public void TolerancePolicy_Problematic421()
  {
    var t = TolerancePolicy.Get(421, hasAuConstant: true);
    t.Strict.ShouldBeFalse();
    t.PositionAu.ShouldBe(2e-12);
    t.VelocityAuPerDay.ShouldBe(5e-15);
  }

  [Fact]
  public void TolerancePolicy_Fallback_NoAu()
  {
    var t = TolerancePolicy.Get(500, hasAuConstant: false);
    t.Strict.ShouldBeFalse();
    t.PositionAu.ShouldBe(5e-8);
    t.VelocityAuPerDay.ShouldBe(1e-10);
  }

  [Fact]
  public void BodyMapping_Validation()
  {
    var path = Path.Combine(AppContext.BaseDirectory, "TestData", "BodyMapping.json");
    File.Exists(path).ShouldBeTrue();
    var json = File.ReadAllText(path);
    var doc = JsonDocument.Parse(json).RootElement;
    doc.ValueKind.ShouldBe(JsonValueKind.Array);
    var testpoSet = new HashSet<int>();
    bool hasEarth = false, hasMoon = false;
    foreach (var el in doc.EnumerateArray())
    {
      int testpo = el.GetProperty("testpo").GetInt32();
      int naif = el.GetProperty("naif").GetInt32();
      testpoSet.Add(testpo).ShouldBeTrue("Duplicate testpo code " + testpo);
      if (testpo == 3 && naif == 399) hasEarth = true;
      if (testpo == 10 && naif == 301) hasMoon = true;
    }
    hasEarth.ShouldBeTrue();
    hasMoon.ShouldBeTrue();
  }

  [Fact]
  public void StatsJson_Schema_IfPresent()
  {
    var cacheRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "cache");
    if (!Directory.Exists(cacheRoot)) return; // nothing to validate yet
    foreach (var file in Directory.EnumerateFiles(cacheRoot, "comparison_stats.*.json", SearchOption.AllDirectories))
    {
      var text = File.ReadAllText(file);
      var root = JsonDocument.Parse(text).RootElement;
      root.TryGetProperty("ephemeris", out _).ShouldBeTrue(file);
      root.TryGetProperty("samples", out _).ShouldBeTrue(file);
      root.TryGetProperty("strictMode", out _).ShouldBeTrue(file);
      root.TryGetProperty("positionMaxAu", out _).ShouldBeTrue(file);
      root.TryGetProperty("positionMeanAu", out _).ShouldBeTrue(file);
      root.TryGetProperty("velocityMaxAuDay", out _).ShouldBeTrue(file);
      root.TryGetProperty("velocityMeanAuDay", out _).ShouldBeTrue(file);
      root.TryGetProperty("hasAuConstant", out _).ShouldBeTrue(file);
      root.TryGetProperty("generatedUtc", out _).ShouldBeTrue(file);
    }
  }

  [Fact]
  public void NoToleranceLiteralsOutsidePolicy()
  {
    // Patterns representing the numeric literals defining tiers.
    string[] literals = { "2e-14", "3e-17", "6e-14", "5e-14", "2e-12", "5e-15", "5e-8", "1e-10" };
    var root = SolutionRoot();
    var offending = new List<string>();
    foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
    {
      var name = Path.GetFileName(file);
      if (name.Equals("TolerancePolicy.cs", StringComparison.OrdinalIgnoreCase)) continue;
      if (file.Contains(Path.Combine("bin", String.Empty)) || file.Contains(Path.Combine("obj", String.Empty))) continue;
      var content = File.ReadAllText(file);
      foreach (var lit in literals)
      {
        if (content.Contains(lit, StringComparison.Ordinal) && !content.Contains("TolerancePolicy"))
        {
          offending.Add($"{file}:{lit}");
        }
      }
    }
    offending.ShouldBeEmpty("Tolerance literals must only appear inside TolerancePolicy.cs\n" + string.Join('\n', offending));
  }

  static string SolutionRoot()
  {
    // Traverse up until we see a solution file or git directory as heuristic.
    var dir = AppContext.BaseDirectory;
    for (int i = 0; i < 6 && dir is not null; i++)
    {
      if (File.Exists(Path.Combine(dir, "SpiceNet.sln")) || Directory.Exists(Path.Combine(dir, ".git")))
        return dir;
      dir = Directory.GetParent(dir)?.FullName ?? dir;
    }
    return AppContext.BaseDirectory; // fallback
  }
}
