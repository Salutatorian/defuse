#!/bin/sh
set -eu
root="${1:?publish directory}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cd "$work"
for pkg in libvlc5 libvlccore9 vlc-plugin-base vlc-plugin-video-output; do
  if apt-cache show "$pkg" >/dev/null 2>&1; then
    apt-get download "$pkg"
  fi
done
mkdir extracted
for deb in *.deb; do
  dpkg-deb -x "$deb" extracted
done
libdir="$(find extracted/usr/lib -maxdepth 1 -type d -name '*-linux-gnu' | head -n 1)"
mkdir -p "$root/lib" "$root/plugins"
cp -a "$libdir"/libvlc.so* "$libdir"/libvlccore.so* "$root/lib/"
plugin_dir="$(find extracted -type d -path '*/vlc/plugins' | head -n 1)"
cp -a "$plugin_dir"/. "$root/plugins/"
