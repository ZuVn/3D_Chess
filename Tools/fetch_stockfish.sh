#!/usr/bin/env bash
# Downloads the official Stockfish 19 binaries used by the Professional difficulty.
set -euo pipefail
cd "$(dirname "$0")/../ThirdParty/Stockfish"
base=https://github.com/official-stockfish/Stockfish/releases/download/sf_19
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
curl -fsSL "$base/stockfish-linux-x86-64-universal.tar.gz" -o "$tmp/lin.tar.gz"
curl -fsSL "$base/stockfish-windows-x86-64-universal.zip" -o "$tmp/win.zip"
mkdir -p "$tmp/lin" "$tmp/win" linux windows
tar xzf "$tmp/lin.tar.gz" -C "$tmp/lin"
unzip -q "$tmp/win.zip" -d "$tmp/win"
install -m 755 "$tmp/lin/stockfish/stockfish-linux-x86-64-universal" linux/stockfish
install -m 644 "$tmp/win/stockfish/stockfish-windows-x86-64-universal.exe" windows/stockfish.exe
cp "$tmp/lin/stockfish/Copying.txt" COPYING.txt
cp "$tmp/lin/stockfish/AUTHORS" AUTHORS
echo "Stockfish 19 installed in ThirdParty/Stockfish/{linux,windows}"
