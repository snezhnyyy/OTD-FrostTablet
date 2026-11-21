FrostTablet (Linux, experimental) — by tgk: snezhnyyy and claude ^^
https://github.com/snezhnyyy/OTD-FrostTablet

1. One-time setup (asks for sudo): ./install.sh
   Installs udev rules + uinput, unloads the kernel drivers that conflict (wacom, hid_uclogic).
2. Replug the tablet, then run: ./FrostTablet
   The interface starts the driver itself. To run only the driver: ./FrostTablet --daemon

If something doesn't work, open an issue with the Console log (… -> Help -> Export diagnostics).
