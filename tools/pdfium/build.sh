#!/usr/bin/env bash
# Builds pdfium.a for .NET 10 browser-wasm (emscripten 3.1.56, wasm EH + wasm SjLj, no pthreads, no V8/XFA/skia).
set -euo pipefail

PDFIUM_REV="${PDFIUM_REV:-f91ca5a72358bb0b00b4da9481b21fe668157614}" # chromium/8046
EMSDK_VERSION=3.1.56
WORK="${WORK:-$(cd "$(dirname "$0")" && pwd)/work}"
OUT="${OUT:-$(cd "$(dirname "$0")" && pwd)}"
WASM_FLAGS='"-fwasm-exceptions", "-sSUPPORT_LONGJMP=wasm", "-msimd128"'

mkdir -p "$WORK" "$OUT"
WORK="$(cd "$WORK" && pwd)" OUT="$(cd "$OUT" && pwd)"
cd "$WORK"

[ -d depot_tools ] || git clone -q https://chromium.googlesource.com/chromium/tools/depot_tools.git
export PATH="$WORK/depot_tools:$PATH" DEPOT_TOOLS_UPDATE=0 DEPOT_TOOLS_METRICS=0

# The upstream emsdk is required: the clang in .NET's emscripten pack emits __wasm_setjmp/__wasm_setjmp_test,
# while the pack's libcompiler_rt-wasm-sjlj.a only provides saveSetjmp/testSetjmp, which 3.1.56's own LLVM emits.
[ -d emsdk ] || git clone -q https://github.com/emscripten-core/emsdk.git
emsdk/emsdk install "$EMSDK_VERSION" >/dev/null
emsdk/emsdk activate "$EMSDK_VERSION" >/dev/null
EMSCRIPTEN="$WORK/emsdk/upstream/emscripten"

mkdir -p src
cd src
[ -f .gclient ] || gclient config --unmanaged --custom-var checkout_configuration=minimal https://pdfium.googlesource.com/pdfium.git
for repo in pdfium pdfium/build; do
  if [ -d "$repo/.git" ]; then git -C "$repo" reset -q --hard; git -C "$repo" clean -qdf; fi
done
gclient sync -r "$PDFIUM_REV" --no-history --shallow -D --force

cd pdfium
python3 - "$EMSCRIPTEN" "$WASM_FLAGS" <<'EOF'
import pathlib, sys

emscripten, flags = sys.argv[1], sys.argv[2]

def patch(path, old, new):
    p = pathlib.Path(path)
    text = p.read_text()
    if old not in text:
        sys.exit(f"patch target not found in {path}: {old!r}")
    p.write_text(text.replace(old, new, 1))

patch("build/config/BUILDCONFIG.gn",
      '''} else if (target_os == "emscripten") {
  # Because it's too hard to remove all targets from //BUILD.gn that do not work
  # with it.
  assert(
      false,
      "emscripten is not a supported target_os. It is available only as secondary toolchain.")
''',
      '''} else if (target_os == "emscripten") {
  _default_toolchain = "//build/toolchain/wasm:$target_cpu"
''')

patch("build/config/compiler/BUILD.gn",
      '''    configs += [ "//build/config/mac:compiler" ]
''',
      '''    configs += [ "//build/config/mac:compiler" ]
  } else if (is_wasm) {
    configs += [ "//build/config/wasm:compiler" ]
''')

patch("build/config/compiler/BUILD.gn",
      '''      } else if (is_posix || is_fuchsia) {
        if (current_os != "aix") {''',
      '''      } else if (is_posix || is_fuchsia) {
        if (current_os != "aix" && !is_wasm) {''')

pathlib.Path("build/config/wasm").mkdir(exist_ok=True)
pathlib.Path("build/config/wasm/BUILD.gn").write_text(f'''config("compiler") {{
  defines = [ "_POSIX_C_SOURCE=200112" ]
  cflags = [ {flags} ]
  ldflags = [ {flags} ]
}}
''')

patch("build/toolchain/wasm/BUILD.gn",
      'emscripten_path = "//third_party/emsdk/upstream/emscripten/"',
      f'emscripten_path = "{emscripten}"')

patch("build/toolchain/wasm/BUILD.gn",
      "  toolchain_args = {",
      '''  extra_cflags = "-Wno-unknown-warning-option"
  extra_cxxflags = "-Wno-unknown-warning-option"

  toolchain_args = {''')

patch("core/fxge/BUILD.gn", "if (is_linux || is_chromeos) {", "if (is_linux || is_chromeos || is_wasm) {")
EOF

ARGS='target_os="emscripten" target_cpu="wasm" is_debug=false symbol_level=0 is_component_build=false
  is_clang=false use_custom_libcxx=false treat_warnings_as_errors=false
  pdf_is_complete_lib=true pdf_use_partition_alloc=false pdf_use_skia=false pdf_enable_v8=false pdf_enable_xfa=false'
GN_HOST=$([ "$(uname)" = Darwin ] && echo mac || echo linux64)
buildtools/$GN_HOST/gn gen out/wasm --args="$(echo $ARGS)"
third_party/ninja/ninja -C out/wasm pdfium

# pdfium bundles its own libjpeg-turbo, zlib, freetype, libpng, openjpeg, lcms, ICU and abseil, which clash with or
# would silently bind to the copies in SkiaSharp, HarfBuzzSharp, e_sqlite3 and the runtime's libz. The archive is
# partially linked into one object whose symbols are all made local except the public API (FPDF*, FORM_*, FSDK_*)
# and the __c_longjmp tag, which has to stay shared with the runtime's wasm SjLj. Comdat groups are dropped so the
# final link cannot swap pdfium's inline copies for someone else's.
LLVM="$WORK/emsdk/upstream/bin"
"$LLVM/wasm-ld" -r --whole-archive out/wasm/obj/libpdfium.a -o out/wasm/pdfium-merged.o
python3 - out/wasm/pdfium-merged.o out/wasm/pdfium.o <<'EOF'
import re, sys

WEAK, LOCAL, UNDEFINED = 0x1, 0x2, 0x10
FUNCTION, DATA, GLOBAL, SECTION, TAG, TABLE = range(6)
SYMBOL_TABLE, COMDAT_INFO = 8, 7
PUBLIC = re.compile(rb"^(FPDF|FORM_|FSDK_)")


def leb(data, pos):
    value = shift = 0
    while True:
        byte = data[pos]
        pos += 1
        value |= (byte & 0x7F) << shift
        shift += 7
        if byte < 0x80:
            return value, pos


def uleb(value):
    out = bytearray()
    while True:
        byte = value & 0x7F
        value >>= 7
        out.append(byte | (0x80 if value else 0))
        if not value:
            return bytes(out)


def name(data, pos):
    size, pos = leb(data, pos)
    return data[pos:pos + size], pos + size


def symbol_table(payload, stats):
    count, pos = leb(payload, 0)
    out = bytearray(uleb(count))
    for _ in range(count):
        start = pos
        kind = payload[pos]
        flags, pos = leb(payload, pos + 1)
        body = pos
        sym = b""
        if kind in (FUNCTION, GLOBAL, TAG, TABLE):
            _, pos = leb(payload, pos)
            if not flags & UNDEFINED or flags & 0x40:
                sym, pos = name(payload, pos)
        elif kind == DATA:
            sym, pos = name(payload, pos)
            if not flags & UNDEFINED:
                for _ in range(3):
                    _, pos = leb(payload, pos)
        else:
            _, pos = leb(payload, pos)
        if kind in (FUNCTION, DATA, GLOBAL) and not flags & (UNDEFINED | LOCAL) and not PUBLIC.match(sym):
            flags = (flags | LOCAL) & ~WEAK
            stats["localized"] += 1
        elif kind != SECTION and not flags & (UNDEFINED | LOCAL) and not PUBLIC.match(sym):
            stats["kept"].append(sym.decode())
        out += bytes([kind]) + uleb(flags) + payload[body:pos]
    return bytes(out)


def main(src, dst):
    data = open(src, "rb").read()
    assert data[:8] == b"\0asm\1\0\0\0"
    out = bytearray(data[:8])
    pos = 8
    stats = {"localized": 0, "kept": []}
    while pos < len(data):
        sid = data[pos]
        size, body = leb(data, pos + 1)
        end = body + size
        section = data[pos:end]
        if sid == 0:
            label, sub = name(data, body)
            if label == b"linking":
                version, sub = leb(data, sub)
                linking = bytearray(uleb(len(label)) + label + uleb(version))
                while sub < end:
                    kind = data[sub]
                    length, payload = leb(data, sub + 1)
                    chunk = data[payload:payload + length]
                    sub = payload + length
                    if kind == COMDAT_INFO:
                        continue
                    if kind == SYMBOL_TABLE:
                        chunk = symbol_table(chunk, stats)
                    linking += bytes([kind]) + uleb(len(chunk)) + chunk
                section = bytes([0]) + uleb(len(linking)) + linking
        out += section
        pos = end
    open(dst, "wb").write(out)
    print(f"localized {stats['localized']} symbols, kept global: {' '.join(sorted(stats['kept']))}")


main(*sys.argv[1:])
EOF
rm -f "$OUT/pdfium.a"
"$LLVM/llvm-ar" rcsD "$OUT/pdfium.a" out/wasm/pdfium.o
shasum -a 256 "$OUT/pdfium.a"
