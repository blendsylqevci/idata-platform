# Arkitektura Offline/Online, Stack dhe Pajisjet (Draft vendimesh)

Status: Drejtim i aprovuar per dokumentim, me disa pika qe kerkojne verifikim teknik/ATK
Data: 2026-06-24
Qellimi: te definohet si duhet te funksionoje platforma KABI + PLANET para se te filloje dokumentimi baze dhe zhvillimi.

Ky dokument nuk eshte implementim final. Eshte vendim arkitekturor paraprak qe na mbron nga supozimet e gabuara.

## 1. Vendimi kryesor

Platforma nuk duhet te jete vetem web dhe nuk duhet te jete vetem Electron.

Vendimi i aprovuar:

- backoffice, administrata, kontabiliteti dhe raportet: web app;
- KABI terren/ambulant/merchandising: mobile app native/cross-platform, default React Native/Expo;
- PLANET KDS dhe dashboard: web/PWA;
- PLANET POS: web/PWA si UI kryesore + local hardware gateway/hub;
- Electron nuk eshte baza e krejt platformes; perdoret vetem nese duhet per hardware bridge, local gateway ose paketim desktop;
- databaza qendrore: PostgreSQL;
- databaza lokale ne pajisje: SQLite;
- files/foto/PDF/dokumente: object storage;
- arkitektura fillestare: modular monolith, jo microservices.

## 2. Parimi i pergjithshem online/offline

Sistemi duhet te funksionoje me kete model:

1. Cloud/serveri qendror eshte source-of-truth final.
2. Pajisjet lokale punojne me kopje te kontrolluar te te dhenave.
3. Cdo veprim offline ruhet lokalisht me UUID dhe status.
4. Sinkronizimi behet me idempotence per te parandaluar dublimet.
5. Fiskalizimi ka outbox te vecante dhe audit te vecante; nuk trajtohet si sync i thjeshte.
6. Kur interneti kthehet, te dhenat ngarkohen ne server, validohen, fiskalizohen/postohen dhe statusi kthehet ne pajisje.

## 3. KABI - modeli offline/online

KABI ka nevoje per offline te forte per ambulantin dhe kontrollin e rafteve.

### Para nisjes se punetorit ne terren

Pajisja duhet te marre sync me:

- user/device permissions;
- ruten e dites;
- klientet dhe lokacionet;
- marreveshjet/cmimet aktive;
- limitet e kreditit dhe borxhet snapshot;
- artikujt aktive;
- TVSH per artikuj;
- stoku i automjetit;
- LOT/batch dhe afatet e skadences;
- rregullat e zbritjes/autorizimit;
- konfigurimi fiskal;
- numrat/serite e dokumenteve qe lejohen offline.

### Gjate punes offline

Pajisja duhet te mundesoje:

- shitje/fature/kupon sipas rregullave ligjore;
- fiscal outbox;
- kthime;
- pagesa cash;
- porosi;
- POD;
- foto nga kamera;
- GPS automatik;
- kontroll rafti;
- diferenca dhe komente;
- ruajtje e te gjitha veprimeve ne SQLite/event log lokal.

### Pas rikthimit te internetit

Procesi:

1. mobile outbox dergon veprimet ne server;
2. serveri kontrollon UUID/idempotence;
3. serveri validon user/device/business rules;
4. serveri poston stok/AR/cash/GL/TVSH kur dokumenti lejohet;
5. fiscal outbox dergon dokumentet fiskale ne SEF/ATK;
6. statuset kthehen ne pajisje;
7. gabimet hyjne ne queue per nderhyrje manuale dhe audit.

### Rregull kunder konflikteve

Stoku i automjetit duhet te kete ownership.

Kur malli ngarkohet ne automjet:

- sasia largohet nga available stock i depos;
- ruhet si vehicle stock i atij automjeti;
- LOT-i dhe afati ruhen per automjet;
- askush tjeter nuk mund ta shese te njejten sasi;
- mbyllja ditore barazon loaded, sold, returned, remaining dhe differences.

Ky model e ul konfliktin kur ambulanti punon offline.

## 4. PLANET - modeli offline/online

PLANET ka nevoje per offline lokal ne restorant, sidomos per POS/KDS.

Vendimi paraprak:

- POS UI: web/PWA;
- local hub/gateway: mini-PC/NUC ne lokacion;
- KDS: web/PWA ne tableta/ekrana;
- printer backup: lidhet permes hub/gateway;
- Electron perdoret vetem nese gateway/hardware bridge kerkon paketim desktop.

### Pse local hub eshte kritik per PLANET

Restoranti nuk duhet te ndalet kur bie interneti.

Hub-i lokal duhet te mundesoje:

- komunikim lokal POS -> KDS;
- komunikim kamarier -> POS/KDS brenda rrjetit lokal;
- printim backup ne kuzhine/bar;
- ruajtje te porosive aktive;
- queue per sinkronizim me cloud;
- health check per ekranet/printeret;
- vazhdim te sherbimit kur cloud/interneti nuk eshte i arritshem.

Per PLANET, hub-i lokal trajtohet si pothuajse i detyrueshem.

## 5. App i kamarierit - decision gate

Kjo pike nuk mbyllet pa vendim me PLANET.

Opsioni A: React Native/Expo

- me i forte per offline;
- me i mire per pajisje te kontrolluara nga biznesi;
- me i mire per njoftime, kamera, skanim dhe stabilitet;
- kerkon instalim dhe menaxhim app-i.

Opsioni B: PWA

- me i lehte per BYOD/telefon personal;
- pa instalim nga store;
- nje kodbaze me web;
- kufizime me hardware/offline/background ne disa pajisje.

Default profesional: React Native/Expo nese biznesi i kontrollon pajisjet; PWA nese PLANET insiston ne BYOD pa instalim.

## 6. Databaza dhe storage

### Qendrore

PostgreSQL eshte databaza qendrore.

Arsye:

- transaksione serioze;
- foreign keys dhe integritet;
- e pershtatshme per kontabilitet/stok/GL;
- multi-tenant data model;
- audit/event log;
- raportim financiar.

### Lokale

SQLite eshte databaza lokale per:

- KABI mobile app;
- app/pajisje offline;
- gateway/hub lokal nese duhet;
- cache operative.

IndexedDB perdoret vetem per PWA te lehta ose cache browser-i, jo si baze kryesore per fiskalizim/ambulant.

### Files

Foto, PDF, fatura te skanuara, dokumente doganore, POD, foto rafte dhe attachment-e duhet te ruhen ne object storage.

Ne databaze ruhen metadata, links, hash, owner, document type dhe audit.

## 7. Sync engine

Vendimi: nuk duhet te shkruajme sync engine komplet nga zero pa analizuar zgjidhje te provuara.

Kandidatet:

- PowerSync;
- WatermelonDB;
- custom sync vetem nese zgjidhjet ekzistuese nuk i mbulojne kerkesat.

Default per vleresim: PowerSync, sepse eshte i orientuar per SQLite local + backend database sync dhe ka mbeshtetje per React Native/Expo dhe Postgres.

Kujdes:

- sync engine nuk e zevendeson fiscal outbox;
- fiskalizimi ka rregulla ligjore, status, nenshkrim, audit dhe afate;
- conflict resolution duhet te dizajnohet sipas biznesit, sidomos stok/LOT/fatura.

## 8. Fiscal outbox dhe SEF/ATK

Fiskalizimi eshte pjese e ndare dhe kritike.

Fiscal outbox duhet te kete:

- UUID dokumenti;
- numerim te kontrolluar;
- status fiskal;
- QR/data per kupon;
- nenshkrim/digital signing sipas SEF;
- retry;
- idempotence;
- audit log per cdo tentim;
- alert 48h per kuponet offline;
- failure handling;
- raport te kuponave te pafiskalizuar.

Nuk lejohet qe nje shitje reale te mbetet vetem "lokale" pa gjurme fiskale.

### Bllokues per verifikim

Para implementimit final duhet te verifikohet ne SEF sandbox/ATK/ofrues fiskal:

- a mbulohet aplikacioni mobile si POS/SEF;
- a duhet POS ID per secilen pajisje mobile;
- a duhet cift PKI per secilen pajisje/terminal;
- si merret dhe lidhet Application ID;
- si printohet kuponi me QR nga printer mobil;
- cfare ndodh kur kuponi offline fiskalizohet pas rikthimit te internetit;
- si trajtohen anulimet/kthimet offline;
- a lejohet modeli mobile printer + app mobile per ambulant.

Kjo nuk duhet te supozohet. Duhet provuar para zhvillimit final te faturimit fiskal.

## 9. Stack i propozuar

Ky eshte drejtim teknik, jo dokument marrëveshjeje finale:

- language: TypeScript;
- backend: NestJS ose framework ekuivalent TypeScript modular;
- frontend web: React/Next.js;
- mobile: React Native/Expo;
- POS/KDS web: React/Next.js PWA;
- desktop/gateway: Node.js service ose Electron vetem kur duhet UI/hardware bridge;
- central DB: PostgreSQL;
- local DB: SQLite;
- queue/background jobs: sistem queue i kontrolluar;
- object storage: S3-compatible;
- realtime: WebSocket/SSE per KDS, alerts dhe status;
- auth: RBAC + 2FA + device registration;
- audit: append-only audit/event log.

## 10. Pajisjet qe duhen planifikuar

### KABI - terren/ambulant

Te nevojshme:

- Android telefone/tableta me 4G/5G, GPS dhe kamere te mire;
- SIM/data mobile per secilen pajisje;
- printer mobil qe mbeshtet QR dhe ESC/POS ose protokoll te integrueshem;
- power banks ose pajisje me bateri te forte;
- mbrojtese/case per terren;
- pajisje reale testimi.

Te rekomanduara:

- barcode scanner ose skanim me kamere;
- mobile device management per pajisjet e kompanise;
- printer rezerve per ambulantet kritike.

### KABI - fabrike/depo

Te nevojshme:

- PC/tablet per pranim qumeshti/depo/prodhim;
- label printer per batch/LOT/afat skadence/barcode;
- peshore per catch-weight/djath;
- barcode scanner;
- router i mire + 4G backup;
- UPS per rrjetin, hub-in dhe pajisjet kritike.

Te rekomanduara:

- mini-PC/NUC local hub per printera, peshore, label printer dhe cache lokale;
- sensor/temperature logging per cold chain ne roadmap;
- printer etiketa rezerve.

### PLANET - restorant

Te nevojshme:

- POS terminal/kase;
- mini-PC/NUC local hub me UPS;
- tableta/telefona per kamarier;
- KDS screens/tableta/TV per grill, bar, kuzhine, sallata/pasta, expo;
- printera termike backup per kuzhine/bar;
- card terminal;
- cash drawer nese perdoret;
- router profesional, switch dhe access points te mire;
- UPS per rrjet, hub dhe POS.

Te rekomanduara:

- ekrana me mounts/mbrojtje per kuzhine;
- printer reserve;
- rrjet i ndare per POS/KDS;
- internet backup 4G/5G.

## 11. Rreziqet qe nuk duhet te injorohen

- SEF mobile/fiskalizimi mund te kete kufizime praktike qe ndryshojne arkitekturen.
- Printeret/peshoret mund te mos kene SDK/protokoll te mire.
- PWA mund te kete kufizime ne background/offline/hardware.
- Sync engine i gatshem mund te mos mbuloje fiscal/legal workflow.
- Offline me stok/LOT mund te krijoje konflikte nese ownership nuk dizajnohet sakte.
- OTA updates ne mobile nuk duhet te ndryshojne logjiken fiskale pa kontroll/versionim.
- Local hub duhet monitoruar; nese hub-i bie, restoranti mund te degradohet.

## 12. Proof-of-concept para zhvillimit te madh

Para implementimit te plote duhet te behet PoC per:

1. KABI mobile offline:
   - SQLite local;
   - create sale offline;
   - upload sync;
   - idempotence;
   - LOT/vehicle stock;
   - POD/foto/GPS.

2. Fiscal PoC:
   - SEF sandbox;
   - QR;
   - signing;
   - printer mobil;
   - offline queue;
   - retry/failure.

3. PLANET local hub:
   - POS -> KDS pa internet;
   - printer backup;
   - cloud sync pas rikthimit;
   - local network reliability.

4. Hardware PoC:
   - label printer;
   - peshore;
   - barcode scanner;
   - ESC/POS printer.

Pa keto PoC, nuk duhet te premtohet qe arkitektura eshte 100% e mbyllur.

## 13. Vendime te hapura

- PowerSync vs WatermelonDB vs custom sync.
- React Native vs PWA per app-in e kamarierit.
- Modeli final i local hub: service pa UI, Electron app, apo kombinim.
- Printeret/peshoret/modeli i pajisjeve qe do blihen.
- SEF provider/integrim direkt dhe kufizimet e certifikimit.
- Hosting/cloud provider.
- Menaxhimi i pajisjeve mobile ne terren.

## 14. Rregulla arkitekturore te pakalueshme

- Nuk ka shitje reale pa audit dhe pa rrugë fiskalizimi.
- Nuk ka stok pa dokument burim.
- Nuk ka levizje LOT pa gjurme.
- Nuk ka offline pa outbox dhe idempotence.
- Nuk ka dokument fiskal pa status fiskal.
- Nuk ka ndryshim cmimi/kthim/write-off pa autorizim/audit.
- Nuk ka varesi totale nga interneti per PLANET gjate sherbimit.
- Nuk ka varesi nga nje pajisje e vetme pa plan backup per operacionet kritike.
