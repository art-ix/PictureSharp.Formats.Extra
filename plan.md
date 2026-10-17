# PictureSharp.Formats.Extra: plan

Wtyczka z dodatkowymi formatami dla PictureSharp: QOI, PNM i TGA (odczyt i zapis) oraz DDS. Osobne repozytorium
i własny SemVer, wzorowane na `PictureSharp.Heif`. Zależność wyłącznie od publicznego pakietu `PictureSharp`
(minimum 0.7.1), bez kopii kodu rdzenia.

TIFF przeniesiono do osobnego pakietu `PictureSharp.Tiff` (repozytorium `../PictureSharp.Tiff`, decyzja użytkownika
z 2026-10-17): to większy format dla innych odbiorców, z kopią dekompresji zlib z rdzenia i własnym rytmem wydań.

## Wersje

| Wersja | Zakres | Status |
| --- | --- | --- |
| 0.1.0 | Szkielet repo, QOI, PNM, TGA (odczyt i zapis) | W toku: kod i testy gotowe (commit b891c11), bez CI i wydania |
| 0.2.0 | DDS: DX9/DX10, nieskompresowane, BC1–BC5, BC7, BC6H; zapis BGRA8/BGRX8 | Planowana |

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
- CI na GitHubie (workflow gotowy, nieuruchomiony; brak remote), release notes i decyzja o publikacji NuGet.
- Smoke test konsumenta z trimmingiem i Native AOT (wzór: `verify-package-consumer.sh` rdzenia).

## 0.2.0 — DDS

- Nagłówek DX9 i DX10; tylko poziom mip 0, pierwsza ściana/warstwa (wybór wymaga opcji dekodera per wtyczka).
- Dekodery bloków BC1–BC5, BC7, BC6H (→ 8 bit z obcięciem do [0, 1]); skalarne, SIMD tylko z testem równości.
- DoD: piksele = Pillow (BC1–BC5, BC7) i dekoder referencyjny (bcdec) dla BC6H; zapis BGRA8 round-trip; próbki
  BC6H/BC7 z `texconv`/`nvcompress` lub external po zgodzie użytkownika.

## Zgłoszenia do rdzenia

- Opcje dekodera per wtyczka (wybór poziomu mip/warstwy DDS): rozszerza istniejący wpis w tabeli luk 0.8.0.
