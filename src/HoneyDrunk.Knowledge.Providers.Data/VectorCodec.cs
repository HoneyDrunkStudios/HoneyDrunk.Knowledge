using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace HoneyDrunk.Knowledge.Providers.Data;

/// <summary>
/// Packs an embedding vector as little-endian IEEE 754 single-precision floats, four bytes each, with nothing
/// between them, and reads it back bit for bit. No message names a value.
/// </summary>
internal static class VectorCodec
{
    /// <summary>Packs a vector.</summary>
    /// <param name="vector">The vector.</param>
    /// <returns>Four bytes per value, little-endian.</returns>
    public static byte[] Encode(IReadOnlyList<float> vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        var bytes = new byte[vector.Count * sizeof(float)];
        for (var index = 0; index < vector.Count; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * sizeof(float)), vector[index]);
        }

        return bytes;
    }

    /// <summary>Reads a packed vector.</summary>
    /// <param name="bytes">The packed vector.</param>
    /// <returns>The values.</returns>
    /// <exception cref="InvalidOperationException">The bytes are not a packed vector.</exception>
    public static float[] Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var values = new float[Dimensions(bytes)];
        Decode(bytes, values);
        return values;
    }

    /// <summary>Reads a packed vector into <paramref name="values"/>, which must hold exactly its values.</summary>
    /// <param name="bytes">The packed vector.</param>
    /// <param name="values">Receives the values.</param>
    /// <exception cref="InvalidOperationException">The bytes are not a packed vector of that length.</exception>
    public static void Decode(ReadOnlySpan<byte> bytes, Span<float> values)
    {
        if (bytes.Length != values.Length * sizeof(float) || values.Length == 0)
        {
            throw new InvalidOperationException("A stored vector does not have the length its version records.");
        }

        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Cast<byte, float>(bytes).CopyTo(values);
            return;
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(index * sizeof(float))..]);
        }
    }

    private static int Dimensions(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
        {
            throw new InvalidOperationException("A stored vector does not have the length its version records.");
        }

        return bytes.Length / sizeof(float);
    }
}
