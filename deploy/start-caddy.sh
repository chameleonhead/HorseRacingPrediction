#!/bin/sh
set -eu

generated_config=/config/Caddyfile.generated
global_block=
redirect_block=
domain_block=

is_ipv4() {
    echo "$1" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$'
}

if [ -z "${DOMAIN_NAME:-}" ] || is_ipv4 "$DOMAIN_NAME"; then
    echo "DOMAIN_NAME must be set to a real hostname (not empty, not a bare IP) so Caddy can obtain a trusted HTTPS certificate for it." >&2
    exit 1
fi

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

# Scoped to the literal IP host only (never an unscoped ":80" block). An unscoped ":80"
# block makes Caddy treat port 80 as fully user-owned and it stops inserting its own
# ACME HTTP-01 challenge responder / redirect for $DOMAIN_NAME's automatic HTTPS.
if [ -n "${LIGHTSAIL_HOST:-}" ] && [ "$LIGHTSAIL_HOST" != "$DOMAIN_NAME" ]; then
    redirect_block=$(cat <<EOF
http://$LIGHTSAIL_HOST {
    redir https://$DOMAIN_NAME{uri} permanent
}
EOF
)
fi

awk -v global_block="$global_block" -v redirect_block="$redirect_block" -v domain_block="$domain_block" '
    $0 == "__GLOBAL_BLOCK__" { print global_block; next }
    $0 == "__REDIRECT_BLOCK__" { print redirect_block; next }
    $0 == "__DOMAIN_BLOCK__" { print domain_block; next }
    { print }
' /etc/caddy/Caddyfile.template > "$generated_config"

if [ "${GENERATE_ONLY:-0}" = "1" ]; then
    cat "$generated_config"
    exit 0
fi

exec caddy run --config "$generated_config" --adapter caddyfile
