# PictureSharp.Formats.Extra: plan

Wtyczka z dodatkowymi formatami dla PictureSharp: QOI, PNM i TGA (odczyt i zapis), DDS oraz TIFF (tylko odczyt).
Osobne repozytorium i własny SemVer, wzorowane na `PictureSharp.Heif`. Zależność wyłącznie od publicznego pakietu
`PictureSharp` (minimum 0.7.1). Jedyny wyjątek od zasady „bez kopii kodu rdzenia”: kopia dekompresji zlib dla TIFF
(decyzja użytkownika z 2026-10-17), synchronizowana skryptem.

## Wersje

| Wersja | Zakres | Status |
| --- | --- | --- |
| 0.1.0 | Szkielet repo, QOI, PNM, TGA (odczyt i zapis) | W toku: kod i testy gotowe, brak commita i wydania |
| 0.2.0 | DDS: DX9/DX10, nieskompresowane, BC1–BC5, BC7, BC6H; zapis BGRA8/BGRX8 | Planowana |
| 0.3.0 | TIFF podstawowy: IFD, paski i kafle, None/PackBits/LZW/Deflate, predyktor 2, gray/palette/RGB(A) 8/16 bit | Planowana |
| 0.4.0 | TIFF rozszerzony: JPEG (7), CCITT G3/G4, predyktor 3 i float, CMYK, YCbCr, associated alpha, EXIF, BigTIFF | Planowana |

## 0.1.0 — QOI, PNM, TGA

Zrobione:
- `ExtraFormats.AddExtraFormats()` i `CreateModules()`; `QoiFormat`, `PnmFormat`, `TgaFormat` w
  `PictureSharp.Formats.<Format>`, opcje enkoderów, skróty `SaveAsXxx`/`ToXxx`/`ImageFormat.Xxx`.
- QOI: port `qoi.h` (`qoi_decode`, `qoi_encode`); kolorystyka linear ↔ `ImageMetadata.Gamma = 1`; plik 3-kanałowy
  zawsze nieprzezroczysty.
- PNM: P1–P6 (plain i binarne), PAM P7 (BLACKANDWHITE, GRAYSCALE, RGB i warianty `_ALPHA`), maxval 1–65535,
  skalowanie jak Pillow (`round(v / maxval * 255)` w double, połówki do parzystej); zapis PBM/PGM/PPM/PAM, `PnmKind.Auto`.
- TGA: typy 1/2/3 i RLE (pakiety mogą przekraczać wiersze), 8/15/16/24/32 bpp, palety 15/16/24/32, orientacja, obszar
  rozszerzeń TGA 2.0 (brak alfy, alfa premultiplikowana); zapis 8-bit szary/24/32 z RLE w obrębie wiersza.
- Testy (279): korpus 44 plików = `reference.json` (Pillow 10.2.0, ImageMagick dla PAM i palet 32-bit), bajtowa
  zgodność enkodera QOI z portem `qoi_encode`, round-trip, przypadki brzegowe, fuzzing (`Slow`), `MaxPixels`,
  anulowanie, architektura, snapshot API, Oracle: pliki z enkoderów czytane przez Pillow 10.2.0 (10 przypadków).

Pozostało do DoD:
- Wyrocznia `qoi.h` (oryginalny `qoiconv`) zamiast portu w Pythonie — wymaga zgody na pobranie `qoi.h` do `reference/`.
- CI na GitHubie (workflow gotowy, nieuruchomiony), pierwszy commit, release notes i decyzja o publikacji NuGet.
- Smoke test konsumenta z trimmingiem i Native AOT (wzór: `verify-package-consumer.sh` rdzenia).

## 0.2.0 — DDS

- Nagłówek DX9 i DX10; tylko poziom mip 0, pierwsza ściana/warstwa (wybór wymaga opcji dekodera per wtyczka).
- Dekodery bloków BC1–BC5, BC7, BC6H (→ 8 bit z obcięciem do [0, 1]); skalarne, SIMD tylko z testem równości.
- DoD: piksele = Pillow (BC1–BC5, BC7) i dekoder referencyjny (bcdec) dla BC6H; zapis BGRA8 round-trip; próbki
  BC6H/BC7 z `texconv`/`nvcompress` lub external po zgodzie użytkownika.

## 0.3.0 i 0.4.0 — TIFF (tylko odczyt)

- Pierwsza strona w pełnej rozdzielczości (pomija IFD z bitem reduced-resolution); `ContainerItemCount` = liczba stron.
- LZW: MSB-first z „early change” i zgodność ze starym LZW LSB-first (libtiff `tif_lzw.c`). Deflate: kopia `Inflater`
  rdzenia + `scripts/sync-compression.sh` (`--check` w CI). JPEG (7): `JPEGTables` + pasek → `DecodeEmbedded`.
- OJPEG, JBIG, LZMA, ZSTD, WebP-in-TIFF, Lab/LogLuv → `NotSupportedException`.
- DoD: korpus z `tiffcp` (`-c none|packbits|lzw:2|zip:2|jpeg|g3|g4`, `-t`, `-p separate`) i ImageMagick; piksele =
  Pillow; fuzz IFD (pętle, offsety poza plikiem, ogromne `StripByteCounts`).

## Zgłoszenia do rdzenia

- Opcje dekodera per wtyczka (wybór strony TIFF, poziomu mip/warstwy DDS): rozszerza istniejący wpis w tabeli luk 0.8.0.
