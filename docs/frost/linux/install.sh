#!/usr/bin/env bash
# FrostTablet — one-time Linux setup (needs sudo). Mirrors what the official OpenTabletDriver packages do:
#  - udev rules so your user can read the tablet (hidraw) and create a virtual pointer (uinput)
#  - stops the kernel tablet drivers that would fight over the device (wacom, hid_uclogic)
#  - libinput quirks so the virtual tablet is treated correctly
set -euo pipefail
cd "$(dirname "$0")"

echo "Installing udev rules, kernel module settings and libinput quirks (sudo)…"
sudo install -Dm644 70-opentabletdriver.rules        /etc/udev/rules.d/70-opentabletdriver.rules
sudo install -Dm644 99-opentabletdriver.conf         /etc/modprobe.d/99-opentabletdriver.conf
sudo install -Dm644 opentabletdriver.conf            /etc/modules-load.d/opentabletdriver.conf
sudo install -Dm644 30-vendor-opentabletdriver.quirks /etc/libinput/local-overrides.quirks.d/30-vendor-opentabletdriver.quirks 2>/dev/null \
  || sudo install -Dm644 30-vendor-opentabletdriver.quirks /usr/share/libinput/30-vendor-opentabletdriver.quirks

for module in hid_uclogic hid_digimend_uclogic wacom; do
  if lsmod | grep -q "^$module"; then sudo rmmod "$module" || true; fi
done
lsmod | grep -q '^uinput' || sudo modprobe uinput

sudo udevadm control --reload-rules
sudo udevadm trigger && sudo udevadm settle -t 15 || true

chmod +x ./FrostTablet
echo
echo "Done. Unplug and replug your tablet, then run ./FrostTablet"
