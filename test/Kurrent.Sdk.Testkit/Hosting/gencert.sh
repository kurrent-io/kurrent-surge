#!/bin/sh
set -e

es-gencert-cli create-ca -out /certs/ca -force

es-gencert-cli create-node \
    -ca-certificate /certs/ca/ca.crt \
    -ca-key /certs/ca/ca.key \
    -out /certs/node \
    -ip-addresses 127.0.0.1 \
    -dns-names localhost \
    -force

es-gencert-cli create-user \
    -username admin \
    -ca-certificate /certs/ca/ca.crt \
    -ca-key /certs/ca/ca.key \
    -out /certs/user-admin \
    -force

find /certs -type f -exec chmod 666 {} +
find /certs -type d -exec chmod 777 {} +
