using System.Security.Cryptography;

namespace ActualLab.Generators;

// Thread-safe!
public sealed class RandomInt64Generator(RandomNumberGenerator? rng = null) : Generator<long>
{
    private readonly byte[] _buffer = new byte[sizeof(long)];
    private readonly RandomNumberGenerator _rng = rng ?? RandomNumberGenerator.Create();

    public override long Next()
    {
        lock (_rng) {
            _rng.GetBytes(_buffer);
        }
        var bufferSpan = MemoryMarshal.Cast<byte, long>(_buffer.AsSpan());
        return bufferSpan![0];
    }
}
