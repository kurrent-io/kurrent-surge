FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble

# Homebrew — universal package manager for the extras.
# Installed as the vscode user since brew refuses to run as root.
USER vscode
RUN NONINTERACTIVE=1 /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
ENV PATH="/home/linuxbrew/.linuxbrew/bin:${PATH}"

RUN brew install gh bun duckdb \
    && brew cleanup --prune=all -s \
    && rm -rf "$(brew --cache)"

# Restore default user for devcontainer lifecycle commands.
USER root
