FrostTablet (macOS, experimental) — by tgk: snezhnyyy and claude ^^
https://github.com/snezhnyyy/OTD-FrostTablet

1. The build is not notarized. Remove the quarantine flag once:
   xattr -dr com.apple.quarantine FrostTablet
2. Run ./FrostTablet
3. Allow "Accessibility" and "Input Monitoring" in System Settings -> Privacy & Security, then restart it.

If something doesn't work, open an issue with the Console log (… -> Help -> Export diagnostics).
