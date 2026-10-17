namespace PictureSharp.Formats.Extra.Common.BlockCompression;

/// <summary>
/// The BC6H block decoder (D3D11 functional spec, BC6H format), matching bcdec and DirectXTex. Half-float results are
/// reduced to 8 bits as Pillow 10.2.0 does: clamped to [0, 1] and truncated after multiplying by 255. Pillow's own BC6H
/// decoder differs from the spec: it interpolates without rounding and, for signed data, does not sign-extend the
/// endpoints of transformed modes after adding the deltas.
/// </summary>
internal static class Bc6HBlock
{
    private static readonly ModeInfo?[] ModeTable = CreateModes();

    public static void Decode(ReadOnlySpan<byte> block, Span<Rgba32> pixels, bool signed)
    {
        var bits = new BlockBits(block);
        int code = bits.Read(2);
        if (code >= 2)
        {
            code |= bits.Read(3) << 2;
        }

        ModeInfo? mode = ModeTable[code];
        if (mode is null)
        {
            // Reserved modes decode to black.
            pixels[..16].Fill(new Rgba32(0, 0, 0));
            return;
        }

        // fields[endpoint * 3 + channel]: r0 g0 b0 r1 g1 b1 r2 g2 b2 r3 g3 b3.
        Span<int> e = stackalloc int[12];
        e.Clear();
        foreach (Field field in mode.Layout)
        {
            int value = field.Reversed ? bits.ReadReversed(field.Count) : bits.Read(field.Count);
            e[field.Target] |= value << field.Low;
        }

        int regions = mode.Regions;
        int partition = regions == 2 ? bits.Read(5) : 0;
        int endpointCount = regions * 2;
        int precision = mode.Precision;
        int mask = (1 << precision) - 1;
        if (signed)
        {
            for (int c = 0; c < 3; c++)
            {
                e[c] = SignExtend(e[c], precision);
            }
        }

        for (int i = 1; i < endpointCount; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                ref int v = ref e[i * 3 + c];
                if (mode.Transformed)
                {
                    v = (e[c] + SignExtend(v, mode.DeltaBits[c])) & mask;
                }

                if (signed)
                {
                    v = SignExtend(v, precision);
                }
            }
        }

        for (int i = 0; i < endpointCount * 3; i++)
        {
            e[i] = signed ? UnquantizeSigned(e[i], precision) : UnquantizeUnsigned(e[i], precision);
        }

        int indexBits = regions == 2 ? 3 : 4;
        ReadOnlySpan<byte> weights = BcPartitions.Weights(indexBits);
        for (int i = 0; i < 16; i++)
        {
            int index = bits.Read(BcPartitions.IsAnchor(regions, partition, i) ? indexBits - 1 : indexBits);
            int subset = BcPartitions.Subset(regions, partition, i);
            int w = weights[index];
            int baseIndex = subset * 6;
            pixels[i] = new Rgba32(
                ToByte(Interpolate(e[baseIndex], e[baseIndex + 3], w), signed),
                ToByte(Interpolate(e[baseIndex + 1], e[baseIndex + 4], w), signed),
                ToByte(Interpolate(e[baseIndex + 2], e[baseIndex + 5], w), signed));
        }
    }

    private static int SignExtend(int value, int bits) => (value << (32 - bits)) >> (32 - bits);

    private static int UnquantizeUnsigned(int x, int bits)
    {
        if (bits >= 15)
        {
            return x;
        }

        if (x == 0)
        {
            return 0;
        }

        return x == (1 << bits) - 1 ? 0xffff : ((x << 16) + 0x8000) >> bits;
    }

    private static int UnquantizeSigned(int x, int bits)
    {
        if (bits >= 16)
        {
            return x;
        }

        bool negative = x < 0;
        if (negative)
        {
            x = -x;
        }

        int result = x == 0 ? 0 : x >= (1 << (bits - 1)) - 1 ? 0x7fff : ((x << 15) + 0x4000) >> (bits - 1);
        return negative ? -result : result;
    }

    // The spec rounds (+32); Pillow 10.2.0 truncates, so its unsigned output can be one lower.
    private static int Interpolate(int a, int b, int weight) => ((64 - weight) * a + weight * b + 32) >> 6;

    // Finishes the unquantisation to half-float bits and reduces the value to 8 bits.
    private static byte ToByte(int value, bool signed)
    {
        ushort half;
        if (!signed)
        {
            half = (ushort)((value * 31) >> 6);
        }
        else
        {
            half = value < 0 ? (ushort)(0x8000 | (((-value) * 31) >> 5)) : (ushort)((value * 31) >> 5);
        }

        float f = (float)BitConverter.UInt16BitsToHalf(half);
        if (!(f > 0f))
        {
            return 0;
        }

        return f >= 1f ? (byte)255 : (byte)(f * 255f);
    }

    private static ModeInfo?[] CreateModes()
    {
        var modes = new ModeInfo?[32];
        void Add(int code, bool transformed, int regions, int precision, int dr, int dg, int db, string layout) =>
            modes[code] = new ModeInfo(transformed, regions, precision, [dr, dg, db], Parse(layout));

        // Field layouts after the mode bits, from the spec's header tables. "r0:9-0" reads bits 0..9 of r0 lowest first;
        // "r0:15>10" reads bits 15 down to 10, highest first.
        Add(0, true, 2, 10, 5, 5, 5, "g2:4 b2:4 b3:4 r0:9-0 g0:9-0 b0:9-0 r1:4-0 g3:4 g2:3-0 g1:4-0 b3:0 g3:3-0 b1:4-0 b3:1 b2:3-0 r2:4-0 b3:2 r3:4-0 b3:3");
        Add(1, true, 2, 7, 6, 6, 6, "g2:5 g3:4 g3:5 r0:6-0 b3:0 b3:1 b2:4 g0:6-0 b2:5 b3:2 g2:4 b0:6-0 b3:3 b3:5 b3:4 r1:5-0 g2:3-0 g1:5-0 g3:3-0 b1:5-0 b2:3-0 r2:5-0 r3:5-0");
        Add(2, true, 2, 11, 5, 4, 4, "r0:9-0 g0:9-0 b0:9-0 r1:4-0 r0:10 g2:3-0 g1:3-0 g0:10 b3:0 g3:3-0 b1:3-0 b0:10 b3:1 b2:3-0 r2:4-0 b3:2 r3:4-0 b3:3");
        Add(6, true, 2, 11, 4, 5, 4, "r0:9-0 g0:9-0 b0:9-0 r1:3-0 r0:10 g3:4 g2:3-0 g1:4-0 g0:10 g3:3-0 b1:3-0 b0:10 b3:1 b2:3-0 r2:3-0 b3:0 b3:2 r3:3-0 g2:4 b3:3");
        Add(10, true, 2, 11, 4, 4, 5, "r0:9-0 g0:9-0 b0:9-0 r1:3-0 r0:10 b2:4 g2:3-0 g1:3-0 g0:10 b3:0 g3:3-0 b1:4-0 b0:10 b2:3-0 r2:3-0 b3:1 b3:2 r3:3-0 b3:4 b3:3");
        Add(14, true, 2, 9, 5, 5, 5, "r0:8-0 b2:4 g0:8-0 g2:4 b0:8-0 b3:4 r1:4-0 g3:4 g2:3-0 g1:4-0 b3:0 g3:3-0 b1:4-0 b3:1 b2:3-0 r2:4-0 b3:2 r3:4-0 b3:3");
        Add(18, true, 2, 8, 6, 5, 5, "r0:7-0 g3:4 b2:4 g0:7-0 b3:2 g2:4 b0:7-0 b3:3 b3:4 r1:5-0 g2:3-0 g1:4-0 b3:0 g3:3-0 b1:4-0 b3:1 b2:3-0 r2:5-0 r3:5-0");
        Add(22, true, 2, 8, 5, 6, 5, "r0:7-0 b3:0 b2:4 g0:7-0 g2:5 g2:4 b0:7-0 g3:5 b3:4 r1:4-0 g3:4 g2:3-0 g1:5-0 g3:3-0 b1:4-0 b3:1 b2:3-0 r2:4-0 b3:2 r3:4-0 b3:3");
        Add(26, true, 2, 8, 5, 5, 6, "r0:7-0 b3:1 b2:4 g0:7-0 b2:5 g2:4 b0:7-0 b3:5 b3:4 r1:4-0 g3:4 g2:3-0 g1:4-0 b3:0 g3:3-0 b1:5-0 b2:3-0 r2:4-0 b3:2 r3:4-0 b3:3");
        Add(30, false, 2, 6, 6, 6, 6, "r0:5-0 g3:4 b3:0 b3:1 b2:4 g0:5-0 g2:5 b2:5 b3:2 g2:4 b0:5-0 g3:5 b3:3 b3:5 b3:4 r1:5-0 g2:3-0 g1:5-0 g3:3-0 b1:5-0 b2:3-0 r2:5-0 r3:5-0");
        Add(3, false, 1, 10, 10, 10, 10, "r0:9-0 g0:9-0 b0:9-0 r1:9-0 g1:9-0 b1:9-0");
        Add(7, true, 1, 11, 9, 9, 9, "r0:9-0 g0:9-0 b0:9-0 r1:8-0 r0:10 g1:8-0 g0:10 b1:8-0 b0:10");
        Add(11, true, 1, 12, 8, 8, 8, "r0:9-0 g0:9-0 b0:9-0 r1:7-0 r0:11>10 g1:7-0 g0:11>10 b1:7-0 b0:11>10");
        Add(15, true, 1, 16, 4, 4, 4, "r0:9-0 g0:9-0 b0:9-0 r1:3-0 r0:15>10 g1:3-0 g0:15>10 b1:3-0 b0:15>10");
        return modes;
    }

    private static Field[] Parse(string layout)
    {
        string[] tokens = layout.Split(' ');
        var fields = new Field[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            int channel = "rgb".IndexOf(token[0], StringComparison.Ordinal);
            int target = (token[1] - '0') * 3 + channel;
            string range = token[3..];
            int separator = range.IndexOfAny(['-', '>']);
            if (separator < 0)
            {
                int bit = int.Parse(range, System.Globalization.CultureInfo.InvariantCulture);
                fields[i] = new Field(target, bit, 1, false);
                continue;
            }

            int high = int.Parse(range[..separator], System.Globalization.CultureInfo.InvariantCulture);
            int low = int.Parse(range[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            fields[i] = new Field(target, low, high - low + 1, range[separator] == '>');
        }

        return fields;
    }

    private readonly record struct Field(int Target, int Low, int Count, bool Reversed);

    private sealed record ModeInfo(bool Transformed, int Regions, int Precision, int[] DeltaBits, Field[] Layout);
}
