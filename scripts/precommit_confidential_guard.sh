#!/usr/bin/env bash
# Lightweight confidentiality guard for iData+ (pre-commit).
#
# The guard evaluates the STAGED CANDIDATE — the index entries and the exact blobs
# the commit will record — never the working-tree copies of the same paths.
# Install manually if desired; README.md ("Guard-i i privacy-t") describes a safe
# installation that never overwrites an existing hook.
#
# Result semantics (exit code):
#   0  PASS            the supported inspection completed and no configured rule was violated
#   1  BLOCK           a staged path or staged content violates a confidentiality rule
#   2  REVIEW_REQUIRED staged content cannot be evaluated automatically (a blob containing
#                      NUL bytes, a symlink or a submodule entry) — a human must review it
#   3  ERROR           the inspection itself could not be completed (not inside a work tree,
#                      missing index file, git enumeration or object read failed, unmerged
#                      entries, any unexpected failure)
# Every non-zero result prevents the commit. PASS is NOT a guarantee that the content is
# free of confidential material: it means the configured detectors found nothing in the
# supported (text) content. Deletions are never inspected, so removing previously tracked
# sensitive material stays possible.
#
# Content detectors run in two passes over every inspected blob; a match in either blocks:
#   byte-wise         C locale — also covers lines that are not valid in the committer's encoding;
#   committer-locale  the locale the guard was started with (LC_ALL, LC_CTYPE, LANG), as the
#                     original guard ran its detectors (under a UTF-8 locale `[[:space:]]` can
#                     also match Unicode whitespace such as a no-break space).
# GREP_OPTIONS is ignored in both passes.
# Diagnostics name the rule class and the path, never the content. A path that matches a
# protected-path rule is itself owner material, so its name is withheld and the entry is
# identified by its position in the staged list instead.
set -euo pipefail

# The committer's locale, recorded before the guard pins its own: the committer-locale pass
# reproduces it exactly (an unset LC_ALL stays unset, so LC_CTYPE/LANG keep deciding).
if [ -n "${LC_ALL+set}" ]; then
  committer_lc_all_set=1
  committer_lc_all=$LC_ALL
else
  committer_lc_all_set=0
  committer_lc_all=''
fi
# Byte-wise, locale-independent processing (identical under BSD and GNU grep/tr/wc).
LC_ALL=C
export LC_ALL
# grep must not be reconfigured by the environment: BSD grep still honours GREP_OPTIONS, where
# e.g. --exclude=* or -m0 would silently switch every detector off (GNU grep ignores it).
unset GREP_OPTIONS
# Inspect the objects the commit will actually record: replacement objects
# (refs/replace/*) must not change what the guard sees.
GIT_NO_REPLACE_OBJECTS=1
export GIT_NO_REPLACE_OBJECTS

verdict=""
work=""

# Single exit path. Anything that ends the script without one of the explicit verdicts
# below — an unexpected failure anywhere, including inside functions — is reported as
# ERROR (3), never as PASS and never as a misleading BLOCK.
finish() {
  local rc=$?
  set +e
  if [ -n "$work" ] && ! rm -rf -- "$work" 2>/dev/null; then
    printf 'PRIVACY GUARD: warning — temporary directory could not be removed: %q\n' "$work" >&2
  fi
  if [ -z "$verdict" ]; then
    printf 'PRIVACY GUARD: ERROR — inspection did not complete (exit %s). Commit prevented.\n' "$rc" >&2
    exit 3
  fi
  exit "$rc"
}
trap finish EXIT

fail() { # <reason>  — explicit inspection ERROR that stops immediately
  verdict=ERROR
  printf 'PRIVACY GUARD: ERROR — %s. Commit prevented.\n' "$1" >&2
  exit 3
}

# A hook always runs inside a work tree; a direct invocation might not.
inside=""
if ! inside=$(git rev-parse --is-inside-work-tree 2>/dev/null) || [ "$inside" != true ]; then
  fail 'not inside a git work tree'
fi
# git treats a missing index file as an EMPTY index. A caller that names an index
# explicitly (git commit always does for hooks) must point at an existing file,
# otherwise nothing would be enumerated and the result would look like PASS.
# git resolves a relative GIT_INDEX_FILE against the top of the work tree;
# `--git-path index` returns that location as a path usable from here.
if [ -n "${GIT_INDEX_FILE:-}" ]; then
  index_path=$(git rev-parse --git-path index)
  [ -f "$index_path" ] || fail 'the index file named by GIT_INDEX_FILE does not exist'
fi

work=$(mktemp -d "${TMPDIR:-/tmp}/idata-privacy-guard.XXXXXX")

# ── Rules (unchanged detector set; case-insensitive like the original `grep -i`) ──
# Path-level hard blocks for owner/private material.
re_path_private='(^|/)(00_raw|03_contracts|docs-private|private-sensitive|evidence-private-local)(/|$)'
re_path_evidence='(^|/)(docs/evidence|docs/archive/prodata)(/|$)'
re_path_owner_docs='Oferta|Ofert|Kontrat|Shtojc|Dokumenti_Master|Certifikimit_SEF|Dosja_Certifikimit|CANONICAL_SPLIT_COPY_MANIFEST'
# Content-level precise blocks. Avoid broad words like "secret" because rules docs mention them.
re_content_keys='(-----BEGIN (RSA |EC |OPENSSH |PRIVATE )?KEY-----|gho_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|sk-[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16})'
re_content_credentials='(^|[^A-Za-z])(api[_-]?key|password|token|secret|db[_-]?url|database_url)[[:space:]]*[:=][[:space:]]*[^[:space:]]+'
re_content_identifiers='([0-9]{2,3}[\.,][0-9]{3}[\.,][0-9]{2}|[0-9]{12}|NUI[[:space:]]+[0-9]{8,}|TVSH[[:space:]]+[0-9]{8,})'

det_regex=("$re_content_keys" "$re_content_credentials" "$re_content_identifiers")
det_class=('content may contain a private key or token'
           'content may contain assigned credentials'
           'content may contain real financial amounts, regulated IDs, or legal identifiers')
det_name=('key/token' 'credential' 'identifier')

blocked=0
review=0
errors=0
records=0
inspected=0
deleted=0

# <label> is the entry as it may be shown: the path quoted with %q (whitespace and newlines
# stay visible), or the withheld form for protected names. Content is never printed.
report() { # <verdict> <class> <label>
  printf '%s %s: %s\n' "$1" "$2" "$3" >&2
}

# Bash ERE match, case-insensitive. nocasematch is enabled only for this test so that
# every `case` statement (e.g. the content exemption below) stays case-sensitive.
matches_regex() { # <value> <ERE>
  local rc=0
  shopt -s nocasematch
  [[ $1 =~ $2 ]] || rc=$?
  shopt -u nocasematch
  case "$rc" in
    0|1) return "$rc" ;;
    *) fail 'a path rule could not be evaluated' ;;
  esac
}

# grep over a file: 0 = match, 1 = no match, anything else = inspection error (returned as-is).
content_matches() { # <pass> <blob-file> <ERE>
  local rc=0
  case "$1" in
    byte-wise)
      grep -a -E -i -q -e "$3" -- "$2" || rc=$? ;;
    committer-locale)
      if [ "$committer_lc_all_set" -eq 1 ]; then
        env LC_ALL="$committer_lc_all" grep -a -E -i -q -e "$3" -- "$2" || rc=$?
      else
        env -u LC_ALL grep -a -E -i -q -e "$3" -- "$2" || rc=$?
      fi ;;
    *) fail 'unknown content pass' ;;
  esac
  return "$rc"
}

inspect_content() { # <path> <blob-sha> <label>
  local path=$1 sha=$2 label=$3 blob="$work/blob" total nonul rc i pass

  # Existing exemption (unchanged, case-sensitive): files that hold detector regexes by design.
  # Their content is not inspected at all (the NUL check included); path rules still apply.
  case "$path" in
    scripts/precommit_confidential_guard.sh|tools-provenance/*) return 0 ;;
  esac

  if ! git cat-file blob "$sha" > "$blob" 2>/dev/null; then
    report ERROR 'staged blob could not be read' "$label"
    errors=$((errors + 1))
    return 0
  fi

  # A NUL byte anywhere (binary data, UTF-16 text, ...) puts the blob outside the supported
  # text inspection: human review, never PASS. No extension-based assumptions.
  total=$(wc -c < "$blob")
  nonul=$(tr -d '\000' < "$blob" | wc -c)
  if [ $((total)) -ne $((nonul)) ]; then
    report REVIEW_REQUIRED 'binary or opaque content (NUL bytes) is not inspected automatically' "$label"
    review=$((review + 1))
    return 0
  fi

  for i in 0 1 2; do
    for pass in byte-wise committer-locale; do
      rc=0
      content_matches "$pass" "$blob" "${det_regex[$i]}" || rc=$?
      case "$rc" in
        0) report BLOCK "${det_class[$i]}" "$label"; blocked=$((blocked + 1)); break ;;
        1) ;;
        *) report ERROR "content inspection failed (${det_name[$i]} detector, $pass pass, grep exit $rc)" "$label"; errors=$((errors + 1)) ;;
      esac
    done
  done
}

# ── Enumerate the staged candidate ───────────────────────────────────────────
# Raw records with full object ids and NUL separators:
#   ":<old-mode> <new-mode> <old-sha> <new-sha> <status>\0<path>\0"
# No --diff-filter: every staged entry is listed and classified below, so no git state
# can drop out of consideration. --no-renames: one path per record (a rename is a
# deletion plus an addition, regardless of diff.renames). --no-relative and
# --ignore-submodules=none: diff.relative / diff.ignoreSubmodules cannot hide entries.
git diff --cached --raw -z --no-abbrev --no-renames --no-relative --ignore-submodules=none > "$work/raw"

while IFS= read -r -d '' meta && IFS= read -r -d '' path; do
  records=$((records + 1))
  meta=${meta#:}
  read -r old_mode new_mode old_sha new_sha status <<< "$meta"

  if [ "$status" = D ]; then
    deleted=$((deleted + 1))   # never inspected: cleanup must stay possible
    continue
  fi

  # Path-level rules first, so that every diagnostic about this entry can withhold a
  # protected name. They apply to every added or modified path, whatever its type.
  path_private=0
  path_owner=0
  if matches_regex "$path" "$re_path_private" || matches_regex "$path" "$re_path_evidence"; then
    path_private=1
  fi
  if matches_regex "$path" "$re_path_owner_docs"; then
    path_owner=1
  fi
  if [ $((path_private + path_owner)) -gt 0 ]; then
    label="staged entry $records (name withheld: it matches a protected-path rule)"
  else
    printf -v label '%q' "$path"
  fi

  case "$status" in
    U) report ERROR 'unmerged index entry cannot be inspected' "$label"; errors=$((errors + 1)); continue ;;
    A|M|T) ;;
    *) report ERROR "unexpected index entry status '$status'" "$label"; errors=$((errors + 1)); continue ;;  # defensive
  esac
  inspected=$((inspected + 1))

  if [ "$path_private" -eq 1 ]; then
    report BLOCK 'path is owner/private material' "$label"; blocked=$((blocked + 1))
  fi
  if [ "$path_owner" -eq 1 ]; then
    report BLOCK 'path name denotes owner-only commercial/legal material' "$label"; blocked=$((blocked + 1))
  fi

  case "$new_mode" in
    160000) report REVIEW_REQUIRED 'submodule/gitlink content is not inspected' "$label"; review=$((review + 1)); continue ;;
    120000) report REVIEW_REQUIRED 'symlink target is not inspected' "$label"; review=$((review + 1)); continue ;;
    100644|100755) ;;
    *) report ERROR "unsupported index entry mode '$new_mode'" "$label"; errors=$((errors + 1)); continue ;;
  esac

  # Defensive: git diff --cached never lists an added/modified entry without a blob
  # (an intent-to-add entry has no staged blob and is not listed; commit -a or
  # `git commit -- <path>` stages its content first, and then it is listed like any addition).
  if [[ $new_sha =~ ^0+$ ]]; then
    report ERROR 'no staged blob recorded for path' "$label"; errors=$((errors + 1)); continue
  fi

  inspect_content "$path" "$new_sha" "$label"
done < "$work/raw"

# Every record must have been consumed: two NUL-terminated fields per record, and nothing
# left over after the last complete record (a list cut inside an entry's fields).
nuls=$(tr -cd '\000' < "$work/raw" | wc -c)
if [ $((nuls)) -ne $((records * 2)) ] || [ -n "$meta" ]; then
  printf 'ERROR the staged entry list could not be parsed completely (%d records, %d separators)\n' "$records" "$((nuls))" >&2
  errors=$((errors + 1))
fi

# ── Verdict ──────────────────────────────────────────────────────────────────
if [ "$blocked" -gt 0 ]; then
  verdict=BLOCK
  printf '\nPRIVACY GUARD: BLOCK — %d violation(s) in the staged candidate (%d entries inspected, %d deletions not inspected). Commit prevented. Remove from staging or ask Blend.\n' \
    "$blocked" "$inspected" "$deleted" >&2
  exit 1
fi
if [ "$errors" -gt 0 ]; then
  verdict=ERROR
  printf '\nPRIVACY GUARD: ERROR — %d inspection failure(s); the staged candidate could not be evaluated. Commit prevented.\n' "$errors" >&2
  exit 3
fi
if [ "$review" -gt 0 ]; then
  verdict=REVIEW_REQUIRED
  printf '\nPRIVACY GUARD: REVIEW_REQUIRED — %d staged entr(y/ies) cannot be evaluated automatically. Unstage them and ask Blend after a manual review. Commit prevented.\n' "$review" >&2
  exit 2
fi
verdict=PASS
exit 0
