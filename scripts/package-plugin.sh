#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || echo "${HOME}/.dotnet/dotnet")}"
if [[ ! -x "${DOTNET_BIN}" ]]; then
    echo "ERROR: dotnet executable not found at ${DOTNET_BIN}" >&2
    exit 1
fi

PLUGIN_VERSION="1.0.0.0"
PLUGIN_GUID="4c7a9d3e-6f12-4b88-9e41-2a5d8c0f7b19"
TARGET_ABI="12.4.0.0"
ASSEMBLY_FILE="Jellyfin.Plugin.BetterPosters.dll"
ARCHIVE_NAME="better-posters_${PLUGIN_VERSION}.zip"

PUBLISH_DIR="${REPO_ROOT}/artifacts/publish"
STAGE_DIR="${REPO_ROOT}/artifacts/stage/BetterPosters_${PLUGIN_VERSION}"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"

rm -rf "${PUBLISH_DIR}" "${REPO_ROOT}/artifacts/stage"
mkdir -p "${STAGE_DIR}" "${ARTIFACTS_DIR}"

echo "==> Publishing BetterPosters (Release, net10.0)..."
"${DOTNET_BIN}" publish "${REPO_ROOT}/src/BetterPosters/BetterPosters.csproj" \
    -c Release \
    -o "${PUBLISH_DIR}" \
    -p:UseSharedCompilation=false

if [[ ! -f "${PUBLISH_DIR}/${ASSEMBLY_FILE}" ]]; then
    echo "ERROR: Expected plugin assembly ${PUBLISH_DIR}/${ASSEMBLY_FILE} was not produced." >&2
    exit 1
fi

# Ensure no Jellyfin host assemblies are copied into the plugin package.
cp "${PUBLISH_DIR}/${ASSEMBLY_FILE}" "${STAGE_DIR}/${ASSEMBLY_FILE}"

cat > "${STAGE_DIR}/meta.json" <<EOF
{
  "category": "Metadata",
  "changelog": "Initial verified release for Jellyfin 12.4 (net10.0).",
  "description": "Customizable primary posters for Movies and Series from btttr.cc (Jellyfin 12).",
  "guid": "${PLUGIN_GUID}",
  "name": "Better Posters",
  "overview": "Native Jellyfin 12 remote primary-image provider and opt-in scheduled refresh backed by btttr.cc.",
  "owner": "benfayzen",
  "targetAbi": "${TARGET_ABI}",
  "timestamp": "2026-11-10T00:00:00Z",
  "version": "${PLUGIN_VERSION}",
  "status": "Active",
  "autoUpdate": false,
  "assemblies": [
    "${ASSEMBLY_FILE}"
  ]
}
EOF

ARCHIVE_PATH="${ARTIFACTS_DIR}/${ARCHIVE_NAME}"
rm -f "${ARCHIVE_PATH}" "${ARCHIVE_PATH}.sha256" "${ARCHIVE_PATH}.md5"

python3 - "${STAGE_DIR}" "${ARCHIVE_PATH}" <<'PY'
import os, sys, zipfile

stage_dir, archive_path = sys.argv[1], sys.argv[2]
files = sorted(os.listdir(stage_dir))
expected = ["Jellyfin.Plugin.BetterPosters.dll", "meta.json"]
if files != expected:
    raise SystemExit(f"ERROR: Unexpected staged files: {files} (expected {expected})")

with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
    for fname in files:
        full_path = os.path.join(stage_dir, fname)
        info = zipfile.ZipInfo(fname, date_time=(2026, 11, 10, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o644 << 16
        with open(full_path, "rb") as f:
            zf.writestr(info, f.read())

with zipfile.ZipFile(archive_path, "r") as zf:
    entries = zf.namelist()
    print("Archive entries:", entries)
    for entry in entries:
        if entry.startswith(("MediaBrowser.", "Jellyfin.Data", "Jellyfin.Naming", "Microsoft.")):
            raise SystemExit(f"ERROR: Forbidden host assembly bundled in archive: {entry}")
PY

sha256sum "${ARCHIVE_PATH}" | awk '{print $1}' > "${ARCHIVE_PATH}.sha256"
md5sum "${ARCHIVE_PATH}" | awk '{print $1}' > "${ARCHIVE_PATH}.md5"

echo "==> Package created: ${ARCHIVE_PATH}"
echo "    SHA256: $(cat "${ARCHIVE_PATH}.sha256")"
echo "    MD5:    $(cat "${ARCHIVE_PATH}.md5")"
