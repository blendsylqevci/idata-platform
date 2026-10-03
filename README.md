# iData+

Platformë e brendshme — **private dhe pronësore (proprietary).**

## ⚠️ Konfidencialiteti (lexo para gjithçkaje)
Çdo gjë e push-uar këtu është e dukshme për çdo collaborator. **Nuk futet kurrë** material komercial, ligjor ose owner-private: dokumente marrëveshjesh, çmime, të dhëna personale klientësh, evidence reale kontabël/GL, sekrete fiskalizimi (SEF), credentials, `.env`, çelësa.

Rregullat e plota: **[REPO_SECURITY_RULES.md](REPO_SECURITY_RULES.md)**

## Para se të punosh këtu — lexo
- [PROJECT_RULES.md](PROJECT_RULES.md)
- [AGENTS.md](AGENTS.md)
- [CLAUDE.md](CLAUDE.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [REPO_SECURITY_RULES.md](REPO_SECURITY_RULES.md)

## Guard-i i privacy-t
Një pre-commit guard (`scripts/precommit_confidential_guard.sh`) bllokon commit-et me material konfidencial sipas rregullave ekzistuese të repo-s:

- rrugë owner/private: `00_raw/`, `03_contracts/`, `docs-private/`, `private-sensitive/`, `evidence-private-local/`, `docs/evidence/`, `docs/archive/prodata/`;
- emra skedarësh të dokumenteve komerciale/ligjore dhe të manifesteve të split-it kanonik;
- në përmbajtje: çelësa privatë dhe token-a, kredenciale të vendosura (fjalët kyçe angleze `password`, `api_key`, `token`, `secret`, `db_url`, `database_url` të ndjekura nga `=` ose `:` dhe një vlerë), shuma dhe identifikues të rregulluar.

**Kandidati i stage-uar është autoritativ.** Guard-i inspekton saktësisht hyrjet e index-it dhe blob-et që commit-i do të regjistrojë (`git diff --cached` + `git cat-file`, pa objekte zëvendësuese `refs/replace`), jo kopjet në working tree: një skedar i pastruar ose i fshirë në disk pas `git add` nuk e zbut vendimin, ndërsa përmbajtje e ndaluar vetëm në working tree, pa u stage-uar, nuk e bllokon. Fshirjet nuk inspektohen kurrë, që pastrimi i materialit të ndjeshëm të mbetet i mundur; një riemërtim trajtohet si fshirje + shtim, pra rruga e re inspektohet. Emrat me hapësira, karaktere të veçanta, Unicode, rreshta të rinj ose të ngjashëm me opsione lexohen me ndarës NUL, pa interpretim shell-i. Direktoritë lokale `output` dhe `tmp` në rrënjë të repo-s injorohen nga `.gitignore`; skedarët e tyre, nëse stage-ohen me `git add -f`, inspektohen si çdo rrugë tjetër.

**Lokali.** Detektorët e përmbajtjes ekzekutohen dy herë mbi çdo blob, dhe një përputhje në cilëndo prej tyre bllokon. Herën e parë bajt-për-bajt, në lokalin C, që kap edhe rreshta me kodim jo-UTF-8, p.sh. një vlerë në Windows-1252. Herën e dytë në lokalin e atij që bën commit-in (`LC_ALL`, `LC_CTYPE`, `LANG`), ashtu si i ekzekutonte guard-i origjinal. Nën një lokal UTF-8, `[[:space:]]` kap edhe hapësira Unicode mes fjalës kyçe dhe vlerës: në macOS edhe hapësirën e pandashme (NBSP), në Linux me glibc hapësira si em-space, por jo NBSP-në. Nën lokalin C ose POSIX, p.sh. në një klient grafik të git-it pa lokal, kapen vetëm hapësirat ASCII, njësoj si te guard-i origjinal. `GREP_OPTIONS` injorohet në të dy kalimet: grep-i BSD i macOS-it ende e lexon, dhe një vlerë si `--exclude=*` do t'i fikte detektorët.

**Rezultatet (kodi i daljes):**

| Rezultati | Exit | Kuptimi |
|---|---|---|
| `PASS` | 0 | inspektimi i mbështetur përfundoi dhe asnjë rregull i konfiguruar nuk u shkel — **jo garanci** se përmbajtja është pa të dhëna konfidenciale |
| `BLOCK` | 1 | një rrugë ose përmbajtje e stage-uar shkel një rregull konfidencialiteti |
| `REVIEW_REQUIRED` | 2 | përmbajtja nuk mund të vlerësohet automatikisht (blob me byte NUL kudo në të, p.sh. binar ose UTF-16, jashtë rrugëve të përjashtuara; symlink; submodule) — kërkon shqyrtim njerëzor |
| `ERROR` | 3 | vetë inspektimi nuk përfundoi (jashtë një work tree, `GIT_INDEX_FILE` që nuk ekziston, numërimi i index-it ose leximi i objektit dështoi, listë hyrjesh e papritur ose e cunguar nga git, hyrje e pa-merge-uar, një kalim i detektorëve dështoi, çdo gabim i papritur) |

Çdo rezultat jo-zero e ndalon commit-in; `REVIEW_REQUIRED` dhe `ERROR` nuk trajtohen kurrë si `PASS`. Diagnostika raporton klasën e gjetjes dhe rrugën (e cituar me `%q`), kurrë përmbajtjen. Pas `REVIEW_REQUIRED`: hiqe skedarin nga staging me `git reset -q -- <rruga>` (funksionon edhe para commit-it të parë), shqyrtoje manualisht dhe kërko vendimin e Blendit; guard-i nuk ka mekanizëm konfirmimi dhe rregullat e repo-s nuk lejojnë anashkalimin e tij.

**Emrat e mbrojtur nuk printohen.** Kur vetë rruga përputhet me një rregull rrugësh (dosje private ose emër dokumenti owner-only), emri nuk shfaqet në asnjë rresht diagnostike: në këtë repo edhe një emër i tillë është material owner-i, dhe dalja e hook-ut mund të përfundojë në logje, transkripte agjentësh ose raporte. Hyrja raportohet si `staged entry N`, ku N është pozicioni në listën që jep `git diff --cached --no-renames --no-relative --ignore-submodules=none --name-only` për një `git commit` të zakonshëm; fshirjet numërohen. Me `git commit -a` ose `git commit -- <rrugë>`, git i jep hook-ut një index të përkohshëm, ndaj numri i referohet atij: stage-o ndryshimet dhe ekzekuto `git commit` pa to për ta gjetur hyrjen. Ajo listë e printon emrin. Për ta hequr hyrjen N nga staging pa e printuar emrin askund, p.sh. kur dalja shkon në transkriptin e një agjenti, zëvendëso `2` me numrin N:

```sh
( cd "$(git rev-parse --show-toplevel)" &&
  git diff --cached --no-renames --no-relative --ignore-submodules=none --name-only -z |
  N=2 perl -0 -ne 'print if $. == $ENV{N}' |
  GIT_LITERAL_PATHSPECS=1 xargs -0 -r git reset -q -- )
```

**Kufizime:**

- Është hook lokal: mund të anashkalohet (`git commit --no-verify`) dhe nuk ekzekutohet fare kur `core.hooksPath` tregon një direktori tjetër; nuk zëvendëson gitleaks në CI as shqyrtimin njerëzor.
- Inspekton vetëm commit-et që kalojnë nga `pre-commit`. Merge me auto-commit, cherry-pick dhe `git revert` pa konflikt, rebase (edhe `--continue` pas një konflikti) dhe `git am` krijojnë commit-e pa e ekzekutuar hook-un. Pas një konflikti, `git commit` që mbyll merge-in, `git cherry-pick --continue` dhe `git revert --continue` e ekzekutojnë. `git commit --amend`, si edhe `reword` në një rebase interaktiv, e ekzekuton, por inspekton vetëm diferencën ndaj HEAD-it aktual: përmbajtja që HEAD-i e ka tashmë kalon në commit-in e ri pa u riinspektuar.
- Detektorët janë regex (POSIX ERE) mbi rreshta teksti: pa OCR, pa nxjerrje nga arkiva/PDF, pa analizë binare. Një header çelësi i ndarë në dy rreshta nuk kapet; detektori i çelësave njeh vetëm disa forma header-i (jo p.sh. `RSA PRIVATE KEY`, `EC PRIVATE KEY`, `OPENSSH PRIVATE KEY`); detektori i shumave/identifikuesve mbi-kap (çdo varg prej 12 e më shumë shifrash; `[\.,]` kap edhe backslash).
- Te vetë guard-i dhe te `tools-provenance/*` përmbajtja nuk inspektohet fare, as kontrolli i byte-ve NUL, ndaj edhe një blob binar aty merr `PASS`; rregullat e rrugëve aplikohen.
- Për një rrugë me filtër `clean` (atributi `filter`, nga `.gitattributes`, `.git/info/attributes` ose `core.attributesFile`; p.sh. Git LFS), blob-i i stage-uar është dalja e filtrit, te LFS një pointer i shkurtër teksti. Guard-i inspekton vetëm atë, jo skedarin real, dhe as gitleaks në CI nuk e sheh përmbajtjen që ngarkohet në LFS. Guard-i origjinal lexonte skedarin në working tree, ndaj për rrugë të tilla mbulimi tani është më i ngushtë.
- Hook-et ndahen nga të gjitha worktree-t e lidhura dhe ekzekutojnë skriptin ku tregon symlink-u (zakonisht checkout-i i worktree-së kryesore), pra një worktree e lidhur mund të përdorë një version tjetër të guard-it.
- Një hyrje intent-to-add (`git add -N`) nuk ka përmbajtje të stage-uar, dhe një commit i zakonshëm nuk e regjistron. `git commit -a` ose `git commit -- <rruga>` e stage-on më parë përmbajtjen e saj, dhe guard-i e inspekton si çdo shtim tjetër.
- `PASS` nuk është provë se përmbajtja është e pastër nga çdo rrjedhje e mundshme.

**Testet e regresionit** (repo sintetike nën `mktemp`, fixtures sintetike, konfigurim git hermetik; ekzekutohen automatikisht edhe në CI, në job-in `build-test`):

```sh
bash scripts/test_precommit_confidential_guard.sh
# e njëjta suitë kundër një build-i tjetër të guard-it, .gitignore-it ose README-së:
GUARD_SCRIPT=/rruga/guard.sh GITIGNORE_FILE=/rruga/.gitignore README_FILE=/rruga/README.md bash scripts/test_precommit_confidential_guard.sh
```

Rastet që kërkojnë një lokal, shell ose klasë hapësire që makina nuk e ka raportohen `SKIP` dhe nuk numërohen si të kaluara; p.sh. NBSP-ja nuk është `[[:space:]]` nën glibc, ndaj rastet me NBSP nuk provohen në Linux. Suita dështon nëse nuk gjen asnjë lokal UTF-8 për kalimin në lokalin e committer-it. Ajo kopjon `README.md` dhe `PROJECT_RULES.md` si përmbajtje legjitime, edhe nën lokal UTF-8, prandaj këto dy dokumente duhet të mbeten të pastra për detektorët. Ajo ekzekuton edhe dy blloqet `sh` të këtij seksioni, recetën e mësipërme dhe snippet-in e instalimit më poshtë, të nxjerra drejtpërdrejt nga ky README.

**Instalim lokal i sigurt** — ngjite në terminal nga worktree-ja kryesore. Nuk mbishkruan asnjë hook ekzistues. Printon `OK:` vetëm pasi verifikon që hook-u tregon guard-in; çdo parakusht që dështon printon `NUK U INSTALUA:` dhe del me kod jo-zero:

```sh
(
  guard=scripts/precommit_confidential_guard.sh
  hook=.git/hooks/pre-commit
  nuk() { echo "NUK U INSTALUA: $*" >&2; exit 1; }
  top=$(git rev-parse --show-toplevel 2>/dev/null) || nuk "nuk je brenda një work tree të git-it"
  cd "$top" || nuk "rrënja e repo-s nuk u hap"
  if git config --get core.hooksPath >/dev/null; then
    nuk "core.hooksPath është aktiv; vendos vetë ku instalohet guard-i"
  fi
  if [ "$(git rev-parse --git-dir)" != .git ] || [ "$(git rev-parse --git-path hooks)" != .git/hooks ]; then
    nuk "ekzekutoje nga worktree-ja kryesore (me .git/hooks standard), jo nga një worktree e lidhur"
  fi
  if [ ! -f "$guard" ] || [ ! -x "$guard" ]; then
    nuk "$guard mungon ose nuk është i ekzekutueshëm në këtë checkout"
  fi
  if [ -e "$hook" ] || [ -L "$hook" ]; then
    [ -L "$hook" ] && [ "$hook" -ef "$guard" ] && { echo "OK: guard-i është tashmë i instaluar ($hook)"; exit 0; }
    ls -l "$hook" >&2
    nuk "ekziston një hook pre-commit tjetër; shqyrtoje, mos e mbishkruaj verbërisht"
  fi
  mkdir -p .git/hooks || nuk "direktoria .git/hooks nuk u krijua"
  ln -s ../../scripts/precommit_confidential_guard.sh "$hook" || nuk "symlink-u nuk u krijua"
  [ -x "$hook" ] && [ "$hook" -ef "$guard" ] && echo "OK: $hook tregon $guard (symlink relativ, i verifikuar)" || nuk "symlink-u nuk tregon guard-in; hiqe me: rm $hook"
)
```

Symlink-u është relativ (`../../scripts/precommit_confidential_guard.sh`), ndaj mbetet i vlefshëm edhe kur repo zhvendoset. Git nuk e ekzekuton një hook jo-ekzekutues: shfaq vetëm një hint, që mund të jetë i fikur me `advice.ignoredHook`. Një symlink të prishur e kapërcen në heshtje. Snippet-i e verifikon hook-un menjëherë pas instalimit, dhe më vonë mund ta kontrollosh me `ls -lL .git/hooks/pre-commit`. Në bash 3.2 të macOS-it (jo në zsh, shell-in parazgjedhur), një ngjitje e gjatë mund të humbasë rreshta: nëse shfaqet një `syntax error` ose mungon rreshti `OK:`, mos e konsidero të instaluar, kontrolloje me `ls -lL` ose ruaje bllokun në një skedar dhe ekzekutoje me `sh <skedari>`.

## Dokumentacioni
Shih `docs/INDEX.md`.

---
© Të gjitha të drejtat e rezervuara. E palicencuar për përdorim publik ose rishpërndarje.
