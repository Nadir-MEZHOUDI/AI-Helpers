#!/usr/bin/env bash
# AI-Helpers installer for Linux / macOS
# Usage: curl -fsSL https://raw.githubusercontent.com/Nadir-MEZHOUDI/AI-Helpers/main/install.sh | bash

set -e

REPO_URL="https://github.com/Nadir-MEZHOUDI/AI-Helpers"
TMP_DIR=$(mktemp -d)
SKILLS_DST="$HOME/.claude/skills"

echo "AI-Helpers installer"
echo "--------------------"

# Clone
echo "Cloning $REPO_URL ..."
git clone --depth 1 "$REPO_URL" "$TMP_DIR" > /dev/null 2>&1

# Skills
SKILLS_SRC="$TMP_DIR/skills"
if [ -d "$SKILLS_SRC" ]; then
    mkdir -p "$SKILLS_DST"
    for skill_dir in "$SKILLS_SRC"/*/; do
        [ -d "$skill_dir" ] || continue
        skill_name=$(basename "$skill_dir")
        dest="$SKILLS_DST/$skill_name"
        rm -rf "$dest"
        cp -r "$skill_dir" "$dest"
        echo "  [skill] $skill_name"
    done
fi

# Cleanup
rm -rf "$TMP_DIR"

echo ""
echo "Done. Restart Claude Code to pick up new skills."
