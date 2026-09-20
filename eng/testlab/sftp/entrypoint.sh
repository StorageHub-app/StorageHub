#!/bin/sh
# Builds one SSH server to order: a user, a home with the directory the fixtures browse, the host
# key whose fingerprint the tests pin, and exactly one accepted way to authenticate.
set -eu

user="${SFTP_USER:-storagehub}"
password="${SFTP_PASSWORD:-}"
mode="${AUTH_MODE:-password}"

if ! id "${user}" >/dev/null 2>&1; then
    adduser --disabled-password --gecos "" --shell /bin/bash "${user}" >/dev/null
fi

if [ -n "${password}" ]; then
    echo "${user}:${password}" | chpasswd
fi

# The provider fixtures connect with Root = "mounted", and CodeLogic resolves that against the
# server's filesystem root rather than the login directory -- verified by watching where the
# conformance run actually created its files, which was /mounted and not ~/mounted.
home="$(getent passwd "${user}" | cut -d: -f6)"
mkdir -p /mounted
chown "${user}:${user}" /mounted

# Copied rather than used in place: sshd refuses a private host key that is group or world
# readable, and the permissions of a bind mount come from the Windows host, which has no say in
# them. Copying puts the key on the container's own filesystem where chmod means something.
copy_host_key() {
    if [ -f "/fixtures/${1}" ]; then
        cp "/fixtures/${1}" "/etc/ssh/ssh_host_ed25519_key"
        chmod 600 /etc/ssh/ssh_host_ed25519_key
        ssh-keygen -y -f /etc/ssh/ssh_host_ed25519_key > /etc/ssh/ssh_host_ed25519_key.pub
        chmod 644 /etc/ssh/ssh_host_ed25519_key.pub
    fi
}
copy_host_key "${HOST_KEY_NAME:-hostkey}"

if [ ! -f /etc/ssh/ssh_host_ed25519_key ]; then
    echo "entrypoint: no host key was mounted at /fixtures/${HOST_KEY_NAME:-hostkey}" >&2
    exit 1
fi

# Exactly one way in per server, and the fixtures check both halves of that: a password is refused
# on the private-key port, and a private key is refused on the password port. A server that quietly
# accepted both would turn each of those assertions into a test of nothing.
#
# This is also why the terminal fixture works against the password port while holding a key as well
# as a password: SSH.NET offers the key, the server declines the method, and it falls back.
case "${mode}" in
    key)
        password_auth="no"
        pubkey_auth="yes"
        ;;
    *)
        password_auth="yes"
        pubkey_auth="no"
        ;;
esac

# The authorized key is published only where public-key authentication is actually on. All three
# servers mount the same fixture directory, so leaving this ungated would hand the key to the
# password server too and let it accept a login it is supposed to refuse.
if [ "${pubkey_auth}" = "yes" ] && [ -f /fixtures/client.pub ]; then
    mkdir -p "${home}/.ssh"
    cp /fixtures/client.pub "${home}/.ssh/authorized_keys"
    chmod 700 "${home}/.ssh"
    chmod 600 "${home}/.ssh/authorized_keys"
    chown -R "${user}:${user}" "${home}/.ssh"
fi

cat > /etc/ssh/sshd_config <<EOF
Port 22
AddressFamily any
ListenAddress 0.0.0.0

HostKey /etc/ssh/ssh_host_ed25519_key

PermitRootLogin no
PasswordAuthentication ${password_auth}
PubkeyAuthentication ${pubkey_auth}
KbdInteractiveAuthentication no
UsePAM yes
AllowUsers ${user}

Subsystem sftp internal-sftp

# A throwaway container on a loopback port. Keeping sessions alive costs nothing here and stops a
# terminal test losing its shell while it waits for output.
ClientAliveInterval 30
ClientAliveCountMax 10
LogLevel VERBOSE
EOF

exec /usr/sbin/sshd -D -e
