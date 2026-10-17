#!/bin/sh
# Runs after install/upgrade (deb: "configure", rpm: 1 = install, 2 = upgrade).
# Must be POSIX sh, idempotent, and must not fail the transaction.

# Apply the udev rule to a deck that is already plugged in (no-op without a running udevd).
if command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload-rules >/dev/null 2>&1 || true
    udevadm trigger --action=add --subsystem-match=hidraw >/dev/null 2>&1 || true
    udevadm trigger --action=add --subsystem-match=usb \
        --attr-match=idVendor=0200 --attr-match=idProduct=2000 >/dev/null 2>&1 || true
fi

exit 0
