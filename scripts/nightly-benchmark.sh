#!/usr/bin/env bash
set -euo pipefail

# chmod +x /home/jordan/Code/LeanCorpus/scripts/nightly-benchmark.sh
# 0 2 * * * /home/jordan/Code/LeanCorpus/scripts/leancorpus-nightly.sh >> /home/jordan/logs/leancorpus/cron.log 2>&1

export PATH="/usr/local/bin:/usr/bin:/bin"

REPO_DIR="/home/jordan/Code/LeanCorpus"
BRANCH="1.4.0"
TMUX_SESSION="leancorpus-benchmark"

/usr/bin/git -C "$REPO_DIR" fetch origin
/usr/bin/git -C "$REPO_DIR" checkout "$BRANCH"
/usr/bin/git -C "$REPO_DIR" reset --hard "origin/$BRANCH"

/usr/bin/tmux kill-session -t "$TMUX_SESSION" 2>/dev/null || true

/usr/bin/tmux new-session -d -s "$TMUX_SESSION" \
  "cd $REPO_DIR && ./scripts/benchmark.sh"