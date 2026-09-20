#!/bin/sh
# Builds one FTP endpoint to order: a user, the directory the fixtures browse, a passive port range
# that matches what compose published, and exactly one TLS policy.
set -eu

user="${FTP_USER:-storagehub}"
password="${FTP_PASSWORD:-}"
mode="${TLS_MODE:-plain}"
pasv_min="${PASV_MIN_PORT:-30000}"
pasv_max="${PASV_MAX_PORT:-30009}"

if ! id "${user}" >/dev/null 2>&1; then
    # A real shell, because vsftpd's PAM stack runs pam_shells and refuses a login whose shell is
    # not listed in /etc/shells. /usr/sbin/nologin would be the tidier choice and does not work.
    adduser --disabled-password --gecos "" --shell /bin/bash "${user}" >/dev/null
fi

if [ -n "${password}" ]; then
    echo "${user}:${password}" | chpasswd
fi

# The same root the SFTP servers use, and for the same reason: CodeLogic resolves Root = "mounted"
# against the filesystem root rather than the login directory.
mkdir -p /mounted
chown "${user}:${user}" /mounted

tls_settings=""
case "${mode}" in
    plain)
        tls_settings="ssl_enable=NO"
        ;;
    explicit)
        tls_settings="ssl_enable=YES
implicit_ssl=NO
force_local_logins_ssl=YES
force_local_data_ssl=YES"
        ;;
    implicit)
        tls_settings="ssl_enable=YES
implicit_ssl=YES
force_local_logins_ssl=YES
force_local_data_ssl=YES"
        ;;
    mtls)
        # require_cert makes the client certificate mandatory and validate_cert checks it against
        # the CA. Without both, the mutual-TLS endpoint is just the explicit one with a longer name
        # and the fixture's client-certificate assertions prove nothing.
        tls_settings="ssl_enable=YES
implicit_ssl=NO
force_local_logins_ssl=YES
force_local_data_ssl=YES
require_cert=YES
validate_cert=YES
ca_certs_file=/tls/ca.crt"
        ;;
    *)
        echo "entrypoint: unknown TLS_MODE '${mode}'" >&2
        exit 1
        ;;
esac

cat > /etc/vsftpd.conf <<EOF
listen=YES
listen_ipv6=NO
background=NO

anonymous_enable=NO
local_enable=YES
write_enable=YES
local_umask=022

# No chroot: the fixtures address /mounted absolutely, and chrooting would put the login directory
# in the way of that.
chroot_local_user=NO

# The container cannot see the address the client used, so it has to be told. Without this, PASV
# hands back the container's own 172.x address and every data connection from the host times out
# somewhere that looks like a hang rather than a refusal.
pasv_enable=YES
pasv_address=127.0.0.1
pasv_addr_resolve=NO
pasv_min_port=${pasv_min}
pasv_max_port=${pasv_max}

pam_service_name=vsftpd
secure_chroot_dir=/var/run/vsftpd/empty
seccomp_sandbox=NO

# Advertise UTF8 in FEAT and treat filenames as UTF-8. The conformance suite deliberately round
# trips a name with non-ASCII characters in it, and without this vsftpd never says it speaks UTF-8,
# so the client stores the file under one encoding and looks for it under another. The upload
# succeeds and the read back fails, which reads like a provider bug and is a server setting.
utf8_filesystem=YES

rsa_cert_file=/tls/server.crt
rsa_private_key_file=/tls/server.key

# ssl_tlsv1 rather than ssl_tlsv1_2. vsftpd 3.0.3 -- which is what Debian ships -- has no
# ssl_tlsv1_1 or ssl_tlsv1_2 setting, and it does not complain about one: an unrecognised variable
# makes it exit 2 before it has any way to say so, which looks exactly like a container that
# crash-loops for no reason. ssl_tlsv1 enables the TLS method, and against OpenSSL 3 that
# negotiates 1.2 or 1.3 regardless; the obsolete protocols are refused below.
ssl_tlsv1=YES
ssl_sslv2=NO
ssl_sslv3=NO

# The data connection does not resume the control connection's TLS session. Several clients cannot
# do it at all, and leaving it required turns every upload into a TLS failure after a login that
# looked fine.
require_ssl_reuse=NO

${tls_settings}

xferlog_enable=YES
xferlog_std_format=NO
log_ftp_protocol=YES

# A real file, not /dev/stdout. vsftpd opens its log once per session, after it has dropped
# privileges, and the open fails there -- so every login is answered "500 OOPS: failed to open
# vsftpd log file" while the container itself stays up and silent. Read it with
# docker compose exec ftp-plain tail -f /var/log/vsftpd.log
vsftpd_log_file=/var/log/vsftpd.log
EOF

exec /usr/sbin/vsftpd /etc/vsftpd.conf
