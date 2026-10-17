# PictureSharp.Formats.Extra: plan

Wtyczka z dodatkowymi formatami dla PictureSharp: QOI, PNM i TGA (odczyt i zapis), DDS, KTX/KTX2, PSD/PSB, SGI,
Sun Raster oraz XBM/XPM. Osobne repozytorium
i własny SemVer, wzorowane na `PictureSharp.Heif`. Zależność wyłącznie od publicznego pakietu `PictureSharp`
(minimum 0.7.1), bez kopii kodu rdzenia.

TIFF przeniesiono do osobnego pakietu `PictureSharp.Tiff` (repozytorium `../PictureSharp.Tiff`, decyzja użytkownika
z 2026-10-17): to większy format dla innych odbiorców, z własnym rytmem wydań.

Decyzja użytkownika (2026-10-17): wtyczki mogą używać `System.IO.Compression` do dekompresji (zlib-ng jest częścią
runtime na każdej platformie). Rdzeń zrezygnuje z tej reguły w przyszłości.

## Wersje

| Wersja | Zakres | Status |
| --- | --- | --- |
| 0.1.0 | Szkielet repo, QOI, PNM, TGA (odczyt i zapis) | W toku: kod i testy gotowe (commit b891c11), bez CI i wydania |
| 0.2.0 | DDS: DX9/DX10, nieskompresowane, BC1–BC5, BC7, BC6H; zapis BGRA8/BGRX8 | Planowana |
| 0.3.0 | KTX 1 i KTX 2 (odczyt): nieskompresowane i BC1–BC7/BC6H z dekoderów DDS | Planowana |
| 0.4.0 | PSD/PSB (odczyt obrazu scalonego) | Planowana |
| 0.5.0 | SGI (odczyt i zapis), Sun Raster (odczyt), XBM (odczyt i zapis), XPM (odczyt) | Planowana |

Decyzja użytkownika (2026-10-17): po DDS dochodzą KTX/KTX2, PSD/PSB, SGI, Sun Raster i XBM/XPM. Poza zakresem: CUR
(pasuje do rdzenia obok ICO), formaty HDR (Radiance, OpenEXR — czekają na HDR SPI), AVIF/JPEG XL/JPEG 2000 (osobne
pakiety jak `PictureSharp.Heif`), BLP, FLI/FLC, DCX.

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

## 0.3.0 — KTX i KTX2

- KTX 1 (`glInternalFormat`) i KTX 2 (`vkFormat`, DFD tylko do walidacji); poziom mip 0, pierwsza warstwa/ściana jak
  w DDS. Formaty: RGBA8/BGRA8/RGB8/R8/RG8 (UNORM i SRGB), BC1–BC5, BC7, BC6H przez wspólne dekodery bloków z 0.2.0
  (przeniesione do `Common/`).
- Supercompression zlib przez `ZLibStream`. `NotSupportedException`: zstd (brak w .NET 10), BasisLZ, UASTC, ETC1/ETC2/EAC, ASTC, PVRTC, formaty
  float poza BC6H, 3D. Kolejność bajtów KTX 1 big-endian obsługiwana.
- DoD: piksele = `toktx`/libktx lub ImageMagick (zgoda na narzędzie), korpus generowany z tych samych źródeł co DDS;
  wykrywanie po identyfikatorze (12 bajtów), fuzzing nagłówków.

## 0.4.0 — PSD/PSB

- Tylko obraz scalony (sekcja Image Data); warstwy, maski i efekty pomijane (sekcje czytane jako długości). Kompresja
  raw i PackBits; ZIP (tylko w warstwach) nie jest potrzebny. PSB: 64-bitowe długości sekcji i wymiary do 300 000.
- Tryby: Bitmap, Grayscale, Indexed, RGB, CMYK (naiwna konwersja jak Pillow, bez ICC), Duotone jako szary; 8 i 16 bit
  (16 → 8 z udokumentowanym zaokrągleniem), 32-bit → `NotSupportedException`. Kanał alfa: pierwszy dodatkowy kanał
  jak Pillow; Lab i Multichannel → `NotSupportedException`.
- Metadane: ICC (zasób 1039), EXIF (1058), XMP (1060) do `ImageMetadata`; rozdzielczość (1005).
- DoD: piksele = Pillow 10.2.0 (8 bit) i ImageMagick (PSB, 16 bit); próbki z GIMP/ImageMagick/Krita, ewentualny
  korpus zewnętrzny po zgodzie użytkownika.

## 0.5.0 — SGI, Sun Raster, XBM, XPM

- SGI (`.sgi`, `.rgb`, `.rgba`, `.bw`): verbatim i RLE, 1–4 kanały, 8 i 16 bit; zapis 8-bit z RLE i bez (jak Pillow).
- Sun Raster (`.ras`): 1/8/24/32 bpp, typy standard/RLE/RGB, mapa kolorów; tylko odczyt.
- XBM: odczyt X10 i X11 (`#define`, `static char`), hotspot do metadanych; zapis X11.
- XPM: XPM3, klucze `c` (i `m`/`g` jako zapas), kolory `#RGB`/`#RRGGBB`/`#RRRRGGGGBBBB`, `None`, nazwy z tabeli X11
  (wbudowana, bez plików systemu); znaki na piksel do 4; tylko odczyt.
- DoD: piksele = Pillow 10.2.0 (SGI, Sun, XBM, XPM), ImageMagick dla wariantów, których Pillow nie czyta (Sun RLE
  24-bit, XPM z nazwami); oracle dla zapisu SGI i XBM.

Każda wersja dopisuje przestrzeń `PictureSharp.Formats.<Format>` do `ArchitectureTests`, aktualizuje snapshot API,
README i `ExtraFormats.AddExtraFormats`.

## Zgłoszenia do rdzenia

- Opcje dekodera per wtyczka (wybór poziomu mip/warstwy DDS): rozszerza istniejący wpis w tabeli luk 0.8.0.
