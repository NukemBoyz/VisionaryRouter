#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -ne 0 ]]; then
    echo "Run this installer with sudo: sudo bash install.sh" >&2
    exit 1
fi

PACKAGE_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
APP_SOURCE="${PACKAGE_DIR}/app"
APP_DIR="/opt/visionary-router"
DATA_DIR="/var/lib/visionary-router"
CONFIG_DIR="/etc/visionary-router"
PASSWORD_FILE="${CONFIG_DIR}/admin-password"
ENVIRONMENT_FILE="${CONFIG_DIR}/environment"
SERVICE_FILE="/etc/systemd/system/visionary-router.service"
SERVICE_USER="visionary-router"
SERVICE_GROUP="visionary-router"

if [[ ! -f "${APP_SOURCE}/VisionaryRouter" ]]; then
    echo "Package is incomplete: app/VisionaryRouter is missing." >&2
    exit 1
fi

PACKAGE_RUNTIME="$(tr -d '\r\n' < "${PACKAGE_DIR}/PACKAGE-RUNTIME")"
MACHINE_ARCH="$(uname -m)"
case "${MACHINE_ARCH}" in
    x86_64) EXPECTED_RUNTIME="linux-x64" ;;
    aarch64|arm64) EXPECTED_RUNTIME="linux-arm64" ;;
    *)
        echo "Unsupported CPU architecture: ${MACHINE_ARCH}" >&2
        exit 1
        ;;
esac

if [[ "${PACKAGE_RUNTIME}" != "${EXPECTED_RUNTIME}" ]]; then
    echo "Wrong package: this server needs ${EXPECTED_RUNTIME}, not ${PACKAGE_RUNTIME}." >&2
    exit 1
fi

WEB_PORT="${VISIONARY_PORT:-8088}"
if [[ -t 0 ]]; then
    read -r -p "Web UI port [${WEB_PORT}]: " ENTERED_PORT
    WEB_PORT="${ENTERED_PORT:-${WEB_PORT}}"
fi
if [[ ! "${WEB_PORT}" =~ ^[0-9]+$ ]] || (( WEB_PORT < 1024 || WEB_PORT > 65535 )); then
    echo "Web UI port must be a number from 1024 through 65535." >&2
    exit 1
fi

if ! getent group "${SERVICE_GROUP}" >/dev/null; then
    groupadd --system "${SERVICE_GROUP}"
fi
if ! id -u "${SERVICE_USER}" >/dev/null 2>&1; then
    useradd --system --gid "${SERVICE_GROUP}" --home-dir "${DATA_DIR}" \
        --shell /usr/sbin/nologin "${SERVICE_USER}"
fi

install -d -m 0755 -o root -g root "${APP_DIR}"
install -d -m 0750 -o "${SERVICE_USER}" -g "${SERVICE_GROUP}" "${DATA_DIR}"
install -d -m 0750 -o root -g "${SERVICE_GROUP}" "${CONFIG_DIR}"

if [[ ! -f "${PASSWORD_FILE}" ]]; then
    ADMIN_PASSWORD="${VISIONARY_ADMIN_PASSWORD:-}"
    if [[ -z "${ADMIN_PASSWORD}" ]]; then
        if [[ ! -t 0 ]]; then
            echo "Set VISIONARY_ADMIN_PASSWORD for a non-interactive first install." >&2
            exit 1
        fi
        while true; do
            read -r -s -p "Administrator password (minimum 8 characters): " ADMIN_PASSWORD
            echo
            read -r -s -p "Confirm administrator password: " ADMIN_PASSWORD_CONFIRM
            echo
            if (( ${#ADMIN_PASSWORD} < 8 )); then
                echo "Password must contain at least 8 characters."
            elif [[ "${ADMIN_PASSWORD}" != "${ADMIN_PASSWORD_CONFIRM}" ]]; then
                echo "Passwords did not match."
            else
                break
            fi
        done
    fi
    if (( ${#ADMIN_PASSWORD} < 8 )); then
        echo "Password must contain at least 8 characters." >&2
        exit 1
    fi
    umask 0077
    printf '%s' "${ADMIN_PASSWORD}" > "${PASSWORD_FILE}"
    unset ADMIN_PASSWORD ADMIN_PASSWORD_CONFIRM VISIONARY_ADMIN_PASSWORD
fi
chown root:"${SERVICE_GROUP}" "${PASSWORD_FILE}"
chmod 0640 "${PASSWORD_FILE}"

if systemctl list-unit-files visionary-router.service >/dev/null 2>&1; then
    systemctl stop visionary-router.service || true
fi

find "${APP_DIR}" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
cp -a "${APP_SOURCE}/." "${APP_DIR}/"
chown -R root:root "${APP_DIR}"
find "${APP_DIR}" -type d -exec chmod 0755 {} +
find "${APP_DIR}" -type f -exec chmod 0644 {} +
chmod 0755 "${APP_DIR}/VisionaryRouter"

cat > "${ENVIRONMENT_FILE}" <<EOF
Router__DataDirectory=${DATA_DIR}
Router__OpenBrowserOnStart=false
Router__InitialListenOnAllInterfaces=true
Router__WebPort=${WEB_PORT}
Router__AdminPasswordFile=${PASSWORD_FILE}
EOF
chown root:"${SERVICE_GROUP}" "${ENVIRONMENT_FILE}"
chmod 0640 "${ENVIRONMENT_FILE}"

install -m 0644 -o root -g root "${PACKAGE_DIR}/visionary-router.service" "${SERVICE_FILE}"
install -m 0755 -o root -g root "${PACKAGE_DIR}/uninstall.sh" \
    /usr/local/sbin/visionary-router-uninstall

systemctl daemon-reload
systemctl enable --now visionary-router.service

ACTIVE_PORT=""
for _ in {1..20}; do
    if systemctl is-active --quiet visionary-router.service; then
        ACTIVE_PORT="$(journalctl -u visionary-router.service -n 40 --no-pager 2>/dev/null | \
            sed -n 's/.*Web UI: http:\/\/localhost:\([0-9][0-9]*\).*/\1/p' | tail -n 1)"
        [[ -n "${ACTIVE_PORT}" ]] && break
    fi
    sleep 0.25
done

if ! systemctl is-active --quiet visionary-router.service; then
    echo "Visionary Router did not start. Recent service log:" >&2
    journalctl -u visionary-router.service -n 30 --no-pager >&2
    exit 1
fi

ACTIVE_PORT="${ACTIVE_PORT:-${WEB_PORT}}"

OPEN_FIREWALL="${VISIONARY_OPEN_FIREWALL:-}"
if command -v ufw >/dev/null && ufw status | grep -q '^Status: active'; then
    if [[ -z "${OPEN_FIREWALL}" && -t 0 ]]; then
        read -r -p "Allow TCP ${ACTIVE_PORT} through UFW? [Y/n]: " FIREWALL_REPLY
        [[ "${FIREWALL_REPLY:-Y}" =~ ^[Yy]$ ]] && OPEN_FIREWALL=1
    fi
    if [[ "${OPEN_FIREWALL}" == "1" ]]; then
        ufw allow "${ACTIVE_PORT}/tcp" comment 'Visionary Router'
    fi
fi

echo
echo "Visionary Router is installed and running."
echo "Service: systemctl status visionary-router"
echo "Logs:    journalctl -u visionary-router -f"
echo "Login:   admin"
for ADDRESS in $(hostname -I 2>/dev/null); do
    echo "Web UI:  http://${ADDRESS}:${ACTIVE_PORT}"
done
