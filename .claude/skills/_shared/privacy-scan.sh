#!/usr/bin/env bash
# Scans what a change ADDS for personal data, secrets and binaries before it reaches the public repo.
# Shared by the create-pr, review-pr and create-release skills. Run from the repo root in Git Bash.
#
#   privacy-scan.sh <base> [<head>]
#     <base> <head>  scan the lines <head> adds compared with merge-base(<base>, <head>)  (e.g. origin/dev pr-123)
#     <base> only    scan committed + uncommitted changes since merge-base(<base>, HEAD), plus untracked files
#
# Values are printed masked. Exit code: 0 clean, 1 findings to review, 2 usage error.
# Findings are leads, not verdicts: test data like "AA:BB:CC:DD:EE:FF" is fine; a real MAC is not.
set -u
base=${1:-}; head=${2:-}
[ -z "$base" ] && { echo "usage: privacy-scan.sh <base> [<head>]" >&2; exit 2; }
root=$(git rev-parse --show-toplevel) || exit 2
cd "$root" || exit 2

if [ -n "$head" ]; then
  mb=$(git merge-base "$base" "$head") || exit 2
  diff_args=("$mb" "$head")
  untracked=""
else
  mb=$(git merge-base "$base" HEAD) || exit 2
  diff_args=("$mb")
  untracked=$(git ls-files --others --exclude-standard)
fi

tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
added="$tmp/added.tsv"   # path<TAB>line

git diff --unified=0 --no-color --no-ext-diff "${diff_args[@]}" -- . ':(exclude).claude/skills/_shared/privacy-scan.sh' |
  awk '/^\+\+\+ /{ f=substr($0,5); sub(/^b\//,"",f); next } /^\+/{ print f "\t" substr($0,2) }' > "$added"
for f in $untracked; do
  [ "$f" = .claude/skills/_shared/privacy-scan.sh ] && continue
  [ -f "$f" ] && grep -Iq . "$f" 2>/dev/null && awk -v f="$f" '{ print f "\t" $0 }' "$f" >> "$added"
done

findings=0
say() { echo "$1"; findings=$((findings+1)); }

# 1. Files that must never be committed, binaries and big files.
{
  git diff --name-only --diff-filter=AMR "${diff_args[@]}"
  [ -n "$untracked" ] && printf '%s\n' $untracked
} | sort -u > "$tmp/files"
while IFS= read -r f; do
  [ -z "$f" ] && continue
  case "$f" in
    *.jks|*.keystore|*.p12|*.pfx|*.pem|*.key|*keystore.properties|*local.properties|.env|*.env|CLAUDE.local.md|*.local.md)
      say "[forbidden-file] $f";;
    windows/Orbit.Windows/ThirdParty/*/README.md) ;;
    windows/Orbit.Windows/ThirdParty/*)
      say "[third-party-binary] $f (must come from tools/fetch-thirdparty.ps1)";;
    *.apk|*.aab|*.exe|*.dll|*.so|*.jar|*.zip|*.7z|*.msi|*.bin)
      say "[binary] $f";;
  esac
  if [ -f "$f" ]; then
    size=$(wc -c < "$f" 2>/dev/null || echo 0)
    [ "$size" -gt 524288 ] && say "[large-file] $f ($((size/1024)) KB)"
  fi
done < "$tmp/files"
git diff --numstat "${diff_args[@]}" | awk -F'\t' '$1=="-" && $2=="-" { print $3 }' | while IFS= read -r f; do
  case "$f" in *.png|*.svg|*.ico|*.webp|*.ttf|*.otf) ;; *) echo "[binary] $f";; esac
done > "$tmp/bin"
while IFS= read -r l; do say "$l"; done < "$tmp/bin"

# 2. Patterns in added lines.
awk -F'\t' '
function mask(s,   n, r, i) { n = length(s); if (n <= 4) return "****"; r = substr(s, 1, 3); for (i = 4; i <= n; i++) r = r "*"; return r }
function check(cat, re, path, line,   rest, m) {
  rest = line
  while (match(rest, re)) {
    m = substr(rest, RSTART, RLENGTH); rest = substr(rest, RSTART + RLENGTH)
    if (cat == "ip" && (m ~ /^(127\.|0\.0\.0\.0|255\.255\.255\.255|192\.0\.2\.|198\.51\.100\.|203\.0\.113\.)/ || line ~ /[Vv]ersion/)) continue
    if (cat == "email" && m ~ /(noreply|example\.(com|org)|@users\.noreply\.github\.com)$/) continue
    if (cat == "mac" && toupper(m) ~ /^((00:){5}00|02:00:00:00:00:00|AA:BB:CC:DD:EE:FF|00:11:22:33:44:55|11:22:33:44:55:66|12:34:56:78:9A:BC|(FF:){5}FF)$/) continue
    printf "[%s] %s: %s\n", cat, path, mask(m)
  }
}
{
  p = $1; l = substr($0, length($1) + 2)
  check("private-key",  "-----BEGIN [A-Z ]*PRIVATE KEY-----", p, l)
  check("token",        "(gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9_-]{20,}|xox[abprs]-[A-Za-z0-9-]{10,})", p, l)
  check("secret",       "([Pp]assword|PASSWORD|[Pp]asswd|[Ss]ecret|SECRET|[Aa]pi[_-]?[Kk]ey|API_KEY)[\"'\'' ]*[:=] *[\"'\''][^\"'\'' ]{6,}[\"'\'']", p, l)
  check("mac",          "([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}", p, l)
  check("ip",           "(^|[^0-9.])([0-9]{1,3}\\.){3}[0-9]{1,3}", p, l)
  check("email",        "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}", p, l)
  check("local-path",   "([A-Za-z]:[\\\\/]+(Users|Documents and Settings)[\\\\/]+[A-Za-z0-9._ -]+|/(Users|home)/[a-z][A-Za-z0-9._-]+)", p, l)
  check("drive-path",   "(^|[^A-Za-z])[D-Zd-z]:[\\\\/][A-Za-z]", p, l)
  check("private-link", "claude\\.ai/(artifact|code/artifact|chat|share)/[A-Za-z0-9-]+", p, l)
}' "$added" > "$tmp/patterns"
while IFS= read -r l; do say "$l"; done < "$tmp/patterns"

# 3. Values from the owner's private notes (CLAUDE.local.md and other *.local.md): serials, MACs, paths, links.
for notes in "$root"/CLAUDE.local.md "$root"/*.local.md; do
  [ -f "$notes" ] || continue
  grep -oE '`[^`]{6,}`|https?://[^ )]+' "$notes" | tr -d '`' |
    grep -E '([0-9A-Fa-f]{2}:){5}|^[A-Z0-9]*[0-9][A-Z0-9]*$|[A-Za-z]:\\|%USERPROFILE%|claude\.ai/(artifact|code)|@' |
    grep -vE '^.{0,7}$' >> "$tmp/private"
done
if [ -s "$tmp/private" ]; then
  sort -u "$tmp/private" -o "$tmp/private"
  # awk, not grep -iF: Git Bash's grep aborts on -i -F -f.
  awk -F'\t' 'NR == FNR { if ($0 != "") v[tolower($0)] = 1; next }
    { l = tolower(substr($0, length($1) + 2)); for (k in v) if (index(l, k)) { print "[private-note] " $1 ": contains a value from your *.local.md notes"; break } }' \
    "$tmp/private" "$added" > "$tmp/privhits"
  while IFS= read -r l; do say "$l"; done < "$tmp/privhits"
fi

if [ "$findings" -eq 0 ]; then echo "privacy-scan: clean ($(wc -l < "$added") added lines checked)"; exit 0; fi
echo "privacy-scan: $findings finding(s) to review"
exit 1
