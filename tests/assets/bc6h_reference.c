/* Decodes the first surface of a DX10 BC6H DDS file with bcdec and prints RGBA bytes to stdout.
   Halves are reduced to 8 bits like the plugin and Pillow: clamped to [0, 1] and truncated after * 255.
   Build: cc -O2 -I reference/bcdec tests/assets/bc6h_reference.c -o bc6h_reference (make_reference.py does it). */
#define BCDEC_IMPLEMENTATION
#include "bcdec.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static float half_to_float(unsigned short h) {
    unsigned sign = (h >> 15) & 1, exp = (h >> 10) & 31, mant = h & 1023;
    float v = exp == 0 ? ldexpf((float)mant, -24) : exp == 31 ? 65504.0f * 2 : ldexpf((float)(mant | 1024), (int)exp - 25);
    return sign ? -v : v;
}

static unsigned char to_byte(unsigned short h) {
    float f = half_to_float(h);
    if (!(f > 0.0f)) return 0;
    return f >= 1.0f ? 255 : (unsigned char)(f * 255.0f);
}

int main(int argc, char** argv) {
    FILE* f = fopen(argv[1], "rb");
    if (!f) return 1;
    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);
    unsigned char* data = malloc(size);
    if (fread(data, 1, size, f) != (size_t)size) return 1;
    unsigned h = *(unsigned*)(data + 12), w = *(unsigned*)(data + 16), dxgi = *(unsigned*)(data + 128);
    int is_signed = dxgi == 96;
    unsigned bx = (w + 3) / 4, by = (h + 3) / 4;
    unsigned short* halves = malloc((size_t)bx * 4 * by * 4 * 3 * 2);
    for (unsigned y = 0; y < by; y++)
        for (unsigned x = 0; x < bx; x++)
            bcdec_bc6h_half(data + 148 + (y * bx + x) * 16, halves + ((y * 4) * bx * 4 + x * 4) * 3, bx * 4 * 3, is_signed);
    for (unsigned y = 0; y < h; y++)
        for (unsigned x = 0; x < w; x++) {
            unsigned short* p = halves + (y * bx * 4 + x) * 3;
            unsigned char px[4] = { to_byte(p[0]), to_byte(p[1]), to_byte(p[2]), 255 };
            fwrite(px, 1, 4, stdout);
        }
    return 0;
}
