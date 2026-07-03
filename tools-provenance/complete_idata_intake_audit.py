#!/usr/bin/env python3
"""
Complete the guarded iData Codex intake after a second file-level audit.

This script is intentionally conservative:
- it copies only explicitly classified candidates;
- it keeps provenance tools separate from product code;
- it writes a completion report + manifest;
- it never copies raw DB backups, raw SQL/PSV/CSV data dumps, caches, pyc, or zip/gz bulk.

Source: <intake-source private-local>
Target: 00_raw/codex
"""

from __future__ import annotations

import csv
import hashlib
import re
import shutil
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path


SRC = Path("<intake-source private-local>")
TARGET = Path("00_raw/codex")


SECRET_PATTERNS = [
    re.compile(r"-----BEGIN [A-Z ]*(?:PRIVATE KEY|KEY|CERTIFICATE)-----"),
    re.compile(r"\b(?:password|passwd|pwd)\s*[:=]\s*['\"][^'\"]{4,}", re.I),
    re.compile(r"\b(?:secret|token|api[_-]?key)\s*[:=]\s*['\"][^'\"]{8,}", re.I),
    re.compile(r"\bServer=.*;.*(?:Password|Pwd)=", re.I),
    re.compile(r"\bgh[pousr]_[A-Za-z0-9_]{20,}\b"),
]

PII_HINTS = [
    re.compile(r"\b(?:phone|telefon|tel|email|e-mail|address|adresa|salary|paga|employee|punetor)\b", re.I),
    re.compile(r"\b\d{3}[- ]?\d{3}[- ]?\d{3}\b"),
]


@dataclass(frozen=True)
class CopyCandidate:
    src_rel: str
    dest_rel: str
    bucket: str
    reason: str
    git_policy: str


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def read_text_safely(path: Path, limit: int = 2_000_000) -> str:
    if path.stat().st_size > limit:
        return ""
    try:
        return path.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        return ""


def scan_flags(path: Path) -> tuple[bool, bool, list[str]]:
    text = read_text_safely(path)
    flags: list[str] = []
    has_secret = False
    has_pii = False
    if text:
        for pat in SECRET_PATTERNS:
            if pat.search(text):
                has_secret = True
                flags.append(f"secret_pattern:{pat.pattern[:32]}")
        for pat in PII_HINTS:
            if pat.search(text):
                has_pii = True
                flags.append(f"pii_hint:{pat.pattern[:32]}")
    return has_secret, has_pii, flags


def source_candidates() -> list[CopyCandidate]:
    candidates: list[CopyCandidate] = []

    # 1) All Python provenance tools. These are audit/extraction provenance, not app code.
    for p in sorted((SRC / "tools").glob("*.py")):
        candidates.append(
            CopyCandidate(
                src_rel=str(p.relative_to(SRC)),
                dest_rel=f"tools-provenance/{p.name}",
                bucket="tools-provenance",
                reason="Extraction/audit provenance script; keep separate from product code.",
                git_policy="intake-review-ok",
            )
        )

    # 2) Specific output reports caught by second audit. These are not raw dumps.
    report_paths = [
        "outputs/prodata_gl_reconciliation_2026-06-28/PRODATA_GL_Posting_Reconciliation_June2026.md",
        "outputs/prodata_posting_deep_dive_2026-06-28/PRODATA_Posting_Logic_Deep_Dive.md",
        "outputs/prodata_supplementary_extract_wave2_restaurant_2026-06-28/PRODATA_Restaurant_POS_KDS_Operational_Review.md",
        "outputs/prodata_accounting_extract/README_KONTABILITETI.md",
        "outputs/finance_operations_micro_extract_2026-06-29/FINANCE_OPERATIONS_Micro_Extract_Review.md",
        "outputs/finance_operations_micro_extract_2026-06-29/finance_micro_extract_manifest.csv",
    ]
    for rel in report_paths:
        p = SRC / rel
        if p.exists():
            candidates.append(
                CopyCandidate(
                    src_rel=rel,
                    dest_rel=f"evidence-sanitized/{rel}",
                    bucket="evidence-sanitized",
                    reason="High-value analysis/report file; review before canonical split.",
                    git_policy="review-before-commit",
                )
            )

    # 3) Phase 0 update pack: provenance/archive, not canonical source.
    phase0_dir = SRC / "outputs/phase0_canonical_update_pack_2026-06-28"
    if phase0_dir.exists():
        for p in sorted(phase0_dir.iterdir()):
            if p.is_file():
                rel = str(p.relative_to(SRC))
                candidates.append(
                    CopyCandidate(
                        src_rel=rel,
                        dest_rel=f"archive/{rel}",
                        bucket="archive",
                        reason="Superseded Phase 0 update pack; archive as provenance, not canonical.",
                        git_policy="archive-provenance",
                    )
                )

    # 4) Owner-private client/commercial reference documents from work/.
    # Raw filenames are intentionally not named in git; keep exact names only
    # in ignored/private-local evidence.
    archive_paths = [
        "work/client-docs-text/CLIENT_SCOPE_KABI.txt",
        "work/client-docs-text/CLIENT_SCOPE_PLANET.txt",
        "work/client-docs-text/CLIENT_COMMERCIAL_OFFER.txt",
        "work/prodata-analysis/Raport_Auditimi_Backup_Prodata_PLANET_2026-06-24.md",
    ]
    for rel in archive_paths:
        p = SRC / rel
        if p.exists():
            candidates.append(
                CopyCandidate(
                    src_rel=rel,
                    dest_rel=f"archive/{rel}",
                    bucket="archive",
                    reason="Contractual/reference source; preserve for traceability, not canonical.",
                    git_policy="archive-review",
                )
            )

    return candidates


def classify_old_tree() -> dict[str, int]:
    counts = {
        "total_files": 0,
        "docs_md": 0,
        "tools_py": 0,
        "outputs_md_csv_reports": 0,
        "work_reference": 0,
        "raw_private_or_bulk": 0,
        "drop_junk": 0,
    }
    for p in SRC.rglob("*"):
        if not p.is_file():
            continue
        rel = p.relative_to(SRC)
        rel_s = str(rel)
        counts["total_files"] += 1
        if rel_s.startswith("docs/") and p.suffix.lower() == ".md":
            counts["docs_md"] += 1
        elif rel_s.startswith("tools/") and p.suffix.lower() == ".py":
            counts["tools_py"] += 1
        elif rel_s.startswith("outputs/") and p.suffix.lower() in {".md", ".csv"}:
            counts["outputs_md_csv_reports"] += 1
        elif rel_s.startswith("work/") and p.suffix.lower() in {".md", ".txt"}:
            counts["work_reference"] += 1
        elif any(part in {"__pycache__", "swift-cache"} for part in rel.parts) or p.suffix.lower() in {".pyc", ".pcm", ".swiftmodule"} or p.name == ".DS_Store":
            counts["drop_junk"] += 1
        else:
            counts["raw_private_or_bulk"] += 1
    return counts


def main() -> None:
    if not SRC.exists():
        raise SystemExit(f"Source missing: {SRC}")
    if not TARGET.exists():
        raise SystemExit(f"Target missing: {TARGET}")

    candidates = source_candidates()
    manifest_rows: list[dict[str, str]] = []
    copied = 0
    unchanged = 0
    replaced = 0
    flagged_secret = 0
    flagged_pii = 0

    for c in candidates:
        src_path = SRC / c.src_rel
        dest_path = TARGET / c.dest_rel
        if not src_path.exists():
            continue

        has_secret, has_pii, flags = scan_flags(src_path)
        flagged_secret += int(has_secret)
        flagged_pii += int(has_pii)

        src_hash = sha256(src_path)
        dest_hash = sha256(dest_path) if dest_path.exists() else ""
        action = "unchanged" if dest_hash == src_hash else ("replace" if dest_path.exists() else "copy")

        if action != "unchanged":
            dest_path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src_path, dest_path)
            copied += 1
            replaced += int(action == "replace")
        else:
            unchanged += 1

        manifest_rows.append(
            {
                "src_rel": c.src_rel,
                "dest_rel": c.dest_rel,
                "bucket": c.bucket,
                "size_bytes": str(src_path.stat().st_size),
                "sha256": src_hash,
                "action": action,
                "git_policy": c.git_policy,
                "contains_secret_pattern": "yes" if has_secret else "no",
                "contains_pii_hint": "yes" if has_pii else "no",
                "scan_notes": ";".join(flags),
                "reason": c.reason,
            }
        )

    report_dir = TARGET
    report_path = report_dir / "HE_INTAKE_COMPLETION_AUDIT.md"
    manifest_path = report_dir / "HE_INTAKE_COMPLETION_MANIFEST.csv"
    old_counts = classify_old_tree()

    with manifest_path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "src_rel",
                "dest_rel",
                "bucket",
                "size_bytes",
                "sha256",
                "action",
                "git_policy",
                "contains_secret_pattern",
                "contains_pii_hint",
                "scan_notes",
                "reason",
            ],
        )
        writer.writeheader()
        writer.writerows(manifest_rows)

    by_bucket: dict[str, int] = {}
    by_action: dict[str, int] = {}
    for row in manifest_rows:
        by_bucket[row["bucket"]] = by_bucket.get(row["bucket"], 0) + 1
        by_action[row["action"]] = by_action.get(row["action"], 0) + 1

    md_lines = [
        "# HE Intake Completion Audit",
        "",
        f"Generated: {datetime.now().isoformat(timespec='seconds')}",
        "",
        "## Purpose",
        "",
        "This is the corrective completion pass after the second file-level audit found missing git-candidate material.",
        "It does not promote anything to canonical. It only completes raw intake buckets for later dedupe/split.",
        "",
        "## Old `/he` tree classification snapshot",
        "",
    ]
    for k, v in old_counts.items():
        md_lines.append(f"- `{k}`: {v}")
    md_lines += [
        "",
        "## Completion results",
        "",
        f"- Candidates considered: {len(manifest_rows)}",
        f"- Copied/replaced: {copied}",
        f"- Unchanged already present: {unchanged}",
        f"- Replaced existing changed files: {replaced}",
        f"- Secret-pattern flags among copied candidates: {flagged_secret}",
        f"- PII-hint flags among copied candidates: {flagged_pii}",
        "",
        "### By bucket",
        "",
    ]
    for k in sorted(by_bucket):
        md_lines.append(f"- `{k}`: {by_bucket[k]}")
    md_lines += [
        "",
        "### By action",
        "",
    ]
    for k in sorted(by_action):
        md_lines.append(f"- `{k}`: {by_action[k]}")
    md_lines += [
        "",
        "## Important guardrails",
        "",
        "- `tools-provenance/` is audit provenance only, not product app code.",
        "- `archive/outputs/phase0_canonical_update_pack_2026-06-28/` is superseded provenance, not a competing source of truth.",
        "- `evidence-sanitized/` is still review-before-commit; raw PRO DATA dumps/backups remain outside git.",
        "- Canonical split must still apply source+reason+testability and conflict logging.",
        "",
        "## Manifest",
        "",
        f"See `{manifest_path.name}`.",
        "",
    ]
    report_path.write_text("\n".join(md_lines), encoding="utf-8")

    print(f"wrote {report_path}")
    print(f"wrote {manifest_path}")
    print(f"candidates={len(manifest_rows)} copied_or_replaced={copied} unchanged={unchanged}")
    print(f"secret_flags={flagged_secret} pii_flags={flagged_pii}")


if __name__ == "__main__":
    main()
