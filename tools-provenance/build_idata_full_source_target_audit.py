#!/usr/bin/env python3
"""
Build a full source -> target audit for the iData intake.

This report is meant to answer:
- what exists in the old Codex `/he` folder;
- what has been transferred to the new `idata-platform/00_raw/codex`;
- what is intentionally not transferred because it is raw/private/bulk/junk;
- which transferred buckets are review-only before canonical split.
"""

from __future__ import annotations

import csv
import hashlib
import re
from collections import Counter
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path


SRC = Path("<intake-source private-local>")
TARGET = Path("00_raw/codex")


TEXT_EXTS = {".md", ".txt", ".py", ".csv", ".json", ".sql", ".psv", ".yml", ".yaml"}
RAW_EXTS = {".bak", ".gz", ".zip", ".7z", ".rar", ".psv", ".csv", ".sql", ".json"}
JUNK_EXTS = {".pyc", ".pcm", ".swiftmodule"}

SECRET_RE = re.compile(
    r"(-----BEGIN [A-Z ]*(?:PRIVATE KEY|KEY|CERTIFICATE)-----|gh[pousr]_[A-Za-z0-9_]{20,}|(?:password|passwd|pwd|token|secret|api[_-]?key)\s*[:=]\s*['\"][^'\"]{4,})",
    re.I,
)
PII_RE = re.compile(r"\b(phone|telefon|email|address|adresa|salary|paga|employee|punetor|personal)\b", re.I)


EXPLICIT_REPORTS = {
    "outputs/prodata_gl_reconciliation_2026-06-28/PRODATA_GL_Posting_Reconciliation_June2026.md",
    "outputs/prodata_posting_deep_dive_2026-06-28/PRODATA_Posting_Logic_Deep_Dive.md",
    "outputs/prodata_supplementary_extract_wave2_restaurant_2026-06-28/PRODATA_Restaurant_POS_KDS_Operational_Review.md",
    "outputs/prodata_accounting_extract/README_KONTABILITETI.md",
    "outputs/finance_operations_micro_extract_2026-06-29/FINANCE_OPERATIONS_Micro_Extract_Review.md",
    "outputs/finance_operations_micro_extract_2026-06-29/finance_micro_extract_manifest.csv",
}

EXPLICIT_ARCHIVE_WORK = {
    # Owner-private client/commercial source documents are intentionally not
    # named in git. Keep raw names only in ignored/private-local evidence.
    "work/client-docs-text/CLIENT_SCOPE_KABI.txt",
    "work/client-docs-text/CLIENT_SCOPE_PLANET.txt",
    "work/client-docs-text/CLIENT_COMMERCIAL_OFFER.txt",
    "work/prodata-analysis/Raport_Auditimi_Backup_Prodata_PLANET_2026-06-24.md",
}


@dataclass
class AuditRow:
    source_rel: str
    size_bytes: int
    source_class: str
    intended_bucket: str
    transfer_status: str
    target_matches: str
    scan_mode: str
    secret_flag: str
    pii_flag: str
    reason: str


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def text_scan(path: Path) -> tuple[str, str, str]:
    if path.suffix.lower() not in TEXT_EXTS:
        return "not-text", "no", "no"
    if path.stat().st_size > 2_000_000:
        return "skipped-large-text", "unknown", "unknown"
    try:
        text = path.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        return "unreadable-text", "unknown", "unknown"
    return "content-scan", "yes" if SECRET_RE.search(text) else "no", "yes" if PII_RE.search(text) else "no"


def expected_target(rel: str, path: Path) -> tuple[str, str, str]:
    suffix = path.suffix.lower()
    parts = Path(rel).parts

    if rel.startswith("docs/") and suffix == ".md":
        return "codex-docs", "docs", "canonical-candidate doc copied for later dedupe"
    if rel.startswith("tools/") and suffix == ".py":
        return "tools-provenance", "tools-provenance", "audit/extraction provenance tool"
    if rel in EXPLICIT_REPORTS:
        return "evidence-sanitized", "evidence-sanitized", "high-value report copied for review before commit"
    if rel.startswith("outputs/phase0_canonical_update_pack_2026-06-28/"):
        return "archive", "archive", "superseded phase0 update pack archived as provenance"
    if rel in EXPLICIT_ARCHIVE_WORK:
        return "archive", "archive", "contract/reference source preserved for traceability"
    if "__pycache__" in parts or "swift-cache" in parts or suffix in JUNK_EXTS or path.name == ".DS_Store":
        return "drop-junk", "not-copied", "build/cache/junk; should not transfer"
    if rel.startswith("tmp/prodata-bak/"):
        return "raw-private-db-backup", "not-copied", "raw database backup; PII/auth by default"
    if rel.startswith("outputs/"):
        if suffix in RAW_EXTS:
            return "raw-private-output", "not-copied", "PRO DATA output/bulk data; private-local by default"
        return "outputs-review", "not-copied", "non-whitelisted output; requires separate review"
    if rel.startswith("work/"):
        return "work-review-private", "not-copied", "work artifact not explicitly promoted; review separately"
    return "unclassified-private", "not-copied", "not a git-candidate under current conservative rules"


def find_target_matches(src: Path, rel: str, bucket: str) -> list[Path]:
    name = src.name
    candidates = list(TARGET.rglob(name))
    if not candidates:
        return []
    # Prefer hash matches for files small enough to hash safely. Hashing these candidates is acceptable:
    src_hash = sha256(src) if src.stat().st_size <= 25_000_000 else ""
    if src_hash:
        matched = []
        for c in candidates:
            if c.is_file() and c.stat().st_size == src.stat().st_size:
                try:
                    if sha256(c) == src_hash:
                        matched.append(c)
                except Exception:
                    pass
        if matched:
            return matched
    return candidates


def main() -> None:
    rows: list[AuditRow] = []
    for path in sorted(p for p in SRC.rglob("*") if p.is_file()):
        rel = str(path.relative_to(SRC))
        source_class, bucket, reason = expected_target(rel, path)
        scan_mode, secret_flag, pii_flag = text_scan(path)
        matches = find_target_matches(path, rel, bucket) if bucket != "not-copied" else []
        if bucket == "not-copied":
            transfer_status = "not-copied-by-design"
        elif matches:
            transfer_status = "present-in-target"
        else:
            transfer_status = "missing-from-target"
        rows.append(
            AuditRow(
                source_rel=rel,
                size_bytes=path.stat().st_size,
                source_class=source_class,
                intended_bucket=bucket,
                transfer_status=transfer_status,
                target_matches=";".join(str(p.relative_to(TARGET)) for p in matches),
                scan_mode=scan_mode,
                secret_flag=secret_flag,
                pii_flag=pii_flag,
                reason=reason,
            )
        )

    out_csv = TARGET / "HE_FULL_SOURCE_TARGET_AUDIT.csv"
    out_md = TARGET / "HE_FULL_SOURCE_TARGET_AUDIT.md"
    with out_csv.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(AuditRow.__dataclass_fields__.keys()))
        writer.writeheader()
        for r in rows:
            writer.writerow(r.__dict__)

    by_status = Counter(r.transfer_status for r in rows)
    by_bucket = Counter(r.intended_bucket for r in rows)
    by_class = Counter(r.source_class for r in rows)
    missing = [r for r in rows if r.transfer_status == "missing-from-target"]
    flags = [r for r in rows if r.secret_flag == "yes" or r.pii_flag == "yes"]
    present = [r for r in rows if r.transfer_status == "present-in-target"]

    md = [
        "# HE Full Source -> Target Audit",
        "",
        f"Generated: {datetime.now().isoformat(timespec='seconds')}",
        "",
        "## Scope",
        "",
        f"- Source: `{SRC}`",
        f"- Target: `{TARGET}`",
        "- This is a conservative audit. Raw PRO DATA exports/backups are private by default and are not promoted to git.",
        "- Content scan is run for small text candidates; large/raw/binary files are classified by path/type/size and kept out of git.",
        "",
        "## Summary",
        "",
        f"- Source files audited: {len(rows)}",
        f"- Present in target: {len(present)}",
        f"- Missing from target among transfer candidates: {len(missing)}",
        f"- Secret/PII text flags: {len(flags)}",
        "",
        "### By transfer status",
        "",
    ]
    for k, v in sorted(by_status.items()):
        md.append(f"- `{k}`: {v}")
    md += ["", "### By intended bucket", ""]
    for k, v in sorted(by_bucket.items()):
        md.append(f"- `{k}`: {v}")
    md += ["", "### By source class", ""]
    for k, v in sorted(by_class.items()):
        md.append(f"- `{k}`: {v}")
    md += ["", "## Missing transfer candidates", ""]
    if missing:
        for r in missing[:200]:
            md.append(f"- `{r.source_rel}` -> `{r.intended_bucket}` ({r.reason})")
    else:
        md.append("- None. All whitelisted transfer candidates are present in target.")
    md += [
        "",
        "## Important notes",
        "",
        "- `not-copied-by-design` is not a failure; it includes raw DB backups, raw outputs, cache/junk, and unpromoted work artifacts.",
        "- `00_raw/codex/evidence-sanitized/` and `00_raw/codex/archive/` are review-local and gitignored until final split.",
        "- Final canonical docs must still cite source + reason and be reviewed before commit.",
        "",
        f"Full CSV: `{out_csv.name}`",
        "",
    ]
    out_md.write_text("\n".join(md), encoding="utf-8")
    print(f"wrote {out_md}")
    print(f"wrote {out_csv}")
    print(f"source_files={len(rows)} missing_candidates={len(missing)} flags={len(flags)}")


if __name__ == "__main__":
    main()
