#!/usr/bin/env bash
# Capture this app's Store screenshots on the Windows test VM, one listing language at a time
# (store-assets/README.md, "Screenshots"). For each locale it copies capture.ps1, the sample data and
# the locale's inputs into the guest, runs capture.ps1 in the desktop session, and copies every raw
# capture the manifest names for that locale (with its .json sidecar) into
# store-assets/raw/microsoft/<locale>/. Render and check afterwards with npm run shots:render,
# npm run hero and npm run validate.
#
# Usage, from the repo root, with the guest prepared (screenshot-desktop.ps1 -Mode Prepare -Scale 200)
# and the app installed:
#   store-assets/capture/capture.sh [locale ...]
# With no locale, it captures every listing locale with its own record in listings.json (en-CA and
# fr-CA reuse en-US and fr-FR through copyFrom, so they need no capture).
#
# Windows must be in the locale's display language when its capture runs: capture.ps1 checks and
# stops otherwise. Switching takes a sign-in (windows-build-tools app_test_language, then a cold
# cycle), so set CAPTURE_PREPARE_LOCALE to a command that does it; it runs with the locale as its only
# argument before each capture, and may also do any per-language setup the app needs (see the README).
# Without it, pass only the locale Windows is in now.
#
# Environment:
#   WINDOWS_BUILD_TOOLS     windows-build-tools checkout (default: a sibling of this repo)
#   CAPTURE_SCALE           display scale the guest was prepared at, 100 or 200 (default 200)
#   CAPTURE_PREPARE_LOCALE  command run with each locale before its capture (default: none)
set -euo pipefail

repo="$(git rev-parse --show-toplevel)"
here="$repo/store-assets/capture"
tools="${WINDOWS_BUILD_TOOLS:-$(dirname "$repo")/windows-build-tools}"
scale="${CAPTURE_SCALE:-200}"

# shellcheck source=/dev/null
source "$tools/vm/app-test/app-test.sh"
app_test_use win11

# Every listing locale with its own record, in listing order.
listing_locales() {
    python3 -I - "$repo/store-assets/microsoft/listings.json" <<'EOF'
import json, sys
locales = json.load(open(sys.argv[1], encoding="utf-8"))["locales"]
print("\n".join(name for name, record in locales.items() if "copyFrom" not in record))
EOF
}

# The raw captures the manifest takes from one locale's folder, as base names without .png, once each
# (the hero and its screenshot can share a capture).
capture_names() {
    python3 -I - "$repo/store-assets/manifest.json" <<'EOF'
import json, os, sys
names = []
for entry in json.load(open(sys.argv[1], encoding="utf-8"))["microsoft"]:
    source = entry["source"]
    if "{locale}" in source:
        name = os.path.splitext(os.path.basename(source))[0]
        if name not in names:
            names.append(name)
print("\n".join(names))
EOF
}

# One locale: inputs into the guest, capture, then copy the captures back. Stops at the first
# missing capture, so a partial set is never mistaken for a complete one.
capture_locale() {
    local locale="$1" input names name dest

    if [[ -n "${CAPTURE_PREPARE_LOCALE:-}" ]]; then
        $CAPTURE_PREPARE_LOCALE "$locale"
    fi

    # capture.ps1 takes no arguments (app_test_ui), so its inputs go in a file beside it.
    input="$(mktemp --suffix=.json)"
    printf '{ "locale": "%s", "scale": %s }\n' "$locale" "$scale" > "$input"
    app_test_put "$tools/vm/app-test/ui-lib.ps1"
    app_test_put "$here/capture.ps1"
    app_test_put "$here/sample-data.json"
    # Any other fixture capture.ps1 reads, such as Little Launcher's seed settings.
    for fixture in "$here"/*.json; do
        [[ "$(basename "$fixture")" == sample-data.json ]] || app_test_put "$fixture"
    done
    app_test_put "$input" capture-input.json
    rm -f "$input"

    if ! app_test_ui 'C:\apptest\capture.ps1' | tr -d '\r' | tee /dev/stderr | grep "^capture finished: $locale$" >/dev/null; then
        echo "capture.sh: $locale did not finish; nothing copied" >&2
        return 1
    fi

    dest="$repo/store-assets/raw/microsoft/$locale"
    mkdir -p "$dest"
    names="$(capture_names)"
    for name in $names; do
        app_test_get "out/$name.png" "$dest/$name.png"
        app_test_get "out/$name.json" "$dest/$name.json"
    done
    echo "capture.sh: $locale: $(wc -w <<< "$names") captures in store-assets/raw/microsoft/$locale"
}

main() {
    local locales=("$@")
    if [[ ${#locales[@]} -eq 0 ]]; then
        mapfile -t locales < <(listing_locales)
    fi
    for locale in "${locales[@]}"; do
        capture_locale "$locale"
    done
}

main "$@"
