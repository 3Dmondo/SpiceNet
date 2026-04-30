using Spice.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed record KernelInspectionOptions(
  string? CatalogPath,
  bool InspectKernels,
  string? CandidateSet,
  bool IncludeSmallBodies,
  double SmallBodyMaxSizeMb,
  string KernelCacheRoot,
  double MaxExplicitDownloadSizeMb,
  bool ForceDownload,
  bool InspectFallbackKernels,
  IReadOnlyList<string> ExtraCandidateNames,
  string OutputPath);

internal sealed record KernelCandidatePlan(CatalogEntry Entry, string Role, bool ShouldInspect, string? SkipReason);

internal static class KernelInspectionSelector
{
  internal const string Milestone11CandidateSet = "milestone-11-major-moons";
  static readonly string[] ExplicitPrimaryNames =
  [
    "de440s.bsp", "de440s_plus_MarsPC.bsp", "jup345.bsp", "jup380s.bsp", "jup346.bsp",
    "sat428.bsp", "sat450.bsp", "sat456.bsp", "ura117.bsp", "ura116.bsp", "ura112.bsp",
    "nep100.bsp", "nep105.bsp", "nep102.bsp", "mar097.bsp"
  ];
  static readonly string[] FallbackNames = ["jup344.bsp", "sat143.bsp", "mar099.bsp"];

  internal static KernelCandidatePlan[] Select(CatalogRoot catalog, KernelInspectionOptions options)
  {
    var byName = catalog.Files
      .GroupBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
    var plansByPath = new Dictionary<string, KernelCandidatePlan>(StringComparer.OrdinalIgnoreCase);

    if (string.Equals(options.CandidateSet, Milestone11CandidateSet, StringComparison.OrdinalIgnoreCase)) {
      foreach (var name in ExplicitPrimaryNames) {
        if (byName.TryGetValue(name, out var entry))
          plansByPath[entry.RelativePath] = BuildPlan(entry, "primary", options.MaxExplicitDownloadSizeMb, "explicit candidate size cap");
      }
      foreach (var name in FallbackNames) {
        if (byName.TryGetValue(name, out var entry)) {
          plansByPath[entry.RelativePath] = options.InspectFallbackKernels
            ? BuildPlan(entry, "fallback", options.MaxExplicitDownloadSizeMb, "fallback candidate size cap")
            : new KernelCandidatePlan(entry, "fallback", false, "fallback-only; inspect after compact candidates fail coverage");
        }
      }
    }

    foreach (var name in options.ExtraCandidateNames) {
      if (byName.TryGetValue(name, out var entry))
        plansByPath[entry.RelativePath] = BuildPlan(entry, "extra", options.MaxExplicitDownloadSizeMb, "extra candidate size cap");
    }

    if (options.IncludeSmallBodies) {
      foreach (var entry in catalog.Files.Where(static file => file.RelativePath.StartsWith("small_bodies/", StringComparison.OrdinalIgnoreCase))) {
        if (!plansByPath.ContainsKey(entry.RelativePath))
          plansByPath[entry.RelativePath] = BuildPlan(entry, "small-body", options.SmallBodyMaxSizeMb, "small-body size cap");
      }
    }

    return plansByPath.Values.OrderBy(static plan => plan.Entry.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
  }

  static KernelCandidatePlan BuildPlan(CatalogEntry entry, string role, double maxSizeMb, string capLabel)
  {
    var sizeLimitBytes = (long)(maxSizeMb * 1024 * 1024);
    if (entry.SizeBytes is long sizeBytes && sizeBytes > sizeLimitBytes)
      return new KernelCandidatePlan(entry, role, false, $"size {entry.SizeDisplay} exceeds {capLabel} {maxSizeMb:0.#} MB");
    return new KernelCandidatePlan(entry, role, true, null);
  }
}

internal static class KernelInspectionWorkflow
{
  static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  internal static async Task<KernelInspectionReport> RunAsync(CatalogRoot catalog, KernelInspectionOptions options, HttpClient http, CancellationToken cancellationToken = default)
  {
    var generatedAtUtc = DateTime.UtcNow;
    var targetWindow = KernelInspectionTargetWindow.FromYears(1950, 2050);
    var kernelReports = new List<KernelInspectionKernelReport>();
    var skippedReports = new List<KernelInspectionSkippedKernel>();

    foreach (var plan in KernelInspectionSelector.Select(catalog, options)) {
      if (!plan.ShouldInspect) {
        skippedReports.Add(KernelInspectionSkippedKernel.FromPlan(plan));
        continue;
      }

      var cachePath = GetCachePath(options.KernelCacheRoot, plan.Entry.RelativePath);
      await EnsureDownloadedAsync(http, plan.Entry, cachePath, options.ForceDownload, cancellationToken);
      try {
        var inspectionReport = KernelSegmentInspector.Inspect(cachePath, plan, generatedAtUtc);
        kernelReports.Add(inspectionReport);
      }
      catch (Exception ex) {
        Console.WriteLine($"Failed to inspect kernel {plan.Entry.RelativePath} at cache path {cachePath}; skipping: {ex.Message}");
      }
    }

    var report = new KernelInspectionReport(
      SchemaVersion: 1,
      GeneratedUtc: generatedAtUtc,
      CatalogGeneratedUtc: catalog.GeneratedUtc,
      CatalogRoot: catalog.Root,
      CandidateSet: options.CandidateSet,
      KernelCacheRoot: options.KernelCacheRoot,
      ApproximateUtcNote: "Approximate UTC intervals are computed by adding TDB seconds to the J2000 UTC anchor used by Spice.WebDataGenerator; raw TDB seconds are the source of truth. Approximate UTC fields are null when the TDB span is outside DateTimeOffset range.",
      TargetWindow: targetWindow,
      Kernels: kernelReports.OrderBy(static kernel => kernel.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
      SkippedKernels: skippedReports.OrderBy(static kernel => kernel.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
      TargetBodies: BuildTargetReports(kernelReports, targetWindow),
      Subsystems: BuildSubsystemReports(kernelReports, targetWindow));

    Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath) ?? ".");
    await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
    return report;
  }

  static string GetCachePath(string cacheRoot, string relativePath)
    => Path.Combine(new[] { cacheRoot }.Concat(relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToArray());

  static async Task EnsureDownloadedAsync(HttpClient http, CatalogEntry entry, string cachePath, bool forceDownload, CancellationToken cancellationToken)
  {
    if (!forceDownload && File.Exists(cachePath))
      return;

    Directory.CreateDirectory(Path.GetDirectoryName(cachePath) ?? ".");
    var tempPath = cachePath + ".part";
    using var response = await http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    response.EnsureSuccessStatusCode();
    await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
    await using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
      await stream.CopyToAsync(file, cancellationToken);

    if (File.Exists(cachePath))
      File.Delete(cachePath);
    File.Move(tempPath, cachePath);
  }

  static KernelInspectionTargetBodyReport[] BuildTargetReports(IReadOnlyList<KernelInspectionKernelReport> kernels, KernelInspectionTargetWindow targetWindow)
    => kernels
      .SelectMany(kernel => kernel.Segments.Select(segment => new { kernel, segment }))
      .GroupBy(item => item.segment.TargetBodyId)
      .OrderBy(static group => group.Key)
      .Select(group =>
      {
        var kernelCoverages = group.GroupBy(item => item.kernel.RelativePath).Select(kernelGroup =>
        {
          var starts = kernelGroup.Select(static item => item.segment.StartTdbSeconds).ToArray();
          var stops = kernelGroup.Select(static item => item.segment.StopTdbSeconds).ToArray();
          return new KernelInspectionTargetKernelCoverage(
            kernelGroup.Key,
            kernelGroup.Select(static item => item.segment.CenterBodyId).Distinct().Order().ToArray(),
            kernelGroup.Select(static item => item.segment.DataType).Distinct().Order().ToArray(),
            starts.Min(), stops.Max(),
            KernelSegmentInspector.TryApproximateUtcFromTdbSeconds(starts.Min()),
            KernelSegmentInspector.TryApproximateUtcFromTdbSeconds(stops.Max()),
            starts.Min() <= targetWindow.StartTdbSeconds && stops.Max() >= targetWindow.StopTdbSeconds);
        }).OrderBy(static coverage => coverage.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var commonStart = kernelCoverages.Length == 0 ? 0 : kernelCoverages.Max(static coverage => coverage.StartTdbSeconds);
        var commonStop = kernelCoverages.Length == 0 ? 0 : kernelCoverages.Min(static coverage => coverage.StopTdbSeconds);
        return new KernelInspectionTargetBodyReport(
          group.Key,
          kernelCoverages.Select(static coverage => coverage.RelativePath).ToArray(),
          group.Select(static item => item.segment.CenterBodyId).Distinct().Order().ToArray(),
          group.Select(static item => item.segment.DataType).Distinct().Order().ToArray(),
          commonStart, commonStop,
          KernelSegmentInspector.TryApproximateUtcFromTdbSeconds(commonStart),
          KernelSegmentInspector.TryApproximateUtcFromTdbSeconds(commonStop),
          kernelCoverages.Any(static coverage => coverage.CoversTargetWindow),
          kernelCoverages);
      })
      .ToArray();

  static KernelInspectionSubsystemReport[] BuildSubsystemReports(IReadOnlyList<KernelInspectionKernelReport> kernels, KernelInspectionTargetWindow targetWindow)
    => KernelInspectionSubsystem.RequiredSubsystems.Select(subsystem => BuildSubsystemReport(subsystem, kernels, targetWindow)).ToArray();

  static KernelInspectionSubsystemReport BuildSubsystemReport(KernelInspectionSubsystem subsystem, IReadOnlyList<KernelInspectionKernelReport> kernels, KernelInspectionTargetWindow targetWindow)
  {
    var candidates = kernels
      .Where(kernel => subsystem.CandidateNamePrefixes.Any(prefix => Path.GetFileNameWithoutExtension(kernel.Name).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
      .Select(kernel =>
      {
        var covered = subsystem.RequiredTargetBodyIds.Where(bodyId => KernelCoversTarget(kernel, bodyId, targetWindow)).Order().ToArray();
        return new KernelInspectionSubsystemCandidate(
          kernel.RelativePath,
          kernel.SizeBytes,
          covered,
          subsystem.RequiredTargetBodyIds.Except(covered).Order().ToArray(),
          covered.Length == subsystem.RequiredTargetBodyIds.Length);
      })
      .OrderBy(static candidate => candidate.SizeBytes ?? long.MaxValue)
      .ThenBy(static candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToArray();
    var selected = candidates.FirstOrDefault(static candidate => candidate.CoversAllRequiredTargets);
    return new KernelInspectionSubsystemReport(
      subsystem.Id,
      subsystem.RequiredTargetBodyIds,
      selected?.RelativePath,
      selected is null
        ? "No inspected compact candidate covers every required target body for the target window."
        : "Smallest inspected candidate covering every required target body for the target window.",
      candidates);
  }

  internal static bool KernelCoversTarget(KernelInspectionKernelReport kernel, int targetBodyId, KernelInspectionTargetWindow targetWindow)
  {
    const double coverageToleranceSeconds = 1;
    var segments = kernel.Segments
      .Where(segment => segment.TargetBodyId == targetBodyId && segment.StopTdbSeconds >= targetWindow.StartTdbSeconds && segment.StartTdbSeconds <= targetWindow.StopTdbSeconds)
      .OrderBy(static segment => segment.StartTdbSeconds)
      .ToArray();
    if (segments.Length == 0 || segments[0].StartTdbSeconds > targetWindow.StartTdbSeconds + coverageToleranceSeconds)
      return false;

    var coveredThrough = segments[0].StopTdbSeconds;
    foreach (var segment in segments.Skip(1)) {
      if (segment.StartTdbSeconds > coveredThrough + coverageToleranceSeconds)
        return false;
      coveredThrough = Math.Max(coveredThrough, segment.StopTdbSeconds);
      if (coveredThrough >= targetWindow.StopTdbSeconds - coverageToleranceSeconds)
        return true;
    }

    return coveredThrough >= targetWindow.StopTdbSeconds - coverageToleranceSeconds;
  }
}

internal static class KernelSegmentInspector
{
  static readonly DateTimeOffset ApproximateJ2000Utc = new(2000, 1, 1, 11, 58, 55, 816, TimeSpan.Zero);

  internal static KernelInspectionKernelReport Inspect(string cachePath, KernelCandidatePlan plan, DateTime inspectedUtc)
  {
    using var stream = File.OpenRead(cachePath);
    using var reader = FullDafReader.Open(stream, leaveOpen: true);
    var segments = reader.EnumerateSegments()
      .Where(static segment => segment.Dc.Length >= 2 && segment.Ic.Length >= 6)
      .Select(static segment => new KernelInspectionSegment(
        segment.Name, segment.Ic[0], segment.Ic[1], segment.Ic[2], segment.Ic[3], segment.Dc[0], segment.Dc[1], segment.InitialAddress, segment.FinalAddress))
      .ToArray();
    var minStart = segments.Length == 0 ? (double?)null : segments.Min(static segment => segment.StartTdbSeconds);
    var maxStop = segments.Length == 0 ? (double?)null : segments.Max(static segment => segment.StopTdbSeconds);
    return new KernelInspectionKernelReport(
      plan.Entry.Name, plan.Entry.RelativePath, plan.Entry.Url, plan.Role, plan.Entry.SizeDisplay, plan.Entry.SizeBytes,
      cachePath, ComputeSha256(cachePath), inspectedUtc, new FileInfo(cachePath).Length, segments.Length,
      segments.Select(static segment => segment.DataType).Distinct().Order().ToArray(),
      segments.Select(static segment => segment.TargetBodyId).Distinct().Order().ToArray(),
      segments.Select(static segment => segment.CenterBodyId).Distinct().Order().ToArray(),
      segments.Select(static segment => segment.FrameId).Distinct().Order().ToArray(),
      minStart, maxStop,
      minStart is null ? null : TryApproximateUtcFromTdbSeconds(minStart.Value),
      maxStop is null ? null : TryApproximateUtcFromTdbSeconds(maxStop.Value),
      segments);
  }

  internal static DateTimeOffset? TryApproximateUtcFromTdbSeconds(double tdbSeconds)
  {
    try {
      return ApproximateJ2000Utc.AddSeconds(tdbSeconds);
    }
    catch (ArgumentOutOfRangeException) {
      return null;
    }
  }

  static string ComputeSha256(string path)
  {
    using var stream = File.OpenRead(path);
    using var sha = SHA256.Create();
    return Convert.ToHexString(sha.ComputeHash(stream));
  }
}

internal sealed record KernelInspectionTargetWindow(int StartYear, int EndYear, double StartTdbSeconds, double StopTdbSeconds, DateTimeOffset? ApproximateStartUtc, DateTimeOffset? ApproximateStopUtc)
{
  static readonly DateTimeOffset ApproximateJ2000Utc = new(2000, 1, 1, 11, 58, 55, 816, TimeSpan.Zero);
  internal static KernelInspectionTargetWindow FromYears(int startYear, int endYear)
  {
    var startUtc = new DateTimeOffset(startYear, 1, 1, 12, 0, 0, TimeSpan.Zero);
    var stopUtc = new DateTimeOffset(endYear, 1, 1, 12, 0, 0, TimeSpan.Zero);
    return new KernelInspectionTargetWindow(startYear, endYear, (startUtc - ApproximateJ2000Utc).TotalSeconds, (stopUtc - ApproximateJ2000Utc).TotalSeconds, startUtc, stopUtc);
  }
}

internal sealed record KernelInspectionSubsystem(string Id, int[] RequiredTargetBodyIds, string[] CandidateNamePrefixes)
{
  internal static readonly KernelInspectionSubsystem[] RequiredSubsystems =
  [
    new("planets", [10, 199, 299, 399, 301, 4, 5, 6, 7, 8], ["de440s", "de440s_plus_MarsPC"]),
    new("mars-system", [401, 402], ["mar097", "mar099"]),
    new("jupiter-system", [501, 502, 503, 504], ["jup345", "jup380s", "jup346", "jup344", "jup365", "jup387"]),
    new("saturn-system", [601, 602, 603, 604, 605, 606, 608], ["sat428", "sat450", "sat456", "sat143", "sat427", "sat440", "sat441"]),
    new("uranus-system", [701, 702, 703, 704, 705], ["ura117", "ura116", "ura112", "ura111", "ura155", "ura158", "ura159", "ura160", "ura161", "ura167", "ura178"]),
    new("neptune-system", [801], ["nep100", "nep105", "nep102", "nep101", "nep103", "nep104", "nep090", "nep096", "nep097", "Triton.nep097"])
  ];
}

internal sealed record KernelInspectionReport(int SchemaVersion, DateTime GeneratedUtc, DateTime CatalogGeneratedUtc, string CatalogRoot, string? CandidateSet, string KernelCacheRoot, string ApproximateUtcNote, KernelInspectionTargetWindow TargetWindow, KernelInspectionKernelReport[] Kernels, KernelInspectionSkippedKernel[] SkippedKernels, KernelInspectionTargetBodyReport[] TargetBodies, KernelInspectionSubsystemReport[] Subsystems);
internal sealed record KernelInspectionKernelReport(string Name, string RelativePath, string Url, string Role, string? SizeDisplay, long? SizeBytes, string CachePath, string Sha256, DateTime InspectedUtc, long ActualByteLength, int SegmentCount, int[] DataTypes, int[] TargetBodyIds, int[] CenterBodyIds, int[] FrameIds, double? MinStartTdbSeconds, double? MaxStopTdbSeconds, DateTimeOffset? ApproximateStartUtc, DateTimeOffset? ApproximateStopUtc, KernelInspectionSegment[] Segments);
internal sealed record KernelInspectionSegment(string Name, int TargetBodyId, int CenterBodyId, int FrameId, int DataType, double StartTdbSeconds, double StopTdbSeconds, int InitialAddress, int FinalAddress);
internal sealed record KernelInspectionSkippedKernel(string Name, string RelativePath, string Url, string Role, string? SizeDisplay, long? SizeBytes, string Reason)
{
  internal static KernelInspectionSkippedKernel FromPlan(KernelCandidatePlan plan)
    => new(plan.Entry.Name, plan.Entry.RelativePath, plan.Entry.Url, plan.Role, plan.Entry.SizeDisplay, plan.Entry.SizeBytes, plan.SkipReason ?? "not selected for inspection");
}
internal sealed record KernelInspectionTargetBodyReport(int TargetBodyId, string[] KernelRelativePaths, int[] CenterBodyIds, int[] DataTypes, double CommonStartTdbSeconds, double CommonStopTdbSeconds, DateTimeOffset? ApproximateCommonStartUtc, DateTimeOffset? ApproximateCommonStopUtc, bool HasAnyKernelCoveringTargetWindow, KernelInspectionTargetKernelCoverage[] Kernels);
internal sealed record KernelInspectionTargetKernelCoverage(string RelativePath, int[] CenterBodyIds, int[] DataTypes, double StartTdbSeconds, double StopTdbSeconds, DateTimeOffset? ApproximateStartUtc, DateTimeOffset? ApproximateStopUtc, bool CoversTargetWindow);
internal sealed record KernelInspectionSubsystemReport(string Id, int[] RequiredTargetBodyIds, string? SelectedKernelRelativePath, string SelectionReason, KernelInspectionSubsystemCandidate[] Candidates);
internal sealed record KernelInspectionSubsystemCandidate(string RelativePath, long? SizeBytes, int[] CoveredTargetBodyIds, int[] MissingTargetBodyIds, bool CoversAllRequiredTargets);
