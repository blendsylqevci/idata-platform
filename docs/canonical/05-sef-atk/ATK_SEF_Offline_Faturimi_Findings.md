# ATK / SEF - Faturimi offline dhe kerkesa per KABI

Status: **Supporting synthesis (historik, 24.06.2026)** — burimet/statuset AKTUALE = `ATK_Source_Register.md` v2.4 + `ATK_SEF_Requirements_Ledger.md` v1.7 + `ATK_Compliance_Gate.md` v2.0. **Korrigjim konteksti (12.07.2026):** rendi normal = **ONLINE-first** (transmeto→prano→shtyp); offline = **vetëm përjashtimi i Nenit 44**; afati 48h **nis nga momenti i ndërprerjes** (siç e thotë saktë §Neni 44 më poshtë).
Data: 2026-06-24
Burime:

- Portali zyrtar SEF i ATK: https://apps.atk-ks.org/sefaplikimi
- Udhëzim Administrativ (MF) Nr. 01/2026 për Shfrytëzimin e PEF/SF/SEF, i publikuar në portalin SEF.
- Specifikat e implementimit SEF / GitHub Fiskalizimi, i linkuar nga portali zyrtar SEF.

## Vendimi praktik

Po, sipas dokumentacionit zyrtar, ne rast te nderprerjes se lidhjes elektronike/internetit, tatimpaguesi mund te vazhdoje te leshoje kuponë fiskalë ne modalitet jashtë linje/offline.

Por kjo nuk do te thote "fature e lire offline pa kontroll". Kuponi offline duhet te ruhet, te kete te dhenat e detyrueshme, te gjenerohet me rregulla SEF dhe te fiskalizohet/dorezohet ne ATK pasi te rikthehet lidhja.

## Rregulli kryesor nga UA MF 01/2026

Neni 44 - Nderprerja e lidhjes elektronike/internetit:

- ne rast nderprerjeje interneti, tatimpaguesi leshon kuponë fiskalë me te dhenat e detyrueshme;
- brenda 48 oreve nga momenti i nderprerjes duhet te vendose lidhje interneti dhe t'ia dorezoje ATK-se te gjithe kuponet e leshuar offline;
- kuponet konsiderohen te fiskalizuar sakte nese procesi kryhet brenda ketij afati;
- nese nuk mund te rivendoset interneti brenda afatit, tatimpaguesi duhet te njoftoje ATK-ne dhe te dergoje prova/dokumentacion;
- kur lidhja kthehet, duhet te fiskalizohen te gjithe kuponet e leshuar offline;
- nese nderprerja ka ndodhur gjate muajit dhe rilidhja nuk eshte bere deri me daten 10 te muajit vijues, tatimpaguesi paraqet te gjithe kuponet e pafiskalizuar ne ATK.

## Nese ndalon plotesisht pajisja

Neni 45 dallon rastin kur nuk eshte vetem interneti, por ndalon plotesisht pajisja per fiskalizim.

Ne kete rast perdoret bllok i vecante i kuponave fiskale dhe pastaj regjistrohen/fiskalizohen ne SEF sipas afateve te UA.

Ky eshte rast tjeter nga "internet offline"; nuk duhet te perzihet me outbox-in normal te software-it.

## Kerkesa per arkitekturen e KABI

Per ambulantin/shitjen ne terren, sistemi duhet te kete:

1. Faturim/kupon offline ne pajisje.
2. Numerim unik dhe idempotence per cdo kupon.
3. Gjenerim te te dhenave te domosdoshme te kuponit sipas SEF.
4. Nenshkrim/dokument fiskal sipas specifikave SEF.
5. Local fiscal outbox/queue.
6. Status per cdo kupon:
   - draft
   - leshuar offline
   - ne pritje per fiskalizim
   - fiskalizuar
   - deshtuar
   - kerkon nderhyrje
7. Retry/dërgim automatik kur kthehet interneti *(kërkesë target — sot vetëm drain MANUAL mock; scheduler production = MISSING/T2-06)*.
8. Monitorim i afatit 48h.
9. Alarm per menaxher/kontabilist para shkeljes se afatit.
10. Raport ditor i kuponave te pafiskalizuar.
11. Audit log per cdo tentim fiskalizimi.
12. Mekanizem qe parandalon dublimin e kuponave ne retry.

## Cfare nuk duhet te supozohet

- Nuk duhet te supozohet qe cdo dokument offline eshte automatikisht i fiskalizuar.
- Nuk duhet te supozohet qe mund te rrije pafundesisht offline pa njoftim/raportim.
- Nuk duhet te supozohet qe aplikacioni i zakonshem mund ta zevendesoje pajisjen/procesin fiskal pa certifikim SEF.
- Nuk duhet te leshohet fature shitjeje reale e cila nuk hyn ne queue fiskalizimi.

## Pyetje qe mbeten per certifikim/testim

Keto duhet te sqarohen ne sandbox/SEF dhe me ATK/ofrues fiskal:

1. Cfare fusha saktesisht duhet te kete kuponi offline ne print/PDF?
2. Si gjenerohet QR kur kuponi ende nuk eshte pranuar nga ATK?
3. A lejohet printimi ne printer mobil nga ambulanti?
4. ~~A duhet secila pajisje mobile/terminal te kete POS ID dhe PKI key pair te vetin?~~ **[MBYLLUR 12.07.2026: PO — keypair unik per POS/makinë + certifikatë per instalim SEF; çelësi privat s'largohet nga makina (Gate v2 §3.9; UA 34; T2-05). S'është më pyetje e hapur.]**
5. Si regjistrohet branch/vehicle/ambulant ne SEF?
6. Si trajtohen kthimet dhe anulimet offline?
7. Cfare ndodh nese kuponi offline deshton ne fiskalizim pas rikthimit te lidhjes?

## Konkluzion per KABI

Per KABI duhet ta dizajnojme faturimin offline si proces fiskal serioz:

`Issue offline coupon -> store locally -> print/send to customer -> fiscal outbox -> sync to ATK within legal deadline -> store transaction id/status -> report exceptions`

Ky modul eshte kritik per MVP nese ambulanti duhet te shese/faturoje ne terren pa internet.
