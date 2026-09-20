#!/bin/sh
# A private CA, a server certificate for the FTPS endpoints, and a client certificate for the
# mutual-TLS one. Everything lands in /out, which the caller mounts.
set -eu

out="/out"
pfx_password="${CLIENT_PFX_PASSWORD:-storagehub-client-pfx}"
days=3650

if [ -f "${out}/server.crt" ] && [ -z "${FORCE:-}" ]; then
    # Deliberately on stdout. Windows PowerShell turns a native command's stderr into error
    # records and, under ErrorActionPreference Stop, treats this notice as a failure.
    echo "generate: certificates already present; pass FORCE=1 to replace them"
else
    rm -f "${out}"/*.crt "${out}"/*.key "${out}"/*.pfx "${out}"/*.srl

    # A CA of its own rather than a self-signed server certificate, because the mutual-TLS endpoint
    # has to validate a client certificate against something. One CA signs both sides.
    openssl req -x509 -newkey rsa:2048 -sha256 -days "${days}" -nodes \
        -keyout "${out}/ca.key" -out "${out}/ca.crt" \
        -subj "/CN=StorageHub Test Lab CA" \
        -addext "basicConstraints=critical,CA:TRUE" \
        -addext "keyUsage=critical,keyCertSign,cRLSign" 2>/dev/null

    # The subject alternative name carries the IP, not just the common name. A .NET client checks
    # the SAN and ignores the CN, so a certificate named only 127.0.0.1 in its subject is rejected
    # by the very validation the fixture is there to exercise.
    openssl req -newkey rsa:2048 -sha256 -nodes \
        -keyout "${out}/server.key" -out "${out}/server.csr" \
        -subj "/CN=127.0.0.1" 2>/dev/null
    openssl x509 -req -in "${out}/server.csr" -sha256 -days "${days}" \
        -CA "${out}/ca.crt" -CAkey "${out}/ca.key" -CAcreateserial \
        -out "${out}/server.crt" \
        -extfile /dev/stdin <<EOF 2>/dev/null
subjectAltName = IP:127.0.0.1, DNS:localhost
extendedKeyUsage = serverAuth
keyUsage = critical, digitalSignature, keyEncipherment
EOF

    openssl req -newkey rsa:2048 -sha256 -nodes \
        -keyout "${out}/client.key" -out "${out}/client.csr" \
        -subj "/CN=storagehub-testlab-client" 2>/dev/null
    openssl x509 -req -in "${out}/client.csr" -sha256 -days "${days}" \
        -CA "${out}/ca.crt" -CAkey "${out}/ca.key" -CAcreateserial \
        -out "${out}/client.crt" \
        -extfile /dev/stdin <<EOF 2>/dev/null
extendedKeyUsage = clientAuth
keyUsage = critical, digitalSignature, keyEncipherment
EOF

    # -legacy so the PKCS#12 uses algorithms the .NET loader accepts without extra configuration.
    # OpenSSL 3 defaults to AES-256-CBC with PBKDF2, which is correct and which .NET on Windows
    # reads, but the older RC2/3DES encoding is the one nothing anywhere argues about.
    openssl pkcs12 -export -legacy \
        -inkey "${out}/client.key" -in "${out}/client.crt" -certfile "${out}/ca.crt" \
        -out "${out}/client.pfx" -passout "pass:${pfx_password}" 2>/dev/null

    rm -f "${out}"/*.csr
    chmod 644 "${out}"/*.crt "${out}"/*.pfx
    chmod 644 "${out}"/*.key
fi

# The fingerprint the fixtures pin: the SHA-256 of the DER-encoded server certificate, which is
# what X509Certificate2.GetCertHashString(SHA256) returns, as uppercase hex with no separators.
fingerprint="$(openssl x509 -in "${out}/server.crt" -noout -fingerprint -sha256 \
    | sed 's/.*=//; s/://g' | tr '[:lower:]' '[:upper:]')"

cat > "${out}/fingerprints.env" <<EOF
STORAGEHUB_FTP_SERVER_SHA256=${fingerprint}
STORAGEHUB_FTP_CLIENT_PFX_PASSWORD=${pfx_password}
EOF

echo "generate: server certificate SHA-256 ${fingerprint}"
