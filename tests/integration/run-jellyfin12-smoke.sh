#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

CACHE_DIR="${JELLYFIN12_CACHE_DIR:-/tmp/jellyfin12-cache}"
TARBALL_URL="https://repo.jellyfin.org/files/server/linux/stable/v12.4/amd64/jellyfin_12.4-amd64.tar.xz"
TARBALL_PATH="${CACHE_DIR}/jellyfin_12.4-amd64.tar.xz"
EXTRACTED_DIR="${CACHE_DIR}/extracted"

PORT="${JELLYFIN12_SMOKE_PORT:-18096}"
WORK_DIR="$(mktemp -d /tmp/jellyfin12-smoke.XXXXXX)"
SERVER_PID=""

cleanup() {
    if [[ -n "${SERVER_PID}" ]] && kill -0 "${SERVER_PID}" 2>/dev/null; then
        kill "${SERVER_PID}" 2>/dev/null || true
        for _ in 1 2 3 4 5; do
            kill -0 "${SERVER_PID}" 2>/dev/null || break
            sleep 1
        done
        kill -9 "${SERVER_PID}" 2>/dev/null || true
    fi
    rm -rf "${WORK_DIR}"
}
trap cleanup EXIT

echo "==> Step 1: Ensure plugin package is built..."
bash "${REPO_ROOT}/scripts/package-plugin.sh"

ARCHIVE_PATH="${REPO_ROOT}/artifacts/better-posters_1.0.0.0.zip"
if [[ ! -f "${ARCHIVE_PATH}" ]]; then
    echo "ERROR: Plugin archive not found at ${ARCHIVE_PATH}" >&2
    exit 1
fi

echo "==> Step 2: Prepare official Jellyfin v12.4 binary in ${CACHE_DIR}..."
mkdir -p "${CACHE_DIR}"
if [[ ! -f "${TARBALL_PATH}" ]]; then
    echo "    Downloading ${TARBALL_URL}..."
    curl -fsSL -A "Jellyfin-BetterPosters-SmokeTest/1.0" "${TARBALL_URL}" -o "${TARBALL_PATH}"
fi

if [[ ! -x "${EXTRACTED_DIR}/jellyfin/jellyfin" ]]; then
    rm -rf "${EXTRACTED_DIR}"
    mkdir -p "${EXTRACTED_DIR}"
    tar -xJf "${TARBALL_PATH}" -C "${EXTRACTED_DIR}"
fi

JELLYFIN_BIN="${EXTRACTED_DIR}/jellyfin/jellyfin"
if [[ ! -x "${JELLYFIN_BIN}" ]]; then
    echo "ERROR: Jellyfin binary not found at ${JELLYFIN_BIN}" >&2
    exit 1
fi

echo "==> Step 3: Provision disposable Jellyfin 12.4 environment at ${WORK_DIR}..."
DATA_DIR="${WORK_DIR}/data"
CONFIG_DIR="${WORK_DIR}/config"
CACHE_RUNTIME_DIR="${WORK_DIR}/cache"
LOG_DIR="${WORK_DIR}/log"
MEDIA_DIR="${WORK_DIR}/media/movies"
PLUGIN_DIR="${DATA_DIR}/plugins/BetterPosters_1.0.0.0"

mkdir -p "${PLUGIN_DIR}" "${CONFIG_DIR}" "${CACHE_RUNTIME_DIR}" "${LOG_DIR}" \
    "${MEDIA_DIR}/Inception (2010)" \
    "${MEDIA_DIR}/Interstellar (2014)"

# Extract plugin archive into disposable plugins directory
python3 -c '
import sys, zipfile
with zipfile.ZipFile(sys.argv[1], "r") as zf:
    zf.extractall(sys.argv[2])
' "${ARCHIVE_PATH}" "${PLUGIN_DIR}"

# Bind strictly to loopback (127.0.0.1) on ephemeral smoke port
cat > "${CONFIG_DIR}/network.xml" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<NetworkConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <EnableUPnP>false</EnableUPnP>
  <EnableIPV4>true</EnableIPV4>
  <EnableIPV6>false</EnableIPV6>
  <EnableRemoteAccess>false</EnableRemoteAccess>
  <LocalNetworkAddresses>
    <string>127.0.0.1</string>
  </LocalNetworkAddresses>
  <InternalHttpPort>${PORT}</InternalHttpPort>
  <PublicHttpPort>${PORT}</PublicHttpPort>
</NetworkConfiguration>
EOF

# Synthetic NFO + STRM fixtures (zero copyrighted media or artwork)
cat > "${MEDIA_DIR}/Inception (2010)/Inception (2010).nfo" <<'EOF'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>Inception</title>
  <year>2010</year>
  <imdbid>tt1375666</imdbid>
  <tmdbid>27205</tmdbid>
</movie>
EOF
echo "http://127.0.0.1:9/synthetic-inception.mp4" > "${MEDIA_DIR}/Inception (2010)/Inception (2010).strm"

cat > "${MEDIA_DIR}/Interstellar (2014)/Interstellar (2014).nfo" <<'EOF'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>Interstellar</title>
  <year>2014</year>
  <tmdbid>157336</tmdbid>
</movie>
EOF
echo "http://127.0.0.1:9/synthetic-interstellar.mp4" > "${MEDIA_DIR}/Interstellar (2014)/Interstellar (2014).strm"

echo "==> Step 4: Starting disposable Jellyfin 12.4 server on 127.0.0.1:${PORT}..."
"${JELLYFIN_BIN}" \
    --datadir "${DATA_DIR}" \
    --configdir "${CONFIG_DIR}" \
    --cachedir "${CACHE_RUNTIME_DIR}" \
    --logdir "${LOG_DIR}" \
    --nowebclient > "${WORK_DIR}/server-stdout.log" 2>&1 </dev/null &
SERVER_PID=$!

echo "==> Step 5: Running end-to-end verification against disposable Jellyfin 12.4 instance..."
python3 - "http://127.0.0.1:${PORT}" "${MEDIA_DIR}" "${DATA_DIR}" "${WORK_DIR}/server-stdout.log" <<'PY'
import json, os, sys, time, urllib.request, urllib.parse, urllib.error

base_url, media_dir, data_dir, log_file = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
plugin_guid = "4c7a9d3e-6f12-4b88-9e41-2a5d8c0f7b19"

# Bypass proxy for localhost
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

def api_req(method, path, body=None, token=None, expect_json=True, quiet_error=False):
    url = base_url + path
    data = json.dumps(body).encode("utf-8") if body is not None else None
    auth_header = 'MediaBrowser Client="SmokeTest", Device="Loopback", DeviceId="smoke-1", Version="1.0.0"'
    if token:
        auth_header += f', Token="{token}"'
    headers = {
        "Accept": "application/json" if expect_json else "*/*",
        "Authorization": auth_header
    }
    if data is not None:
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with opener.open(req, timeout=20) as resp:
            raw = resp.read()
            if not expect_json or not raw:
                return raw.decode("utf-8", errors="replace")
            return json.loads(raw.decode("utf-8"))
    except urllib.error.HTTPError as exc:
        err_body = exc.read().decode("utf-8", errors="replace")
        if not quiet_error:
            print(f"HTTP {exc.code} on {method} {path}: {err_body}", file=sys.stderr)
        raise

# 1. Wait for full MVC server readiness (not the early camelCase migration splash listener)
public_info = None
for attempt in range(90):
    try:
        info = api_req("GET", "/System/Info/Public", quiet_error=True)
        if info and "Version" in info:
            public_info = info
            break
    except Exception:
        pass
    time.sleep(1)

if not public_info:
    if os.path.exists(log_file):
        print(open(log_file).read()[-4000:], file=sys.stderr)
    raise SystemExit("ERROR: Disposable Jellyfin server did not become ready in time.")

print(f"  [OK] Server ready: ProductName={public_info.get('ProductName')} Version={public_info.get('Version')}")
assert public_info.get("Version", "").startswith("12.4"), f"Unexpected server version: {public_info}"

# 2. Complete initial startup wizard on disposable instance
api_req("POST", "/Startup/Configuration", {
    "UICulture": "en-US",
    "MetadataCountryCode": "US",
    "PreferredMetadataLanguage": "en"
}, expect_json=False)
startup_user = api_req("GET", "/Startup/User")
admin_name = startup_user.get("Name") or "smokeadmin"
admin_pass = "SmokeTestPass123!"
api_req("POST", "/Startup/User", {
    "Name": admin_name,
    "Password": admin_pass
}, expect_json=False)
api_req("POST", "/Startup/Complete", {}, expect_json=False)

auth = api_req("POST", "/Users/AuthenticateByName", {
    "Username": admin_name,
    "Pw": admin_pass
})
token = auth["AccessToken"]
print("  [OK] Startup wizard completed and authenticated on loopback.")

# 3. Verify plugin is loaded and Active
plugins = api_req("GET", "/Plugins", token=token)
bp = next((p for p in plugins if p.get("Id", "").replace("-", "").lower() == plugin_guid.replace("-", "").lower()), None)
assert bp is not None, f"Better Posters plugin not found in /Plugins: {plugins}"
print(f"  [OK] Plugin loaded: Name={bp['Name']} Id={bp['Id']} Version={bp['Version']} Status={bp['Status']}")
assert bp["Status"] == "Active", f"Expected Active status, got {bp['Status']}"

# 4. Verify embedded configuration HTML page is served
config_html = api_req("GET", "/web/ConfigurationPage?name=" + urllib.parse.quote("Better Posters"), token=token, expect_json=False)
assert "BetterPostersConfigPage" in config_html, "Config page HTML did not contain BetterPostersConfigPage"
print("  [OK] Embedded configuration page (/web/ConfigurationPage?name=Better%20Posters) served.")

# 5. Verify default plugin configuration & round-trip persistence
cfg = api_req("GET", f"/Plugins/{plugin_guid}/Configuration", token=token)
print("  [OK] Default configuration retrieved:", json.dumps(cfg))
assert cfg["EnableTrendTags"] is True
assert cfg["EnableQualityTags"] is False
assert cfg["EnableGenre"] is True
assert cfg["EnableRating"] is True
assert cfg["EnableAgeRating"] is False
assert cfg["RatingSource"] == "Average"
assert cfg["Language"] == "English"
assert cfg["EnableTmdbFallback"] is True
assert cfg["RequestTimeoutSeconds"] == 15
assert cfg["MaxRetryAttempts"] == 2
assert "EnableScheduledRefreshOptIn" not in cfg

# Update configuration
cfg["EnableTrendTags"] = False
cfg["EnableQualityTags"] = True
cfg["EnableAgeRating"] = True
cfg["RatingSource"] = "Imdb"
cfg["Language"] = "German"
api_req("POST", f"/Plugins/{plugin_guid}/Configuration", body=cfg, token=token, expect_json=False)

cfg_after = api_req("GET", f"/Plugins/{plugin_guid}/Configuration", token=token)
assert cfg_after["EnableTrendTags"] is False
assert cfg_after["EnableQualityTags"] is True
assert cfg_after["EnableAgeRating"] is True
assert cfg_after["RatingSource"] == "Imdb"
assert cfg_after["Language"] == "German"
print("  [OK] Updated configuration persisted and round-tripped:", json.dumps(cfg_after))

# 6. Verify no Better Posters scheduled task is registered
tasks = api_req("GET", "/ScheduledTasks", token=token)
bp_task = next((t for t in tasks if t.get("Key") == "JellyfinPluginBetterPostersRefresh"), None)
assert bp_task is None, f"Unexpected Better Posters scheduled task found: {bp_task}"
print("  [OK] Verified no Better Posters scheduled task is registered.")

# 7. Create a disposable Movies library with synthetic NFO/STRM fixtures and test IRemoteImageProvider
add_lib_path = "/Library/VirtualFolders?name=SmokeMovies&collectionType=movies&refreshLibrary=true&paths=" + urllib.parse.quote(media_dir)
lib_body = {
    "LibraryOptions": {
        "EnableRealtimeMonitor": False,
        "LocalMetadataReaderOrder": ["Nfo"],
        "TypeOptions": [
            {
                "Type": "Movie",
                "MetadataFetchers": [],
                "ImageFetchers": ["Better Posters"]
            }
        ]
    }
}
api_req("POST", add_lib_path, body=lib_body, token=token, expect_json=False)

movies = []
for _ in range(45):
    res = api_req("GET", "/Items?IncludeItemTypes=Movie&Recursive=true&Fields=ProviderIds", token=token)
    movies = res.get("Items", [])
    if len(movies) >= 2 and all(m.get("ProviderIds") for m in movies):
        break
    time.sleep(1)

assert len(movies) >= 2, f"Expected 2 synthetic movies in disposable library, got {movies}"
print(f"  [OK] Disposable library scanned {len(movies)} synthetic movies: {[(m.get('Name'), m.get('ProviderIds')) for m in movies]}")

inception = next(m for m in movies if "Inception" in m.get("Name", ""))
interstellar = next(m for m in movies if "Interstellar" in m.get("Name", ""))

# Query remote images for Inception (has IMDb ID tt1375666)
remote_inc = api_req("GET", f"/Items/{inception['Id']}/RemoteImages?type=Primary&includeAllLanguages=true&providerName=Better%20Posters", token=token)
bp_images_inc = [img for img in remote_inc.get("Images", []) if img.get("ProviderName") == "Better Posters"]
assert len(bp_images_inc) == 1, f"Expected 1 Better Posters candidate for Inception, got {remote_inc}"
expected_inc_url = "https://btttr.cc/poster-qa/imdb/poster-default/tt1375666.jpg?tag=none&lang=de&rs=IM"
assert bp_images_inc[0]["Url"] == expected_inc_url, f"Unexpected URL {bp_images_inc[0]['Url']} != {expected_inc_url}"
assert bp_images_inc[0]["Language"] == "de", f"Expected language 'de', got {bp_images_inc[0].get('Language')}"
print(f"  [OK] RemoteImageProvider (IMDb) returned expected candidate: {bp_images_inc[0]['Url']}")

# Query remote images for Interstellar (TMDB-only 157336 -> resolved via verified /meta/movie/tmdb:157336.json to tt0816692)
remote_int = api_req("GET", f"/Items/{interstellar['Id']}/RemoteImages?type=Primary&includeAllLanguages=true&providerName=Better%20Posters", token=token)
bp_images_int = [img for img in remote_int.get("Images", []) if img.get("ProviderName") == "Better Posters"]
assert len(bp_images_int) == 1, f"Expected 1 Better Posters candidate for Interstellar via TMDB fallback, got {remote_int}"
expected_int_url = "https://btttr.cc/poster-qa/imdb/poster-default/tt0816692.jpg?tag=none&lang=de&rs=IM"
assert bp_images_int[0]["Url"] == expected_int_url, f"Unexpected URL {bp_images_int[0]['Url']} != {expected_int_url}"
print(f"  [OK] RemoteImageProvider (TMDB fallback -> tt0816692) returned expected candidate: {bp_images_int[0]['Url']}")

# 8. Download and apply primary poster via Jellyfin's native RemoteImages/Download endpoint
dl_path = f"/Items/{inception['Id']}/RemoteImages/Download?Type=Primary&ImageUrl=" + urllib.parse.quote(expected_inc_url, safe="")
api_req("POST", dl_path, token=token, expect_json=False)
inc_after_dl = api_req("GET", f"/Items/{inception['Id']}", token=token)
assert inc_after_dl.get("ImageTags", {}).get("Primary"), f"Expected Primary image tag after RemoteImages/Download, got {inc_after_dl.get('ImageTags')}"
print(f"  [OK] RemoteImageProvider.GetImageResponse downloaded and saved validated primary poster (Primary tag={inc_after_dl['ImageTags']['Primary']}).")
PY

echo "==> Disposable Jellyfin 12.4 smoke test PASSED!"
