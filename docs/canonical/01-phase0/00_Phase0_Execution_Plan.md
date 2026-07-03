# Faza 0 — Plani i Ekzekutimit (iData+)

**Versioni:** 1.2 · **Data:** 28 Qershor 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.5
**Parim kryesor:** Faza 0 = **dokumentim + provë + setup**, **PA kod prodhimi**. Asnjë feature pa status; asnjë vendim pa pronar + afat.

---

## 1. Objektivi
Të çojmë projektin nga "~85–90% gati" në **100% gati për Fazën 1 (Foundation)** duke mbyllur paketën organizative dhe teknike: repo + board + plan + 5 PoC + RBAC + parity kontabël + data model + decision gates + nisja e SEF sandbox. Në fund të Fazës 0 dimë **me prova** se arkitektura qëndron.

## 2. Kohëzgjatja & ritmi
- **1–2 javë.** Pa kod biznesi. Lejohet vetëm kod "**Walking Skeleton**" (aplikacione bosh që dërgojnë "ping" nga mobili → hub → cloud) dhe PoC të izoluar.
- **Definition of Done** për çdo punë; provë (screenshot/log/test) e detyrueshme.

## 3. Workstream-et (paralele)

| # | Workstream | Përmbajtja | Pronari |
|---|---|---|---|
| A | Repo & CI | repo privat, emër neutral, monorepo, scaffold, CI bazë, branch protection | Blend (krijim) + Claude Code (skeleti) |
| B | Board & Docs | Notion/board me swimlanes; SSOT në repo; regjistrat | Blend (krijim) + Claude Code (përmbajtja) |
| C | 5 PoC | specifikimet + ekzekutimi + evidenca | Claude Code + Codex (verifikim) |
| D | SEF/ATK | **test phase AKTIV** → analizo github.com/fiskalizimi + Swagger; onboarding PKI (TEST); printer hw vs sw | Blend + Claude Code |
| E | Parity kontabël | Accounting Compatibility Matrix vs PRO DATA | Claude Code + Kontabilisti (validim) |
| F | RBAC & Data Model | matrica RBAC + drafti i modelit | Claude Code + Codex |
| G | Decision gates | regjistri ADR + vendimet ku mundemi | Codex + Blend |
| H | Mjedisi | Node 24 LTS (`.nvmrc`), pnpm, Docker, GH CLI, Expo, `.env.example` | Claude Code |

## 4. Deliverables (checklist)

| Deliverable | Skedari | Status |
|---|---|---|
| Plani i Fazës 0 | `00_Phase0_Execution_Plan.md` | ✅ ky dokument |
| 5 PoC specs | `01_PoC_Specs.md` | ✅ |
| RBAC matrix | `02_RBAC_Matrix.md` | ✅ |
| Accounting Compatibility Matrix | `03_Accounting_Compatibility_Matrix.md` | ✅ |
| Data Model draft | `04_Data_Model_Draft.md` | ✅ |
| Decision Gates (ADR) | `05_Decision_Gates_ADR.md` | ✅ |
| Feature Status Register | `Feature_Status_Register.md` | ✅ |
| Repo scaffold | `scaffold/README.md`, `AGENTS.md`, `CLAUDE.md`, `CONTRIBUTING.md` | ✅ |
| SEF test phase (intern) | `06_SEF_Test_Phase_Active.md` | ✅ AKTIV |
| Repo privat + board | GitHub + Notion | ⏳ Blend |
| SEF "Testim para Aplikimit" | ATK | ✅ PRANUAR 28.06.2026 |
| 5 PoC të kaluar me evidencë | repo `/poc` + raport | ⏳ Faza 0 |

## 5. Kriteret e daljes nga Faza 0 (DoD)
Faza 0 **mbyllet** kur:
1. Repo privat + monorepo skeleton + CI jeshile + branch protection.
2. Board me të gjitha tasket e Fazës 0 + swimlanes (Waiting Accountant/ATK/Factory).
3. **5 PoC të kaluar** me evidencë (ose dështimi i dokumentuar + korrigjim arkitekture).
4. **SEF test phase AKTIV** + GitHub Fiskalizimi i analizuar + onboarding/PoC në TEST të përgatitur.
5. Accounting Compatibility Matrix e plotësuar + e validuar nga kontabilisti për kolonat kyçe.
6. RBAC matrix + Data Model draft të aprovuara nga Blend/Codex.
7. Të gjitha decision gates kanë **status** (mbyllur / në pritje me afat).
8. Mjedisi i fiksuar (`.nvmrc`, pnpm, Docker, `.env.example`).

## 6. Sekuenca (rruga e propozuar)
1. **Repo privat + SSOT brenda repo-s** (Blend krijon; Claude Code shton scaffold).
2. **Board** + import i tasks/pyetjeve të hapura.
3. **Decision Gates + Data Model + RBAC + Accounting Matrix** (dokumentet — gati këtu).
4. **SEF (test phase AKTIV):** analizo `github.com/fiskalizimi` + `models.proto` + Swagger → onboarding PKI në TEST → PoC-5.
5. **5 PoC** një nga një (tenant isolation → accounting → offline → hub → SEF).
6. **Walking Skeleton** (mobile→hub→cloud ping) → pastaj Faza 1.

## 7. Rolet
- **Claude Code:** ndërton (scaffold, PoC, skeleton).
- **Codex:** PM / arkitekt / quality-gate (rishikon PR, teste, evidencë).
- **Blend:** porta njerëzore (SEF/GL/migrim/siguri), krijon repo/board/llogari, dërgon te ATK.
- **Kontabilisti / ATK / Fabrika:** përgjigjen pyetjeve përkatëse (swimlanes).

## 8. Rreziqet & zbutja
| Rrezik | Ndikimi | Zbutja |
|---|---|---|
| Onboarding/analizë SEF e vonuar | bllokon PoC5 + KABI live | **test phase AKTIV**; analizo github.com/fiskalizimi + onboard në TEST sa më shpejt; PoC5 në test server |
| Konfliktet offline (stok/çmim/kredi) | dëme financiare | rregullat në SSOT §2; PoC3 i provon para Fazës 1 |
| Parity i paplotë me PRO DATA | kontabilisti humb të dhëna | Accounting Compatibility Matrix + validim mujor paralel |
| Printera fiskalë hw vs sw i pavendosur | blerje e gabuar pajisjesh | pyetja #8 te ATK para çdo blerjeje |
| Floor-plan/inventar i munguar (PLANET) | numri i ekraneve KDS gabim | pyetjet #10/#11; finalizim pas vizitës |

---
*Çdo ndryshim → SSOT + Decisions Log. Ky plan qëndron në repo `docs/`.*
