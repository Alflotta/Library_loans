#!/usr/bin/env bash
# Builds the native library for the current platform into runtimes/<rid>/native.
# Needs gcc (or clang). Run from any folder: bash native/build.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
case "$(uname -s)" in
  Linux)  rid=linux-x64;  file=libraryloans_native.so;    flags="-shared -fPIC" ;;
  Darwin) rid=osx-x64;    file=libraryloans_native.dylib; flags="-dynamiclib" ;;
  MINGW*|MSYS*|CYGWIN*) rid=win-x64; file=libraryloans_native.dll; flags="-shared" ;;
  *) echo "Unsupported platform"; exit 1 ;;
esac
mkdir -p "$root/runtimes/$rid/native"
${CC:-gcc} -O2 -Wall -Wextra -std=c99 $flags -o "$root/runtimes/$rid/native/$file" "$here/libraryloans_native.c"
echo "Built runtimes/$rid/native/$file"
