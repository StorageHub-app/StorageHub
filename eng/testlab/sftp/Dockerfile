# An SSH server with a real shell, for the SFTP and terminal fixtures.
#
# Not one of the ready-made SFTP images: those chroot to an internal-sftp subsystem with no shell,
# which serves the storage provider tests and not the terminal ones -- and StorageHub's SSH client
# panes open an interactive shell. This runs a normal sshd so one image answers both.
FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install --yes --no-install-recommends openssh-server \
    && rm --recursive --force /var/lib/apt/lists/* \
    && mkdir --parents /run/sshd

# The host keys that ship in the image are deleted. A fixture pins a fingerprint, so the key has to
# be the one the generator made and hashed, never one apt happened to create at install time.
RUN rm --force /etc/ssh/ssh_host_*

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

EXPOSE 22
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
