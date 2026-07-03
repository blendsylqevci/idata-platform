# Dokumentet e projektit KABI + PLANET

Ky folder nuk duhet të trajtohet si grumbull dokumentesh të rastësishme. Ka hierarki të qartë.

## Burimi kryesor

### `Audit_Permbledhje_Per_Aprovim_KABI_PLANET_DataPlus_2026-06-24.md`

Ky është dokumenti kryesor për planifikim para zhvillimit.

Përdoret për:

- vendimet kryesore;
- scope;
- arkitekturë;
- faza;
- rreziqe;
- çfarë është gati;
- çfarë pret përgjigje;
- kriteret e pranimit.

Nëse del informacion i ri që ndryshon vendim, scope, fazë, risk ose kriter pranimi, përditësohet ky dokument.

## Evidence / reference teknike

### `PRODATA_Backup_Extraction_2026-06-27.md`

Ky dokument shpjegon materialin real të nxjerrë nga backup-i i PRO DATA për PLANET.

Folderi i të dhënave:

- `outputs/prodata_reports_extracted_2026-06-27/`

Përdoret për:

- compatibility matrix PLANET ↔ PRO DATA;
- Libri i Shitjes;
- Libri i Blerjes;
- TVSH;
- GL;
- master-data;
- POS/stok;
- validim paralel me muaj real.

Ky nuk e zëvendëson dokumentin master; e furnizon master-in me prova teknike.

### `Finance_Operations_PLANET_KABI_2026-06-29.md`

Dokument canonical për ndarjen **Finance Operations** nga **Accounting**.

Përdoret për:

- barazimin ditor të kamarierëve/arkës;
- cash/card/bank/POS terminale;
- saldo/float;
- shpenzime operative;
- payment-method permissions;
- handover të financave drejt kontabilistit;
- kufirin e saktë mes vajzës së financave/menaxhmentit dhe kontabilistit.

Ky dokument mbështetet në backup coverage audit, DDL/DML cross-check dhe micro-extract të sanitizuar. Nëse PLANET konfirmon workflow të ri live, përditësohet këtu dhe pastaj reflektohet në master/SSOT.

### `Finance_Operations_Workflow_Spec_2026-06-29.md`

Specifikimi i ekranit/workflow-it për Finance Operations.

Përdoret për:

- Daily Finance Dashboard;
- business day / service day;
- saldo/float;
- barazimin e kamarierit/arkatarit;
- cash office;
- POS terminal reconciliation;
- shpenzime dhe withdrawals;
- handover te kontabilisti;
- approvals/audit;
- acceptance criteria për implementim.

### `PRODATA_Evidence_Traceability_Matrix_2026-06-29.md`

Matrix canonical që lidh çdo modul/feature me evidence konkrete nga backup-i ose me status `requires live confirmation`.

Përdoret për:

- të mos ndërtohet feature pa burim;
- të dallohen gjërat evidence-backed nga supozimet;
- të ruhet PRO DATA si golden reference gjatë development/UAT/cutover;
- të ndihmojë Claude Code/Codex të mos shpikin logjikë jashtë evidence.

### `Accounting_10_10_Hardening_Checklist_2026-06-29.md`

Checklist për ta ngritur kontabilitetin në nivel 10/10.

Përdoret për:

- posting rules;
- invariants debit=kredit;
- VAT books/declaration;
- PLANET parity gates;
- KABI accounting gates;
- report pack;
- UAT/parallel month;
- accountant sign-off.

### `Accounting_Posting_Rules_v0_2026-06-29.md`

Dokumenti i parë i implementimit për motorin e postimit.

Përdoret për:

- SHV / shitje;
- POS / restaurant sale;
- BLV/BLS / blerje;
- PGH/PGD / pagesa;
- COGS/stok;
- credit note/correction;
- manual journal;
- ndarjen mes posting engine dhe tax-book engine.

### `Accounting_Posting_Test_Fixtures_v0_2026-06-29.md`

Test pack minimal për kontabilitetin.

Përdoret për:

- fixtures document → GL;
- debit=kredit invariant;
- idempotency;
- tax-book consistency;
- subledger/open-item allocation;
- period-close behavior;
- finance handover reconciliation;
- parallel month validation.

### `VAT_Book_Mapping_v0_2026-06-29.md`

Mapping canonical për Librin e Shitjes, Librin e Blerjes dhe VAT summary.

Përdoret për:

- tax-book data model;
- sales buckets 18%/8%/exempt/export/reverse-charge/notes;
- purchase buckets local/import/investive/non-deductible/farmer/reverse-charge/notes;
- mixed VAT invoices;
- VAT summary output-input;
- accountant update 2026-06-30: qershori ende jo zyrtar, PLANET sales 18%, mixed VAT purchase confirmed;
- KABI farmer/product VAT gates;
- accountant review pack.

### `Accountant_Review_Pack_v0_2026-06-29.md`

Paketa e pyetjeve/konfirmimeve për kontabilistin.

Përdoret për:

- krahasimin me një muaj të mbyllur zyrtarisht; qershori 2026 përsëritet pas deklarimit;
- konfirmimin e tax codes dhe llogarive të TVSH-së;
- finalizimin e Librit të Shitjes/Blerjes;
- product VAT për KABI;
- farmer flat-rate / box 51;
- përgjigjen fillestare të kontabilistit më 2026-06-30;
- COGS dhe vlerësimin e inventarit;
- inter-company KABI → PLANET;
- returns/write-off/skadenca;
- sign-off para freezing të accounting rules.

### `Outstanding_Confirmations_Tracker_2026-06-29.md`

Tracker canonical për të gjitha konfirmimet e jashtme që mbeten hapur.

Përdoret për:

- kontabilistin;
- financat e PLANET-it;
- fabrikën KABI;
- ATK/SEF;
- hardware/printera/pajisje;
- strukturën `RESORT` / business unit;
- repo/documentation setup;
- statusin `open/sent/answered/approved/closed`;
- pronarin, prioritetin, evidence dhe çfarë zhbllokon çdo përgjigje.

Ky është vendi ku ndiqet çfarë presim nga njerëzit jashtë zhvillimit, që development-i të mos ecë me hamendje.

## Dokumente pune

### `KABI_Logjika_Operacionale_Kontabiliteti_Draft.md`

Draft për kuptimin real të KABI-t: qumësht, fermerë, tank/pool, prodhim, batch/LOT, ambulant, raft-control, kontabilitet.

Kur KABI jep përgjigje të reja, përditësohet ky dokument. Nëse përgjigjja ndryshon scope/fazë/risk, përditësohet edhe master-i.

### `KABI_Pyetje_Per_Fabrike_Dhe_Kontabilist.md`

Lista e pyetjeve për fabrikën dhe kontabilistin.

Nuk është dokument final; është log pune për vendime që presin përgjigje.

### `ATK_SEF_Offline_Faturimi_Findings.md`

Findings për ATK/SEF, offline fiscalization, 48h, sandbox/testim dhe aplikim.

Nëse ATK jep përgjigje të re zyrtare, përditësohet këtu dhe në master.

### `Arkitektura_Offline_Online_Stack_Draft.md`

Draft teknik për stack, offline/online, local hub, sync/outbox, Supabase/NestJS/Postgres.

Vendimet finale teknike reflektohen në master.

## Rregulli i organizimit

- Master-i tregon çfarë kemi vendosur.
- Dokumentet e punës tregojnë si kemi arritur aty dhe çfarë po presim.
- Evidence pack mban prova reale/data/backup/eksporte.
- Asnjë dokument nuk duhet të ketë vendim final që nuk është reflektuar në master.

Kjo strukturë e mban projektin profesional dhe të lexueshëm edhe kur të fillojë zhvillimi.
