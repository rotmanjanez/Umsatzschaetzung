#!/usr/bin/env bash
# Push the code, the generator and the JSON half of the corpus to halle. Images stay local.
# Scoring is not pushed: the box tags and dumps, tools/eval scores the dump here.
set -euo pipefail
HOST=${HOST:-halle}
REMOTE=${REMOTE:-'~/umsatz'}
HERE=$(cd "$(dirname "$0")/../.." && pwd)

ssh "$HOST" "mkdir -p $REMOTE/tools/train $REMOTE/tools/corpus $REMOTE/data $REMOTE/fixtures/dataset/gen"

rsync -az --delete --exclude="runs/" --exclude="*.log" --exclude="__pycache__/" "$HERE/tools/train/" "$HOST:$REMOTE/tools/train/"
rsync -az --delete --exclude="__pycache__/" "$HERE/tools/corpus/" "$HOST:$REMOTE/tools/corpus/"
rsync -az "$HERE/tools/units.py" "$HOST:$REMOTE/tools/units.py"
rsync -az "$HERE/data/units.json" "$HOST:$REMOTE/data/units.json"

rsync -az --stats \
  --include='*/' --include='truth.json' --include='expected.json' --include='*.ocr.json' --exclude='*' \
  "$HERE/fixtures/dataset/gen/" "$HOST:$REMOTE/fixtures/dataset/gen/"

echo "synced to $HOST:$REMOTE"
