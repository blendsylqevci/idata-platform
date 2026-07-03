# Faza 0 — Decision Gates / ADR Register (iData+)

**Versioni:** 1.2 · **Data:** 28 Qershor 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.5 §2, §11
**Format:** ADR (Architecture Decision Record). Status: **Accepted** (mbyllur) / **Open** (vendoset në Faza 0) / **Proposed**.
Çdo vendim i ri → kjo skedar + një rresht te SSOT Decisions Log.

## Të mbyllura (Accepted)
| ADR | Vendimi | Data | Shënim |
|---|---|---|---|
| ADR-001 | **Stack = Fortified Hybrid** (NestJS+Next.js+RN+Postgres+Supabase) | 27.06 | §2 |
| ADR-002 | **Mobile = native React Native/Expo; PWA e përjashtuar** | 27.06 | offline+BT+hub |
| ADR-003 | **Migrim = fillim i pastër; cutover 1 Jan 2027** (vetëm saldo hapëse + master-data) | 27.06 | §8 |
| ADR-004 | **Payroll = Faza 2** (jo v1) | 27.06 | §4 |
| ADR-005 | **COGS = default mesatare e ponderuar** + batch costing KABI (finalizim me kontabilistin) | 27.06 | §4 |
| ADR-006 | **Node = 24 LTS**, fiksuar me `.nvmrc` | 27.06 | Active LTS→2028 |
| ADR-007 | **RBAC pa "Fshirje" për financiare** → Anulim/Korrigjim + Aprovim | 27.06 | §9 |
| ADR-008 | **Ndarje rolesh + iCode Support = break-glass** | 27.06 | §9 |
| ADR-009 | **Konsignacion = shitje direkte me të drejtë kthimi** (qumështore) | 27.06 | njohja kontabël → pending |
| ADR-019 | **SEF Test Phase AKTIV** — "Testim para Aplikimit" pranuar; test server `fiskalizimi-test.atk-ks.org` | 28.06 | §7 / file 06 |
| ADR-020 | **Fiskalizimi GitHub = referencë e detyrueshme** (github.com/fiskalizimi: `models.proto`, ECDSA P-256, QR, **tax A/C/D/E**, **money per-field** items €0.0001/totals cents, **CouponType Sale=1/Cancel=2/Return=3**); **PoC SEF s'nis pa analizë** | 28.06 | file 06 |
| ADR-021 | **Secret management** — secrets store; asnjë sekret/SEF-ID/çelës në repo; audit redaction | 28.06 | §9 |
| ADR-022 | **Branch protection + GitHub 2FA** (PR-based, review i detyrueshëm, pa force-push) | 28.06 | §9 |
| ADR-023 | **Backup / PITR / restore drill** i detyrueshëm para Fazës 1 (promovon ADR-015) | 28.06 | §2 |

## Të hapura (Open — vendosen në Faza 0)
### ADR-010 · Supabase: hosted vs self-host EU
**Konteksti:** kërkohet rezidencë EU (06/L-082) + on-prem hub. **Opsione:** (a) Supabase managed EU; (b) self-host EU (Postgres + GoTrue/Storage). **Rekomandim paraprak:** managed EU për shpejtësi, me plan exit→self-host. **Pronari:** Blend+Claude Code. **Afati:** Faza 0.

### ADR-011 · Sync: PowerSync vs custom
**Konteksti:** offline-first, 60–80 konkurrentë, hub lokal. **Opsione:** PowerSync (gati) vs custom outbox+CDC. **Rekomandim:** vendoset **pas PoC-3**. **Pronari:** Claude Code. **Afati:** pas PoC-3.

### ADR-012 · GL posting: Postgres functions vs NestJS
**Konteksti:** atomicitet i postimit (debit=kredit). **Opsione:** (a) postim kritik në Postgres functions (atomik), orkestrim NestJS; (b) gjithçka NestJS me transaksione. **Rekomandim:** hibrid (a). **Pronari:** Codex+Claude Code. **Afati:** Faza 0 → provë në PoC-2.

### ADR-013 · DB e hub-it lokal: Postgres vs SQLite/embedded
**Konteksti:** hub PLANET/KABI offline. **Opsione:** Postgres lokal (Docker) vs SQLite/embedded për disa role. **Rekomandim:** Postgres në hub; SQLite në mobile. **Pronari:** Claude Code. **Afati:** pas PoC-4.

### ADR-014 · Connection pooling
**Opsione:** PgBouncer/Supavisor; pool sizing për 60–80 konkurrentë + hub. **Rekomandim:** pooler i dedikuar. **Afati:** Faza 0.

### ADR-015 · Backup / PITR / restore drill → **VENDOSUR (shih ADR-023)**
Sistem financiar — recovery i provuar (PITR, RPO/RTO, **restore drill** real) para Fazës 1. Status: **Accepted** (ADR-023).

### ADR-016 · RN/Expo build strategy
**Opsione:** EAS (managed) vs bare workflow. **Konsiderata:** printer BT, native modules, App Store/Play Store. **Rekomandim:** EAS fillimisht, bare nëse duhen module native të thella. **Afati:** Faza 0.

### ADR-017 · Printera fiskalë: hardware vs SEF software
**Konteksti:** Codex listoi drajverë fiskalë (Tremol/Datecs); me SEF software-fiskalizim mund të mjaftojë printer termik + nënshkrim software. **Veprim:** **konfirmo gjatë test phase AKTIVE (kanali SEF u hap), PARA blerjes së pajisjeve.** **Pronari:** Blend→ATK. **Afati:** test phase. (= SSOT pyetja #8)

### ADR-018 · Emri neutral i repo-s + tooling monorepo
**Opsione:** emër neutral (jo KABI/PLANET) p.sh. `idata-platform`; tooling pnpm + Turborepo/Nx. **Rekomandim:** `idata-platform` (privat) + pnpm workspaces + Turborepo. **Pronari:** Blend (krijim). **Afati:** Faza 0, dita 1.

---
## Përmbledhje statusi
- **Accepted:** 14 (ADR-001…009, 019…023).
- **Open në Faza 0:** 8 (ADR-010…014, 016…018) — secili me pronar + afat.
- Asnjë ADR pa status; asnjë kalim në Fazën 1 pa mbyllur ADR-et bllokuese (010–013, 018; backup = ADR-023 ✔).
