#!/usr/bin/env bash
# Regression suite for scripts/precommit_confidential_guard.sh.
#
# Every case runs the REAL guard (directly or as an installed pre-commit hook) inside
# throwaway synthetic git repositories created under mktemp; nothing touches the
# repository this script lives in. Fixtures are synthetic and are assembled from
# fragments at runtime so that this file never contains a literal detector match
# (otherwise the guard — and the repository secret scanner — would rightly flag it).
# Case names carry the acceptance-case number of the package:
#   A1–A14  staged authority, difficult names, ignored root dirs, deletions, opaque content,
#           inspection failures, legitimate content, hook contexts, non-disclosure
#   A15     every existing detector family, with near misses that must stay allowed
#   A16     boundaries of the existing content exemption
#   A17     committer locale (Unicode whitespace, as the original guard) + byte-wise coverage
#   A18     initial commits (unborn HEAD, orphan branch)
#   A19     difficult names under the path rules
#   A20     protected names are withheld from diagnostics; other paths stay visible
#   A21     the README installation snippet, extracted from README.md and executed
#   A22     the README recipe that unstages a withheld entry without printing its name
#
# Usage:
#   bash scripts/test_precommit_confidential_guard.sh       # exit 0 = every case passes
#   GUARD_SCRIPT=/path/guard.sh GITIGNORE_FILE=/path/.gitignore README_FILE=/path/README.md \
#     bash scripts/test_precommit_confidential_guard.sh     # same suite, other builds
#
# The suite copies README.md and PROJECT_RULES.md as legitimate content (A2, A17): those two
# documents must stay clean for the detectors. Guard output is captured to a file; on a
# failure only a short excerpt of it (classes and labels) is printed, and fixture content —
# whole lines and every 5-byte fragment of a protected value — is asserted to be ABSENT
# from it. Cases that need a locale or shell this machine does not have are reported as
# SKIP and are never counted as passed.
set -euo pipefail
LC_ALL=C
export LC_ALL

# Hermetic git: the developer's global/system configuration (core.hooksPath, excludes,
# templates, aliases, init.defaultBranch) and inherited git variables must not change any result.
GIT_CONFIG_GLOBAL=/dev/null
GIT_CONFIG_NOSYSTEM=1
export GIT_CONFIG_GLOBAL GIT_CONFIG_NOSYSTEM
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR GIT_OBJECT_DIRECTORY \
      GIT_CONFIG_COUNT GIT_CONFIG_PARAMETERS GIT_NO_REPLACE_OBJECTS

here=$(cd "$(dirname "$0")" && pwd)
repo_root=$(cd "$here/.." && pwd)
GUARD=${GUARD_SCRIPT:-$here/precommit_confidential_guard.sh}
GITIGNORE=${GITIGNORE_FILE:-$repo_root/.gitignore}
README=${README_FILE:-$repo_root/README.md}
PROJECT_RULES=$repo_root/PROJECT_RULES.md
SUITE=$here/$(basename "$0")
bash_bin=$(command -v bash)
real_git=$(command -v git)
real_grep=$(command -v grep)

[ -f "$GUARD" ] || { echo "guard script not found: $GUARD" >&2; exit 2; }
[ -x "$GUARD" ] || { echo "guard script is not executable (git does not run such hooks): $GUARD" >&2; exit 2; }
[ -f "$GITIGNORE" ] || { echo ".gitignore not found: $GITIGNORE" >&2; exit 2; }
[ -f "$README" ] || { echo "README not found: $README (content in A2/A17, snippet in A21)" >&2; exit 2; }
[ -f "$PROJECT_RULES" ] || { echo "PROJECT_RULES.md not found (used as legitimate content in A2/A17)" >&2; exit 2; }

sandbox=$(mktemp -d "${TMPDIR:-/tmp}/idata-guard-tests.XXXXXX")
trap 'rm -rf -- "$sandbox"' EXIT
# No git templates (a template hook must not pre-empt the hook under test) and no repository
# discovery above the sandbox (a TMPDIR inside another work tree must not be found).
mkdir -p -- "$sandbox/empty-template"
GIT_TEMPLATE_DIR=$sandbox/empty-template
GIT_CEILING_DIRECTORIES=$sandbox
export GIT_TEMPLATE_DIR GIT_CEILING_DIRECTORIES

# ── Synthetic fixtures (assembled, never literal) ─────────────────────────────
# Protected VALUES are kept apart from the surrounding syntax: no 5-byte fragment of any
# of them may ever reach guard or hook output (A3, A14, A15, A17, A20).
v_kbody="c3ludGhl""dGljLWtl""eS1tYXRl""cmlhbA=="                # base64 of a synthetic phrase
fx_key="-----BEGIN ""PRIVATE ""KEY-----"$'\n'"$v_kbody"    # PKCS#8 header shape
v_akid="Z7XQ""4M2K""9W5R""3N8T"                                  # 16 upper-case alphanumerics
fx_akid="AK""IA""$v_akid"                                    # access-key-id shape
v_cred="Vq3x""Kz81""Wm4p"
fx_cred="api_""key = $v_cred"
v_cred_lower="Hp2w""Rn64""Tq5s"
fx_cred_lower="pass""word: $v_cred_lower"                   # case-insensitive keyword, colon form
fx_digits="2025""0101""8899"                                 # 12-digit run
v_body2="TWFya2VyS2V5""Qm9keTI="
v_gho="Fz8kQ2""wR7tY4""pL0"
v_ghp="11AB""CDEFG0""_xYz9"
v_sk="Ab12""Cd34""Ef56""Gh78""Ij90"                          # exactly 20 characters
v_sk2="proj""-Ab12_Cd34""-Ef56_Gh78"                         # body with - and _ (prefixed key shapes)
v_cv="Rk5m""Ty83""Qw7e"
v_amount1="12.""345,67"
v_amount2="123,""456.78"
v_nui="1234""5678"
v_tvsh="8765""4321"
v_client="QZ7c""Xv2L""m9Rt"                                    # distinctive part of protected names
v_loc_db="Kd7r""Pq42""Zx1d"
v_loc_tok="Wm3n""Ys58""Bv2k"
v_loc_lat="Jb6t""Gx71""Nc4h"
v_loc_nui="2233""4455""66"
v_loc_tvsh="5566""7788"
v_loc_space="1122""3344""55"
v_cp1252=$'\xe9\xe9\xe9\xe9\xe9'                             # a value in a legacy single-byte encoding
# Rule tokens, assembled (the repository scanner and the linter flag them in tracked files).
t_oferta="Ofe""rta"; t_ofert="Ofe""rt"; t_kontrat="Kon""trat"; t_shtojc="Sht""ojc"
t_dokumenti="Dokumenti""_Master"; t_cert_sef="Certifikimit""_SEF"; t_dosja="Dosja""_Certifikimit"
t_manifest="CANONICAL_SPLIT""_COPY_MANIFEST"
d_raw="00_""raw"; d_contracts="03_""contracts"; d_docs_private="docs-""private"
d_sensitive="private-""sensitive"; d_epl="evidence-""private-local"
d_evidence="docs/""evidence"; d_prodata="docs/archive/""prodata"
t_gho="gh""o_"; t_ghp="git""hub_pat_"; t_sk="s""k-"
ws_nbsp=$'\xc2\xa0'; ws_nnbsp=$'\xe2\x80\xaf'; ws_emsp=$'\xe2\x80\x83'
fx_owner_doc="${t_kontrat}a_synthetic_fixture.md"
fx_owner_doc_upper="KON""TRATA_SYNTHETIC_FIXTURE.MD"
fx_private_dir=$d_contracts
fx_private_dir_upper="03_""CONTRACTS"
zero_sha=$(printf '%040d' 0)
zero_mode=$(printf '%06d' 0)

# Clean technical content that must stay allowed.
clean_cs='namespace Synthetic;

public sealed class TokenBucket
{
    // The access token is validated by the authority; nothing is assigned here.
    public int Capacity { get; init; } = 1_000;
}
'
clean_md='# Synthetic notes

Fjalëkalimet ruhen vetëm në vault; ky dokument përshkruan procesin, jo vlerat.
Shumat reale nuk shkruhen këtu; numra të shkurtër si 20250101 janë të lejuar.
'
clean_nbsp="Shënim${ws_nbsp}teknik: hapësira e pandashme ndan fjalët, nuk mban vlera."

pass_count=0
fail_count=0
skip_count=0
case_no=0
out=""
rc=0

ok()   { pass_count=$((pass_count + 1)); printf 'PASS %3d: %s\n' "$case_no" "$1"; }
bad()  { fail_count=$((fail_count + 1)); printf 'FAIL %3d: %s — %s\n' "$case_no" "$1" "$2"; }
skip() { skip_count=$((skip_count + 1)); printf 'SKIP %3d: %s — %s\n' "$case_no" "$1" "$2"; }
new_case() { case_no=$((case_no + 1)); }

# Every 5-byte window of a value (windows across a line break are left out).
frag_len=5
fragments_of() { # <value>
  local v=$1 i=0 n=${#1} w
  while [ $((i + frag_len)) -le "$n" ]; do
    w=${v:i:frag_len}
    case "$w" in *$'\n'*) ;; *) printf '%s\n' "$w" ;; esac
    i=$((i + 1))
  done
}
has_fragment() { # <value> <file>  → 0 if any fragment of the value occurs in the file
  fragments_of "$1" > "$sandbox/fragments.pat"
  [ -s "$sandbox/fragments.pat" ] || return 1
  grep -q -F -f "$sandbox/fragments.pat" -- "$2"
}
protected_values=("$v_kbody" "$v_akid" "$v_cred" "$v_cred_lower" "$fx_digits" "$v_body2" "$v_gho"
  "$v_ghp" "$v_sk" "$v_sk2" "$v_cv" "$v_amount1" "$v_amount2" "$v_nui" "$v_tvsh" "$v_client" "$v_loc_db"
  "$v_loc_tok" "$v_loc_lat" "$v_loc_nui" "$v_loc_tvsh" "$v_loc_space" "$v_cp1252")
: > "$sandbox/protected.pat"
for v in "${protected_values[@]}"; do fragments_of "$v" >> "$sandbox/protected.pat"; done
any_protected_fragment() { grep -q -F -f "$sandbox/protected.pat" -- "$1"; }
# Path-rule tokens (whole tokens, never fragments: guard messages share short words with them).
printf '%s\n' "$t_ofert" "$t_kontrat" "$t_shtojc" "$t_dokumenti" "$t_cert_sef" "$t_dosja" "$t_manifest" \
  "$d_raw" "$d_contracts" "$d_docs_private" "$d_sensitive" "$d_epl" "$d_evidence" "$d_prodata" > "$sandbox/tokens.pat"

# Short excerpt of the guard output for a FAIL line — withheld if it carries fixture content.
summary() {
  if any_protected_fragment "$out" || grep -q -i -F -f "$sandbox/tokens.pat" -- "$out"; then
    printf '%s' '(guard output withheld: it contains protected fixture content or a protected name)'
  else
    tr '\n' '|' < "$out" | cut -c1-300
  fi
}

# Fresh synthetic repository with the repository's real .gitignore committed.
new_repo() { # <name>  → prints the path
  local r="$sandbox/$1"
  mkdir -p -- "$r"
  git -c init.defaultBranch=main init -q "$r"
  git -C "$r" config user.email synthetic-tester   # deliberately not an e-mail address (repository PII scanner)
  git -C "$r" config user.name 'Synthetic Tester'
  git -C "$r" config commit.gpgsign false
  git -C "$r" config core.hooksPath "$r/.git/hooks"
  mkdir -p -- "$r/.git/hooks"
  cp -- "$GITIGNORE" "$r/.gitignore"
  git -C "$r" add -- .gitignore
  git -C "$r" commit -q -m 'synthetic baseline (.gitignore)'
  printf '%s' "$r"
}

# Repository without any commit (unborn HEAD).
new_unborn_repo() { # <name>  → prints the path
  local r="$sandbox/$1"
  mkdir -p -- "$r"
  git -c init.defaultBranch=main init -q "$r"
  git -C "$r" config user.email synthetic-tester
  git -C "$r" config user.name 'Synthetic Tester'
  git -C "$r" config commit.gpgsign false
  git -C "$r" config core.hooksPath "$r/.git/hooks"
  mkdir -p -- "$r/.git/hooks"
  printf '%s' "$r"
}

write_file() { # <repo> <relative path> <content>
  mkdir -p -- "$(dirname -- "$1/$2")"
  printf '%s\n' "$3" > "$1/$2"
}

# Plain add: a fixture that the repository .gitignore unexpectedly ignores fails loudly here.
stage() { GIT_LITERAL_PATHSPECS=1 git -C "$1" add -- "$2"; }
# Force-add: for fixtures that are ignored on purpose (owner-only names, private dirs, root-local output and tmp).
stage_forced() { GIT_LITERAL_PATHSPECS=1 git -C "$1" add -f -- "$2"; }

install_hook() { ln -s -- "$GUARD" "$1/.git/hooks/pre-commit"; }

# Run the real guard with the CWD inside the repository (or a subdirectory); optional
# env arguments (VAR=value, -u VAR) describe the committer's environment.
run_guard() { # <dir> [env argument ...]
  local dir=$1
  shift
  out="$sandbox/out.txt"
  rc=0
  (cd -- "$dir" && env ${1+"$@"} "$bash_bin" "$GUARD") > "$out" 2>&1 || rc=$?
}

expect_rc() { # <expected exit> <case name>
  if [ "$rc" -ne "$1" ]; then
    bad "$2" "expected exit $1, got $rc; guard output: $(summary)"
  elif [ "$1" -ne 0 ] && [ ! -s "$out" ]; then
    bad "$2" "exit $rc without any diagnostic line"
  else
    ok "$2 (exit $rc)"
  fi
}

expect_output_has() { # <needle> <case name>
  if grep -q -F -e "$1" -- "$out"; then ok "$2"; else bad "$2" "output lacks '$1': $(summary)"; fi
}

expect_no_disclosure() { # <fixture value> <case name> [file]  — no line of the fixture may appear
  local file=${3:-$out} line found=0
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    if grep -q -F -e "$line" -- "$file"; then found=1; fi
  done <<< "$1"
  if [ "$found" -eq 0 ]; then ok "$2"; else bad "$2" "output disclosed fixture content"; fi
}

expect_no_fragments() { # <protected value> <case name> [file]  — not even a 5-byte fragment
  if has_fragment "$1" "${3:-$out}"; then bad "$2" "output disclosed a fragment of a protected value"; else ok "$2"; fi
}

commit_with_hook() { # <repo> <message> [-- <pathspec>...]  → 0 if the commit was created
  local repo=$1 message=$2
  shift 2
  git -C "$repo" commit -q -m "$message" ${1+"$@"} > /dev/null 2>> "$sandbox/hook.err"
}

commit_blocked_by_guard() { # <repo> <message>  → 0 if the commit was refused AND the guard reported BLOCK
  local repo=$1 message=$2 e="$sandbox/commit.err" crc=0
  git -C "$repo" commit -q -m "$message" > /dev/null 2> "$e" || crc=$?
  cat -- "$e" >> "$sandbox/hook.err"
  [ "$crc" -ne 0 ] && grep -q -F -e 'PRIVACY GUARD: BLOCK' -- "$e"
}

commit_with_hook_env() { # <repo> <message> <env argument ...>  → 0 if the commit was created
  local repo=$1 message=$2
  shift 2
  env "$@" git -C "$repo" commit -q -m "$message" > /dev/null 2>> "$sandbox/hook.err"
}

class_needle() { # <class key>
  case "$1" in
    path_private) printf '%s' 'owner/private material' ;;
    path_owner)   printf '%s' 'commercial/legal material' ;;
    key)          printf '%s' 'private key or token' ;;
    cred)         printf '%s' 'assigned credentials' ;;
    ident)        printf '%s' 'regulated IDs' ;;
    *)            printf '%s' '' ;;
  esac
}

# One staged entry per case in a shared repository (index and working tree reset to the
# baseline first), so that each verdict depends on exactly one rule. Checks the exit code,
# the reported class, silence on PASS and that no fragment of the protected value leaks.
fam_repo=""
entry_case() { # <env cfg|-> <expected exit> <class key|-> <path> <content> <protected value|-> <case name>
  local cfg=$1 exp=$2 cls=$3 path=$4 content=$5 pval=$6 name=$7 needle problems=""
  local -a envargs
  envargs=()
  [ "$cfg" = - ] || read -r -a envargs <<< "$cfg"
  new_case
  git -C "$fam_repo" read-tree HEAD
  git -C "$fam_repo" clean -f -d -x -q
  write_file "$fam_repo" "$path" "$content"
  stage_forced "$fam_repo" "$path"
  run_guard "$fam_repo" ${envargs[@]+"${envargs[@]}"}
  [ "$rc" -eq "$exp" ] || problems="$problems expected exit $exp, got $rc;"
  if [ "$exp" -eq 0 ]; then
    [ ! -s "$out" ] || problems="$problems diagnostics printed on PASS;"
  else
    needle=$(class_needle "$cls")
    if [ -n "$needle" ] && ! grep -q -F -e "$needle" -- "$out"; then problems="$problems class '$needle' not reported;"; fi
  fi
  if [ "$pval" != - ] && has_fragment "$pval" "$out"; then problems="$problems a fragment of the protected value was printed;"; fi
  if [ -z "$problems" ]; then ok "$name (exit $rc)"; else bad "$name" "${problems# } guard output: $(summary)"; fi
}
fam() { entry_case - "$@"; }

# Committer environments for the locale cases: a UTF-8 character type this machine supports.
ws_is_space() { # <env cfg> <whitespace bytes>  → 0 if [[:space:]] matches them in that environment
  local -a a
  read -r -a a <<< "$1"
  [ "$(printf 'a%sb\n' "$2" | env "${a[@]}" grep -c -E 'a[[:space:]]b' 2>/dev/null || true)" = 1 ]
}

# Tool shims for the inspection-failure cases: every tool the guard uses is linked, one is replaced.
make_shim() { # <name> <tool> <sh body>  → prints the shim directory
  local d="$sandbox/$1" t p
  mkdir -p -- "$d"
  for t in git grep mktemp rm wc tr env cat head sed; do
    if [ "$t" = "$2" ]; then continue; fi
    if p=$(command -v "$t"); then ln -s -- "$p" "$d/$t"; fi
  done
  printf '#!/bin/sh\n%s\n' "$3" > "$d/$2"
  chmod +x "$d/$2"                                           # BSD chmod has no "--"
  printf '%s' "$d"
}

# ═════════════════════════════════════════════════════════════════════════════
case "$GUARD" in "$repo_root"/*) guard_shown=${GUARD#"$repo_root"/} ;; *) guard_shown="$(basename "$GUARD") (GUARD_SCRIPT override)" ;; esac
echo "guard under test: $guard_shown"
echo "bash: $BASH_VERSION · $(git --version) · grep: $(command -v grep)"
echo
: > "$sandbox/hook.err"

# ── A1 · nothing staged ──────────────────────────────────────────────────────
new_case; r=$(new_repo a01-nothing)
run_guard "$r"; expect_rc 0 "A1 nothing staged is allowed"

# ── A2 · ordinary clean source/docs ──────────────────────────────────────────
new_case; r=$(new_repo a02-clean)
write_file "$r" src/TokenBucket.cs "$clean_cs"; stage "$r" src/TokenBucket.cs
write_file "$r" docs/notes.md "$clean_md"; stage "$r" docs/notes.md
cp -- "$README" "$r/docs/README-copy.md"; stage "$r" docs/README-copy.md
cp -- "$PROJECT_RULES" "$r/docs/PROJECT_RULES-copy.md"; stage "$r" docs/PROJECT_RULES-copy.md
run_guard "$r"; expect_rc 0 "A2 clean source and docs (incl. repository README/PROJECT_RULES) are allowed"

# ── A3 · existing detector classes still block ───────────────────────────────
new_case; r=$(new_repo a03-key)
write_file "$r" src/leak.txt "$fx_key"; stage "$r" src/leak.txt
run_guard "$r"; expect_rc 1 "A3 private key header is blocked"
expect_output_has 'private key or token' "A3 key detector class is reported"
expect_no_disclosure "$fx_key" "A3 key fixture is not echoed (all lines)"
expect_no_fragments "$v_kbody" "A3 no fragment of the key material is echoed"

new_case; r=$(new_repo a03-akid)
write_file "$r" src/env.txt "aws_id: $fx_akid"; stage "$r" src/env.txt
run_guard "$r"; expect_rc 1 "A3 access-key-id shaped token is blocked"
expect_output_has 'private key or token' "A3 token class is reported"
expect_no_disclosure "$fx_akid" "A3 token fixture is not echoed"
expect_no_fragments "$v_akid" "A3 no fragment of the token is echoed"

new_case; r=$(new_repo a03-cred)
write_file "$r" src/config.txt "$fx_cred"; stage "$r" src/config.txt
run_guard "$r"; expect_rc 1 "A3 assigned credential is blocked"
expect_output_has 'assigned credentials' "A3 credential class is reported"
expect_no_disclosure "$fx_cred" "A3 credential fixture is not echoed"
expect_no_fragments "$v_cred" "A3 no fragment of the credential value is echoed"

new_case; r=$(new_repo a03-cred-lower)
write_file "$r" src/config.yml "$fx_cred_lower"; stage "$r" src/config.yml
run_guard "$r"; expect_rc 1 "A3 assigned credential (lowercase keyword, colon) is blocked"
expect_output_has 'assigned credentials' "A3 credential class is reported (colon form)"
expect_no_fragments "$v_cred_lower" "A3 no fragment of the colon-form value is echoed"

new_case; r=$(new_repo a03-digits)
write_file "$r" docs/ledger.md "reference $fx_digits end"; stage "$r" docs/ledger.md
run_guard "$r"; expect_rc 1 "A3 12-digit identifier is blocked"
expect_output_has 'regulated IDs' "A3 identifier class is reported"
expect_no_fragments "$fx_digits" "A3 no fragment of the identifier is echoed"

new_case; r=$(new_repo a03-owner-name)
write_file "$r" "docs/$fx_owner_doc" "$clean_md"; stage_forced "$r" "docs/$fx_owner_doc"
run_guard "$r"; expect_rc 1 "A3 owner-only document name is blocked by the path rule"
expect_output_has 'commercial/legal material' "A3 owner-document class is reported"

new_case; r=$(new_repo a03-owner-name-upper)
write_file "$r" "docs/$fx_owner_doc_upper" "$clean_md"; stage_forced "$r" "docs/$fx_owner_doc_upper"
run_guard "$r"; expect_rc 1 "A3 owner-only document name in upper case is blocked (path rules stay case-insensitive)"

new_case; r=$(new_repo a03-private-path)
write_file "$r" "$fx_private_dir/agreement.md" "$clean_md"; stage_forced "$r" "$fx_private_dir/agreement.md"
run_guard "$r"; expect_rc 1 "A3 private path segment is blocked"
expect_output_has 'owner/private material' "A3 private-path class is reported"

new_case; r=$(new_repo a03-private-path-upper)
write_file "$r" "$fx_private_dir_upper/agreement.md" "$clean_md"; stage_forced "$r" "$fx_private_dir_upper/agreement.md"
run_guard "$r"; expect_rc 1 "A3 private path segment in upper case is blocked"

# The existing content exemption is unchanged: exact-case only, never widened (more in A16).
new_case; r=$(new_repo a03-exemption-case)
write_file "$r" Tools-Provenance/leak.txt "$fx_cred"; stage "$r" Tools-Provenance/leak.txt
run_guard "$r"; expect_rc 1 "A3 case variant of the exempt tools-provenance/ directory is still inspected"
new_case; r=$(new_repo a03-exemption-case-guard)
write_file "$r" SCRIPTS/PRECOMMIT_CONFIDENTIAL_GUARD.SH "$fx_cred"; stage "$r" SCRIPTS/PRECOMMIT_CONFIDENTIAL_GUARD.SH
run_guard "$r"; expect_rc 1 "A3 case variant of the exempt guard path is still inspected"
new_case; r=$(new_repo a03-exemption-exact)
write_file "$r" tools-provenance/notes.txt "$fx_cred"; stage "$r" tools-provenance/notes.txt
run_guard "$r"; expect_rc 0 "A3 existing exact-case tools-provenance/ content exemption is preserved (documented)"

# ── A4 · staged prohibited content, working tree cleaned → still blocked ────
new_case; r=$(new_repo a04-staged-dirty-wt-clean)
write_file "$r" src/settings.txt "$fx_cred"; stage "$r" src/settings.txt
write_file "$r" src/settings.txt "$clean_md"
run_guard "$r"; expect_rc 1 "A4 staged credential blocks after the working-tree copy was cleaned"

# Staged candidate = the objects the commit records: replacement objects cannot mask them.
new_case; r=$(new_repo a04-replace-blob)
write_file "$r" src/s.txt "$fx_key"; stage "$r" src/s.txt
real_blob=$(git -C "$r" rev-parse :src/s.txt)
decoy_blob=$(printf 'clean\n' | git -C "$r" hash-object -w --stdin)
git -C "$r" replace "$real_blob" "$decoy_blob"
run_guard "$r"; expect_rc 1 "A4 a replacement object (refs/replace) cannot mask the staged blob"
new_case; r=$(new_repo a04-replace-head)
write_file "$r" src/s.txt "$fx_key"; stage "$r" src/s.txt
fake_head=$(git -C "$r" commit-tree "$(git -C "$r" write-tree)" -p HEAD -m 'synthetic replacement head')
git -C "$r" replace HEAD "$fake_head"
run_guard "$r"; expect_rc 1 "A4 a replaced HEAD commit cannot hide staged content from the comparison"

# ── A5 · staged clean, prohibited content only in the working tree → allowed ─
new_case; r=$(new_repo a05-staged-clean-wt-dirty)
write_file "$r" src/settings.txt "$clean_md"; stage "$r" src/settings.txt
write_file "$r" src/settings.txt "$fx_cred"
run_guard "$r"; expect_rc 0 "A5 clean staged candidate passes although the working tree holds a credential"

# ── A6 · staged prohibited content, working-tree file removed → still blocked ─
new_case; r=$(new_repo a06-staged-dirty-wt-removed)
write_file "$r" src/removed.txt "$fx_key"; stage "$r" src/removed.txt
rm -f -- "$r/src/removed.txt"
run_guard "$r"; expect_rc 1 "A6 staged key blocks when the working-tree file no longer exists"

# ── A7 · difficult filenames do not bypass the policy ────────────────────────
for name in 'with space.txt' $'tab\there.txt' 'ë-ç-ü-unicode.txt' 'trailing. ' $'line1\nline2.txt' 'star*.txt' 'question?.txt'; do
  new_case; r=$(new_repo "a07-$case_no")
  write_file "$r" "src/$name" "$fx_cred"; stage "$r" "src/$name"
  run_guard "$r"; expect_rc 1 "A7 difficult filename is inspected: src/$(printf '%q' "$name")"
done
# Option-like names at the repository root (an unquoted/unterminated argument list would
# hand them to grep as options).
for name in '-rf' '--help' '-e' '--version' ':colon-start.txt'; do
  new_case; r=$(new_repo "a07-$case_no")
  write_file "$r" "$name" "$fx_cred"; stage "$r" "$name"
  run_guard "$r"; expect_rc 1 "A7 option-like root filename is inspected: $(printf '%q' "$name")"
done
# A glob-like name next to a clean decoy that the glob would match.
new_case; r=$(new_repo a07-brackets)
write_file "$r" 'src/brackets[1].txt' "$fx_cred"; stage "$r" 'src/brackets[1].txt'
write_file "$r" src/brackets1.txt "$clean_md"        # untracked decoy on disk
run_guard "$r"; expect_rc 1 "A7 glob-like filename is inspected itself, not a decoy it would expand to"

# ── A8 · root-local output and tmp directories ──────────────────────────────
new_case; r=$(new_repo a08-policy)
if git -C "$r" check-ignore -q output/artifact.txt && git -C "$r" check-ignore -q tmp/scratch.txt \
   && git -C "$r" check-ignore -q outputs/report.txt; then
  ok "A8 root-local output and tmp directories (and the existing outputs) are ignored by the repository .gitignore"
else
  bad "A8 .gitignore policy" "output/, tmp/ or outputs/ is not ignored"
fi
new_case
if git -C "$r" check-ignore -q nested/output/file.txt || git -C "$r" check-ignore -q src/tmp/file.txt; then
  bad "A8 root-local scope" "a nested output/ or tmp/ directory is ignored although only the root-local ones are in scope"
else
  ok "A8 only the root-local output and tmp directories are ignored (nested ones unaffected)"
fi
new_case; r=$(new_repo a08-forced-key)
write_file "$r" output/dump.txt "$fx_key"; stage_forced "$r" output/dump.txt
run_guard "$r"; expect_rc 1 "A8 force-staged key under output/ is blocked"
expect_output_has 'private key or token' "A8 force-staged key class is reported"
new_case; r=$(new_repo a08-forced-cred-removed)
write_file "$r" tmp/scratch.txt "$fx_cred"; stage_forced "$r" tmp/scratch.txt
rm -f -- "$r/tmp/scratch.txt"
run_guard "$r"; expect_rc 1 "A8 force-staged credential under tmp/ is blocked even after the local file is gone"
new_case; r=$(new_repo a08-forced-clean)
write_file "$r" tmp/notes.txt "$clean_md"; stage_forced "$r" tmp/notes.txt
run_guard "$r"; expect_rc 0 "A8 force-staged ignored artifact without violations is judged by the detectors like any path"

# ── A9 · deletion-only cleanup stays possible ────────────────────────────────
new_case; r=$(new_repo a09-cleanup-name)
write_file "$r" "legacy/$fx_owner_doc" "$fx_cred"; stage_forced "$r" "legacy/$fx_owner_doc"
git -C "$r" commit -q -m 'synthetic legacy material (no hook installed)'
git -C "$r" rm -q -- "legacy/$fx_owner_doc"
run_guard "$r"; expect_rc 0 "A9 deleting tracked owner-only material is allowed"
new_case; r=$(new_repo a09-cleanup-dir)
write_file "$r" "$fx_private_dir/old.md" "$fx_cred"; stage_forced "$r" "$fx_private_dir/old.md"
git -C "$r" commit -q -m 'synthetic legacy material (no hook installed)'
git -C "$r" rm -q -- "$fx_private_dir/old.md"
run_guard "$r"; expect_rc 0 "A9 deleting tracked material under a private directory is allowed"
new_case; r=$(new_repo a09-rename-out)
write_file "$r" "$fx_private_dir/moved.md" "$clean_md"; stage_forced "$r" "$fx_private_dir/moved.md"
git -C "$r" commit -q -m 'synthetic legacy material (no hook installed)'
mkdir -p -- "$r/docs"; git -C "$r" mv -- "$fx_private_dir/moved.md" docs/moved.md
run_guard "$r"; expect_rc 0 "A9 moving clean material out of a private directory is allowed (deletion + clean addition)"
new_case; r=$(new_repo a09-rename-in)
write_file "$r" docs/plain.md "$clean_md"; stage "$r" docs/plain.md
git -C "$r" commit -q -m 'synthetic plain doc'
git -C "$r" mv -- docs/plain.md "docs/$fx_owner_doc"
run_guard "$r"; expect_rc 1 "A9 renaming a file to an owner-only document name is blocked (the added path is inspected)"

# ── A10 · unsupported/opaque content → REVIEW_REQUIRED, never PASS ───────────
new_case; r=$(new_repo a10-binary)
# NUL bytes via \x escapes (runs like backslash-digit-digit-digit would trip the amount detector).
{ printf 'BIN\x00\x01\x02'; printf '%s' "$fx_key"; printf '\x00\xff'; } > "$r/blob.bin"
stage "$r" blob.bin
run_guard "$r"; expect_rc 2 "A10 binary blob with embedded key material is REVIEW_REQUIRED"
expect_output_has 'binary or opaque' "A10 binary class is reported"
expect_no_disclosure "$fx_key" "A10 binary fixture is not echoed"
new_case; r=$(new_repo a10-utf16)
printf 'p\x00a\x00s\x00s\x00w\x00o\x00r\x00d\x00 \x00=\x00 \x00x\x00\n\x00' > "$r/utf16.txt"; stage "$r" utf16.txt
run_guard "$r"; expect_rc 2 "A10 UTF-16 text (NUL bytes) is REVIEW_REQUIRED rather than scanned as clean"
new_case; r=$(new_repo a10-utf16-late)
{ head -c 9000 /dev/zero | tr '\000' 'a'; printf '\n'; printf 'p\x00a\x00s\x00s\x00w\x00o\x00r\x00d\x00 \x00=\x00 \x00x\x00\n\x00'; } > "$r/late.txt"
stage "$r" late.txt
run_guard "$r"; expect_rc 2 "A10 NUL bytes after a long clean text prefix still make the blob REVIEW_REQUIRED"
new_case; r=$(new_repo a10-symlink)
ln -s -- /nonexistent/target "$r/link"; stage "$r" link
run_guard "$r"; expect_rc 2 "A10 symlink entry is REVIEW_REQUIRED"
new_case; r=$(new_repo a10-typechange)
write_file "$r" src/t.txt "$clean_md"; stage "$r" src/t.txt; git -C "$r" commit -q -m 'synthetic file'
rm -f -- "$r/src/t.txt"; ln -s -- ../.gitignore "$r/src/t.txt"; stage "$r" src/t.txt
run_guard "$r"; expect_rc 2 "A10 file replaced by a symlink (typechange) is REVIEW_REQUIRED"
new_case; r=$(new_repo a10-gitlink)
git -C "$r" update-index --add --cacheinfo 160000,"$(git -C "$r" rev-parse HEAD)",sub
run_guard "$r"; expect_rc 2 "A10 submodule/gitlink entry is REVIEW_REQUIRED"
new_case
git -C "$r" config diff.ignoreSubmodules all
run_guard "$r"; expect_rc 2 "A10 gitlink stays REVIEW_REQUIRED under diff.ignoreSubmodules=all"

# ── A11 · enumeration/read/inspection failure never becomes PASS ─────────────
# Every case stages only clean content, so a guard that ignored the failure would PASS.
new_case; r=$(new_repo a11-missing-object)
write_file "$r" src/data.txt "$clean_md"; stage "$r" src/data.txt
sha=$(git -C "$r" rev-parse :src/data.txt)
rm -f -- "$r/.git/objects/${sha:0:2}/${sha:2}"
run_guard "$r"; expect_rc 3 "A11 unreadable staged blob is an inspection ERROR"
expect_output_has 'could not be read' "A11 read-failure class is reported"
new_case; d="$sandbox/a11-not-a-repo"; mkdir -p -- "$d"
run_guard "$d"; expect_rc 3 "A11 running outside a work tree is an ERROR"
new_case; git -c init.defaultBranch=main init -q --bare "$sandbox/a11-bare.git"
run_guard "$sandbox/a11-bare.git"; expect_rc 3 "A11 running inside a bare repository (no work tree, empty candidate) is an ERROR"
expect_output_has 'not inside a git work tree' "A11 the work-tree precondition is reported"
new_case; r=$(new_repo a11-missing-index)
write_file "$r" src/leak.txt "$fx_cred"; stage "$r" src/leak.txt
run_guard "$r" GIT_INDEX_FILE="$sandbox/does-not-exist/index"
expect_rc 3 "A11 a GIT_INDEX_FILE that does not exist is an ERROR, not an empty candidate"
new_case; r=$(new_repo a11-unmerged)
write_file "$r" src/m.txt "base line"; stage "$r" src/m.txt; git -C "$r" commit -q -m base
git -C "$r" checkout -q -b side
write_file "$r" src/m.txt "side line"; stage "$r" src/m.txt; git -C "$r" commit -q -m side
git -C "$r" checkout -q main
write_file "$r" src/m.txt "main line"; stage "$r" src/m.txt; git -C "$r" commit -q -m main
if git -C "$r" merge -q side > /dev/null 2>&1; then bad "A11 unmerged setup" "merge unexpectedly succeeded"; fi
run_guard "$r"; expect_rc 3 "A11 unmerged (conflicted) index entries are an inspection ERROR"
expect_output_has 'unmerged index entry' "A11 the unmerged entry is reported as such"
new_case; r=$(new_repo a11-toolchain)
write_file "$r" src/clean.txt "$clean_md"; stage "$r" src/clean.txt
shim=$(make_shim a11-shim-tr tr 'exit 1')
run_guard "$r" PATH="$shim"
expect_rc 3 "A11 a failing tool inside the content inspection is an ERROR with a diagnostic"
expect_output_has 'ERROR' "A11 toolchain failure is reported as ERROR"

# Inspection-command failures (git enumeration, grep, mktemp), each with clean content staged.
new_case; r=$(new_repo a11-enum)
write_file "$r" src/clean.txt "$clean_md"; stage "$r" src/clean.txt
shim=$(make_shim a11-shim-passthrough git "exec '$real_git' \"\$@\"")
run_guard "$r" PATH="$shim"; expect_rc 0 "A11 control: the tool shims themselves work (pass-through git, clean candidate passes)"
new_case
shim=$(make_shim a11-shim-diff-fails git "[ \"\$1\" = diff ] && exit 128
exec '$real_git' \"\$@\"")
run_guard "$r" PATH="$shim"; expect_rc 3 "A11 git diff --cached failing (no output) is an ERROR, not an empty candidate"
new_case
shim=$(make_shim a11-shim-diff-partial git "if [ \"\$1\" = diff ]; then '$real_git' \"\$@\"; exit 1; fi
exec '$real_git' \"\$@\"")
run_guard "$r" PATH="$shim"; expect_rc 3 "A11 git diff --cached failing after complete clean output is still an ERROR"
clean_sha=$(printf 'clean line\n' | git -C "$r" hash-object -w --stdin)
crafted_case() { # <file name> <needle> <case name>  — git diff --cached replaced by crafted raw output
  local shim_dir
  shim_dir=$(make_shim "a11-shim-$1" git "if [ \"\$1\" = diff ]; then cat '$sandbox/$1'; exit 0; fi
exec '$real_git' \"\$@\"")
  new_case
  run_guard "$r" PATH="$shim_dir"; expect_rc 3 "$3"
  expect_output_has "$2" "$3 — reported"
}
printf ':%s 100644 %s %s X\0src/x.txt\0' "$zero_mode" "$zero_sha" "$clean_sha" > "$sandbox/raw-status"
crafted_case raw-status 'unexpected index entry status' "A11 an index entry status the guard does not know is an ERROR"
printf ':%s 100664 %s %s A\0src/x.txt\0' "$zero_mode" "$zero_sha" "$clean_sha" > "$sandbox/raw-mode"
crafted_case raw-mode 'unsupported index entry mode' "A11 an index entry mode the guard does not support is an ERROR"
printf ':%s 100644 %s %s A\0src/x.txt\0' "$zero_mode" "$zero_sha" "$zero_sha" > "$sandbox/raw-zero"
crafted_case raw-zero 'no staged blob recorded' "A11 an added entry without a staged blob is an ERROR"
printf ':%s 100644 %s %s A\0src/ok.txt\0:%s 100644 %s %s A\0src/cut.txt' \
  "$zero_mode" "$zero_sha" "$clean_sha" "$zero_mode" "$zero_sha" "$clean_sha" > "$sandbox/raw-cut"
crafted_case raw-cut 'could not be parsed completely' "A11 a truncated entry list is an ERROR (the complete clean part does not PASS)"
printf ':%s 100644 %s %s A\0src/ok.txt\0:%s 100644 %s' "$zero_mode" "$zero_sha" "$clean_sha" "$zero_mode" "$zero_sha" > "$sandbox/raw-cut-meta"
crafted_case raw-cut-meta 'could not be parsed completely' "A11 a list cut inside an entry's fields is an ERROR (the complete clean part does not PASS)"
new_case
shim=$(make_shim a11-shim-grep2 grep 'exit 2')
run_guard "$r" PATH="$shim"; expect_rc 3 "A11 grep failing (exit 2) in every detector is an ERROR"
expect_output_has 'content inspection failed' "A11 the detector failure is reported"
new_case
shim=$(make_shim a11-shim-grep-locale grep "[ \"\${LC_ALL-}\" = C ] && exec '$real_grep' \"\$@\"
exit 2")
run_guard "$r" -u LC_ALL PATH="$shim"; expect_rc 3 "A11 grep failing only in the committer-locale pass is an ERROR"
expect_output_has 'committer-locale pass' "A11 the failing committer-locale pass is named"
new_case
shim=$(make_shim a11-shim-grep-bytes grep "[ \"\${LC_ALL-}\" = C ] && exit 2
exec '$real_grep' \"\$@\"")
run_guard "$r" -u LC_ALL PATH="$shim"; expect_rc 3 "A11 grep failing only in the byte-wise pass is an ERROR"
expect_output_has 'byte-wise pass' "A11 the failing byte-wise pass is named"
new_case
shim=$(make_shim a11-shim-mktemp mktemp 'exit 1')
run_guard "$r" PATH="$shim"; expect_rc 3 "A11 a failing mktemp is an ERROR"

# ── A12 · legitimate technical content stays allowed ─────────────────────────
new_case; r=$(new_repo a12-self)
mkdir -p -- "$r/scripts"
cp -- "$GUARD" "$r/scripts/precommit_confidential_guard.sh"
cp -- "$SUITE" "$r/scripts/test_precommit_confidential_guard.sh"
stage "$r" scripts/precommit_confidential_guard.sh; stage "$r" scripts/test_precommit_confidential_guard.sh
run_guard "$r"; expect_rc 0 "A12 the guard and its regression suite can themselves be committed"
new_case; r=$(new_repo a12-near-miss)
write_file "$r" docs/near.md 'token count: 42; password policy is documented elsewhere; secret-free; db-url rotated (no value here); 20250101 and 2025.01.01 are dates.'
stage "$r" docs/near.md
run_guard "$r"; expect_rc 0 "A12 near-miss prose without assignments or 12-digit runs stays allowed"

# ── A13 · repository / worktree / hook contexts ──────────────────────────────
new_case; r=$(new_repo a13-subdir)
write_file "$r" src/sub/leak.txt "$fx_cred"; stage "$r" src/sub/leak.txt
run_guard "$r/src/sub"; expect_rc 1 "A13 invocation from a subdirectory evaluates the whole staged candidate"
# git resolves a relative GIT_INDEX_FILE against the work-tree top (as in a hook).
new_case
run_guard "$r/src/sub" GIT_INDEX_FILE=.git/index
expect_rc 1 "A13 a top-relative GIT_INDEX_FILE is honoured when invoked from a subdirectory"
new_case
run_guard "$r/src/sub" GIT_INDEX_FILE=../../.git/index
expect_rc 3 "A13 a relative GIT_INDEX_FILE that git cannot resolve is an ERROR, not an empty candidate"
new_case; r=$(new_repo a13-diff-relative)
git -C "$r" config diff.relative true
write_file "$r" src/leak.txt "$fx_cred"; stage "$r" src/leak.txt
mkdir -p -- "$r/docs/sub"
run_guard "$r/docs/sub"; expect_rc 1 "A13 diff.relative=true cannot hide staged content outside the current directory"
new_case; r=$(new_repo a13-worktree)
git -C "$r" worktree add -q -b synthetic-branch "$sandbox/a13-linked" > /dev/null 2>&1
write_file "$sandbox/a13-linked" src/leak.txt "$fx_key"; stage "$sandbox/a13-linked" src/leak.txt
run_guard "$sandbox/a13-linked"; expect_rc 1 "A13 linked worktree: staged key is blocked"
new_case
install_hook "$r"
if commit_with_hook "$sandbox/a13-linked" 'should be blocked'; then
  bad "A13 shared hook in linked worktree" "commit with a staged key succeeded"
else
  ok "A13 the shared hook blocks the commit in a linked worktree"
fi

new_case; r=$(new_repo a13-grep-options)
write_file "$r" src/leak.txt "$fx_cred"; stage "$r" src/leak.txt
run_guard "$r" 'GREP_OPTIONS=--exclude=*'
expect_rc 1 "A13 GREP_OPTIONS in the committer environment cannot switch the detectors off"

# Installed hook: blocks a dirty candidate and lets a clean one through.
new_case; r=$(new_repo a13-hook); install_hook "$r"
write_file "$r" src/leak.txt "$fx_cred"; stage "$r" src/leak.txt
if commit_with_hook "$r" 'should be blocked'; then bad "A13 installed hook" "commit with a staged credential succeeded"; else ok "A13 installed hook blocks the commit"; fi
new_case
write_file "$r" src/leak.txt "$clean_md"; stage "$r" src/leak.txt
if commit_with_hook "$r" 'clean'; then ok "A13 installed hook lets a clean commit through"; else bad "A13 installed hook" "clean commit was blocked"; fi

# The headline defect end to end through git's hook invocation (A4/A5/A6 via the hook).
# One fresh repository per case, so that no case depends on the outcome of another.
new_case; r=$(new_repo a13-hook-wt-clean); install_hook "$r"
write_file "$r" src/wt.txt "$fx_cred"; stage "$r" src/wt.txt
write_file "$r" src/wt.txt "$clean_md"                       # working tree cleaned, index still dirty
if commit_with_hook "$r" 'staged dirty, working tree clean'; then
  bad "A13 hook: staged authority (working tree cleaned)" "commit succeeded although the staged blob holds a credential"
else
  ok "A13 hook: staged credential blocks the commit after the working-tree copy was cleaned"
fi
new_case; r=$(new_repo a13-hook-wt-removed); install_hook "$r"
write_file "$r" src/wt.txt "$fx_cred"; stage "$r" src/wt.txt
rm -f -- "$r/src/wt.txt"                                     # working tree removed, index still dirty
if commit_with_hook "$r" 'staged dirty, working tree removed'; then
  bad "A13 hook: staged authority (working-tree file removed)" "commit succeeded although the staged blob holds a credential"
else
  ok "A13 hook: staged credential blocks the commit after the working-tree file was removed"
fi
new_case; r=$(new_repo a13-hook-wt-dirty); install_hook "$r"
write_file "$r" src/wt.txt "$clean_md"; stage "$r" src/wt.txt
write_file "$r" src/wt.txt "$fx_cred"                        # dirty only in the working tree
if commit_with_hook "$r" 'staged clean, working tree dirty'; then
  ok "A13 hook: clean staged candidate commits although the working tree holds a credential"
else
  bad "A13 hook: staged authority (working tree dirty)" "clean staged candidate was blocked by working-tree content"
fi

# Temporary indexes built by git for partial commits and commit -a.
new_case; r=$(new_repo a13-hook-partial); install_hook "$r"
write_file "$r" src/other.txt "$fx_cred"; stage "$r" src/other.txt
write_file "$r" src/clean.txt "$clean_cs"; stage "$r" src/clean.txt
if commit_with_hook "$r" 'partial' -- src/clean.txt; then
  if git -C "$r" diff --cached --name-only | grep -q -F -e 'src/other.txt'; then
    ok "A13 hook: partial commit of a clean path succeeds while the dirty path stays staged"
  else
    bad "A13 partial commit" "dirty path vanished from the index"
  fi
else
  bad "A13 partial commit" "clean-path commit was blocked although the candidate excluded the dirty path"
fi
new_case
if commit_with_hook "$r" 'should be blocked'; then bad "A13 follow-up commit" "dirty staged path was committed"; else ok "A13 hook: follow-up commit of the remaining dirty path is blocked"; fi
new_case; r=$(new_repo a13-hook-commit-a); install_hook "$r"
write_file "$r" src/leak.txt "$clean_md"; stage "$r" src/leak.txt
if ! commit_with_hook "$r" 'clean baseline'; then bad "A13 commit -a setup" "clean baseline commit was blocked"; fi
write_file "$r" src/leak.txt "$fx_cred"                              # tracked path, modified, NOT staged
if commit_with_hook "$r" 'commit -a must see the temporary index' -a; then
  bad "A13 commit -a" "working-tree credential committed via the temporary index"
else
  ok "A13 hook: commit -a — the candidate git builds (GIT_INDEX_FILE) is what the hook inspects"
fi
new_case; r=$(new_repo a13-hook-commit-a-clean); install_hook "$r"
write_file "$r" src/notes.txt "$clean_md"; stage "$r" src/notes.txt
if ! commit_with_hook "$r" 'clean baseline'; then bad "A13 clean commit -a setup" "clean baseline commit was blocked"; fi
write_file "$r" src/notes.txt "$clean_cs"                            # tracked path, clean modification, NOT staged
if commit_with_hook "$r" 'clean commit -a' -a && [ "$(git -C "$r" show HEAD:src/notes.txt)" = "$(printf '%s' "$clean_cs")" ]; then
  ok "A13 hook: commit -a with a clean modification succeeds (the temporary index is inspected, not refused)"
else
  bad "A13 clean commit -a" "a clean commit -a was blocked or did not record the modification"
fi
# Intent-to-add entries (git add -N): no staged content until a commit stages it.
new_case; r=$(new_repo a13-hook-ita); install_hook "$r"
write_file "$r" src/ita.txt "$fx_cred"; git -C "$r" add -N -- src/ita.txt
if commit_with_hook "$r" 'commit -a stages the intent-to-add entry' -a; then
  bad "A13 intent-to-add + commit -a" "commit -a recorded an intent-to-add entry holding a credential"
else
  ok "A13 hook: commit -a stages an intent-to-add entry first, and its content is blocked"
fi
new_case
write_file "$r" src/plain.txt "$clean_md"; stage "$r" src/plain.txt
if commit_with_hook "$r" 'plain commit' && ! git -C "$r" cat-file -e HEAD:src/ita.txt 2> /dev/null; then
  ok "A13 hook: a plain commit does not record an intent-to-add entry (nothing to inspect)"
else
  bad "A13 intent-to-add + plain commit" "the plain commit was blocked or recorded the intent-to-add entry"
fi

# ── A15 · every existing detector family, with near misses that stay allowed ──
fam_repo=$(new_repo a15-families)
# Path rules (clean content). Private directories: (^|/)<segment>(/|$), case-insensitive.
fam 1 path_private "$d_raw/a.md" "$clean_md" - "A15 private directory (raw intake) is blocked"
fam 1 path_private "$d_contracts/a.md" "$clean_md" - "A15 private directory (contracts) is blocked"
fam 1 path_private "$d_docs_private/a.md" "$clean_md" - "A15 private directory (docs-private) is blocked"
fam 1 path_private "$d_sensitive/a.md" "$clean_md" - "A15 private directory (private-sensitive) is blocked"
fam 1 path_private "$d_epl/a.md" "$clean_md" - "A15 private directory (evidence-private-local) is blocked"
fam 1 path_private "src/nested/$d_raw/a.md" "$clean_md" - "A15 a nested private directory is blocked"
fam 1 path_private "$d_sensitive" "$clean_md" - "A15 a file named exactly like a private segment is blocked"
fam 1 path_private "Docs-Private/a.md" "$clean_md" - "A15 a private directory in another case is blocked"
fam 0 - "my$d_raw/a.md" "$clean_md" - "A15 near miss: a private segment glued to a prefix stays allowed"
fam 0 - "$d_contracts.md" "$clean_md" - "A15 near miss: a file merely starting with a private segment stays allowed"
fam 0 - "$d_docs_private-notes/a.md" "$clean_md" - "A15 near miss: a private segment with a suffix stays allowed"
# Evidence directories: (^|/)(docs/evidence|docs/archive/prodata)(/|$).
fam 1 path_private "$d_evidence/a.md" "$clean_md" - "A15 evidence directory is blocked"
fam 1 path_private "$d_prodata/a.md" "$clean_md" - "A15 PRO DATA archive directory is blocked"
fam 1 path_private "pkg/$d_evidence/a.md" "$clean_md" - "A15 a nested evidence directory is blocked"
fam 1 path_private "$d_evidence" "$clean_md" - "A15 a file named exactly like the evidence directory is blocked"
fam 0 - "$d_evidence-old/a.md" "$clean_md" - "A15 near miss: an evidence segment with a suffix stays allowed"
fam 0 - "$d_prodata-notes.md" "$clean_md" - "A15 near miss: a file next to the PRO DATA archive stays allowed"
fam 0 - "my$d_evidence/a.md" "$clean_md" - "A15 near miss: an evidence segment glued to a prefix stays allowed"
# Owner-only document names: unanchored tokens, case-insensitive.
fam 1 path_owner "docs/${t_oferta}_x.md" "$clean_md" - "A15 owner-document token (offer, full) is blocked"
fam 1 path_owner "docs/${t_ofert}_x.md" "$clean_md" - "A15 owner-document token (offer, stem) is blocked"
fam 1 path_owner "docs/${t_kontrat}a_x.md" "$clean_md" - "A15 owner-document token (contract) is blocked"
fam 1 path_owner "docs/${t_shtojc}a_x.md" "$clean_md" - "A15 owner-document token (annex) is blocked"
fam 1 path_owner "docs/${t_dokumenti}_x.md" "$clean_md" - "A15 owner-document token (master document) is blocked"
fam 1 path_owner "docs/x_${t_cert_sef}.md" "$clean_md" - "A15 owner-document token (SEF certification) is blocked"
fam 1 path_owner "docs/${t_dosja}_x.md" "$clean_md" - "A15 owner-document token (certification dossier) is blocked"
fam 1 path_owner "docs/${t_manifest}_x.csv" "$clean_md" - "A15 owner-document token (split-copy manifest) is blocked"
fam 1 path_owner "docs/ofe""rta_lower.md" "$clean_md" - "A15 an owner-document token in lower case is blocked"
fam 0 - "docs/Ofer_x.md" "$clean_md" - "A15 near miss: a shortened offer token stays allowed"
fam 0 - "docs/Kon""tra_x.md" "$clean_md" - "A15 near miss: a shortened contract token stays allowed"
fam 0 - "docs/Certifikimit.md" "$clean_md" - "A15 near miss: the certification word without _SEF stays allowed"
# Keys and tokens.
fam 1 key src/case.txt "-----BEGIN ""KEY-----"$'\n'"$v_body2" "$v_body2" "A15 key header without a type is blocked"
fam 1 key src/case.txt "-----BEGIN ""RSA ""KEY-----"$'\n'"$v_body2" "$v_body2" "A15 RSA key header is blocked"
fam 1 key src/case.txt "-----BEGIN ""EC ""KEY-----"$'\n'"$v_body2" "$v_body2" "A15 EC key header is blocked"
fam 1 key src/case.txt "-----BEGIN ""OPENSSH ""KEY-----"$'\n'"$v_body2" "$v_body2" "A15 OpenSSH key header is blocked"
fam 1 key src/case.txt "$fx_key" "$v_kbody" "A15 PKCS#8 private key header is blocked"
fam 1 key src/case.txt "ref $t_gho$v_gho" "$v_gho" "A15 GitHub OAuth token shape is blocked"
fam 1 key src/case.txt "ref $t_ghp$v_ghp" "$v_ghp" "A15 GitHub fine-grained token shape is blocked"
fam 1 key src/case.txt "ref $t_sk$v_sk" "$v_sk" "A15 sk- secret key shape (20 characters) is blocked"
fam 1 key src/case.txt "ref $t_sk$v_sk2" "$v_sk2" "A15 sk- shape with '-' and '_' in its body is blocked"
fam 1 key src/case.txt "id $fx_akid" "$v_akid" "A15 access-key-id shape is blocked"
fam 0 - src/case.txt "-----BEGIN ""PUBLIC ""KEY-----"$'\n'"$v_body2" - "A15 near miss: a public key header stays allowed"
fam 0 - src/case.txt "-----BEGIN ""CERTIFICATE-----"$'\n'"$v_body2" - "A15 near miss: a certificate header stays allowed"
fam 0 - src/case.txt "ref $t_sk${v_sk:0:19}" - "A15 near miss: an sk- shape with 19 characters stays allowed"
fam 0 - src/case.txt "id AK""IA${v_akid:0:15} end" - "A15 near miss: an access-key-id shape one character short stays allowed"
# Assigned credentials: every keyword variant, both separators, case-insensitive, boundaries.
fam 1 cred src/case.txt "api""key=$v_cv" "$v_cv" "A15 credential variant (apikey, equals sign) is blocked"
fam 1 cred src/case.txt "api_""key = $v_cv" "$v_cv" "A15 credential variant (api_key, spaced equals sign) is blocked"
fam 1 cred src/case.txt "api-""key: $v_cv" "$v_cv" "A15 credential variant (api-key, colon) is blocked"
fam 1 cred src/case.txt "pass""word: $v_cv" "$v_cv" "A15 credential variant (password, colon) is blocked"
fam 1 cred src/case.txt "tok""en=$v_cv" "$v_cv" "A15 credential variant (token, equals sign) is blocked"
fam 1 cred src/case.txt "sec""ret: $v_cv" "$v_cv" "A15 credential variant (secret, colon) is blocked"
fam 1 cred src/case.txt "db""url=$v_cv" "$v_cv" "A15 credential variant (dburl, equals sign) is blocked"
fam 1 cred src/case.txt "db_""url = $v_cv" "$v_cv" "A15 credential variant (db_url, spaced equals sign) is blocked"
fam 1 cred src/case.txt "db-""url: $v_cv" "$v_cv" "A15 credential variant (db-url, colon) is blocked"
fam 1 cred src/case.txt "data""base_url=$v_cv" "$v_cv" "A15 credential variant (database_url, equals sign) is blocked"
fam 1 cred src/case.txt "PASS""WORD=$v_cv" "$v_cv" "A15 credential variant in upper case (PASSWORD, equals sign) is blocked"
fam 1 cred src/case.txt "API_""KEY: $v_cv" "$v_cv" "A15 credential variant in upper case (API_KEY, colon) is blocked"
fam 1 cred src/case.txt "DATA""BASE_URL=$v_cv" "$v_cv" "A15 credential variant in upper case (DATABASE_URL, equals sign) is blocked"
fam 1 cred src/case.txt "my_""tok""en = $v_cv" "$v_cv" "A15 a keyword after a non-letter is blocked"
fam 1 cred src/case.txt "  tok""en  :$v_cv" "$v_cv" "A15 spaces around the separator are blocked"
fam 0 - src/case.txt "my""tok""en = $v_cv" - "A15 near miss: a keyword glued to letters stays allowed"
fam 0 - src/case.txt "pass""word:" - "A15 near miss: a keyword without a value stays allowed"
fam 0 - src/case.txt "tok""ens = 5" - "A15 near miss: a plural keyword stays allowed"
fam 0 - src/case.txt "the sec""ret is safe" - "A15 near miss: a keyword in prose stays allowed"
fam 0 - src/case.txt "pass""word_hint = $v_cv" - "A15 near miss: a keyword with a suffix stays allowed"
# Amounts and regulated identifiers.
fam 1 ident src/case.txt "Totali $v_amount1 EUR" "$v_amount1" "A15 amount with dot thousands and comma decimals is blocked"
fam 1 ident src/case.txt "Total $v_amount2" "$v_amount2" "A15 amount with comma thousands and dot decimals is blocked"
fam 1 ident src/case.txt "ref $fx_digits" "$fx_digits" "A15 12-digit run is blocked"
fam 1 ident src/case.txt "NU""I $v_nui" "$v_nui" "A15 NUI followed by 8 digits is blocked"
fam 1 ident src/case.txt "TV""SH $v_tvsh" "$v_tvsh" "A15 TVSH followed by 8 digits is blocked"
fam 1 ident src/case.txt "nu""i $v_nui" "$v_nui" "A15 lower-case nui is blocked"
fam 1 ident src/case.txt "NU""I"$'\t'"$v_nui" "$v_nui" "A15 NUI separated by a tab is blocked"
fam 0 - src/case.txt "ref 1234""5678""901" - "A15 near miss: an 11-digit run stays allowed"
fam 0 - src/case.txt "NU""I 1234""567" - "A15 near miss: NUI with 7 digits stays allowed"
fam 0 - src/case.txt "Shuma 1.""234,56" - "A15 near miss: a small amount stays allowed"
fam 0 - src/case.txt "Data 2025-10-03, versioni 1.22.333" - "A15 near miss: dates and version numbers stay allowed"

# ── A16 · boundaries of the existing content exemption ───────────────────────
fam 0 - scripts/precommit_confidential_guard.sh "$fx_cred" - "A16 exact guard path: the content exemption applies (unchanged)"
fam 1 cred lib/precommit_confidential_guard.sh "$fx_cred" "$v_cred" "A16 the guard's file name in another directory is inspected"
fam 1 cred scripts/other.sh "$fx_cred" "$v_cred" "A16 other files under scripts/ are inspected"
fam 1 cred scripts/precommit_confidential_guard.sh.bak "$fx_cred" "$v_cred" "A16 a suffix after the guard path is inspected"
fam 1 cred tools-provenance-extra/x.txt "$fx_cred" "$v_cred" "A16 a sibling of tools-provenance/ (no slash) is inspected"
fam 1 cred sub/tools-provenance/x.txt "$fx_cred" "$v_cred" "A16 a nested tools-provenance/ is inspected"
fam 1 cred ' tools-provenance/x.txt' "$fx_cred" "$v_cred" "A16 a leading space before tools-provenance/ is kept (inspected)"
fam 1 cred 'scripts/precommit_confidential_guard.sh ' "$fx_cred" "$v_cred" "A16 a trailing space after the guard path is kept (inspected)"
fam 1 path_private "tools-provenance/$d_contracts/x.md" "$clean_md" - "A16 path rules still apply under the exempt directory"

# ── A17 · committer locale (as the original guard) and byte-wise coverage ────
utf8_cfgs=()
# Each environment sets LC_ALL, LC_CTYPE and LANG explicitly, so only the variable under test
# can give the committer pass a UTF-8 character type (an inherited LANG cannot mask a defect).
for cfg in 'LC_ALL=C.UTF-8 LC_CTYPE=C LANG=C' 'LC_ALL=en_US.UTF-8 LC_CTYPE=C LANG=C' \
           '-u LC_ALL LC_CTYPE=UTF-8 LANG=C' '-u LC_ALL LC_CTYPE=C.UTF-8 LANG=C' \
           '-u LC_ALL -u LC_CTYPE LANG=C.UTF-8' '-u LC_ALL -u LC_CTYPE LANG=en_US.UTF-8'; do
  read -r -a cfg_args <<< "$cfg"
  if [ "$(printf '\xc3\xa9\n' | env "${cfg_args[@]}" grep -c -E '^.$' 2> /dev/null || true)" = 1 ]; then
    utf8_cfgs+=("$cfg")
  fi
done
if [ "${#utf8_cfgs[@]}" -gt 0 ]; then
  printf 'INFO: UTF-8 committer environments available here: %s\n' "$(printf '[%s] ' "${utf8_cfgs[@]}")"
else
  new_case; bad "A17 UTF-8 committer environment" "no UTF-8 locale is installed, so the committer-locale pass cannot be exercised"
fi
loc_applicable=0
loc_hook_cfg=""
loc_hook_content=""
for cfg in ${utf8_cfgs[@]+"${utf8_cfgs[@]}"}; do
  # The reported Unicode-whitespace cases are blocked wherever the committer's locale treats
  # the character as [[:space:]] — exactly where the original guard blocked them.
  for spec in nui:nbsp tvsh:nbsp tvsh:nnbsp db:nbsp tok:emsp; do
    kind=${spec%%:*}
    wsname=${spec#*:}
    case $wsname in nbsp) ws=$ws_nbsp ;; nnbsp) ws=$ws_nnbsp ;; *) ws=$ws_emsp ;; esac
    case $kind in
      nui)  cls=ident; pval=$v_loc_nui;  content="Numri NU""I${ws}$v_loc_nui" ;;
      tvsh) cls=ident; pval=$v_loc_tvsh; content="Numri TV""SH${ws}$v_loc_tvsh" ;;
      db)   cls=cred;  pval=$v_loc_db;   content="db_""url${ws}= $v_loc_db" ;;
      *)    cls=cred;  pval=$v_loc_tok;  content="tok""en${ws}: $v_loc_tok" ;;
    esac
    if ws_is_space "$cfg" "$ws"; then
      loc_applicable=$((loc_applicable + 1))
      entry_case "$cfg" 1 "$cls" src/case.txt "$content" "$pval" "A17 [$cfg] $kind separated by $wsname is blocked"
      if [ -z "$loc_hook_cfg" ]; then loc_hook_cfg=$cfg; loc_hook_content=$content; fi
    else
      new_case
      skip "A17 [$cfg] $kind separated by $wsname" "$wsname is not [[:space:]] in this locale, so the original guard does not match it either"
    fi
  done
  entry_case "$cfg" 1 ident src/case.txt "Numri NU""I $v_loc_space" "$v_loc_space" "A17 [$cfg] ASCII-space control is blocked"
  entry_case "$cfg" 1 cred src/case.txt "pass""word=$v_cp1252" "$v_cp1252" "A17 [$cfg] a value in a legacy single-byte encoding is blocked (byte-wise pass)"
  entry_case "$cfg" 1 cred src/case.txt "caf"$'\xe9'" pass""word = $v_loc_lat" "$v_loc_lat" "A17 [$cfg] a credential on a line that is not valid UTF-8 is blocked (byte-wise pass)"
  entry_case "$cfg" 0 - docs/nbsp.md "$clean_nbsp" - "A17 [$cfg] clean text with a no-break space stays allowed"
  new_case; r=$(new_repo "a17-docs-$case_no")
  mkdir -p -- "$r/docs" "$r/scripts"
  cp -- "$README" "$r/docs/README-copy.md"; stage "$r" docs/README-copy.md
  cp -- "$PROJECT_RULES" "$r/docs/PROJECT_RULES-copy.md"; stage "$r" docs/PROJECT_RULES-copy.md
  cp -- "$GUARD" "$r/scripts/precommit_confidential_guard.sh"; stage "$r" scripts/precommit_confidential_guard.sh
  cp -- "$SUITE" "$r/scripts/test_precommit_confidential_guard.sh"; stage "$r" scripts/test_precommit_confidential_guard.sh
  read -r -a cfg_args <<< "$cfg"
  run_guard "$r" "${cfg_args[@]}"; expect_rc 0 "A17 [$cfg] README, PROJECT_RULES, the guard and this suite stay allowed"
done
new_case
if [ "$loc_applicable" -gt 0 ]; then
  ok "A17 the committer-locale pass was exercised ($loc_applicable Unicode-whitespace cases applicable here)"
elif [ "${#utf8_cfgs[@]}" -gt 0 ]; then
  bad "A17 committer-locale coverage" "no reported Unicode whitespace is [[:space:]] under any available UTF-8 locale"
else
  skip "A17 committer-locale coverage" "no UTF-8 locale available (reported above as a failure)"
fi
if [ -n "$loc_hook_cfg" ]; then
  read -r -a cfg_args <<< "$loc_hook_cfg"
  new_case; r=$(new_repo a17-hook); install_hook "$r"
  write_file "$r" src/case.txt "$loc_hook_content"; stage "$r" src/case.txt
  if commit_with_hook_env "$r" 'unicode whitespace' "${cfg_args[@]}"; then
    bad "A17 hook [$loc_hook_cfg]" "commit created although the committer's locale matches the detector"
  else
    ok "A17 hook [$loc_hook_cfg]: the commit is blocked in the committer's environment"
  fi
  new_case
  write_file "$r" src/case.txt "$clean_nbsp"; stage "$r" src/case.txt
  if commit_with_hook_env "$r" 'clean text with a no-break space' "${cfg_args[@]}"; then
    ok "A17 hook [$loc_hook_cfg]: clean text with a no-break space commits"
  else
    bad "A17 hook [$loc_hook_cfg]" "a clean commit was blocked in the committer's environment"
  fi
fi

# ── A18 · initial commits (no HEAD yet) ──────────────────────────────────────
new_case; r=$(new_unborn_repo a18-unborn-clean)
write_file "$r" src/first.txt "$clean_md"; stage "$r" src/first.txt
run_guard "$r"; expect_rc 0 "A18 unborn HEAD: a clean first-commit candidate passes"
new_case; r=$(new_unborn_repo a18-unborn-dirty)
write_file "$r" src/first.txt "$fx_cred"; stage "$r" src/first.txt
run_guard "$r"; expect_rc 1 "A18 unborn HEAD: a credential in the first commit is blocked"
new_case; r=$(new_unborn_repo a18-unborn-path)
write_file "$r" "$d_docs_private/first.md" "$clean_md"; stage_forced "$r" "$d_docs_private/first.md"
run_guard "$r"; expect_rc 1 "A18 unborn HEAD: a protected path in the first commit is blocked"
new_case; r=$(new_unborn_repo a18-unborn-hook); install_hook "$r"
write_file "$r" src/first.txt "$fx_cred"; stage "$r" src/first.txt
if commit_with_hook "$r" 'dirty root commit'; then bad "A18 hook: root commit" "a dirty root commit was created"; else ok "A18 hook: a dirty root commit is blocked"; fi
new_case
write_file "$r" src/first.txt "$clean_md"; stage "$r" src/first.txt
if commit_with_hook "$r" 'clean root commit' && git -C "$r" rev-parse -q --verify HEAD > /dev/null; then
  ok "A18 hook: a clean root commit is created"
else
  bad "A18 hook: root commit" "a clean root commit was blocked"
fi
new_case; r=$(new_repo a18-orphan)
git -C "$r" checkout -q --orphan synthetic-orphan
write_file "$r" src/orphan.txt "$fx_cred"; stage "$r" src/orphan.txt
run_guard "$r"; expect_rc 1 "A18 orphan branch (unborn HEAD, inherited index): a credential is blocked"

# ── A19 · difficult names under the path rules (clean content) ───────────────
fam 1 path_private "$d_contracts/tab"$'\t'"here.md" "$clean_md" - "A19 private directory: a tab in the name does not escape the rule"
fam 1 path_private "$d_docs_private/line1"$'\n'"line2.md" "$clean_md" - "A19 private directory: a newline in the name does not escape the rule"
fam 1 path_private "$d_evidence/quote\"d.md" "$clean_md" - "A19 evidence directory: a double quote in the name does not escape the rule"
fam 1 path_private "$d_sensitive/back\\slash.md" "$clean_md" - "A19 private directory: a backslash in the name does not escape the rule"
fam 1 path_private "$d_raw/ë-unicode.md" "$clean_md" - "A19 private directory: a non-ASCII name does not escape the rule"
fam 1 path_private "$d_prodata/-dash.md" "$clean_md" - "A19 PRO DATA archive: an option-like name does not escape the rule"
fam 1 path_private "$d_epl/with space.md" "$clean_md" - "A19 private directory: a space in the name does not escape the rule"
fam 1 path_owner "docs/${t_kontrat}a e re.md" "$clean_md" - "A19 owner-document name with spaces is blocked"
fam 1 path_owner "docs/${t_kontrat}ë.md" "$clean_md" - "A19 owner-document name with a non-ASCII letter is blocked"
fam 1 path_owner "docs/tab"$'\t'"${t_oferta}.md" "$clean_md" - "A19 owner-document name after a tab is blocked"
# Clean content under the same kinds of names stays allowed (no over-blocking of unusual names).
for name in 'with space.txt' $'tab\there.txt' 'ë-ç-ü-unicode.txt' 'trailing. ' $'line1\nline2.txt' 'star*.txt' \
            'question?.txt' 'brackets[1].txt' 'quote"d.txt' 'back\slash.txt'; do
  fam 0 - "src/$name" "$clean_md" - "A19 clean content under a difficult name stays allowed: src/$(printf '%q' "$name")"
done
for name in '-rf' '--help' ':colon-start.txt'; do
  fam 0 - "$name" "$clean_md" - "A19 clean content under an option-like root name stays allowed: $(printf '%q' "$name")"
done

# ── A20 · protected names are withheld; other paths stay visible ─────────────
withheld_case() { # <repo> <case name>  — exit 1, label withheld, no fragment of the protected name
  if [ "$rc" -eq 1 ] && grep -q -F -e 'name withheld' -- "$out" && ! has_fragment "$v_client" "$out"; then
    ok "$2 (exit $rc)"
  else
    bad "$2" "exit $rc; the protected name was printed or not withheld"
  fi
}
new_case; r=$(new_repo a20-owner-name)
write_file "$r" "docs/${t_kontrat}a_$v_client.md" "$clean_md"; stage_forced "$r" "docs/${t_kontrat}a_$v_client.md"
run_guard "$r"; withheld_case "$r" "A20 an owner-document name is withheld from the diagnostics"
new_case; r=$(new_repo a20-private-dir)
write_file "$r" "$d_contracts/$v_client/agreement.md" "$clean_md"; stage_forced "$r" "$d_contracts/$v_client/agreement.md"
run_guard "$r"; withheld_case "$r" "A20 a name under a private directory is withheld"
new_case; r=$(new_repo a20-evidence-dir)
write_file "$r" "$d_evidence/$v_client.md" "$clean_md"; stage_forced "$r" "$d_evidence/$v_client.md"
run_guard "$r"; withheld_case "$r" "A20 a name under an evidence directory is withheld"
new_case; r=$(new_repo a20-private-dirty)
write_file "$r" "$d_raw/$v_client.txt" "$fx_cred"; stage_forced "$r" "$d_raw/$v_client.txt"
run_guard "$r"
if [ "$rc" -eq 1 ] && [ "$(grep -c -F -e 'name withheld' -- "$out")" -eq 2 ] && grep -q -F -e 'assigned credentials' -- "$out" \
   && ! has_fragment "$v_client" "$out" && ! has_fragment "$v_cred" "$out"; then
  ok "A20 the path and the content violation of a protected entry both withhold its name (exit 1)"
else
  bad "A20 protected entry with dirty content" "exit $rc; a diagnostic named the entry or the content class is missing"
fi
new_case; r=$(new_repo a20-private-binary)
mkdir -p -- "$r/$d_docs_private"; printf 'BIN\x00\x01payload\n' > "$r/$d_docs_private/$v_client.bin"
stage_forced "$r" "$d_docs_private/$v_client.bin"
run_guard "$r"
if [ "$rc" -eq 1 ] && grep -q -F -e 'REVIEW_REQUIRED binary or opaque content (NUL bytes) is not inspected automatically: staged entry' -- "$out" \
   && ! has_fragment "$v_client" "$out"; then
  ok "A20 a REVIEW_REQUIRED diagnostic about a protected entry withholds its name too (exit 1)"
else
  bad "A20 protected binary entry" "exit $rc; the REVIEW_REQUIRED line named the entry or is missing"
fi
new_case; r=$(new_repo a20-visible)
write_file "$r" src/visible-name.txt "$fx_cred"; stage "$r" src/visible-name.txt
run_guard "$r"; expect_rc 1 "A20 a credential under an ordinary path is blocked"
expect_output_has 'src/visible-name.txt' "A20 an ordinary path is named, so it can be unstaged"
new_case; r=$(new_repo a20-ordinal)
write_file "$r" a/clean.txt "$clean_md"; stage "$r" a/clean.txt
write_file "$r" "b/${t_kontrat}a_$v_client.md" "$clean_md"; stage_forced "$r" "b/${t_kontrat}a_$v_client.md"
write_file "$r" c/clean.txt "$clean_md"; stage "$r" c/clean.txt
run_guard "$r"
second=$(git -C "$r" diff --cached --no-renames --no-relative --ignore-submodules=none --name-only | sed -n 2p)
if [ "$rc" -eq 1 ] && grep -q -F -e 'staged entry 2 ' -- "$out" && [ "$second" = "b/${t_kontrat}a_$v_client.md" ]; then
  ok "A20 the withheld entry number is its position in the documented staged listing"
else
  bad "A20 withheld entry number" "exit $rc; the reported number does not locate the entry"
fi
new_case; r=$(new_repo a20-ordinal-deletion)
write_file "$r" a/old.txt "$clean_md"; stage "$r" a/old.txt; git -C "$r" commit -q -m 'synthetic old file'
git -C "$r" rm -q -- a/old.txt
write_file "$r" "b/${t_kontrat}a_$v_client.md" "$clean_md"; stage_forced "$r" "b/${t_kontrat}a_$v_client.md"
run_guard "$r"
second=$(git -C "$r" diff --cached --no-renames --no-relative --ignore-submodules=none --name-only | sed -n 2p)
if [ "$rc" -eq 1 ] && grep -q -F -e 'staged entry 2 ' -- "$out" && [ "$second" = "b/${t_kontrat}a_$v_client.md" ]; then
  ok "A20 deletions count in the entry number, exactly as in the documented listing"
else
  bad "A20 entry number after a deletion" "exit $rc; the reported number does not locate the entry"
fi
new_case; r=$(new_repo a20-missing-object)
write_file "$r" "docs/${t_kontrat}a_$v_client.md" "$clean_md"; stage_forced "$r" "docs/${t_kontrat}a_$v_client.md"
sha=$(git -C "$r" rev-parse ":docs/${t_kontrat}a_$v_client.md"); rm -f -- "$r/.git/objects/${sha:0:2}/${sha:2}"
run_guard "$r"
if [ "$rc" -eq 1 ] && grep -q -F -e 'ERROR staged blob could not be read: staged entry' -- "$out" && ! has_fragment "$v_client" "$out"; then
  ok "A20 an ERROR line about a protected entry withholds its name (exit 1)"
else
  bad "A20 ERROR line for a protected entry" "exit $rc; the ERROR line named the entry or is missing"
fi
new_case; r=$(new_repo a20-unmerged)
pp="docs/${t_kontrat}a_$v_client.md"
write_file "$r" "$pp" "base line"; stage_forced "$r" "$pp"; git -C "$r" commit -q -m base
git -C "$r" checkout -q -b side
write_file "$r" "$pp" "side line"; stage_forced "$r" "$pp"; git -C "$r" commit -q -m side
git -C "$r" checkout -q main
write_file "$r" "$pp" "main line"; stage_forced "$r" "$pp"; git -C "$r" commit -q -m main
if git -C "$r" merge -q side > /dev/null 2>&1; then bad "A20 unmerged setup" "merge unexpectedly succeeded"; fi
run_guard "$r"
if [ "$rc" -eq 3 ] && grep -q -F -e 'unmerged index entry cannot be inspected: staged entry' -- "$out" && ! has_fragment "$v_client" "$out"; then
  ok "A20 an ERROR verdict about an unmerged protected entry withholds its name (exit 3)"
else
  bad "A20 unmerged protected entry" "exit $rc; the ERROR line named the entry or is missing"
fi
new_case; r=$(new_repo a20-symlink)
mkdir -p -- "$r/$d_docs_private"; ln -s -- ../README.md "$r/$d_docs_private/$v_client.lnk"; stage_forced "$r" "$d_docs_private/$v_client.lnk"
run_guard "$r"
if [ "$rc" -eq 1 ] && grep -q -F -e 'REVIEW_REQUIRED symlink target is not inspected: staged entry' -- "$out" && ! has_fragment "$v_client" "$out"; then
  ok "A20 a symlink REVIEW_REQUIRED line about a protected entry withholds its name (exit 1)"
else
  bad "A20 protected symlink" "exit $rc; the REVIEW_REQUIRED line named the entry or is missing"
fi
new_case; r=$(new_repo a20-gitlink)
git -C "$r" update-index --add --cacheinfo 160000,"$(git -C "$r" rev-parse HEAD)","$d_raw/$v_client"
run_guard "$r"
if [ "$rc" -eq 1 ] && grep -q -F -e 'REVIEW_REQUIRED submodule/gitlink content is not inspected: staged entry' -- "$out" && ! has_fragment "$v_client" "$out"; then
  ok "A20 a gitlink REVIEW_REQUIRED line about a protected entry withholds its name (exit 1)"
else
  bad "A20 protected gitlink" "exit $rc; the REVIEW_REQUIRED line named the entry or is missing"
fi
new_case; r=$(new_repo a20-upper)
d_upper=$(printf '%s' "$d_docs_private" | tr 'a-z' 'A-Z')
write_file "$r" "$d_upper/$v_client.md" "$clean_md"; stage_forced "$r" "$d_upper/$v_client.md"
run_guard "$r"; withheld_case "$r" "A20 a protected name in upper case is withheld too"
new_case; r=$(new_repo a20-hook); install_hook "$r"
write_file "$r" "docs/${t_kontrat}a_$v_client.md" "$clean_md"; stage_forced "$r" "docs/${t_kontrat}a_$v_client.md"
if commit_with_hook "$r" 'protected name'; then bad "A20 hook" "a commit with a protected name was created"; else ok "A20 hook: a protected name blocks the commit (A14 checks that hook output withholds it)"; fi
new_case; r=$(new_repo a20-quoted)
write_file "$r" "src/line1"$'\n'"line2.txt" "$fx_cred"; stage "$r" "src/line1"$'\n'"line2.txt"
run_guard "$r"
grep -F -e 'line1' -- "$out" > "$sandbox/line1.txt" || true
if [ "$rc" -eq 1 ] && grep -q -F -e 'line2' -- "$sandbox/line1.txt"; then
  ok "A20 an ordinary path containing a newline is printed quoted on one line (%q)"
else
  bad "A20 quoted path" "exit $rc; the path was not printed on a single quoted line"
fi

# ── A21 · README installation snippet (extracted from README.md and executed) ─
snippet="$sandbox/install-snippet.sh"
awk 'index($0, "Instalim lokal i sigurt") { seen = 1 }
     seen && !inb && /^```sh[[:space:]]*$/ { inb = 1; next }
     inb && /^```[[:space:]]*$/ { exit }
     inb { print }' "$README" > "$snippet"
new_case
if [ -s "$snippet" ]; then
  ok "A21 README contains an installation snippet (extracted for execution)"
else
  bad "A21 README installation snippet" "no sh block after the 'Instalim lokal i sigurt' line"
fi
hook_target=../../scripts/precommit_confidential_guard.sh
snippet_repo() { # <name> → plain repository (no core.hooksPath) with the guard under scripts/
  local r="$sandbox/$1"
  mkdir -p -- "$r/scripts"
  git -c init.defaultBranch=main init -q "$r"
  git -C "$r" config user.email synthetic-tester
  git -C "$r" config user.name 'Synthetic Tester'
  git -C "$r" config commit.gpgsign false
  cp -- "$GUARD" "$r/scripts/precommit_confidential_guard.sh"; chmod 755 "$r/scripts/precommit_confidential_guard.sh"
  cp -- "$GITIGNORE" "$r/.gitignore"
  git -C "$r" add -- .gitignore scripts/precommit_confidential_guard.sh
  git -C "$r" commit -q -m 'synthetic baseline (guard, no hook)'
  printf '%s' "$r"
}
run_snippet() { # <shell> <dir>
  out="$sandbox/snippet.out"
  rc=0
  (cd -- "$2" && "$1" "$snippet") > "$out" 2>&1 || rc=$?
}
no_hook() { [ ! -e "$1/.git/hooks/pre-commit" ] && [ ! -L "$1/.git/hooks/pre-commit" ]; }
refused() { # <case name> <repo whose hooks must stay untouched|->
  if [ "$rc" -ne 0 ] && grep -q -F -e 'NUK U INSTALUA' -- "$out" && { [ "$2" = - ] || no_hook "$2"; }; then
    ok "$1 (exit $rc, nothing installed)"
  else
    bad "$1" "exit $rc: $(summary)"
  fi
}
for sh_name in sh bash zsh; do
  if ! command -v "$sh_name" > /dev/null 2>&1; then
    new_case; skip "A21 [$sh_name] installation snippet" "$sh_name is not installed on this machine"; continue
  fi
  [ -s "$snippet" ] || continue
  # Fresh main worktree: relative symlink, verified, and the hook really blocks.
  new_case; r=$(snippet_repo "a21-$sh_name-fresh")
  run_snippet "$sh_name" "$r"
  link=$(readlink "$r/.git/hooks/pre-commit" 2> /dev/null || true)
  if [ "$rc" -eq 0 ] && grep -q '^OK:' "$out" && [ "$link" = "$hook_target" ] && [ -x "$r/.git/hooks/pre-commit" ]; then
    ok "A21 [$sh_name] fresh install: relative symlink created and verified (exit 0)"
  else
    bad "A21 [$sh_name] fresh install" "exit $rc, link '$link': $(summary)"
  fi
  new_case
  write_file "$r" src/leak.txt "$fx_cred"; stage "$r" src/leak.txt
  if commit_blocked_by_guard "$r" 'blocked by the installed hook'; then ok "A21 [$sh_name] the installed hook blocks a dirty commit"; else bad "A21 [$sh_name] installed hook" "the commit was not refused by the guard"; fi
  new_case; mv -- "$r" "$r-moved"; r="$r-moved"
  if [ -x "$r/.git/hooks/pre-commit" ] && commit_blocked_by_guard "$r" 'still blocked after the move'; then
    ok "A21 [$sh_name] after moving the repository the hook still resolves and blocks"
  else
    bad "A21 [$sh_name] moved repository" "the hook no longer resolves or no longer blocks"
  fi
  new_case; run_snippet "$sh_name" "$r"
  if [ "$rc" -eq 0 ] && grep -q '^OK:' "$out" && [ "$(readlink "$r/.git/hooks/pre-commit" 2> /dev/null || true)" = "$hook_target" ]; then
    ok "A21 [$sh_name] a second run reports the existing installation (exit 0, link unchanged)"
  else
    bad "A21 [$sh_name] second run" "exit $rc: $(summary)"
  fi
  new_case; r=$(snippet_repo "a21-$sh_name-subdir"); mkdir -p -- "$r/src/deep"
  run_snippet "$sh_name" "$r/src/deep"
  if [ "$rc" -eq 0 ] && [ "$(readlink "$r/.git/hooks/pre-commit" 2> /dev/null || true)" = "$hook_target" ]; then
    ok "A21 [$sh_name] run from a subdirectory installs at the work-tree top (exit 0)"
  else
    bad "A21 [$sh_name] subdirectory" "exit $rc: $(summary)"
  fi
  # Unmet prerequisites: never overwritten, never reported as success.
  new_case; r=$(snippet_repo "a21-$sh_name-other"); mkdir -p -- "$r/.git/hooks"
  printf '#!/bin/sh\nexit 0\n' > "$r/.git/hooks/pre-commit"; chmod 755 "$r/.git/hooks/pre-commit"
  before=$(cksum < "$r/.git/hooks/pre-commit")
  run_snippet "$sh_name" "$r"
  if [ "$rc" -ne 0 ] && grep -q -F -e 'NUK U INSTALUA' -- "$out" && [ ! -L "$r/.git/hooks/pre-commit" ] \
     && [ "$(cksum < "$r/.git/hooks/pre-commit")" = "$before" ]; then
    ok "A21 [$sh_name] an existing different hook is left unchanged and reported (exit $rc)"
  else
    bad "A21 [$sh_name] existing hook" "exit $rc: $(summary)"
  fi
  new_case; r=$(snippet_repo "a21-$sh_name-broken"); mkdir -p -- "$r/.git/hooks"
  ln -s ../../missing-target "$r/.git/hooks/pre-commit"
  run_snippet "$sh_name" "$r"
  if [ "$rc" -ne 0 ] && grep -q -F -e 'NUK U INSTALUA' -- "$out" \
     && [ "$(readlink "$r/.git/hooks/pre-commit" 2> /dev/null || true)" = ../../missing-target ]; then
    ok "A21 [$sh_name] an existing broken symlink is left unchanged and reported (exit $rc)"
  else
    bad "A21 [$sh_name] broken symlink" "exit $rc: $(summary)"
  fi
  new_case; r=$(snippet_repo "a21-$sh_name-hookspath"); git -C "$r" config core.hooksPath hooks-elsewhere
  run_snippet "$sh_name" "$r"; refused "A21 [$sh_name] core.hooksPath set: not installed" "$r"
  new_case; r=$(snippet_repo "a21-$sh_name-noguard"); rm -f -- "$r/scripts/precommit_confidential_guard.sh"
  run_snippet "$sh_name" "$r"; refused "A21 [$sh_name] guard missing from the checkout: not installed" "$r"
  new_case; r=$(snippet_repo "a21-$sh_name-noexec"); chmod 644 "$r/scripts/precommit_confidential_guard.sh"
  run_snippet "$sh_name" "$r"; refused "A21 [$sh_name] guard not executable: not installed" "$r"
  new_case; r=$(snippet_repo "a21-$sh_name-main")
  git -C "$r" worktree add -q -b "a21-$sh_name-linked" "$sandbox/a21-$sh_name-linked" > /dev/null 2>&1
  run_snippet "$sh_name" "$sandbox/a21-$sh_name-linked"; refused "A21 [$sh_name] linked worktree: not installed" "$r"
  new_case; d="$sandbox/a21-$sh_name-norepo"; mkdir -p -- "$d"
  run_snippet "$sh_name" "$d"; refused "A21 [$sh_name] outside a repository: not installed" -
done

# ── A22 · README recipe: unstage entry N without printing its name ───────────
recipe="$sandbox/unstage-recipe.sh"
awk 'index($0, "pa e printuar emrin") { seen = 1 }
     seen && !inb && /^```sh[[:space:]]*$/ { inb = 1; next }
     inb && /^```[[:space:]]*$/ { exit }
     inb { print }' "$README" > "$recipe"
new_case
if [ -s "$recipe" ] && grep -q -F -e 'N=2' -- "$recipe"; then
  ok "A22 README contains the unstage recipe (entry number 2 as its example)"
else
  bad "A22 README unstage recipe" "no sh block with N=2 after the 'pa e printuar emrin' line"
fi
if [ -s "$recipe" ]; then
  new_case; r=$(new_repo a22-unstage)
  write_file "$r" 'a/[x].txt' "$clean_md"; stage "$r" 'a/[x].txt'
  write_file "$r" "b/${t_kontrat}a_$v_client.md" "$clean_md"; stage_forced "$r" "b/${t_kontrat}a_$v_client.md"
  write_file "$r" 'c/*.txt' "$clean_md"; stage "$r" 'c/*.txt'
  mkdir -p -- "$r/c/deep"
  rrc=0; (cd -- "$r/c/deep" && sh "$recipe") > "$sandbox/recipe.out" 2>&1 || rrc=$?
  left=$(git -C "$r" diff --cached --no-renames --no-relative --ignore-submodules=none --name-only | tr '\n' '|')
  if [ "$rrc" -eq 0 ] && [ ! -s "$sandbox/recipe.out" ] && [ "$left" = 'a/[x].txt|c/*.txt|' ]; then
    ok "A22 the recipe unstages exactly entry 2 from a subdirectory and prints nothing"
  else
    bad "A22 unstage recipe" "exit $rrc, output bytes $(wc -c < "$sandbox/recipe.out" | tr -d ' '), still staged: $left"
  fi
fi

# ── A14 · diagnostics never disclose protected fixture content ───────────────
new_case; r=$(new_repo a14-disclosure)
write_file "$r" src/a.txt "$fx_key"; write_file "$r" src/b.txt "$fx_cred"; write_file "$r" src/c.txt "$fx_digits"
write_file "$r" src/d.txt "id $fx_akid"
stage "$r" src/a.txt; stage "$r" src/b.txt; stage "$r" src/c.txt; stage "$r" src/d.txt
run_guard "$r"; expect_rc 1 "A14 several violations are reported together"
expect_no_disclosure "$fx_key" "A14 key fixture is absent from the guard output"
expect_no_disclosure "$fx_cred" "A14 credential fixture is absent from the guard output"
expect_no_disclosure "$fx_digits" "A14 identifier fixture is absent from the guard output"
expect_no_disclosure "$fx_akid" "A14 token fixture is absent from the guard output"
new_case
if any_protected_fragment "$out"; then
  bad "A14 fragments" "a fragment of a protected value reached the guard output"
else
  ok "A14 no 5-byte fragment of any protected value reaches the guard output"
fi
new_case
if [ -s "$sandbox/hook.err" ]; then
  ok "A14 hook-mode diagnostics were produced (the absence checks below are not vacuous)"
else
  bad "A14 hook-mode diagnostics" "no hook output captured"
fi
expect_no_disclosure "$fx_key" "A14 key fixture is absent from every hook-mode diagnostic" "$sandbox/hook.err"
expect_no_disclosure "$fx_cred" "A14 credential fixture is absent from every hook-mode diagnostic" "$sandbox/hook.err"
new_case
if any_protected_fragment "$sandbox/hook.err"; then
  bad "A14 hook-mode fragments" "a fragment of a protected value or protected name reached hook output"
else
  ok "A14 no fragment of any protected value or protected name reaches hook-mode output"
fi


echo
printf 'privacy-guard regression: %d passed, %d failed, %d skipped (not run on this machine)\n' "$pass_count" "$fail_count" "$skip_count"
[ "$fail_count" -eq 0 ]
