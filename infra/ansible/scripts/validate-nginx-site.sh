#!/bin/sh
set -eu

# nginx -t initializes temporary paths. Give the test its own paths so a
# fragment validation cannot change ownership of the running nginx buffers.
bikehub_validation_directory="$(mktemp -d /tmp/bikehub-nginx-validation.XXXXXX)"
trap 'rm -rf -- "$bikehub_validation_directory"' EXIT
bikehub_validation_config="$bikehub_validation_directory/nginx.conf"
cat > "$bikehub_validation_config" <<EOF
user www-data;
pid "$bikehub_validation_directory/nginx.pid";
error_log stderr;
events {}
http {
    client_body_temp_path "$bikehub_validation_directory/body";
    proxy_temp_path "$bikehub_validation_directory/proxy";
    fastcgi_temp_path "$bikehub_validation_directory/fastcgi";
    scgi_temp_path "$bikehub_validation_directory/scgi";
    uwsgi_temp_path "$bikehub_validation_directory/uwsgi";
    access_log off;
    include /etc/nginx/mime.types;
    include "$1";
}
EOF
/usr/sbin/nginx -t -c "$bikehub_validation_config"
