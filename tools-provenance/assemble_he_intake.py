#!/usr/bin/env python3
"""
Build a guarded intake package from the current /he workspace into the main
iData-platform folder.

Principles:
- Inventory everything.
- Treat raw PRO DATA exports/dumps as PII by default.
- Copy only safe/sanitized/canonical-candidate material into 00_raw/codex.
- Never copy private-sensitive/raw-PII evidence into git-bound folders.
- Keep manifest values generic: flags, disposition, reason; no secret values.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import os
import re
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


SECRET_PATTERNS = [
    re.compile(rb"-----BEGIN [A-Z ]*(PRIVATE )?KEY-----"),
    re.compile(rb"\b(?:password|passwd|pwd)\s*[:=]", re.I),
    re.compile(rb"\b(?:secret|token|api[_-]?key|access[_-]?key)\s*[:=]", re.I),
    re.compile(rb"\b(?:server|host)\s*=\s*[^;\n]+;\s*(?:database|initial catalog)\s*=", re.I),
    re.compile(rb"\b(?:user id|uid)\s*=\s*[^;\n]+;\s*(?:password|pwd)\s*=", re.I),
    # Known SEF identifiers are detected by shape only here. Do not write values to manifest.
    re.compile(rb"\b\d{12}\b"),
]

TEXT_EXTENSIONS = {
    ".md",
    ".txt",
    ".csv",
    ".json",
    ".sql",
    ".py",
    ".yml",
    ".yaml",
    ".toml",
    ".env",
    ".example",
    ".log",
}

SKIP_DIR_NAMES = {".git", ".agents", ".codex", "__pycache__", "node_modules"}
SKIP_FILE_NAMES = {".DS_Store"}


@dataclass
class Row:
    path: str
    size_bytes: int
    ext: str
    sha256: str
    source_bucket: str
    contains_secret: str
    contains_pii: str
    duplicate_of: str
    recommended_bucket: str
    git_disposition: str
    reason: str
    copied_to: str


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def is_text_like(path: Path) -> bool:
    return path.suffix.lower() in TEXT_EXTENSIONS


def scan_secret(path: Path) -> bool:
    # Scan only text-like and small-ish files. Raw binary/data dumps are classified by policy.
    if not is_text_like(path):
        return False
    try:
        data = path.read_bytes()
    except OSError:
        return True
    if len(data) > 5 * 1024 * 1024:
        data = data[:5 * 1024 * 1024]
    return any(p.search(data) for p in SECRET_PATTERNS)


def rel_parts(rel: str) -> tuple[str, ...]:
    return tuple(Path(rel).parts)


def source_bucket(rel: str) -> str:
    parts = rel_parts(rel)
    return parts[0] if parts else ""


def pii_by_default(rel: str) -> bool:
    parts = rel_parts(rel)
    lower = rel.lower()

    # Any raw PRO DATA extraction/data dump is private by default.
    if parts and parts[0] == "outputs":
        raw_markers = [
            "/raw_",
            "/data/",
            "/sample_previews/",
            "prodata_accounting_extract",
            "prodata_reports_extracted_2026-06-27",
            "raw_restaurant_tables",
            "raw_supplementary_tables",
            "raw_critical_tables",
        ]
        if any(marker in f"/{lower}" for marker in raw_markers):
            return True

    # tmp/work are working areas and not safe for git by default.
    if parts and parts[0] in {"tmp", "work"}:
        return True

    # Spreadsheet/report exports from accountants are evidence, normally private unless
    # explicitly copied as sanitized analysis docs.
    if Path(rel).suffix.lower() in {".xlsx", ".xls", ".prnx", ".bak", ".zip", ".7z", ".rar"}:
        return True

    return False


def classify(rel: str, path: Path, contains_secret: bool, contains_pii: bool) -> tuple[str, str, str]:
    parts = rel_parts(rel)
    lower = rel.lower()
    ext = path.suffix.lower()

    if path.name in SKIP_FILE_NAMES:
        return "drop", "not-in-git", "OS/editor artifact"

    if contains_secret:
        return "private-sensitive", "gitignore", "Secret-like pattern detected"

    if contains_pii:
        return "evidence-private-local", "gitignore", "Raw/working/export data treated as private by default"

    if parts and parts[0] == "docs" and ext == ".md":
        return "codex-docs", "copy-to-raw-codex", "Codex-authored documentation candidate"

    if parts and parts[0] == "tools" and ext == ".py":
        return "tools-provenance", "copy-to-raw-codex", "Script provenance for generated evidence"

    if parts and parts[0] == "outputs":
        safe_markers = [
            "sanitized",
            "summaries",
            "schema_review",
            "procedure_review",
            "module_map",
            "backup_completeness_audit",
            "evidence_closure_parity_matrix",
            "ddl_extracted",
            "ddl_dml_crosscheck",
            "finance_operations_backup_audit",
            "phase0_canonical_update_pack",
        ]
        if any(marker in lower for marker in safe_markers) and ext in {".md", ".csv", ".json", ".sql", ".txt"}:
            return "evidence-sanitized", "copy-to-raw-codex", "Sanitized/review evidence candidate"
        return "evidence-private-local", "gitignore", "Output evidence not explicitly sanitized"

    if parts and parts[0] == "tmp":
        return "temp-archive-local", "not-in-git", "Temporary workspace output"

    if parts and parts[0] == "work":
        return "work-archive-local", "not-in-git", "Intermediate work product"

    return "archive-review", "not-in-git", "Needs manual review"


def copy_target_for(rel: str, bucket: str, target: Path) -> Path | None:
    if bucket == "codex-docs":
        return target / "00_raw" / "codex" / "docs" / Path(rel).relative_to("docs")
    if bucket == "tools-provenance":
        return target / "00_raw" / "codex" / "tools-provenance" / Path(rel).relative_to("tools")
    if bucket == "evidence-sanitized":
        return target / "00_raw" / "codex" / "evidence-sanitized" / rel
    return None


def iter_files(source: Path) -> Iterable[Path]:
    for root, dirs, files in os.walk(source):
        dirs[:] = [d for d in dirs if d not in SKIP_DIR_NAMES]
        root_path = Path(root)
        for name in files:
            yield root_path / name


def append_gitignore(target: Path) -> None:
    gitignore = target / ".gitignore"
    existing = gitignore.read_text() if gitignore.exists() else ""
    additions = [
        "evidence-private-local/",
        "00_raw/evidence-private-local/",
        "00_raw/codex/evidence-private-local/",
        "00_raw/**/evidence-private-local/",
    ]
    missing = [line for line in additions if line not in existing.splitlines()]
    if missing:
        with gitignore.open("a", encoding="utf-8") as f:
            if existing and not existing.endswith("\n"):
                f.write("\n")
            f.write("# Private/local evidence — NEVER commit\n")
            for line in missing:
                f.write(f"{line}\n")


def write_review(rows: list[Row], out_path: Path, source: Path, target: Path) -> None:
    total = len(rows)
    copied = sum(1 for r in rows if r.copied_to)
    secret = sum(1 for r in rows if r.contains_secret == "yes")
    pii = sum(1 for r in rows if r.contains_pii == "yes")
    by_disposition: dict[str, int] = {}
    by_bucket: dict[str, int] = {}
    for r in rows:
        by_disposition[r.git_disposition] = by_disposition.get(r.git_disposition, 0) + 1
        by_bucket[r.recommended_bucket] = by_bucket.get(r.recommended_bucket, 0) + 1

    lines = [
        "# HE Intake Review",
        "",
        "**Status:** guarded intake generated before canonical split",
        "",
        f"- Source: `{source}`",
        f"- Target: `{target}`",
        f"- Total files inventoried: **{total}**",
        f"- Files copied into `00_raw/codex`: **{copied}**",
        f"- Secret-like files flagged: **{secret}**",
        f"- PII/private-by-default files flagged: **{pii}**",
        "",
        "## Guardrails applied",
        "",
        "- Raw PRO DATA exports/dumps are treated as PII by default and are not copied to git-bound folders.",
        "- Regex scanning is used as a safety net for docs/scripts, not as proof that raw dumps are safe.",
        "- Manifest records only flags and dispositions; it does not include secret or PII values.",
        "- `evidence-private-local/` patterns are protected in root `.gitignore`.",
        "- Claude review is required before copy/split/commit is finalized.",
        "",
        "## Counts by git disposition",
        "",
    ]
    for key in sorted(by_disposition):
        lines.append(f"- `{key}`: {by_disposition[key]}")
    lines.extend(["", "## Counts by recommended bucket", ""])
    for key in sorted(by_bucket):
        lines.append(f"- `{key}`: {by_bucket[key]}")
    lines.extend(
        [
            "",
            "## Next required gate",
            "",
            "Claude must review `HE_INTAKE_MANIFEST.csv` for completeness, duplicate handling, secret flags, and PII flags before canonical split or first commit.",
            "",
            "## Important note",
            "",
            "This is an intake package, not the final canonical documentation. Final output still needs `docs/canonical`, `docs/evidence`, `docs/archive`, and `docs-private` split.",
            "",
        ]
    )
    out_path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--target", required=True)
    args = parser.parse_args()

    source = Path(args.source).resolve()
    target = Path(args.target).resolve()
    codex_raw = target / "00_raw" / "codex"
    codex_raw.mkdir(parents=True, exist_ok=True)
    append_gitignore(target)

    rows: list[Row] = []
    seen_hash: dict[str, str] = {}
    for path in sorted(iter_files(source), key=lambda p: str(p.relative_to(source))):
        rel = str(path.relative_to(source))
        ext = path.suffix.lower()
        size = path.stat().st_size
        digest = sha256_file(path)
        duplicate_of = seen_hash.get(digest, "")
        if not duplicate_of:
            seen_hash[digest] = rel
        has_secret = scan_secret(path)
        has_pii = pii_by_default(rel)
        bucket, disposition, reason = classify(rel, path, has_secret, has_pii)
        copied_to = ""
        dest = copy_target_for(rel, bucket, target)
        if dest is not None and disposition == "copy-to-raw-codex":
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, dest)
            copied_to = str(dest.relative_to(target))
        rows.append(
            Row(
                path=rel,
                size_bytes=size,
                ext=ext,
                sha256=digest,
                source_bucket=source_bucket(rel),
                contains_secret="yes" if has_secret else "no",
                contains_pii="yes" if has_pii else "no",
                duplicate_of=duplicate_of,
                recommended_bucket=bucket,
                git_disposition=disposition,
                reason=reason,
                copied_to=copied_to,
            )
        )

    manifest = codex_raw / "HE_INTAKE_MANIFEST.csv"
    with manifest.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(Row.__dataclass_fields__.keys()))
        writer.writeheader()
        for row in rows:
            writer.writerow(row.__dict__)

    review = codex_raw / "HE_INTAKE_REVIEW.md"
    write_review(rows, review, source, target)
    print(f"manifest={manifest}")
    print(f"review={review}")
    print(f"files_inventoried={len(rows)}")
    print(f"files_copied={sum(1 for r in rows if r.copied_to)}")
    print(f"secret_flagged={sum(1 for r in rows if r.contains_secret == 'yes')}")
    print(f"pii_flagged={sum(1 for r in rows if r.contains_pii == 'yes')}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
