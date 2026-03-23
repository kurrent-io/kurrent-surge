FROM mcr.microsoft.com/devcontainers/dotnet:dev-10.0-noble

# -----------------------------------------------------------
# Environment variables
# -----------------------------------------------------------

# .NET
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=false

# TUnit
ENV TUNIT_DISABLE_LOGO=true \
    TUNIT_DISABLE_HTML_REPORTER=true \
    TUNIT_DISABLE_JUNIT_REPORTER=true \
    TUNIT_MAX_PARALLEL_TESTS=2

# Microsoft Testing Platform
ENV TESTINGPLATFORM_TELEMETRY_OPTOUT=1 \
    TESTINGPLATFORM_UI_LANGUAGE=en-US

# -----------------------------------------------------------
# Additional .NET SDKs (8.0, 9.0) alongside the base 10.0
# -----------------------------------------------------------
COPY --from=mcr.microsoft.com/dotnet/sdk:8.0 /usr/share/dotnet/sdk /usr/share/dotnet/sdk
COPY --from=mcr.microsoft.com/dotnet/sdk:8.0 /usr/share/dotnet/shared /usr/share/dotnet/shared
COPY --from=mcr.microsoft.com/dotnet/sdk:9.0 /usr/share/dotnet/sdk /usr/share/dotnet/sdk
COPY --from=mcr.microsoft.com/dotnet/sdk:9.0 /usr/share/dotnet/shared /usr/share/dotnet/shared

# -----------------------------------------------------------
# System packages
# -----------------------------------------------------------
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        # GitHub CLI
        gh \
        # Git Large File Support
        git-lfs \
        # Python
        python3 \
        python3-pip \
        # Shell
        zsh \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# -----------------------------------------------------------
# GitHub CLI — install from official repo if not in apt
# -----------------------------------------------------------
RUN if ! command -v gh &> /dev/null; then \
        curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
            | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg \
        && chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg \
        && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
            | tee /etc/apt/sources.list.d/github-cli.list > /dev/null \
        && apt-get update \
        && apt-get install -y --no-install-recommends gh \
        && apt-get clean \
        && rm -rf /var/lib/apt/lists/*; \
    fi

# -----------------------------------------------------------
# Set zsh as default shell (Oh My Zsh already in base image)
# -----------------------------------------------------------
USER vscode
ENV SHELL=/usr/bin/zsh

# -----------------------------------------------------------
# Aspire CLI
# -----------------------------------------------------------
RUN curl -sSL https://aspire.dev/install.sh | bash
ENV PATH="${PATH}:/home/vscode/.aspire/bin"

# -----------------------------------------------------------
# Git LFS
# -----------------------------------------------------------
RUN git lfs install

USER vscode
