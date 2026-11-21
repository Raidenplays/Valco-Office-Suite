#!/usr/bin/env bash
# Dev harness (Linux): run Firefox ESR + the ValcoNet layer under Xvfb with Marionette on.
# Usage: tools/dev-run.sh start|stop   (screens via tools/vn.py)
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FF="${FF_DIR:-/tmp/ffesr/firefox}"
INST=/tmp/vn-install
PROF=/tmp/vn-profile
case "$1" in
start)
  if [ ! -d "$INST" ]; then cp -a "$FF" "$INST"; fi
  ln -sfn "$ROOT/app/valconet" "$INST/valconet"
  ln -sf "$ROOT/app/valconet.cfg" "$INST/valconet.cfg"
  ln -sf "$ROOT/app/defaults/pref/valconet-autoconfig.js" "$INST/defaults/pref/valconet-autoconfig.js"
  mkdir -p "$INST/distribution"
  # Dev only: trust this sandbox's HTTPS proxy CA so real sites load.
  python3 - "$ROOT/app/distribution/policies.json" "$INST/distribution/policies.json" <<'PY'
import json, sys, os
p = json.load(open(sys.argv[1]))
import glob
cas = sorted(glob.glob("/tmp/ca/proxy-*.pem"))
if cas:
    p["policies"]["Certificates"] = {"Install": cas}
json.dump(p, open(sys.argv[2], "w"), indent=1)
PY
  [ "$2" = "fresh" ] && rm -rf "$PROF"
  mkdir -p "$PROF"
  cat > "$PROF/user.js" <<'P'
user_pref("devtools.console.stdout.chrome", true);
user_pref("browser.dom.window.dump.enabled", true);
user_pref("marionette.port", 2828);
user_pref("remote.prefs.recommended", false);
user_pref("ui.prefersReducedMotion", 0);
user_pref("font.name.sans-serif.x-western", "DejaVu Sans");
P
  pgrep Xvfb >/dev/null || (Xvfb :99 -screen 0 1400x900x24 >/dev/null 2>&1 &)
  sleep 1
  DISPLAY=:99 nohup "$INST/firefox" --no-remote --marionette -remote-allow-system-access -profile "$PROF" >/tmp/vn.log 2>&1 &
  sleep "${WAIT:-9}"
  ;;
stop)
  pkill -f "$INST/firefox" || true; sleep 1 ;;
esac
