#!/usr/bin/env bash
set -euo pipefail
# Run as root inside a dedicated Debian WSL distribution, not on Windows.
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y --no-install-recommends ca-certificates curl clang clang-19 lld-19 zlib1g-dev libicu-dev git wget unzip make \
    cmake ninja-build meson rsync pkg-config libclang-rt-19-dev python3-mako python3-yaml python3-packaging python3-ply \
    python3-jinja2 bison flex llvm-19-dev libclang-19-dev libclc-19-dev libclc-19 \
    libllvmspirvlib-19-dev libzstd-dev libdrm-dev libexpat1-dev libelf-dev spirv-tools spirv-headers glslang-tools
mkdir -p /opt/waddamburo-build/sdk
mkdir -p /opt/waddamburo-dotnet
installer=$(mktemp)
trap 'rm -f "$installer"' EXIT
curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
bash "$installer" --version 10.0.201 --install-dir /opt/waddamburo-dotnet --no-path
printf '%s\n' 'export DOTNET_ROOT=/opt/waddamburo-dotnet' 'export PATH=/opt/waddamburo-dotnet:$PATH' > /etc/profile.d/waddamburo-dotnet.sh
/opt/waddamburo-dotnet/dotnet --version
