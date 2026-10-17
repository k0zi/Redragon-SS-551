#!/bin/sh
# Runs after removal and upgrades (deb: "remove|purge|upgrade", rpm: 0 = erase, 1 = upgrade).
# Must be POSIX sh, idempotent, and must not fail the transaction.

# The udev rule file is gone on removal; drop it from udevd's cache too.
case "$1" in
    remove|purge|0)
        if command -v udevadm >/dev/null 2>&1; then
            udevadm control --reload-rules >/dev/null 2>&1 || true
        fi
        ;;
esac

exit 0
