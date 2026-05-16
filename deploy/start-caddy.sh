#!/bin/sh
set -eu

apk add --no-cache openssl >/dev/null

generated_config=/config/Caddyfile.generated
global_block=
domain_block=
ip_block=

is_ipv4() {
    echo "$1" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$'
}

if [ -n "${DOMAIN_NAME:-}" ] && ! is_ipv4 "$DOMAIN_NAME"; then
    if [ -n "${ACME_EMAIL:-}" ]; then
        global_block=$(cat <<EOF
{
    email $ACME_EMAIL
}
EOF
)
    fi

    domain_block=$(cat <<EOF
https://$DOMAIN_NAME {
    encode zstd gzip
    reverse_proxy api:8080
}
EOF
)
fi

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

    ip_block=$(cat <<EOF
:443 {
    tls /certs/ip.crt /certs/ip.key
    encode zstd gzip
    reverse_proxy api:8080
}

https://$LIGHTSAIL_HOST {
    tls /certs/ip.crt /certs/ip.key
    encode zstd gzip
    reverse_proxy api:8080
}
EOF
)
fi

if [ -z "$domain_block" ] && [ -z "$ip_block" ]; then
    echo "No DOMAIN_NAME or LIGHTSAIL_HOST configured for Caddy." >&2
    exit 1
fi

awk -v global_block="$global_block" -v domain_block="$domain_block" -v ip_block="$ip_block" '
    $0 == "__GLOBAL_BLOCK__" { print global_block; next }
    $0 == "__DOMAIN_BLOCK__" { print domain_block; next }
    $0 == "__IP_BLOCK__" { print ip_block; next }
    { print }
' /etc/caddy/Caddyfile.template > "$generated_config"

if [ "${GENERATE_ONLY:-0}" = "1" ]; then
    cat "$generated_config"
    exit 0
fi

exec caddy run --config "$generated_config" --adapter caddyfile