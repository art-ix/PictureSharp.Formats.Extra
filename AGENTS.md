# Working on PictureSharp.Formats.Extra

PictureSharp.Formats.Extra is a format plugin for [PictureSharp](https://github.com/art-ix/PictureSharp) that adds QOI,
Netpbm and TGA (and later DDS and read-only TIFF) entirely in C#. It is a separate repository and package with its own
version; it uses PictureSharp only through its public NuGet API.

Before starting, read [plan.md](plan.md) to identify the current version and its definition of done (DoD), then read
the latest entries in [progres.md](progres.md). Append progress after significant steps and before ending a session:
changes, decisions, test results, remaining work, and the next step. Preserve earlier entries. `plan.md` tracks scope
and status; `progres.md` is a local log and must stay out of commits. Changes the plugin needs in PictureSharp itself
go to the core repository's `plan.md` (sibling folder `../PictureSharp`).

## Required constraints

- Target `net10.0` with C# 14 (`LangVersion=14.0`); support .NET 10 and later.
- `src/PictureSharp.Formats.Extra` depends only on the `PictureSharp` package (minimum version in
  `Directory.Build.props`) and the BCL: no other `PackageReference`, native libraries, P/Invoke or
  `System.IO.Compression`. Use only public PictureSharp API; no `InternalsVisibleTo` or reflection. An architecture
  test checks this.
- One deliberate exception, decided by the user: TIFF needs zlib inflate, so `Compression/` will hold a copy of the
  core's `Inflater`, Huffman tables and `Adler32`, kept in sync by `scripts/sync-compression.sh` (with `--check` in
  CI). Copy nothing else from the core.
- Do not use the `unsafe` keyword. Use `Span<T>`, `ref`, `Unsafe.Add` and `MemoryMarshal`.
- Each format is a public `XxxFormat : IImageFormatModule` in `PictureSharp.Formats.<Format>`, with an
  `XxxEncoderOptions` record and extension blocks (`SaveAsXxx`, `ToXxx`, `ImageFormat.Xxx`). Codecs are `internal`.
  `ExtraFormats.AddExtraFormats` registers every format.
- Return `Image` pixels as `Rgba32` with straight alpha. Reduce deeper samples to 8 bits with the documented rounding.
- Call `ImageDecodeContext.ReservePixels` / `ReserveMetadata` before allocating and check cancellation between rows.
  Corrupt input must throw `ImageFormatException`; valid input with unsupported features must throw
  `NotSupportedException`. Malicious input must never expose other exceptions; `RobustnessTests` fuzz the corpus.

## Code and layout

Follow `.editorconfig`. Enable nullable annotations, `TreatWarningsAsErrors` and XML documentation for public API. Use
English for identifiers, comments and XML docs; project documents may use Polish. Keep types focused, match filenames
to the main type, give public enums separate files, keep hot loops allocation-free (`ArrayPool<T>.Shared`, the pooled
`Common/OutputBuffer` for encoders) and write sparse comments that explain why or name the port source
(`// port: qoi.h qoi_decode`).

| Path | Purpose |
| --- | --- |
| `src/PictureSharp.Formats.Extra/` | Plugin: one folder per format plus `Common/` |
| `tests/PictureSharp.Formats.Extra.Tests/` | TUnit tests |
| `tests/assets/` | Corpus, `reference.json`, public API snapshot and generation scripts |
| `reference/` | Uncommitted reference sources and tools |

## Reference data and oracles

- Pixels must match `tests/assets/reference.json` exactly. Regenerate with Pillow 10.2.0 and ImageMagick:

  ```bash
  python3 -I tests/assets/generate_corpus.py
  python3 -I tests/assets/make_reference.py
  ```

- `Category=Oracle` tests decode the encoders' output in Pillow 10.2.0 and skip when it is missing. `Category=Slow`
  marks fuzzing. Obtain user approval before downloading external corpora or reference sources.
- Maintain `THIRD-PARTY-NOTICES.txt` for ported sources; the NuGet package includes `LICENSE` and the notices.

## Build and verification

```bash
dotnet build -c Release
dotnet test -c Release
dotnet test -c Release --treenode-filter "/*/*/*/*[Category=Oracle]"
```

A public API change updates `tests/assets/PublicApi.approved.txt` from the `PublicApi.received.txt` the test writes.

## Git and versioning

- Commit and push only when the user explicitly asks.
- `VERSION` contains the SemVer version; `Directory.Build.props` applies it.
- Never commit `progres.md`, `reference/`, `tests/assets/external/`, `bin/` or `obj/`.
