# Finance Operations — PLANET + KABI

**Data:** 2026-06-29
**Status:** canonical working document
**Burimi:** PRO DATA backup audit + DDL/DML cross-check + sanitized micro-extract
**Qëllimi:** të ndajë qartë financat operative nga kontabiliteti zyrtar, që vajza e financave/menaxhmenti dhe kontabilisti të kenë workflow të pastër pa përzierje rolesh.

**Workflow spec:** `Finance_Operations_Workflow_Spec_2026-06-29.md`

---

## 1. Vendimi kryesor

Në iData+ duhet të ekzistojnë dy shtresa të ndara:

1. **Finance Operations** — kontrolli ditor i parasë, POS terminaleve, saldo/float, kamarierëve, shpenzimeve, pagesave furnitorëve, dorëzimit cash, raportit ditor dhe barazimit operativ.
2. **Accounting** — GL, TVSH, libra tatimorë, AR/AP, mbyllje periudhe, pasqyra financiare dhe postime zyrtare.

Finance Operations nuk zëvendëson kontabilistin. Finance Operations prodhon evidence të pastër dhe të aprovuar; Accounting e konsumon dhe e poston zyrtarisht.

---

## 2. Çfarë kontrolluam nga backup-i

U kryen tri shtresa kontrolli:

- **Backup coverage audit** — a kemi data reale dhe tabela relevante për financat operative?
- **DDL/DML cross-check** — a ekzistojnë tabelat në DDL dhe a referohen/mutohen nga procedura, views, functions ose triggers?
- **Sanitized micro-extract** — a kishte tabela të vogla që ia vlenin të nxirreshin për zero-doubt evidence?

Evidence kryesore:

- `outputs/finance_operations_backup_audit_2026-06-29/FINANCE_OPERATIONS_Backup_Coverage_Audit.md`
- `outputs/finance_operations_ddl_dml_crosscheck_2026-06-29/FINANCE_OPERATIONS_DDL_DML_Crosscheck.md`
- `outputs/finance_operations_micro_extract_2026-06-29/FINANCE_OPERATIONS_Micro_Extract_Review.md`

---

## 3. Rezultati i DDL/DML cross-check

Kontrolli teknik i DDL/DML nuk gjeti mungesë të papritur DDL-je për objektet finance-ops.

| Metrikë | Rezultat |
| --- | ---: |
| Finance tables checked | 104 |
| DDL/DML references | 3382 |
| Review rows | 61 |
| `ddl_missing_unexpected` | 0 |

Verdiktet:

| Verdict | Count | Kuptimi |
| --- | ---: | --- |
| `covered_or_low_risk` | 43 | Ka evidence/data ose rrezik të ulët; përdoret për parity/dizajn. |
| `micro_extract_candidate` | 4 | Tabela të vogla që u nxorën me sanitization për evidence shtesë. |
| `process_gap_confirm_with_planet` | 4 | Nuk është gap ekstraktimi; kërkon konfirmim live të workflow-it. |
| `schema_only_with_logic_reference` | 53 | Ka DDL/procedura por zero rows; përdoret si sinjal dizajni, jo migrim real. |

Operacionet e gjetura në DDL/DML:

| Operation | References |
| --- | ---: |
| READ | 3182 |
| INSERT | 252 |
| UPDATE | 222 |
| DELETE | 256 |
| REFERENCE | 12 |

---

## 4. Micro-extract i sanitizuar

U nxorën dhe u sanitizuan 4 tabela:

| Database | Table | Rows | Pse ka rëndësi |
| --- | --- | ---: | --- |
| PRODATA_PLANET2 | `tblDocBlerjetShpenzimetTjera` | 4 | Shpenzime/kosto shtesë të lidhura me blerjet. |
| PRODATA_PLANET2 | `tblDocPagesatAvanset` | 1 | Semantikë e avansit/parapagimit. |
| PRODATA_PLANET2 | `tblUserMenyratPagesa` | 2102 | Lidhje user → mënyrë pagese; ndihmon RBAC/payment-method permissions. |
| PRODATA_PLANET_RESORT | `tblUserMenyratPagesa` | 4423 | Lidhje user → mënyrë pagese për subjektin tjetër. |

Këto nuk janë për migrim final të userëve/password/PIN. Janë evidence për modelin e iData+.

---

## 5. Çfarë kemi të mjaftueshme për PLANET

Nga backup-i kemi bazë të fortë për:

- mënyrat e pagesës dhe bankat/POS terminalet;
- POS sales financial source;
- POS payments;
- pagesa/arkëtime;
- fatura hyrëse dhe shpenzime shtesë të blerjes;
- ekstrakte bankare dhe reconciliation logic si DDL/procedure reference;
- audit/correction/deleted objects si sinjal që iData+ duhet të ketë reversal/audit, jo fshirje të thjeshtë;
- user payment-method permission model si evidence për RBAC;
- raporte POS/arka/barazim si procedure evidence.

Për PLANET, backup-i na jep mjaftueshëm për të ndërtuar Finance Operations MVP, por barazimi real duhet të validohet live sepse tabelat `tblDocPOSBarazimiMaster` dhe `tblDocPOSBarazimiDetalet` janë zero-row.

---

## 6. Çfarë duhet të implementohet në iData+

### 6.1 Finance dashboard

Duhet të tregojë:

- cash në zyrë;
- cash për saldo/float;
- cash i pritur nga kamarierët/arkatarët;
- pagesa me kartë sipas bankës/POS terminalit;
- shpenzime ditore;
- pagesa furnitorëve;
- diferenca;
- kush ka marrë para nga zyra, sa, kur dhe me çfarë arsye.

### 6.2 Barazimi i kamarierit / arkatarit

Për çdo punëtor duhet të shihet:

- tavolinat që i ka hapur;
- porositë brenda secilës tavolinë;
- pagesat cash/kartë;
- zbritjet/anulimet me PIN/aprovim;
- saldo fillestare;
- cash i pritur;
- cash i dorëzuar;
- diferenca;
- aprovuesi dhe arsyeja në rast korrigjimi.

### 6.3 Business day / service day

PLANET ka punë pas mesnatës. Sistemi duhet të dallojë:

- `calendar_date` — data reale;
- `business_day` / `service_day` — dita operative e restorantit.

Mbyllja automatike në 00:00 duhet të specifikohet qartë: porositë/faturat pas mesnatës hyjnë në saldo të re sipas vendimit të biznesit.

### 6.4 Payment methods dhe POS terminals

Duhet të ketë:

- cash;
- kartë/POS bankar;
- bankë;
- borxh/kredi;
- mënyra të tjera vetëm nëse aprovohen;
- lidhje pagesë → bankë/POS terminal;
- leje për përdorues se cilat mënyra pagese mund t’i përdorë.

### 6.5 Expenses dhe cash withdrawals

Çdo shpenzim nga arka/zyra duhet të ketë:

- shumën;
- datën;
- kategorinë;
- arsyen;
- personin që i mori paratë;
- aprovuesin;
- dokument/foto nëse ka;
- lidhje me Finance Operations dhe, kur duhet, me Accounting.

### 6.6 Handover për Accounting

Finance Operations duhet të përgatisë paketën ditore:

- raport ditor cash/card/bank;
- listë pagesash;
- listë shpenzimesh;
- diferenca;
- faturat/kuponët;
- evidence të aprovimeve.

Accounting e merr këtë paketë për postim/rakordim, por nuk e bën punën operative të vajzës së financave.

---

## 7. Çfarë mbetet për validim live me PLANET

Këto nuk dalin 100% nga backup-i dhe duhet pyetur vajza e financave / Donati / menaxherët:

1. Si hapet dita financiare dhe kush e hap?
2. Kush ia jep saldon/float kamarierit/arkatarit?
3. A ka saldo fikse për secilin punëtor apo ndryshon sipas ndërrimit?
4. Çfarë ndodh me tavolinat e hapura pas mesnatës?
5. Si ndahet cash nga pagesat me kartë/POS bankar?
6. A ka më shumë se një POS terminal bankar dhe si ndahet raporti?
7. Si regjistrohen shpenzimet ditore?
8. Kush merr cash nga zyra dhe si dokumentohet arsyeja?
9. Si trajtohen diferencat: mungesë cash, tepricë cash, pagesë gabim?
10. A bëhet barazim për kamarier, për arkatar, për shank, për lokacion, apo për të gjitha?
11. Cilat raporte i përdor vajza e financave çdo ditë?
12. Cilat raporte i dërgohen kontabilistit?
13. Çfarë duhet të mos lejohet pa PIN/aprovim?
14. A përdoret aktualisht ndonjë barazim në PRO DATA apo bëhet jashtë sistemit/manual?

---

## 8. KABI Finance Operations

Backup-i i PLANET-it ndihmon si model, por nuk mjafton për KABI. Për KABI duhet workflow i veçantë:

- barazimi i shoferit/ambulantit;
- ngarkim automjeti → shitje → kthime → cash/kredi → mbyllje ditore;
- avanse fermerësh;
- pagesa mujore fermerësh;
- gajbe/ambalazh kthyes;
- klientë me afat pagese;
- bllokim kredie;
- POS/SEF offline në terren.

Parimi është i njëjtë: Finance Operations prodhon evidence, Accounting e poston/rakordon.

---

## 9. Rregulla që nuk duhet të thyhen

- Asnjë fshirje reale e dokumenteve financiare; vetëm anulim/korrigjim/reversal me audit.
- Çdo diferencë cash/card/bank duhet të ketë arsye dhe aprovues.
- Çdo veprim sensitiv kërkon permission + audit log.
- Userët/password/PIN nga PRO DATA nuk migrohen.
- Tabelat zero-row nuk përdoren si fakt historik, vetëm si sinjal dizajni.
- Barazimi i PLANET-it nuk supozohet nga backup-i; validohet live.

---

## 10. Vendimi për fazim

Për MVP të PLANET/KABI, Finance Operations duhet të jetë pjesë e platformës, por me kufij:

- **MVP:** daily cash/card/bank control, worker settlement, payment method permissions, expenses, handover to accounting, audit/approval.
- **Later:** AI anomaly detection, advanced forecasting, automated bank import nëse nuk është i nevojshëm për go-live.
- **Requires PLANET confirmation:** exact settlement workflow, business-day cutoff, multi-shank/multi-terminal process.
- **Requires KABI confirmation:** van-sales settlement, farmer advances/payment cycle, gajbe, route-level cash/credit rules.

Ky dokument duhet të qëndrojë i lidhur me Accounting Compatibility Matrix dhe RBAC Matrix, sepse Finance Operations prek të dyja.
