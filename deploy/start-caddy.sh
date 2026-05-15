#!/bin/sh
set -eu

apk add --no-cache openssl >/dev/null

if [ -n "${LIGHTSAIL_HOST:-}" ]; then
    mkdir -p /certs

    if [ ! -f /certs/ip.crt ] || [ ! -f /certs/ip.key ] || ! openssl x509 -in /certs/ip.crt -noout -checkip "$LIGHTSAIL_HOST" >/dev/null 2>&1; then
        cat > /certs/ip-openssl.cnf <<EOF
[req]
distinguished_name=req_distinguished_name
x509_extensions=v3_req
prompt=no

[req_distinguished_name]
CN=$LIGHTSAIL_HOST

[v3_req]
subjectAltName=@alt_names
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth

[alt_names]
IP.1=$LIGHTSAIL_HOST
EOF

        openssl req -x509 -nodes -newkey rsa:2048 -sha256 -days 3650 \
            -keyout /certs/ip.key \
            -out /certs/ip.crt \
            -config /certs/ip-openssl.cnf
    fi
fi

exec caddy run --config /etc/caddy/Caddyfile --adapter caddyfile