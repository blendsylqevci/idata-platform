#!/usr/bin/env python3
"""Move copied intake files with secret-like patterns into gitignored quarantine."""

from __future__ import annotations

import argparse
import re
import shutil
from pathlib import Path


PATTERNS = [
    re.compile(r"-----BEGIN [A-Z ]*(PRIVATE )?KEY-----", re.I),
    re.compile(r"PRIVATE KEY", re.I),
    re.compile(r"\b(?:password|passwd|pwd)\s*[:=]", re.I),
    re.compile(r"\b(?:api[_-]?key|access[_-]?key)\s*[:=]", re.I),
    re.compile(r"connection string", re.I),
    re.compile(r"\b(?:user id|uid)\s*=", re.I),
]


def has_match(path: Path) -> bool:
    try:
        data = path.read_bytes()
    except OSError:
        return True
    texts: list[str] = []
    for enc in ("utf-8", "utf-16", "utf-16-le", "latin-1"):
        try:
            texts.append(data.decode(enc, errors="ignore"))
        except Exception:
            pass
    return any(pattern.search(text) for text in texts for pattern in PATTERNS)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--target", required=True)
    args = parser.parse_args()

    target = Path(args.target).resolve()
    safe_root = target / "00_raw" / "codex" / "evidence-sanitized"
    quarantine_root = target / "00_raw" / "codex" / "evidence-private-local" / "quarantine-secret-patterns"
    moved: list[tuple[Path, Path]] = []

    if safe_root.exists():
        for path in sorted(p for p in safe_root.rglob("*") if p.is_file()):
            if has_match(path):
                rel = path.relative_to(safe_root)
                dest = quarantine_root / rel
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.move(str(path), str(dest))
                moved.append((path, dest))

    review = target / "00_raw" / "codex" / "HE_INTAKE_REVIEW.md"
    if review.exists():
        existing = review.read_text(encoding="utf-8")
        note = [
            "",
            "## Quarantine after safety verification",
            "",
            f"- Files moved from `evidence-sanitized` to gitignored `evidence-private-local/quarantine-secret-patterns`: **{len(moved)}**",
            "- Reason: secret-like text patterns in SQL/procedure evidence. Treat as private until manually reviewed.",
            "",
        ]
        marker = "## Quarantine after safety verification"
        if marker not in existing:
            review.write_text(existing.rstrip() + "\n" + "\n".join(note), encoding="utf-8")

    for src, dest in moved:
        print(f"{src.relative_to(target)} -> {dest.relative_to(target)}")
    print(f"moved={len(moved)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
