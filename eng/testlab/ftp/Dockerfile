# One vsftpd, configured at start into whichever TLS mode it is playing.
#
# Four containers from this image rather than one server with four listeners: what the fixture
# tests is the endpoint's policy -- plain, explicit TLS, implicit TLS, or mutual TLS -- and vsftpd
# holds exactly one policy per process.
FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install --yes --no-install-recommends vsftpd \
    && rm --recursive --force /var/lib/apt/lists/* \
    && mkdir --parents /var/run/vsftpd/empty

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
