using Shouldly;

namespace Spice.Tests;

public sealed class KernelInspectionTests
{
  [Fact]
  public void Candidate_Filter_Includes_MajorMoon_And_CappedSmallBody_Kernels() {
    var catalog = BuildCatalog(
      Entry("de440s.bsp", "planets/bsp/de440s.bsp", 31),
      Entry("jup345.bsp", "satellites/bsp/jup345.bsp", 43),
      Entry("mar097.bsp", "satellites/bsp/mar097.bsp", 439),
      Entry("sb-101955-110.bsp", "small_bodies/orex/asteroid/sb-101955-110.bsp", 0.1),
      Entry("sb441-n16.bsp", "small_bodies/asteroids_de441/sb441-n16.bsp", 615.8));

    var plans = KernelInspectionSelector.Select(catalog, BuildOptions(includeSmallBodies: true));

    plans.Single(plan => plan.Entry.Name == "de440s.bsp").ShouldInspect.ShouldBeTrue();
    plans.Single(plan => plan.Entry.Name == "jup345.bsp").ShouldInspect.ShouldBeTrue();
    plans.Single(plan => plan.Entry.Name == "mar097.bsp").ShouldInspect.ShouldBeTrue();
    plans.Single(plan => plan.Entry.Name == "sb-101955-110.bsp").ShouldInspect.ShouldBeTrue();

    var skippedSmallBody = plans.Single(plan => plan.Entry.Name == "sb441-n16.bsp");
    skippedSmallBody.ShouldInspect.ShouldBeFalse();
    skippedSmallBody.SkipReason.ShouldNotBeNull();
    skippedSmallBody.SkipReason.ShouldContain("small-body size cap");
  }

  [Fact]
  public void Candidate_Filter_Records_Fallback_Kernels_As_Skipped() {
    var catalog = BuildCatalog(
      Entry("jup344.bsp", "satellites/bsp/jup344.bsp", 289.4),
      Entry("sat143.bsp", "satellites/bsp/sat143.bsp", 137),
      Entry("mar099.bsp", "satellites/bsp/mar099.bsp", 1100));

    var plans = KernelInspectionSelector.Select(catalog, BuildOptions(includeSmallBodies: false));

    plans.Length.ShouldBe(3);
    foreach (var plan in plans) {
      plan.ShouldInspect.ShouldBeFalse();
      plan.SkipReason.ShouldNotBeNull();
      plan.SkipReason.ShouldContain("fallback-only");
    }
  }

  [Fact]
  public void Segment_Inspection_Reports_Unsupported_DataTypes_And_Preserves_TdbValues() {
    var path = Path.Combine(Path.GetTempPath(), $"spice-test-{Guid.NewGuid():N}.bsp");
    try {
      File.WriteAllBytes(path, BuildMinimalDaf(dataType: 99, start: 123, stop: 456));
      var plan = new KernelCandidatePlan(Entry("custom.bsp", "small_bodies/custom.bsp", 1), "small-body", true, null);

      var report = KernelSegmentInspector.Inspect(path, plan, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

      report.SegmentCount.ShouldBe(1);
      report.DataTypes.ShouldBe([99]);
      report.TargetBodyIds.ShouldBe([12345]);
      report.MinStartTdbSeconds.ShouldBe(123);
      report.MaxStopTdbSeconds.ShouldBe(456);
      report.Segments[0].StartTdbSeconds.ShouldBe(123);
      report.Segments[0].StopTdbSeconds.ShouldBe(456);
    }
    finally {
      File.Delete(path);
    }
  }


  [Fact]
  public void Coverage_Check_Allows_Contiguous_Split_Target_Segments() {
    var kernel = new KernelInspectionKernelReport(
      Name: "split.bsp",
      RelativePath: "satellites/bsp/split.bsp",
      Url: "https://example.test/split.bsp",
      Role: "extra",
      SizeDisplay: "1M",
      SizeBytes: 1024,
      CachePath: "split.bsp",
      Sha256: "ABC",
      InspectedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
      ActualByteLength: 1024,
      SegmentCount: 2,
      DataTypes: [2],
      TargetBodyIds: [801],
      CenterBodyIds: [8],
      FrameIds: [1],
      MinStartTdbSeconds: -2_000,
      MaxStopTdbSeconds: 2_000,
      ApproximateStartUtc: null,
      ApproximateStopUtc: null,
      Segments:
      [
        new KernelInspectionSegment("FIRST", 801, 8, 1, 2, -2_000, 0, 1, 2),
        new KernelInspectionSegment("SECOND", 801, 8, 1, 2, 0, 2_000, 3, 4)
      ]);
    var targetWindow = new KernelInspectionTargetWindow(1950, 2050, -1_000, 1_000, null, null);

    KernelInspectionWorkflow.KernelCoversTarget(kernel, 801, targetWindow).ShouldBeTrue();
  }

  static KernelInspectionOptions BuildOptions(bool includeSmallBodies)
    => new(
      CatalogPath: null,
      InspectKernels: true,
      CandidateSet: KernelInspectionSelector.Milestone11CandidateSet,
      IncludeSmallBodies: includeSmallBodies,
      SmallBodyMaxSizeMb: 75,
      KernelCacheRoot: "cache",
      MaxExplicitDownloadSizeMb: 500,
      ForceDownload: false,
      InspectFallbackKernels: false,
      ExtraCandidateNames: [],
      OutputPath: "kernel_inspection.json");

  static CatalogRoot BuildCatalog(params CatalogEntry[] entries)
    => new("https://ssd.jpl.nasa.gov/ftp/eph/", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), entries.Length, entries.ToList());

  static CatalogEntry Entry(string name, string relativePath, double sizeMb)
  {
    var sizeBytes = (long)(sizeMb * 1024 * 1024);
    return new CatalogEntry(
      name,
      $"https://ssd.jpl.nasa.gov/ftp/eph/{relativePath}",
      relativePath,
      LastModified: null,
      SizeDisplay: $"{sizeMb:0.#}M",
      SizeBytes: sizeBytes,
      CollectedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
  }

  static byte[] BuildMinimalDaf(int dataType, double start, double stop) {
    byte[] file = new byte[1024 * 3];
    WriteAscii(file, 0, "DAF/SPK ");
    WriteInt(file, 8, 2);
    WriteInt(file, 12, 6);
    WriteAscii(file, 16, "TEST DAF".PadRight(60));
    WriteInt(file, 76, 2);
    WriteInt(file, 80, 2);

    int summaryBase = 1024;
    WriteInt(file, summaryBase + 0, 0);
    WriteInt(file, summaryBase + 8, 0);
    WriteInt(file, summaryBase + 16, 1);
    int wordIndex = 3;
    WriteDouble(file, summaryBase + wordIndex++ * 8, start);
    WriteDouble(file, summaryBase + wordIndex++ * 8, stop);
    WritePackedInts(file, summaryBase + wordIndex++ * 8, 12345, 0);
    WritePackedInts(file, summaryBase + wordIndex++ * 8, 1, dataType);
    WritePackedInts(file, summaryBase + wordIndex++ * 8, 385, 400);

    WriteAscii(file, 2048, "UNSUPPORTED".PadRight(40));
    return file;
  }

  static void WriteAscii(byte[] buffer, int offset, string text) {
    var bytes = System.Text.Encoding.ASCII.GetBytes(text);
    Array.Copy(bytes, 0, buffer, offset, bytes.Length);
  }

  static void WriteInt(byte[] buffer, int offset, int value)
    => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), value);

  static void WriteDouble(byte[] buffer, int offset, double value)
    => System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(value));

  static void WritePackedInts(byte[] buffer, int offset, int a, int b) {
    WriteInt(buffer, offset, a);
    WriteInt(buffer, offset + 4, b);
  }
}
