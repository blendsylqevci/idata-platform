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
Një pre-commit guard (`scripts/precommit_confidential_guard.sh`) bllokon commit-et me përmbajtje konfidenciale. Instalim lokal:

```
ln -sf ../../scripts/precommit_confidential_guard.sh .git/hooks/pre-commit
```

## Dokumentacioni
Shih `docs/INDEX.md`.

---
© Të gjitha të drejtat e rezervuara. E palicencuar për përdorim publik ose rishpërndarje.
