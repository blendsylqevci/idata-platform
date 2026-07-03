#!/usr/bin/env bash
# Lightweight confidentiality guard for iData+.
# Install manually if desired:
#   ln -sf ../../scripts/precommit_confidential_guard.sh .git/hooks/pre-commit
set -euo pipefail

staged=$(git diff --cached --name-only --diff-filter=ACMR)
# Only scan added/copied/modified/renamed files. Deletions of previously tracked sensitive files must be allowed for cleanup commits.
[ -z "$staged" ] && exit 0

bad=""

# Path-level hard blocks for owner/private material.
echo "$staged" | grep -iE '(^|/)(00_raw|03_contracts|docs-private|private-sensitive|evidence-private-local)(/|$)' && bad="1" || true
echo "$staged" | grep -iE '(^|/)(docs/evidence|docs/archive/prodata)(/|$)' && bad="1" || true
echo "$staged" | grep -iE 'Oferta|Ofert|Kontrat|Shtojc|Dokumenti_Master|Certifikimit_SEF|Dosja_Certifikimit|CANONICAL_SPLIT_COPY_MANIFEST' && bad="1" || true

# Content-level precise blocks. Avoid broad words like "secret" because rules docs mention them.
for f in $staged; do
  [ -f "$f" ] || continue
  case "$f" in
    scripts/precommit_confidential_guard.sh|tools-provenance/*)
      # Provenance scripts may contain detector regexes; they are reviewed separately.
      continue
      ;;
  esac
  if grep -IEi '(-----BEGIN (RSA |EC |OPENSSH |PRIVATE )?KEY-----|gho_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|sk-[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16})' "$f" >/dev/null 2>&1; then
    echo "BLOCKED: '$f' may contain a private key or token." >&2
    bad="1"
  fi
  if grep -IEi '(^|[^A-Za-z])(api[_-]?key|password|token|secret|db[_-]?url|database_url)[[:space:]]*[:=][[:space:]]*[^[:space:]]+' "$f" >/dev/null 2>&1; then
    echo "BLOCKED: '$f' may contain assigned credentials." >&2
    bad="1"
  fi
  if grep -IEi '([0-9]{2,3}[\.,][0-9]{3}[\.,][0-9]{2}|[0-9]{12}|NUI[[:space:]]+[0-9]{8,}|TVSH[[:space:]]+[0-9]{8,})' "$f" >/dev/null 2>&1; then
    echo "BLOCKED: '$f' may contain real financial amounts, regulated IDs, or legal identifiers." >&2
    bad="1"
  fi
done

if [ -n "$bad" ]; then
  echo "" >&2
  echo "COMMIT BLOCKED — possible confidential material. Remove from staging or ask Blend." >&2
  exit 1
fi
