using Shouldly;
using Spice.Kernels;

namespace Spice.Tests;

public class MetaKernelParserTests
{
  [Fact]
  public void Parses_Relative_And_Absolute_Paths()
  {
    var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
      // Compose an absolute path (need not exist) and two relative ones.
      var absKernel = Path.GetFullPath(Path.Combine(tempRoot, "absolute.bsp"));
      var rel1 = "a.tls";
      var rel2 = "subdir/ephem.bsp";
      var metaContent = $"\\begindata\nKERNELS_TO_LOAD = ( '{rel1}'\n  \"{rel2}\"  '{absKernel}' )";
      var metaPath = Path.Combine(tempRoot, "meta.tm");
      File.WriteAllText(metaPath, metaContent);

      using var ms = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(metaContent));
      var meta = MetaKernelParser.Parse(ms, metaPath);
      meta.KernelPaths.Count.ShouldBe(3);

      meta.KernelPaths.ShouldContain(Path.GetFullPath(Path.Combine(tempRoot, rel1)));
      meta.KernelPaths.ShouldContain(Path.GetFullPath(Path.Combine(tempRoot, rel2.Replace('/', Path.DirectorySeparatorChar))));
      meta.KernelPaths.ShouldContain(absKernel);
    }
    finally
    {
      // Clean up meta file only; created directories may remain harmlessly if removal fails.
      if (Directory.Exists(tempRoot))
      {
        try { Directory.Delete(tempRoot, recursive:true); } catch { /* ignore */ }
      }
    }
  }

  [Fact]
  public void KernelRegistry_Deduplicates_Paths()
  {
    var registry = new KernelRegistry();
    var root = Path.GetTempPath();
    var p1 = Path.Combine(root, "dupA.tls");
    var p2 = Path.Combine(root, "dupA.tls"); // identical
    var p3 = Path.Combine(root, "other.bsp");

    registry.AddKernelPaths(new[]{p1, p2, p3});
    registry.KernelPaths.Count.ShouldBe(2); // deduped
    registry.KernelPaths.ShouldContain(Path.GetFullPath(p1));
    registry.KernelPaths.ShouldContain(Path.GetFullPath(p3));
  }

  [Fact]
  public void Missing_Block_Throws()
  {
    const string content = "\\begindata\nFRAME_KERNELS = ( 'f.tf' )";
    using var ms = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(content));
    Should.Throw<InvalidDataException>(() => MetaKernelParser.Parse(ms, "dummy.tm"));
  }
}
