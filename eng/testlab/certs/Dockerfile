# Mints the lab's TLS material.
#
# In a container rather than on the host: openssl ships with Git for Windows but is not on PATH
# from PowerShell, and reaching into Git's own installation directory to find it is the kind of
# thing that works until somebody installs Git somewhere else. This way the lab needs nothing on
# the host but Docker.
FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install --yes --no-install-recommends openssl \
    && rm --recursive --force /var/lib/apt/lists/*

COPY generate.sh /usr/local/bin/generate.sh
RUN chmod +x /usr/local/bin/generate.sh

ENTRYPOINT ["/usr/local/bin/generate.sh"]
