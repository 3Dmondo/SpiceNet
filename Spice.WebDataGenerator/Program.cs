using Spice.Core;
using Spice.Ephemeris;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Spice.WebDataGenerator;

internal static class Program
{
  const int OutputSchemaVersion = 1;
  const string ChunkBoundaryTimeEncoding = "approximate_tdb_seconds_from_j2000";
  const string SampleTimeEncoding = "chunk_start_plus_body_cadence_days_with_terminal_chunk_end_sample";
  const string SampleValueLayout = "xyz_vxvyvz";
  const int SampleValueComponentsPerSample = 6;
  const string PositionUnits = "km";
  const string VelocityUnits = "km/s";
  const string InterpolationHint = "cubic_hermite_position_velocity";

  static readonly JsonSerializerOptions ReportJsonOptions = new()
  {
    WriteIndented = true
  };

  static readonly JsonSerializerOptions OutputJsonOptions = new()
  {
    WriteIndented = false
  };

  static int Main(string[] args)
  {
    if (args.Length == 0 || HasFlag(args, "--help", "-h")) {
      PrintHelp();
      return 0;
    }

    if (!TryParseOptions(args, out var options, out var error)) {
      Console.Error.WriteLine(error);
      Console.Error.WriteLine();
      PrintHelp();
      return 1;
    }

    Console.WriteLine("Spice.WebDataGenerator");
    Console.WriteLine($"SPK: {options.SpkPath}");
    Console.WriteLine($"LSK: {options.LskPath}");
    Console.WriteLine($"Output: {options.OutputPath}");
    Console.WriteLine($"Coverage: {options.StartYear} through {options.EndYear}");
    Console.WriteLine($"Chunk years: {options.ChunkYears}");
    Console.WriteLine($"Default sample days: {options.SampleDays}");
    Console.WriteLine($"Center body: {options.CenterBodyId}");
    Console.WriteLine($"Bodies: {string.Join(", ", options.BodyIds)}");
    if (options.BodyCadenceOverrides.Count > 0) {
      Console.WriteLine($"Body cadence overrides: {FormatBodyCadenceOverrides(options.BodyCadenceOverrides)}");
    }
    if (options.BenchmarkMercury) {
      Console.WriteLine($"Benchmark cadences: {string.Join(", ", options.BenchmarkCadences)}");
      Console.WriteLine($"Benchmark truth hours: {options.BenchmarkTruthHours}");
    }
    if (options.BenchmarkBodies) {
      Console.WriteLine($"Body benchmark cadences: {string.Join(", ", options.BenchmarkCadences)}");
      Console.WriteLine($"Body benchmark truth hours: {options.BenchmarkTruthHours}");
    }
    if (options.BenchmarkConfiguredCadence) {
      Console.WriteLine($"Configured cadence benchmark truth hours: {options.BenchmarkTruthHours}");
    }
    if (options.BenchmarkConfiguredChunkYears) {
      Console.WriteLine($"Configured chunk-year benchmark values: {string.Join(", ", options.BenchmarkChunkYears)}");
      Console.WriteLine($"Configured chunk-year benchmark truth hours: {options.BenchmarkTruthHours}");
    }
    Console.WriteLine();

    Directory.CreateDirectory(options.OutputPath);

    using var service = new EphemerisService();

    if (!string.IsNullOrWhiteSpace(options.LskPath)) {
      service.Load(options.LskPath);
    }

    service.Load(options.SpkPath);

    if (options.BenchmarkBodies) {
      var benchmark = RunBodyBenchmark(service, options);
      var benchmarkPath = Path.Combine(options.OutputPath, "body-benchmark.json");
      File.WriteAllText(benchmarkPath, JsonSerializer.Serialize(benchmark, ReportJsonOptions));

      Console.WriteLine($"Benchmark: {benchmarkPath}");
      foreach (var cadence in benchmark.Results) {
        Console.WriteLine(
          $"Cadence {cadence.SampleDays,3}d: raw {cadence.TotalOutputBytes,10} bytes, gzip {cadence.TotalGzipBytes,10} bytes");
      }
      return 0;
    }

    if (options.BenchmarkMercury) {
      var benchmark = RunMercuryBenchmark(service, options);
      var benchmarkPath = Path.Combine(options.OutputPath, "mercury-benchmark.json");
      File.WriteAllText(benchmarkPath, JsonSerializer.Serialize(benchmark, ReportJsonOptions));

      Console.WriteLine($"Benchmark: {benchmarkPath}");
      foreach (var result in benchmark.Results) {
        Console.WriteLine(
          $"Cadence {result.SampleDays,3}d: max {result.MaxPositionErrorKm,12:F0} km, mean {result.MeanPositionErrorKm,12:F0} km, raw {result.TotalOutputBytes,10} bytes, gzip {result.TotalGzipBytes,10} bytes");
      }
      return 0;
    }

    if (options.BenchmarkConfiguredCadence) {
      var benchmark = RunConfiguredCadenceBenchmark(service, options);
      var benchmarkPath = Path.Combine(options.OutputPath, "configured-cadence-benchmark.json");
      File.WriteAllText(benchmarkPath, JsonSerializer.Serialize(benchmark, ReportJsonOptions));

      Console.WriteLine($"Benchmark: {benchmarkPath}");
      Console.WriteLine($"Configured output: raw {benchmark.TotalOutputBytes} bytes, gzip {benchmark.TotalGzipBytes} bytes");
      foreach (var result in benchmark.BodyResults) {
        Console.WriteLine(
          $"Body {result.RequestedBodyName,-8} ({result.RequestedBodyId,3}) at {result.SampleDays,3}d: max {result.MaxPositionErrorKm,12:F0} km, mean {result.MeanPositionErrorKm,12:F0} km");
      }
      return 0;
    }

    if (options.BenchmarkConfiguredChunkYears) {
      var benchmark = RunConfiguredChunkYearBenchmark(service, options);
      var benchmarkPath = Path.Combine(options.OutputPath, "configured-chunk-year-benchmark.json");
      File.WriteAllText(benchmarkPath, JsonSerializer.Serialize(benchmark, ReportJsonOptions));

      Console.WriteLine($"Benchmark: {benchmarkPath}");
      foreach (var result in benchmark.Results) {
        Console.WriteLine(
          $"Chunk years {result.ChunkYears,3}: total raw {result.TotalOutputBytes,10} bytes, total gzip {result.TotalGzipBytes,10} bytes, largest chunk gzip {result.MaxChunkGzipBytes,10} bytes");
      }
      return 0;
    }

    var output = WriteGenerationOutput(service, options);

    Console.WriteLine($"Manifest: {output.ManifestPath}");
    foreach (var chunk in output.ChunkSummaries) {
      Console.WriteLine(
        $"Chunk: {Path.Combine(options.OutputPath, chunk.FileName)} ({chunk.SampleCount} samples/body max, {chunk.ByteLength} bytes raw, {chunk.GzipByteLength} bytes gzip)");
    }

    return 0;
  }

  static MercuryBenchmarkReport RunMercuryBenchmark(EphemerisService service, GeneratorOptions options)
  {
    var startUtc = CreateChunkBoundary(options.StartYear);
    var endUtc = CreateChunkBoundary(options.EndYear);
    var mercuryBodyId = 199;
    var mercurySourceBodyId = ResolveSourceBodyId(service, options.CenterBodyId, mercuryBodyId, startUtc);
    var results = new List<MercuryCadenceResult>();

    foreach (var sampleDays in options.BenchmarkCadences.Distinct().OrderByDescending(static value => value)) {
      var cadenceOutputPath = Path.Combine(options.OutputPath, $"sample-{sampleDays}d");
      Directory.CreateDirectory(cadenceOutputPath);

      var cadenceOptions = options with
      {
        OutputPath = cadenceOutputPath,
        SampleDays = sampleDays,
        BenchmarkMercury = false
      };

      var output = WriteGenerationOutput(service, cadenceOptions);
      var interpolation = BenchmarkMercuryInterpolation(
        service,
        options.CenterBodyId,
        mercuryBodyId,
        mercurySourceBodyId,
        startUtc,
        endUtc,
        sampleDays,
        options.BenchmarkTruthHours);

      results.Add(new MercuryCadenceResult(
        SampleDays: sampleDays,
        MercuryBodyId: mercuryBodyId,
        MercurySourceBodyId: mercurySourceBodyId,
        MercurySourceBodyName: ResolveBodyName(mercurySourceBodyId),
        MaxPositionErrorKm: interpolation.MaxPositionErrorKm,
        MeanPositionErrorKm: interpolation.MeanPositionErrorKm,
        TotalOutputBytes: output.TotalBytes,
        TotalGzipBytes: output.TotalGzipBytes,
        ChunkSummaries: output.ChunkSummaries.ToArray()));
    }

    return new MercuryBenchmarkReport(
      GeneratedAtUtc: DateTimeOffset.UtcNow,
      SpkPath: options.SpkPath,
      LskPath: options.LskPath,
      StartYear: options.StartYear,
      EndYear: options.EndYear,
      CenterBodyId: options.CenterBodyId,
      BenchmarkTruthHours: options.BenchmarkTruthHours,
      ApproximationNote: "UTC is mapped to TDB seconds past J2000 using a fixed J2000 UTC anchor and ignores leap seconds for this benchmark step.",
      Results: results.ToArray());
  }

  static ConfiguredCadenceBenchmarkReport RunConfiguredCadenceBenchmark(EphemerisService service, GeneratorOptions options)
  {
    var startUtc = CreateChunkBoundary(options.StartYear);
    var endUtc = CreateChunkBoundary(options.EndYear);
    var output = WriteGenerationOutput(service, options);
    var bodyResults = options.BodyIds
      .Select((bodyId) => BenchmarkBodyInterpolation(
        service,
        options.CenterBodyId,
        bodyId,
        startUtc,
        endUtc,
        ResolveSampleDays(options, bodyId),
        options.BenchmarkTruthHours))
      .OrderBy(static (result) => result.RequestedBodyId)
      .ToArray();

    return new ConfiguredCadenceBenchmarkReport(
      GeneratedAtUtc: DateTimeOffset.UtcNow,
      SpkPath: options.SpkPath,
      LskPath: options.LskPath,
      StartYear: options.StartYear,
      EndYear: options.EndYear,
      ChunkYears: options.ChunkYears,
      CenterBodyId: options.CenterBodyId,
      DefaultSampleDays: options.SampleDays,
      BodyCadences: BuildBodyCadenceSettings(options),
      BenchmarkTruthHours: options.BenchmarkTruthHours,
      ApproximationNote: "UTC is mapped to TDB seconds past J2000 using a fixed J2000 UTC anchor and ignores leap seconds for this benchmark step.",
      TotalOutputBytes: output.TotalBytes,
      TotalGzipBytes: output.TotalGzipBytes,
      ChunkSummaries: output.ChunkSummaries,
      BodyResults: bodyResults);
  }

  static ConfiguredChunkYearBenchmarkReport RunConfiguredChunkYearBenchmark(EphemerisService service, GeneratorOptions options)
  {
    var results = new List<ConfiguredChunkYearResult>();

    foreach (var chunkYears in options.BenchmarkChunkYears.Distinct().OrderByDescending(static value => value)) {
      var chunkOutputPath = Path.Combine(options.OutputPath, $"chunk-years-{chunkYears}");
      Directory.CreateDirectory(chunkOutputPath);

      var chunkOptions = options with
      {
        OutputPath = chunkOutputPath,
        ChunkYears = chunkYears,
        BenchmarkConfiguredChunkYears = false
      };

      var configured = RunConfiguredCadenceBenchmark(service, chunkOptions);
      results.Add(new ConfiguredChunkYearResult(
        ChunkYears: chunkYears,
        TotalOutputBytes: configured.TotalOutputBytes,
        TotalGzipBytes: configured.TotalGzipBytes,
        MaxChunkGzipBytes: configured.ChunkSummaries.Max(static (chunk) => chunk.GzipByteLength),
        ChunkSummaries: configured.ChunkSummaries,
        BodyResults: configured.BodyResults));
    }

    return new ConfiguredChunkYearBenchmarkReport(
      GeneratedAtUtc: DateTimeOffset.UtcNow,
      SpkPath: options.SpkPath,
      LskPath: options.LskPath,
      StartYear: options.StartYear,
      EndYear: options.EndYear,
      CenterBodyId: options.CenterBodyId,
      DefaultSampleDays: options.SampleDays,
      BodyCadences: BuildBodyCadenceSettings(options),
      BenchmarkChunkYears: options.BenchmarkChunkYears.ToArray(),
      BenchmarkTruthHours: options.BenchmarkTruthHours,
      ApproximationNote: "UTC is mapped to TDB seconds past J2000 using a fixed J2000 UTC anchor and ignores leap seconds for this benchmark step.",
      Results: results.ToArray());
  }

  static BodyBenchmarkReport RunBodyBenchmark(EphemerisService service, GeneratorOptions options)
  {
    var startUtc = CreateChunkBoundary(options.StartYear);
    var endUtc = CreateChunkBoundary(options.EndYear);
    var results = new List<BodyCadenceBenchmarkResult>();

    foreach (var sampleDays in options.BenchmarkCadences.Distinct().OrderByDescending(static value => value)) {
      var cadenceOutputPath = Path.Combine(options.OutputPath, $"sample-{sampleDays}d");
      Directory.CreateDirectory(cadenceOutputPath);

      var cadenceOptions = options with
      {
        OutputPath = cadenceOutputPath,
        SampleDays = sampleDays,
        BenchmarkMercury = false,
        BenchmarkBodies = false
      };

      var output = WriteGenerationOutput(service, cadenceOptions);
      var bodyResults = options.BodyIds
        .Select((bodyId) => BenchmarkBodyInterpolation(service, options.CenterBodyId, bodyId, startUtc, endUtc, sampleDays, options.BenchmarkTruthHours))
        .OrderBy(static (result) => result.RequestedBodyId)
        .ToArray();

      results.Add(new BodyCadenceBenchmarkResult(
        SampleDays: sampleDays,
        TotalOutputBytes: output.TotalBytes,
        TotalGzipBytes: output.TotalGzipBytes,
        ChunkSummaries: output.ChunkSummaries.ToArray(),
        BodyResults: bodyResults));
    }

    return new BodyBenchmarkReport(
      GeneratedAtUtc: DateTimeOffset.UtcNow,
      SpkPath: options.SpkPath,
      LskPath: options.LskPath,
      StartYear: options.StartYear,
      EndYear: options.EndYear,
      CenterBodyId: options.CenterBodyId,
      BenchmarkTruthHours: options.BenchmarkTruthHours,
      ApproximationNote: "UTC is mapped to TDB seconds past J2000 using a fixed J2000 UTC anchor and ignores leap seconds for this benchmark step.",
      Results: results.ToArray());
  }

  static MercuryInterpolationResult BenchmarkMercuryInterpolation(
    EphemerisService service,
    int centerBodyId,
    int requestedBodyId,
    int sourceBodyId,
    DateTimeOffset startUtc,
    DateTimeOffset endUtc,
    int sampleDays,
    int truthHours)
  {
    var sampleStates = BuildStateSamples(service, centerBodyId, sourceBodyId, startUtc, endUtc, sampleDays);
    var truthStep = TimeSpan.FromHours(truthHours);
    var segmentIndex = 0;
    double maxErrorKm = 0;
    double sumErrorKm = 0;
    var sampleCount = 0;

    for (var truthUtc = startUtc + truthStep; truthUtc < endUtc; truthUtc = truthUtc.Add(truthStep)) {
      while (segmentIndex < sampleStates.Count - 2 && truthUtc > sampleStates[segmentIndex + 1].Utc) {
        segmentIndex++;
      }

      var left = sampleStates[segmentIndex];
      var right = sampleStates[segmentIndex + 1];
      var truthState = BuildStateSample(service, centerBodyId, sourceBodyId, truthUtc);
      var interpolated = InterpolateHermite(left, right, truthUtc);
      var errorKm = (interpolated - truthState.State.PositionKm).Length();

      sumErrorKm += errorKm;
      sampleCount++;
      if (errorKm > maxErrorKm) {
        maxErrorKm = errorKm;
      }
    }

    return new MercuryInterpolationResult(
      RequestedBodyId: requestedBodyId,
      SourceBodyId: sourceBodyId,
      MaxPositionErrorKm: maxErrorKm,
      MeanPositionErrorKm: sampleCount == 0 ? 0 : sumErrorKm / sampleCount);
  }

  static BodyInterpolationResult BenchmarkBodyInterpolation(
    EphemerisService service,
    int centerBodyId,
    int requestedBodyId,
    DateTimeOffset startUtc,
    DateTimeOffset endUtc,
    int sampleDays,
    int truthHours)
  {
    var sourceBodyId = ResolveSourceBodyId(service, centerBodyId, requestedBodyId, startUtc);
    var sampleStates = BuildStateSamples(service, centerBodyId, sourceBodyId, startUtc, endUtc, sampleDays);
    var truthStep = TimeSpan.FromHours(truthHours);
    var segmentIndex = 0;
    double maxErrorKm = 0;
    double sumErrorKm = 0;
    var sampleCount = 0;

    for (var truthUtc = startUtc + truthStep; truthUtc < endUtc; truthUtc = truthUtc.Add(truthStep)) {
      while (segmentIndex < sampleStates.Count - 2 && truthUtc > sampleStates[segmentIndex + 1].Utc) {
        segmentIndex++;
      }

      var left = sampleStates[segmentIndex];
      var right = sampleStates[segmentIndex + 1];
      var truthState = BuildStateSample(service, centerBodyId, sourceBodyId, truthUtc);
      var interpolated = InterpolateHermite(left, right, truthUtc);
      var errorKm = (interpolated - truthState.State.PositionKm).Length();

      sumErrorKm += errorKm;
      sampleCount++;
      if (errorKm > maxErrorKm) {
        maxErrorKm = errorKm;
      }
    }

    return new BodyInterpolationResult(
      RequestedBodyId: requestedBodyId,
      RequestedBodyName: ResolveBodyName(requestedBodyId),
      SampleDays: sampleDays,
      SourceBodyId: sourceBodyId,
      SourceBodyName: ResolveBodyName(sourceBodyId),
      MaxPositionErrorKm: maxErrorKm,
      MeanPositionErrorKm: sampleCount == 0 ? 0 : sumErrorKm / sampleCount);
  }

  static Vector3d InterpolateHermite(StateSample left, StateSample right, DateTimeOffset utc)
  {
    var durationSeconds = (right.Utc - left.Utc).TotalSeconds;
    var elapsedSeconds = (utc - left.Utc).TotalSeconds;
    var u = elapsedSeconds / durationSeconds;
    var h00 = 2 * u * u * u - 3 * u * u + 1;
    var h10 = u * u * u - 2 * u * u + u;
    var h01 = -2 * u * u * u + 3 * u * u;
    var h11 = u * u * u - u * u;

    return h00 * left.State.PositionKm
         + h10 * durationSeconds * left.State.VelocityKmPerSec
         + h01 * right.State.PositionKm
         + h11 * durationSeconds * right.State.VelocityKmPerSec;
  }

  static GenerationOutput WriteGenerationOutput(EphemerisService service, GeneratorOptions options)
  {
    var referenceUtc = CreateChunkBoundary(options.StartYear);
    var bodySettings = BuildBodyExportSettings(service, options, referenceUtc);
    var chunkSummaries = GenerateChunks(service, options, bodySettings);
    var manifest = new GeneratorManifest(
      SchemaVersion: OutputSchemaVersion,
      GeneratedAtUtc: DateTimeOffset.UtcNow,
      SpkPath: options.SpkPath,
      LskPath: options.LskPath,
      UsesApproximateUtcConversion: true,
      ApproximationNote: "UTC is mapped to TDB seconds past J2000 using a fixed J2000 UTC anchor and ignores leap seconds for this benchmark step.",
      StartYear: options.StartYear,
      EndYear: options.EndYear,
      ChunkYears: options.ChunkYears,
      DefaultSampleDays: options.SampleDays,
      CenterBodyId: options.CenterBodyId,
      RuntimeLayout: new ManifestRuntimeLayout(
        ChunkBoundaryTimeEncoding: ChunkBoundaryTimeEncoding,
        SampleTimeEncoding: SampleTimeEncoding,
        SampleValueLayout: SampleValueLayout,
        SampleComponentsPerSample: SampleValueComponentsPerSample,
        PositionUnits: PositionUnits,
        VelocityUnits: VelocityUnits,
        InterpolationHint: InterpolationHint),
      Bodies: bodySettings.Select(static (body) => new ManifestBody(
        BodyId: body.BodyId,
        BodyName: body.BodyName,
        SourceBodyId: body.SourceBodyId,
        SourceBodyName: body.SourceBodyName,
        SampleDays: body.SampleDays)).ToArray(),
      Chunks: chunkSummaries.Select(static (chunk) => new ManifestChunk(
        FileName: chunk.FileName,
        StartUtc: chunk.StartUtc,
        EndUtc: chunk.EndUtc,
        StartTdbSecondsFromJ2000: chunk.StartTdbSecondsFromJ2000,
        EndTdbSecondsFromJ2000: chunk.EndTdbSecondsFromJ2000)).ToArray());

    var manifestPath = Path.Combine(options.OutputPath, "manifest.json");
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, OutputJsonOptions));
    var manifestBytes = new FileInfo(manifestPath).Length;
    var manifestGzipBytes = GetGzipSize(manifestPath);
    var totalBytes = manifestBytes + chunkSummaries.Sum(static (chunk) => chunk.ByteLength);
    var totalGzipBytes = manifestGzipBytes + chunkSummaries.Sum(static (chunk) => chunk.GzipByteLength);

    return new GenerationOutput(
      ManifestPath: manifestPath,
      ChunkSummaries: chunkSummaries.ToArray(),
      TotalBytes: totalBytes,
      TotalGzipBytes: totalGzipBytes);
  }

  static IReadOnlyList<GeneratedChunkSummary> GenerateChunks(
    EphemerisService service,
    GeneratorOptions options,
    IReadOnlyList<BodyExportSetting> bodySettings)
  {
    var summaries = new List<GeneratedChunkSummary>();

    for (var chunkStartYear = options.StartYear; chunkStartYear < options.EndYear; chunkStartYear += options.ChunkYears) {
      var chunkEndYear = Math.Min(chunkStartYear + options.ChunkYears, options.EndYear);
      var chunkStartUtc = CreateChunkBoundary(chunkStartYear);
      var chunkEndUtc = CreateChunkBoundary(chunkEndYear);
      var bodyChunks = bodySettings
        .Select((body) => BuildBodyChunk(
          service,
          options.CenterBodyId,
          body,
          chunkStartUtc,
          chunkEndUtc))
        .ToArray();
      var chunkStartTdbSeconds = ApproximateUtcToInstant(chunkStartUtc).TdbSecondsFromJ2000;
      var chunkEndTdbSeconds = ApproximateUtcToInstant(chunkEndUtc).TdbSecondsFromJ2000;
      var chunkFileName = $"chunk-{chunkStartYear}-{chunkEndYear}.json";
      var chunk = new EphemerisChunk(
        SchemaVersion: OutputSchemaVersion,
        CenterBodyId: options.CenterBodyId,
        StartTdbSecondsFromJ2000: chunkStartTdbSeconds,
        EndTdbSecondsFromJ2000: chunkEndTdbSeconds,
        Bodies: bodyChunks);
      var chunkPath = Path.Combine(options.OutputPath, chunkFileName);

      File.WriteAllText(
        chunkPath,
        JsonSerializer.Serialize(chunk, OutputJsonOptions));

      summaries.Add(new GeneratedChunkSummary(
        FileName: chunkFileName,
        StartUtc: chunkStartUtc,
        EndUtc: chunkEndUtc,
        StartTdbSecondsFromJ2000: chunkStartTdbSeconds,
        EndTdbSecondsFromJ2000: chunkEndTdbSeconds,
        SampleCount: bodyChunks.Max(static (body) => body.Samples.Length / 6),
        ByteLength: new FileInfo(chunkPath).Length,
        GzipByteLength: GetGzipSize(chunkPath)));
    }

    return summaries;
  }

  static long GetGzipSize(string path)
  {
    var bytes = File.ReadAllBytes(path);
    using var memoryStream = new MemoryStream();
    using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true)) {
      gzipStream.Write(bytes, 0, bytes.Length);
    }

    return memoryStream.Length;
  }

  static BodyChunk BuildBodyChunk(
    EphemerisService service,
    int centerBodyId,
    BodyExportSetting body,
    DateTimeOffset chunkStartUtc,
    DateTimeOffset chunkEndUtc)
  {
    var stateSamples = BuildStateSamples(service, centerBodyId, body.SourceBodyId, chunkStartUtc, chunkEndUtc, body.SampleDays);

    return new BodyChunk(
      BodyId: body.BodyId,
      Samples: FlattenStateSamples(stateSamples));
  }

  static int ResolveSourceBodyId(
    EphemerisService service,
    int centerBodyId,
    int requestedBodyId,
    DateTimeOffset referenceUtc)
  {
    var referenceInstant = ApproximateUtcToInstant(referenceUtc);

    if (service.TryGetState(new BodyId(requestedBodyId), new BodyId(centerBodyId), referenceInstant, out _)) {
      return requestedBodyId;
    }

    if (!QueryFallbackBodyIds.TryGetValue(requestedBodyId, out var fallbackBodyId)) {
      throw new InvalidOperationException(
        $"No state available for target {requestedBodyId} relative to center {centerBodyId} at {referenceUtc:O}, and no query fallback is configured.");
    }

    if (service.TryGetState(new BodyId(fallbackBodyId), new BodyId(centerBodyId), referenceInstant, out _)) {
      return fallbackBodyId;
    }

    throw new InvalidOperationException(
      $"No state available for target {requestedBodyId} or fallback {fallbackBodyId} relative to center {centerBodyId} at {referenceUtc:O}.");
  }

  static IReadOnlyList<StateSample> BuildStateSamples(
    EphemerisService service,
    int centerBodyId,
    int bodyId,
    DateTimeOffset startUtc,
    DateTimeOffset endUtc,
    int sampleDays)
  {
    var samples = new List<StateSample>();
    var sampleUtc = startUtc;

    while (sampleUtc < endUtc) {
      samples.Add(BuildStateSample(service, centerBodyId, bodyId, sampleUtc));
      sampleUtc = sampleUtc.AddDays(sampleDays);
    }

    if (samples.Count == 0 || samples[^1].Utc != endUtc) {
      samples.Add(BuildStateSample(service, centerBodyId, bodyId, endUtc));
    }

    return samples;
  }

  static StateSample BuildStateSample(EphemerisService service, int centerBodyId, int bodyId, DateTimeOffset utc)
  {
    var instant = ApproximateUtcToInstant(utc);

    if (!service.TryGetState(new BodyId(bodyId), new BodyId(centerBodyId), instant, out var state)) {
      throw new InvalidOperationException(
        $"No state available for target {bodyId} relative to center {centerBodyId} at {utc:O}.");
    }

    return new StateSample(Utc: utc, Instant: instant, State: state);
  }

  static DateTimeOffset CreateChunkBoundary(int year)
    => new(year, 1, 1, 12, 0, 0, TimeSpan.Zero);

  static int ResolveSampleDays(GeneratorOptions options, int bodyId)
    => options.BodyCadenceOverrides.TryGetValue(bodyId, out var sampleDays) ? sampleDays : options.SampleDays;

  static BodyExportSetting[] BuildBodyExportSettings(EphemerisService service, GeneratorOptions options, DateTimeOffset referenceUtc)
    => options.BodyIds
      .Select((bodyId) =>
      {
        var sourceBodyId = ResolveSourceBodyId(service, options.CenterBodyId, bodyId, referenceUtc);
        return new BodyExportSetting(
          BodyId: bodyId,
          BodyName: ResolveBodyName(bodyId),
          SourceBodyId: sourceBodyId,
          SourceBodyName: ResolveBodyName(sourceBodyId),
          SampleDays: ResolveSampleDays(options, bodyId));
      })
      .OrderBy(static (body) => body.BodyId)
      .ToArray();

  static BodyCadenceSetting[] BuildBodyCadenceSettings(GeneratorOptions options)
    => options.BodyIds
      .Select((bodyId) => new BodyCadenceSetting(
        BodyId: bodyId,
        BodyName: ResolveBodyName(bodyId),
        SampleDays: ResolveSampleDays(options, bodyId)))
      .OrderBy(static (setting) => setting.BodyId)
      .ToArray();

  static double[] FlattenStateSamples(IReadOnlyList<StateSample> stateSamples)
  {
    var values = new double[stateSamples.Count * 6];
    var index = 0;

    foreach (var sample in stateSamples) {
      values[index++] = sample.State.PositionKm.X;
      values[index++] = sample.State.PositionKm.Y;
      values[index++] = sample.State.PositionKm.Z;
      values[index++] = sample.State.VelocityKmPerSec.X;
      values[index++] = sample.State.VelocityKmPerSec.Y;
      values[index++] = sample.State.VelocityKmPerSec.Z;
    }

    return values;
  }

  static string FormatBodyCadenceOverrides(IReadOnlyDictionary<int, int> bodyCadenceOverrides)
    => string.Join(
      ", ",
      bodyCadenceOverrides
        .OrderBy(static (entry) => entry.Key)
        .Select((entry) => $"{ResolveBodyName(entry.Key)} ({entry.Key})={entry.Value}d"));

  static Instant ApproximateUtcToInstant(DateTimeOffset utc)
  {
    var secondsFromJ2000 = (utc - ApproximateJ2000Utc).TotalSeconds;
    return Instant.FromSeconds(checked((long)Math.Round(secondsFromJ2000)));
  }

  static string ResolveBodyName(int bodyId)
    => KnownBodyNames.TryGetValue(bodyId, out var name) ? name : $"NAIF {bodyId}";

  static bool TryParseOptions(string[] args, out GeneratorOptions options, out string? error)
  {
    error = null;

    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var bodyIds = new List<int>();
    var bodyCadenceOverrides = new Dictionary<int, int>();

    for (int index = 0; index < args.Length; index++) {
      var current = args[index];

      if (string.Equals(current, "--body", StringComparison.OrdinalIgnoreCase)) {
        if (!TryReadValue(args, ref index, out var bodyValue)) {
          error = "Missing value for --body.";
          options = default;
          return false;
        }

        if (!int.TryParse(bodyValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bodyId)) {
          error = $"Invalid body id '{bodyValue}'.";
          options = default;
          return false;
        }

        bodyIds.Add(bodyId);
        continue;
      }

      if (string.Equals(current, "--benchmark-mercury", StringComparison.OrdinalIgnoreCase)) {
        values[current] = "true";
        continue;
      }

      if (string.Equals(current, "--benchmark-bodies", StringComparison.OrdinalIgnoreCase)) {
        values[current] = "true";
        continue;
      }

      if (string.Equals(current, "--benchmark-configured-cadence", StringComparison.OrdinalIgnoreCase)) {
        values[current] = "true";
        continue;
      }

      if (string.Equals(current, "--benchmark-configured-chunk-years", StringComparison.OrdinalIgnoreCase)) {
        values[current] = "true";
        continue;
      }

      if (string.Equals(current, "--body-cadence", StringComparison.OrdinalIgnoreCase)) {
        if (!TryReadValue(args, ref index, out var bodyCadenceValue)) {
          error = "Missing value for --body-cadence.";
          options = default;
          return false;
        }

        if (!TryParseBodyCadence(bodyCadenceValue, out var bodyCadence, out error)) {
          options = default;
          return false;
        }

        bodyCadenceOverrides[bodyCadence.BodyId] = bodyCadence.SampleDays;
        continue;
      }

      if (!current.StartsWith("--", StringComparison.Ordinal)) {
        error = $"Unexpected argument '{current}'.";
        options = default;
        return false;
      }

      if (!TryReadValue(args, ref index, out var value)) {
        error = $"Missing value for {current}.";
        options = default;
        return false;
      }

      values[current] = value;
    }

    if (!values.TryGetValue("--spk", out var spkPath) || string.IsNullOrWhiteSpace(spkPath)) {
      error = "Missing required --spk <path> argument.";
      options = default;
      return false;
    }

    if (!values.TryGetValue("--output", out var outputPath) || string.IsNullOrWhiteSpace(outputPath)) {
      error = "Missing required --output <path> argument.";
      options = default;
      return false;
    }

    if (!TryParseInt(values, "--start-year", 1950, out var startYear, out error) ||
        !TryParseInt(values, "--end-year", 2050, out var endYear, out error) ||
        !TryParseInt(values, "--chunk-years", 50, out var chunkYears, out error) ||
        !TryParseInt(values, "--sample-days", 365, out var sampleDays, out error) ||
        !TryParseInt(values, "--center", 10, out var centerBodyId, out error) ||
        !TryParseInt(values, "--benchmark-truth-hours", 12, out var benchmarkTruthHours, out error)) {
      options = default;
      return false;
    }

    if (endYear <= startYear) {
      error = "--end-year must be greater than --start-year.";
      options = default;
      return false;
    }

    if (chunkYears <= 0) {
      error = "--chunk-years must be greater than zero.";
      options = default;
      return false;
    }

    if (sampleDays <= 0) {
      error = "--sample-days must be greater than zero.";
      options = default;
      return false;
    }

    if (!File.Exists(spkPath)) {
      error = $"SPK file not found: {spkPath}";
      options = default;
      return false;
    }

    values.TryGetValue("--lsk", out var lskPath);
    if (!string.IsNullOrWhiteSpace(lskPath) && !File.Exists(lskPath)) {
      error = $"LSK file not found: {lskPath}";
      options = default;
      return false;
    }

    var benchmarkMercury = values.ContainsKey("--benchmark-mercury");
    var benchmarkBodies = values.ContainsKey("--benchmark-bodies");
    var benchmarkConfiguredCadence = values.ContainsKey("--benchmark-configured-cadence");
    var benchmarkConfiguredChunkYears = values.ContainsKey("--benchmark-configured-chunk-years");
    var benchmarkModesEnabled =
      (benchmarkMercury ? 1 : 0) +
      (benchmarkBodies ? 1 : 0) +
      (benchmarkConfiguredCadence ? 1 : 0) +
      (benchmarkConfiguredChunkYears ? 1 : 0);
    if (benchmarkModesEnabled > 1) {
      error = "Choose only one benchmark mode: --benchmark-mercury, --benchmark-bodies, --benchmark-configured-cadence, or --benchmark-configured-chunk-years.";
      options = default;
      return false;
    }

    if (!TryParseCadences(values, out var benchmarkCadences, out error)) {
      options = default;
      return false;
    }

    if (!TryParseChunkYears(values, chunkYears, out var benchmarkChunkYears, out error)) {
      options = default;
      return false;
    }

    var selectedBodyIds = bodyIds.Count == 0
      ? DefaultBodyIds.ToArray()
      : bodyIds.ToArray();
    foreach (var overrideBodyId in bodyCadenceOverrides.Keys) {
      if (!selectedBodyIds.Contains(overrideBodyId)) {
        error = $"--body-cadence specified for body {overrideBodyId}, but that body is not part of the selected body set.";
        options = default;
        return false;
      }
    }

    options = new GeneratorOptions(
      SpkPath: spkPath,
      LskPath: lskPath,
      OutputPath: outputPath,
      StartYear: startYear,
      EndYear: endYear,
      ChunkYears: chunkYears,
      SampleDays: sampleDays,
      CenterBodyId: centerBodyId,
      BodyIds: selectedBodyIds,
      BodyCadenceOverrides: bodyCadenceOverrides,
      BenchmarkMercury: benchmarkMercury,
      BenchmarkBodies: benchmarkBodies,
      BenchmarkConfiguredCadence: benchmarkConfiguredCadence,
      BenchmarkConfiguredChunkYears: benchmarkConfiguredChunkYears,
      BenchmarkCadences: benchmarkCadences,
      BenchmarkChunkYears: benchmarkChunkYears,
      BenchmarkTruthHours: benchmarkTruthHours);

    return true;
  }

  static bool TryParseCadences(
    IReadOnlyDictionary<string, string> values,
    out IReadOnlyList<int> cadences,
    out string? error)
  {
    error = null;

    if (!values.TryGetValue("--benchmark-cadences", out var rawCadences) || string.IsNullOrWhiteSpace(rawCadences)) {
      cadences = DefaultBenchmarkCadences;
      return true;
    }

    var parsed = new List<int>();
    foreach (var token in rawCadences.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
      if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cadence) || cadence <= 0) {
        error = $"Invalid cadence value '{token}' in --benchmark-cadences.";
        cadences = Array.Empty<int>();
        return false;
      }

      parsed.Add(cadence);
    }

    if (parsed.Count == 0) {
      error = "--benchmark-cadences must contain at least one positive integer.";
      cadences = Array.Empty<int>();
      return false;
    }

    cadences = parsed;
    return true;
  }

  static bool TryParseChunkYears(
    IReadOnlyDictionary<string, string> values,
    int defaultChunkYears,
    out IReadOnlyList<int> chunkYears,
    out string? error)
  {
    error = null;

    if (!values.TryGetValue("--benchmark-chunk-years", out var rawChunkYears) || string.IsNullOrWhiteSpace(rawChunkYears)) {
      chunkYears = [defaultChunkYears, 25, 10];
      return true;
    }

    var parsed = new List<int>();
    foreach (var token in rawChunkYears.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
      if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chunkYear) || chunkYear <= 0) {
        error = $"Invalid chunk year value '{token}' in --benchmark-chunk-years.";
        chunkYears = Array.Empty<int>();
        return false;
      }

      parsed.Add(chunkYear);
    }

    if (parsed.Count == 0) {
      error = "--benchmark-chunk-years must contain at least one positive integer.";
      chunkYears = Array.Empty<int>();
      return false;
    }

    chunkYears = parsed;
    return true;
  }

  static bool TryParseBodyCadence(string rawValue, out BodyCadenceOverride bodyCadence, out string? error)
  {
    error = null;
    var parts = rawValue.Split(':', StringSplitOptions.TrimEntries);

    if (parts.Length != 2 ||
        !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bodyId) ||
        !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sampleDays)) {
      bodyCadence = default;
      error = $"Invalid --body-cadence value '{rawValue}'. Expected <body-id>:<days>.";
      return false;
    }

    if (sampleDays <= 0) {
      bodyCadence = default;
      error = $"Invalid --body-cadence value '{rawValue}'. Sample days must be greater than zero.";
      return false;
    }

    bodyCadence = new BodyCadenceOverride(BodyId: bodyId, SampleDays: sampleDays);
    return true;
  }

  static bool TryParseInt(
    IReadOnlyDictionary<string, string> values,
    string key,
    int defaultValue,
    out int parsed,
    out string? error)
  {
    error = null;

    if (!values.TryGetValue(key, out var rawValue)) {
      parsed = defaultValue;
      return true;
    }

    if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) {
      return true;
    }

    error = $"Invalid integer value '{rawValue}' for {key}.";
    return false;
  }

  static bool TryReadValue(string[] args, ref int index, out string value)
  {
    var nextIndex = index + 1;

    if (nextIndex >= args.Length) {
      value = string.Empty;
      return false;
    }

    value = args[nextIndex];
    index = nextIndex;
    return true;
  }

  static bool HasFlag(string[] args, params string[] flags)
    => args.Any((current) => flags.Contains(current, StringComparer.OrdinalIgnoreCase));

  static void PrintHelp()
  {
    Console.WriteLine(
      """
      Spice.WebDataGenerator

      Usage:
        dotnet run --project Spice.WebDataGenerator -- --spk <path> --output <dir> [options]

      Required:
        --spk <path>         Path to the planetary SPK kernel, such as de441t.bsp.
        --output <dir>       Output directory for generated manifest and chunk files.

      Optional:
        --lsk <path>         Optional leap-second kernel path. Ignored by the current approximate benchmark time conversion.
        --start-year <year>  Coverage start year. Default: 1950.
        --end-year <year>    Coverage end year. Default: 2050.
        --chunk-years <n>    Initial chunk duration in years. Default: 50.
        --sample-days <n>    Sample spacing inside a chunk. Default: 365.
        --center <naif-id>   Center body id for generated states. Default: 10.
        --body <naif-id>     Body NAIF id to include. Repeat to override the default body set.
        --body-cadence       Per-body cadence override in the form <naif-id>:<days>. Repeat as needed.
        --benchmark-mercury  Generate multiple exports and compare Mercury Hermite interpolation error by cadence.
        --benchmark-bodies   Generate multiple exports and compare Hermite interpolation error for every selected body.
        --benchmark-configured-cadence
                            Generate one export using the configured default cadence plus per-body overrides, then validate it body by body.
        --benchmark-configured-chunk-years
                            Generate configured mixed-cadence exports across several shared chunk durations and compare size plus validation results.
        --benchmark-cadences Comma-separated cadence list in days. Default: 90,30,14,7,3.
        --benchmark-chunk-years
                            Comma-separated chunk duration list in years. Default: current chunk years, 25, 10.
        --benchmark-truth-hours
                            Truth sampling step in hours for benchmark validation. Default: 12.

      Notes:
        This benchmark step emits manifest and chunk JSON files using an approximate UTC-to-TDB conversion.
        Leap seconds and kernel-derived metadata extraction will arrive in later steps.
      """);
  }

  static readonly int[] DefaultBodyIds = [10, 199, 299, 399, 301, 499, 599, 699, 799, 899];
  static readonly int[] DefaultBenchmarkCadences = [90, 30, 14, 7, 3];
  static readonly DateTimeOffset ApproximateJ2000Utc = new(2000, 1, 1, 11, 58, 55, 816, TimeSpan.Zero);
  static readonly IReadOnlyDictionary<int, string> KnownBodyNames = new Dictionary<int, string>
  {
    [0] = "Solar System Barycenter",
    [1] = "Mercury Barycenter",
    [2] = "Venus Barycenter",
    [4] = "Mars Barycenter",
    [5] = "Jupiter Barycenter",
    [6] = "Saturn Barycenter",
    [7] = "Uranus Barycenter",
    [8] = "Neptune Barycenter",
    [10] = "Sun",
    [199] = "Mercury",
    [299] = "Venus",
    [399] = "Earth",
    [301] = "Moon",
    [499] = "Mars",
    [599] = "Jupiter",
    [699] = "Saturn",
    [799] = "Uranus",
    [899] = "Neptune"
  };
  static readonly IReadOnlyDictionary<int, int> QueryFallbackBodyIds = new Dictionary<int, int>
  {
    [199] = 1,
    [299] = 2,
    [499] = 4,
    [599] = 5,
    [699] = 6,
    [799] = 7,
    [899] = 8
  };

  readonly record struct GeneratorOptions(
    string SpkPath,
    string? LskPath,
    string OutputPath,
    int StartYear,
    int EndYear,
    int ChunkYears,
    int SampleDays,
    int CenterBodyId,
    IReadOnlyList<int> BodyIds,
    IReadOnlyDictionary<int, int> BodyCadenceOverrides,
    bool BenchmarkMercury,
    bool BenchmarkBodies,
    bool BenchmarkConfiguredCadence,
    bool BenchmarkConfiguredChunkYears,
    IReadOnlyList<int> BenchmarkCadences,
    IReadOnlyList<int> BenchmarkChunkYears,
    int BenchmarkTruthHours);

  readonly record struct GeneratedChunkSummary(
    string FileName,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    long StartTdbSecondsFromJ2000,
    long EndTdbSecondsFromJ2000,
    int SampleCount,
    long ByteLength,
    long GzipByteLength);

  readonly record struct BodyExportSetting(
    int BodyId,
    string BodyName,
    int SourceBodyId,
    string SourceBodyName,
    int SampleDays);

  readonly record struct GenerationOutput(string ManifestPath, GeneratedChunkSummary[] ChunkSummaries, long TotalBytes, long TotalGzipBytes);

  readonly record struct StateSample(DateTimeOffset Utc, Instant Instant, StateVector State);

  readonly record struct MercuryInterpolationResult(int RequestedBodyId, int SourceBodyId, double MaxPositionErrorKm, double MeanPositionErrorKm);

  readonly record struct BodyInterpolationResult(
    int RequestedBodyId,
    string RequestedBodyName,
    int SampleDays,
    int SourceBodyId,
    string SourceBodyName,
    double MaxPositionErrorKm,
    double MeanPositionErrorKm);

  readonly record struct BodyCadenceOverride(int BodyId, int SampleDays);

  sealed record GeneratorManifest(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string SpkPath,
    string? LskPath,
    bool UsesApproximateUtcConversion,
    string ApproximationNote,
    int StartYear,
    int EndYear,
    int ChunkYears,
    int DefaultSampleDays,
    int CenterBodyId,
    ManifestRuntimeLayout RuntimeLayout,
    ManifestBody[] Bodies,
    ManifestChunk[] Chunks);

  sealed record MercuryBenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string SpkPath,
    string? LskPath,
    int StartYear,
    int EndYear,
    int CenterBodyId,
    int BenchmarkTruthHours,
    string ApproximationNote,
    MercuryCadenceResult[] Results);

  sealed record BodyBenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string SpkPath,
    string? LskPath,
    int StartYear,
    int EndYear,
    int CenterBodyId,
    int BenchmarkTruthHours,
    string ApproximationNote,
    BodyCadenceBenchmarkResult[] Results);

  sealed record ConfiguredCadenceBenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string SpkPath,
    string? LskPath,
    int StartYear,
    int EndYear,
    int ChunkYears,
    int CenterBodyId,
    int DefaultSampleDays,
    BodyCadenceSetting[] BodyCadences,
    int BenchmarkTruthHours,
    string ApproximationNote,
    long TotalOutputBytes,
    long TotalGzipBytes,
    GeneratedChunkSummary[] ChunkSummaries,
    BodyInterpolationResult[] BodyResults);

  sealed record ConfiguredChunkYearBenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string SpkPath,
    string? LskPath,
    int StartYear,
    int EndYear,
    int CenterBodyId,
    int DefaultSampleDays,
    BodyCadenceSetting[] BodyCadences,
    int[] BenchmarkChunkYears,
    int BenchmarkTruthHours,
    string ApproximationNote,
    ConfiguredChunkYearResult[] Results);

  sealed record MercuryCadenceResult(
    int SampleDays,
    int MercuryBodyId,
    int MercurySourceBodyId,
    string MercurySourceBodyName,
    double MaxPositionErrorKm,
    double MeanPositionErrorKm,
    long TotalOutputBytes,
    long TotalGzipBytes,
    GeneratedChunkSummary[] ChunkSummaries);

  sealed record BodyCadenceBenchmarkResult(
    int SampleDays,
    long TotalOutputBytes,
    long TotalGzipBytes,
    GeneratedChunkSummary[] ChunkSummaries,
    BodyInterpolationResult[] BodyResults);

  sealed record ConfiguredChunkYearResult(
    int ChunkYears,
    long TotalOutputBytes,
    long TotalGzipBytes,
    long MaxChunkGzipBytes,
    GeneratedChunkSummary[] ChunkSummaries,
    BodyInterpolationResult[] BodyResults);

  sealed record BodyCadenceSetting(int BodyId, string BodyName, int SampleDays);

  sealed record ManifestRuntimeLayout(
    string ChunkBoundaryTimeEncoding,
    string SampleTimeEncoding,
    string SampleValueLayout,
    int SampleComponentsPerSample,
    string PositionUnits,
    string VelocityUnits,
    string InterpolationHint);

  sealed record ManifestBody(int BodyId, string BodyName, int SourceBodyId, string SourceBodyName, int SampleDays);

  sealed record ManifestChunk(
    string FileName,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    long StartTdbSecondsFromJ2000,
    long EndTdbSecondsFromJ2000);

  sealed record EphemerisChunk(
    int SchemaVersion,
    int CenterBodyId,
    long StartTdbSecondsFromJ2000,
    long EndTdbSecondsFromJ2000,
    BodyChunk[] Bodies);

  sealed record BodyChunk(
    int BodyId,
    double[] Samples);
}
