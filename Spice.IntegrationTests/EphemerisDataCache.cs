// Original design: Download & local cache manager for ephemeris (BSP) + testpo reference files.
using System.Security.Cryptography;
using System.Text.Json;

namespace Spice.IntegrationTests;

internal sealed class EphemerisDataCache
{
  readonly string _root;
  readonly HttpClient _http = new();

  record Meta(string Ephemeris, string TestPoFile, string BspFile, long BspSizeBytes, DateTime DownloadedUtc, string? BspSha256, string? TestPoSha256);

  EphemerisDataCache(string root)
  {
    _root = root;
    Directory.CreateDirectory(root);
  }

  public static EphemerisDataCache CreateDefault()
  {
    var overridePath = Environment.GetEnvironmentVariable("SPICE_INTEGRATION_CACHE");
    string root = string.IsNullOrWhiteSpace(overridePath)
      ? Path.Combine(AppContext.BaseDirectory, "TestData", "cache")
      : overridePath;
    return new EphemerisDataCache(root);
  }

  public async Task<(string testPoPath, string bspPath)?> EnsureAsync(EphemerisEntry entry, CancellationToken ct = default)
  {
    string dir = Path.Combine(_root, entry.DirectoryName);
    Directory.CreateDirectory(dir);
    string testPoPath = Path.Combine(dir, entry.TestPoFileName);
    string bspPath = Path.Combine(dir, entry.PreferredBspFileName);
    bool needTestPo = !File.Exists(testPoPath);
    bool needBsp = !File.Exists(bspPath);

    // Large file gating
    if (!entry.UnderSizeLimit && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SPICE_ALLOW_LARGE_KERNELS")))
      return null; // skip

    try
    {
      if (needTestPo)
      {
        await DownloadAsync(entry.TestPoUrl, testPoPath, ct);
      }
      if (needBsp)
      {
        await DownloadAsync(entry.BspUrl, bspPath, ct);
        var fi = new FileInfo(bspPath);
        if (fi.Length != entry.SizeBytes && entry.UnderSizeLimit)
        {
          // Not fatal: keep but note mismatch
        }
      }
      await WriteMetaAsync(dir, entry, testPoPath, bspPath, ct);
      return (testPoPath, bspPath);
    }
    catch
    {
      // On any network failure leave partial files (future runs can retry)
      return null;
    }
  }

  async Task DownloadAsync(string url, string destination, CancellationToken ct)
  {
    using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
    resp.EnsureSuccessStatusCode();
    string tmp = destination + ".part";
    await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
      await resp.Content.CopyToAsync(fs, ct);
    if (File.Exists(destination)) File.Delete(destination);
    File.Move(tmp, destination);
  }

  static async Task WriteMetaAsync(string dir, EphemerisEntry entry, string testPo, string bsp, CancellationToken ct)
  {
    string metaPath = Path.Combine(dir, "meta.json");
    string? testHash = await TryHashAsync(testPo, ct);
    string? bspHash = await TryHashAsync(bsp, ct);
    var meta = new Meta(entry.Number, entry.TestPoFileName, entry.PreferredBspFileName, entry.SizeBytes, DateTime.UtcNow, bspHash, testHash);
    var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(metaPath, json, ct);
  }

  static async Task<string?> TryHashAsync(string path, CancellationToken ct)
  {
    try
    {
      await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
      using var sha = SHA256.Create();
      var hash = await sha.ComputeHashAsync(fs, ct);
      return Convert.ToHexString(hash);
    }
    catch { return null; }
  }
}
