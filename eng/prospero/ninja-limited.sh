#!/usr/bin/env bash
exec ninja -j "${BUILD_JOBS:-4}" "$@"
