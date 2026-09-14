#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -ne 0 ]]; then
    echo "Run with sudo: sudo visionary-router-uninstall" >&2
    exit 1
fi

PURGE=false
[[ "${1:-}" == "--purge" ]] && PURGE=true

systemctl disable --now visionary-router.service 2>/dev/null || true
rm -f -- /etc/systemd/system/visionary-router.service
systemctl daemon-reload
rm -rf -- /opt/visionary-router

if [[ "${PURGE}" == "true" ]]; then
    rm -rf -- /var/lib/visionary-router /etc/visionary-router
    rm -f -- /usr/local/sbin/visionary-router-uninstall
    userdel visionary-router 2>/dev/null || true
    groupdel visionary-router 2>/dev/null || true
    echo "Visionary Router and its saved data were removed."
else
    echo "Visionary Router was removed. Saved data remains in /var/lib/visionary-router."
    echo "Run 'sudo visionary-router-uninstall --purge' before uninstalling to remove data too."
fi
