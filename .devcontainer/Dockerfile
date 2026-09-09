FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble

# Homebrew — universal package manager for the extras.
# Installed as the vscode user since brew refuses to run as root.
USER vscode
RUN NONINTERACTIVE=1 /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
ENV PATH="/home/linuxbrew/.linuxbrew/bin:${PATH}"

RUN brew install node gh oven-sh/bun/bun duckdb \
    && brew cleanup --prune=all -s \
    && rm -rf "$(brew --cache)"

# Restore default user for devcontainer lifecycle commands.
USER root

# Pre-create the named-volume mount targets from devcontainer.json. Docker creates a
# missing mount path (and any missing parent) as root:root, which locks the vscode user
# out of /home/vscode/.dotnet and breaks the dotnet CLI first-run sentinel write. When
# the path already exists in the image, Docker seeds the fresh volume from it and keeps
# the ownership. Keep this list in sync with the "mounts" array in devcontainer.json.
RUN mkdir -p /home/vscode/.dotnet/tools /home/vscode/.nuget /commandhistory \
    && chown -R vscode:vscode /home/vscode/.dotnet /home/vscode/.nuget /commandhistory
