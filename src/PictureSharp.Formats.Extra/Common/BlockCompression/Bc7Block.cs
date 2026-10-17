namespace PictureSharp.Formats.Extra.Common.BlockCompression;

/// <summary>The BC7 block decoder (D3D11 functional spec, BC7 format).</summary>
internal static class Bc7Block
{
    // Per mode: subsets, partition bits, rotation bits, index selection bits, colour bits, alpha bits,
    // per-endpoint P-bits, shared P-bits, index bits, secondary index bits.
    private static ReadOnlySpan<byte> Modes =>
    [
        3, 4, 0, 0, 4, 0, 1, 0, 3, 0,
        2, 6, 0, 0, 6, 0, 0, 1, 3, 0,
        3, 6, 0, 0, 5, 0, 0, 0, 2, 0,
        2, 6, 0, 0, 7, 0, 1, 0, 2, 0,
        1, 0, 2, 1, 5, 6, 0, 0, 2, 3,
        1, 0, 2, 0, 7, 8, 0, 0, 2, 2,
        1, 0, 0, 0, 7, 7, 1, 0, 4, 0,
        2, 6, 0, 0, 5, 5, 1, 0, 2, 0,
    ];

    public static void Decode(ReadOnlySpan<byte> block, Span<Rgba32> pixels)
    {
        int mode = block[0] == 0 ? 8 : System.Numerics.BitOperations.TrailingZeroCount(block[0]);
        if (mode == 8)
        {
            // Reserved mode: Pillow 10.2.0 writes opaque black (the spec says transparent black).
            pixels[..16].Fill(new Rgba32(0, 0, 0));
            return;
        }

        ReadOnlySpan<byte> m = Modes.Slice(mode * 10, 10);
        int subsets = m[0], colorBits = m[4], alphaBits = m[5], indexBits = m[8], indexBits2 = m[9];
        var bits = new BlockBits(block);
        bits.Read(mode + 1);
        int partition = bits.Read(m[1]);
        int rotation = bits.Read(m[2]);
        int indexSelection = bits.Read(m[3]);

        // endpoints[subset * 2 + end][channel]
        Span<int> endpoints = stackalloc int[6 * 4];
        int count = subsets * 2;
        for (int channel = 0; channel < 3; channel++)
        {
            for (int e = 0; e < count; e++)
            {
                endpoints[e * 4 + channel] = bits.Read(colorBits);
            }
        }

        for (int e = 0; e < count; e++)
        {
            endpoints[e * 4 + 3] = alphaBits == 0 ? 255 : bits.Read(alphaBits);
        }

        int colorPrecision = colorBits, alphaPrecision = alphaBits;
        if (m[6] != 0 || m[7] != 0)
        {
            Span<int> pBits = stackalloc int[6];
            for (int e = 0; e < count; e++)
            {
                pBits[e] = m[6] != 0 ? bits.Read(1) : (e & 1) == 0 ? bits.Read(1) : pBits[e - 1];
            }

            for (int e = 0; e < count; e++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    endpoints[e * 4 + channel] = (endpoints[e * 4 + channel] << 1) | pBits[e];
                }

                if (alphaBits != 0)
                {
                    endpoints[e * 4 + 3] = (endpoints[e * 4 + 3] << 1) | pBits[e];
                }
            }

            colorPrecision++;
            if (alphaBits != 0)
            {
                alphaPrecision++;
            }
        }

        for (int e = 0; e < count; e++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                endpoints[e * 4 + channel] = Expand(endpoints[e * 4 + channel], colorPrecision);
            }

            if (alphaBits != 0)
            {
                endpoints[e * 4 + 3] = Expand(endpoints[e * 4 + 3], alphaPrecision);
            }
        }

        Span<byte> primary = stackalloc byte[16];
        Span<byte> secondary = stackalloc byte[16];
        for (int i = 0; i < 16; i++)
        {
            primary[i] = (byte)bits.Read(BcPartitions.IsAnchor(subsets, partition, i) ? indexBits - 1 : indexBits);
        }

        if (indexBits2 != 0)
        {
            for (int i = 0; i < 16; i++)
            {
                secondary[i] = (byte)bits.Read(i == 0 ? indexBits2 - 1 : indexBits2);
            }
        }

        ReadOnlySpan<byte> colorWeights = BcPartitions.Weights(indexBits);
        ReadOnlySpan<byte> alphaWeights = BcPartitions.Weights(indexBits2 != 0 ? indexBits2 : indexBits);
        for (int i = 0; i < 16; i++)
        {
            int subset = BcPartitions.Subset(subsets, partition, i);
            ReadOnlySpan<int> e0 = endpoints.Slice(subset * 8, 4);
            ReadOnlySpan<int> e1 = endpoints.Slice(subset * 8 + 4, 4);
            int colorWeight, alphaWeight;
            if (indexBits2 == 0)
            {
                colorWeight = alphaWeight = colorWeights[primary[i]];
            }
            else if (indexSelection == 0)
            {
                colorWeight = colorWeights[primary[i]];
                alphaWeight = alphaWeights[secondary[i]];
            }
            else
            {
                colorWeight = alphaWeights[secondary[i]];
                alphaWeight = colorWeights[primary[i]];
            }

            var px = new Rgba32(
                Interpolate(e0[0], e1[0], colorWeight),
                Interpolate(e0[1], e1[1], colorWeight),
                Interpolate(e0[2], e1[2], colorWeight),
                Interpolate(e0[3], e1[3], alphaWeight));
            pixels[i] = rotation switch
            {
                1 => new Rgba32(px.A, px.G, px.B, px.R),
                2 => new Rgba32(px.R, px.A, px.B, px.G),
                3 => new Rgba32(px.R, px.G, px.A, px.B),
                _ => px,
            };
        }
    }

    private static int Expand(int value, int precision) => (value << (8 - precision)) | (value >> (2 * precision - 8));

    private static byte Interpolate(int e0, int e1, int weight) => (byte)(((64 - weight) * e0 + weight * e1 + 32) >> 6);
}
