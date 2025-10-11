// CSPICE Port Reference: N/A (original design)
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;

namespace Spice.IO;

/// <summary>
/// Abstraction over ephemeris coefficient storage enabling lazy, zero-copy (memory-mapped) or streamed
/// access to SPK element data addressed by 1-based DAF double word addresses.
/// </summary>
internal interface IEphemerisDataSource : IDisposable
{
  double ReadDouble(long address1Based);
  void ReadDoubles(long address1Based, Span<double> destination);
  bool LittleEndian { get; }
}

static class Endian
{
  public static double ToDouble(long rawBits, bool little) => BitConverter.Int64BitsToDouble(little ? rawBits : BinaryPrimitives.ReverseEndianness(rawBits));
}

/// <summary>Stream based implementation using a seekable readable <see cref="Stream"/>.</summary>
internal sealed class StreamEphemerisDataSource : IEphemerisDataSource
{
  readonly Stream _stream;
  readonly bool _leaveOpen;
  readonly byte[] _buffer8 = new byte[8];
  public bool LittleEndian { get; }

  public StreamEphemerisDataSource(Stream stream, bool littleEndian, bool leaveOpen = false)
  {
    if (!stream.CanSeek || !stream.CanRead) throw new ArgumentException("Stream must be seekable & readable");
    _stream = stream; _leaveOpen = leaveOpen; LittleEndian = littleEndian;
  }

  public double ReadDouble(long address1Based)
  {
    Seek(address1Based);
    if (_stream.Read(_buffer8,0,8) != 8) throw new EndOfStreamException();
    long bits = BinaryPrimitives.ReadInt64LittleEndian(_buffer8); // raw bytes as little layout
    return Endian.ToDouble(bits, LittleEndian);
  }

  public void ReadDoubles(long address1Based, Span<double> destination)
  {
    Seek(address1Based);
    for (int i=0;i<destination.Length;i++)
    {
      if (_stream.Read(_buffer8,0,8)!=8) throw new EndOfStreamException();
      long bits = BinaryPrimitives.ReadInt64LittleEndian(_buffer8);
      destination[i]=Endian.ToDouble(bits, LittleEndian);
    }
  }

  void Seek(long address1Based)
  {
    long byteOffset = (address1Based - 1) * 8;
    _stream.Seek(byteOffset, SeekOrigin.Begin);
  }

  public void Dispose()
  {
    if (!_leaveOpen) _stream.Dispose();
  }
}

/// <summary>Memory-mapped file data source for fast random access.</summary>
internal sealed class MemoryMappedEphemerisDataSource : IEphemerisDataSource
{
  readonly MemoryMappedFile _mmf;
  readonly MemoryMappedViewAccessor _acc;
  public bool LittleEndian { get; }

  public MemoryMappedEphemerisDataSource(string filePath, bool littleEndian)
  {
    LittleEndian = littleEndian;
    _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
    _acc = _mmf.CreateViewAccessor(0,0,MemoryMappedFileAccess.Read);
  }

  public double ReadDouble(long address1Based)
  {
    long byteOffset = (address1Based - 1) * 8;
    long bits = _acc.ReadInt64(byteOffset);
    return Endian.ToDouble(bits, LittleEndian);
  }

  public void ReadDoubles(long address1Based, Span<double> destination)
  {
    long baseOffset = (address1Based - 1) * 8;
    for (int i=0;i<destination.Length;i++)
    {
      long bits = _acc.ReadInt64(baseOffset + i*8);
      destination[i] = Endian.ToDouble(bits, LittleEndian);
    }
  }

  public void Dispose()
  {
    _acc.Dispose();
    _mmf.Dispose();
  }
}

/// <summary>Factory helpers.</summary>
internal static class EphemerisDataSource
{
  internal static IEphemerisDataSource FromStream(Stream stream, bool littleEndian, bool leaveOpen=false) => new StreamEphemerisDataSource(stream, littleEndian, leaveOpen);
  internal static IEphemerisDataSource MemoryMapped(string filePath, bool littleEndian) => new MemoryMappedEphemerisDataSource(filePath, littleEndian);
  internal static ValueTask<IEphemerisDataSource> FromStreamAsync(string filePath, bool littleEndian, bool memoryMap=false, CancellationToken ct=default)
  {
    if (memoryMap)
      return ValueTask.FromResult<IEphemerisDataSource>(MemoryMapped(filePath, littleEndian));
    var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous|FileOptions.SequentialScan);
    return ValueTask.FromResult<IEphemerisDataSource>(FromStream(fs, littleEndian));
  }
}
