# Builds the native library on Windows (x64) into runtimes\win-x64\native.
# Option 1: gcc from MinGW-w64 / MSYS2 is on PATH. Option 2: run from "x64 Native Tools Command Prompt" (cl.exe).
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$out = Join-Path $root "runtimes\win-x64\native"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$dll = Join-Path $out "libraryloans_native.dll"
if (Get-Command gcc -ErrorAction SilentlyContinue) {
    gcc -O2 -Wall -Wextra -std=c99 -shared -o $dll (Join-Path $here "libraryloans_native.c")
} elseif (Get-Command cl -ErrorAction SilentlyContinue) {
    cl /nologo /O2 /LD (Join-Path $here "libraryloans_native.c") /Fe:$dll
} else {
    throw "Neither gcc nor cl.exe was found. Install MinGW-w64 (MSYS2) or Visual Studio Build Tools."
}
Write-Host "Built $dll"
