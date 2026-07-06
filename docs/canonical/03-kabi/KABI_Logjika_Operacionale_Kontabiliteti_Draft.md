# KABI - Logjika Operacionale dhe Kontabiliteti (Draft)

Status: Draft diskutimi
Data: 2026-06-24
Qellimi: te kapet logjika reale e KABI para fillimit te zhvillimit. Ky dokument nuk eshte scope final implementimi derisa te aprovohet me klientin, kontabilistin dhe ekipin teknik.

> **Shenim (05.07):** ky eshte **draft/referenca legacy** e logjikes fillestare. Per gjendjen e KONFIRMUAR operative (pas pergjigjeve te KABI-t) shih `KABI_Operational_Evidence_Update_2026-07-05.md`. Ne rast mospërputhjeje, Evidence Update ka perparesi.

## Vendime te aprovuara ne kete iterim

Keto pika konsiderohen te vlefshme dhe duhet te mbahen ne modelin funksional/te dhenave te KABI:

- peshë variabile / catch-weight per produkte qe shiten me kg, sidomos djathi;
- hierarki klientesh me shume lokacione, p.sh. qender + dyqane;
- rezervim/alokim stoku per porosi te konfirmuara;
- batch/LOT ne automjet dhe batch/LOT ne rreshtin e fatures ne terren;
- Proof of Delivery (POD) si konfirmim dorëzimi;
- balanca ditore e qumeshtit mes produkteve;
- marrëveshje komerciale per cmime, konsignacion, rabate dhe risk skadence;
- 2FA, qeverisje qasjeje dhe matrice aprovimesh per veprime kritike.

## 1. Pozicionimi brenda platformes

Platforma eshte nje software i vetem multi-tenant/multi-business.

Pas login-it, perdoruesi sheh bizneset ku ka qasje, p.sh.:

- KABI
- PLANET

Te dhenat e secilit biznes jane te ndara brenda platformes:

- klientet
- furnitoret/fermeret
- artikujt
- stoku
- faturat
- financat
- kontabiliteti
- raportet
- rolet/qasjet

Ne nivel teknik, cdo entitet kritik duhet te lidhet me `tenant_id` / `business_id`.

## 2. Mbledhja dhe pranimi i qumeshtit

KABI duhet te mbeshtese pranim ditor te qumeshtit per fermer.

Te dhenat minimale:

- fermeri
- data dhe ora
- sasia ne litra
- lokacioni ose pika e pranimit
- modaliteti: fermeri e sjell / KABI e merr
- punetori/operatori qe e regjistroi
- statusi: draft, ne kontroll, pranuar, refuzuar

Testet e cilesise qe duhet te diskutohen/konfigurohen:

- yndyre/fat
- SNF/proteina
- dendesi
- aciditet
- temperature
- analiza tjera sipas procesit te KABI

Sistemi duhet te lejoje refuzim te qumeshtit jashte standardit, me arsye dhe audit log.

## 3. Cmimi i qumeshtit sipas cilesise

Cmimi nuk duhet te jete vetem "litra x cmim fiks".

Duhet te mbeshtetet rate-chart / skeme cmimi:

- cmim baze per liter
- bonus sipas yndyres/fat
- bonus/penalty sipas SNF/proteines
- penalty per temperature/cilesi jashte normes
- sezonalitet
- marreveshje specifike per fermer

Formula finale duhet te jete e konfigurueshme dhe e aprovuar nga KABI/kontabilisti.

## 4. Kartela dhe shlyerjet e fermerit

Per cdo fermer duhet kartela financiare:

- litrat e pranuara per periudhe
- cilesia mesatare
- cmimi i aplikuar
- vlera bruto
- avanset e dhena
- inputet/huate e dhena fermerit
- zbritjet/penalty
- detyrimi neto
- pagesat
- bilanci aktual

Shlyerja duhet te gjeneroje detyrim ndaj fermerit/furnitorit dhe pagesa duhet te lidhet me kartelen.

Skemat e pageses se fermereve nuk jane te njejta per te gjithe:

- disa paguhen ne momentin qe e sjellin qumeshtin
- disa paguhen javor
- disa paguhen mujor
- disa kane fatura/dokumente te rregullta
- disa nuk kane fatura

Kjo duhet te trajtohet si konfigurim per fermer, jo si rregull i vetem global. Trajtimi tatimor dhe dokumentacioni per fermeret pa fatura duhet te konfirmohet me kontabilistin.

## 5. Prodhimi, normativi dhe yield

Qumeshti ndahet ne sektore/prodhime:

- qumesht
- djath
- jogurt
- ajran
- gjize
- kos
- produkte te reja

Per cdo produkt duhet normativ:

- input kryesor, p.sh. qumesht
- perberes shtese
- paketim
- output i pritur
- humbje normale
- nen-produkte, p.sh. hirra/whey, ajke

Shembull diskutimi:

- 2 litra qumesht -> 1 kg djath

Sistemi duhet te krahasoje:

- plan/normativ
- yield real
- variance
- arsye per variance kur ka devijim

## 5.1 Planifikimi i prodhimit dhe kerkeses

Per mall te prishshem, planifikimi i prodhimit eshte kritik.

Sistemi duhet te ndihmoje ne vendimin "sa te prodhohet" duke marre parasysh:

- porosite aktive
- historikun e shitjeve
- stokun aktual
- afatet e skadences
- sezonalitetin
- kapacitetin e prodhimit
- kthimet/humbjet historike

Qellimi: te ulet mbiprodhimi dhe humbja nga skadenca.

Per MVP mund te fillohet me raport/sugjerim manual; me vone mund te avancohet ne planifikim automatik.

## 5.2 QC i produktit final dhe karantina

Pervec kontrollit te qumeshtit ne pranim, duhet te kete edhe kontroll te produktit final para dergimit.

Statuset e batch-it/prodhimit:

- ne prodhim
- ne karantine / pret kontroll
- aprovuar per shitje
- bllokuar
- refuzuar/asgjesuar

Produktet qe nuk jane aprovuar nuk duhet te lejohen per ngarkim/shitje.

## 5.3 Balanca ditore e qumeshtit mes produkteve

KABI duhet te kete kontroll mbi vendimin ditor: sa qumesht shkon per secilin produkt.

Sistemi duhet te mbeshtese:

- sasia totale e qumeshtit ne dispozicion per dite/tank/batch;
- alokimi i qumeshtit per djath, jogurt, ajran, gjize, kos, qumesht te paketuar dhe produkte tjera;
- lidhja e alokimit me porosite aktive dhe stokun ekzistues;
- krahasimi plan vs real;
- arsye per ndryshim alokimi;
- operatori/menaxheri qe e aprovoi planin ditor.

Pa kete, prodhimi mund te regjistrohet, por menaxhimi i yield-it, kostos dhe humbjes nga skadenca mbetet i dobet.

## 6. Batch, LOT dhe gjurmueshmeria

Afati i skadences nuk duhet te ruhet vetem ne artikull; duhet te ruhet ne batch/LOT.

Cdo prodhim krijon batch/LOT me:

- kodi i batch-it
- produkti
- data e prodhimit
- afati i skadences
- sasia e prodhuar
- inputet e perdorura
- fermeret/batch-et e qumeshtit te lidhura
- operatori
- statusi

Gjurmueshmeria e kerkuar:

fermer -> pranim qumeshti -> batch lende e pare -> prodhim -> batch produkt final -> depo/automjet -> klient -> fature.

Kjo mundeson recall/tërheqje nga tregu.

## 6.1 LOT ne automjet dhe LOT ne rreshtin e fatures

Per KABI nuk mjafton te dihet vetem produkti dhe sasia. Duhet te dihet saktë cili LOT eshte levizur dhe shitur.

Sistemi duhet te ruaje:

- LOT-et e ngarkuara ne secilin automjet;
- sasia per LOT ne automjet;
- FEFO gjate shitjes ne terren;
- LOT-i ne cdo rresht fature/dorezimi;
- afati i skadences ne momentin e shitjes/dorezimit;
- LOT-et e kthyera nga klienti;
- diferenca per LOT ne mbylljen ditore.

Kjo eshte e pakalueshme per:

- alarme skadence pas shitjes;
- recall te sakte;
- kthime/note krediti;
- kontroll te stokut ne automjet;
- auditim te gjurmes produkt -> klient -> fature.

## 7. Inventari i prishshem dhe FEFO

KABI duhet te punoje me FEFO - First Expired, First Out.

Sistemi duhet te sugjeroje daljen/ngarkimin e lot-eve qe skadojne me heret.

Gjendjet e inventarit duhet te ndahen sipas fazave:

- qumesht/lende e pare
- WIP / ne prodhim
- produkt final
- paketim
- kthime
- mall i demtuar/skaduar

## 8. Alarmet e skadences pas shitjes/dorezimit

Sistemi duhet te dije cfare lot-i eshte derguar te cili klient dhe me cilen fature.

Shembull njoftimi:

Jogurt 1L, lot JOG-2026-05-03-001, fatura NR-1234, derguar te Market A me 03/05/2026, skadon pas 3 ditesh.

Alarmet duhet te jene task/njoftime operative, jo vetem raport pasiv.

Parametra te diskutueshem:

- njoftim 7 dite para skadences
- njoftim 3 dite para skadences
- njoftim ne diten e skadences
- prioritet sipas klientit/produktit/sasise

Risku i skadences nuk eshte gjithmone i njejte:

- ne disa pika riskun e bart KABI
- ne disa pika riskun e bart marketi/klienti
- varet nga marreveshja komerciale me klientin

Sistemi duhet ta ruaje kete rregull ne nivel klienti ose marrëveshje/kanali. Ky konfigurim ndikon ne kthim, note krediti, write-off, raport humbjesh dhe pergjegjesi komerciale.

## 9. Stoku i automjetit / ambulantit

Punetori qe shperndan mall duhet te kete depo mobile / stok automjeti.

Rrjedha:

1. Ngarkim ne fabrike/depo
2. Transfer depo -> automjet
3. Shitje/dorezim ne pika
4. Fature ne vend
5. Pagesa cash/banke/kartel/borxh
6. Kthim i mallit nga klienti
7. Kthim automjet -> depo
8. Barazim ditor

Dokumentet qe duhen:

- urdher-ngarkim
- transfer depo -> automjet
- fature shitje ne terren
- arketim/pagesa
- kthim nga klienti
- kthim automjet -> depo
- raport ditor i punetorit
- dokument diference/humbje/demtim

## 9.1 Shitje direkte vs konsignacion

Duhet te mbeshteten dy modele, sepse disa markete/pika mund te punojne ndryshe:

### Shitje direkte

Mall dergohet dhe faturohet menjehere.

Efekti:

- del nga stoku i KABI
- krijohet fature shitje
- krijohet AR/cash
- llogaritet TVSH
- llogaritet COGS

### Konsignacion / mall ne raft te klientit

Mall dergohet te klienti, por nuk konsiderohet shitje finale derisa te shitet ose te faturohet sipas marreveshjes.

Efekti duhet te konfirmohet me kontabilistin, por sistemi duhet te modeloje:

- stoku ne konsignacion per klient/lokacion
- batch/LOT dhe afat skadence te mallit ne raft
- shitje/faturim periodik ose sipas konfirmimit
- kthime nga konsignacioni
- risku i skadences sipas marreveshjes

Nuk duhet te supozohet qe cdo dergese ne market eshte fature e menjehershme.

## 9.2 Cmimore, zbritje, promocione dhe autorizime

Sistemi duhet te mbeshtese cmime shitjeje sipas:

- klientit
- grupit/kanalit, p.sh. dyqan, HoReCa, zinxhir marketesh
- marrëveshjes
- zones/territorit
- periudhes/promocionit
- sasisë

Zbritjet ne terren duhet te kontrollohen:

- maksimum zbritje per rol/punetor
- PIN/aprovim per zbritje te madhe
- arsye zbritjeje
- audit log

Duhet te mbeshteten komisione te shitesit/agjentit nese KABI i perdor.

## 9.2.1 Marrëveshjet komerciale

Per klientet e medhenj dhe marketet zinxhir, sistemi duhet te ruaje marrëveshje komerciale.

Marrëveshja duhet te mundesoje:

- periudhe vlefshmerie;
- cmime te kontraktuara;
- rabate/zbritje;
- kushte pagese;
- limite kredie;
- model pune: shitje direkte ose konsignacion;
- kush e bart riskun e skadences;
- rregulla per kthime;
- rregulla per gajbe/ambalazh;
- aprovime per ndryshim cmimi ose dalje nga marrëveshja.

Cmimet dhe rregullat e fatures nuk duhet te varen nga kujtesa e punetorit; duhet te dalin nga klienti/lokacioni/marrëveshja aktive.

## 9.3 Kontroll kredie dhe aging per klient

Para shitjes me borxh ne terren, sistemi duhet te kontrolloje:

- limiti i kreditit
- borxhi aktual
- fatura te skaduara
- aging 0-30, 31-60, 61-90, 90+
- statusi i klientit: aktiv, i bllokuar, kerkon aprovim

Nese tejkalohet limiti, sistemi duhet te bllokoje ose te kerkoje aprovim nga menaxheri.

## 9.4 Kanale porosie nga klienti

Pervec porosive nga punetori i rafteve/ambulanti, duhet te diskutohet nese klientet mund te porosisin permes:

- telefonit dhe operatori e regjistron
- portalit B2B
- WhatsApp/SMS
- app te klientit

Per MVP mund te mbetet regjistrim nga stafi; per roadmap duhet te parashihet B2B/portal.

## 9.5 Hierarkia e klientit me shume lokacione

Klientet si zinxhire marketesh nuk duhet te modelohen vetem si nje klient i thjeshte.

Sistemi duhet te mbeshtese:

- klient juridik/qender, p.sh. klient zinxhir;
- lokacione/dyqane te ndara;
- adresa dhe GPS per secilin lokacion;
- cmime sipas qendres ose lokacionit;
- porosi sipas lokacionit;
- dorezim sipas lokacionit;
- faturim sipas lokacionit ose qendres, varesisht nga marreveshja;
- risk skadence sipas lokacionit ose marrëveshjes;
- limiti i kreditit ne nivel qendre dhe/ose lokacioni;
- raporte shitje/kthime/skadence per qender dhe per dyqan.

Ky model duhet te ekzistoje prej fillimit ne databaze, edhe nese UI e pare eshte me e thjeshte.

## 9.6 Rezervim dhe alokim stoku per porosi

Per mall te prishshem dhe shperndarje me shume automjete, sistemi duhet te kete konceptin ATP/rezervim stoku.

Kur nje porosi konfirmohet, sistemi duhet te mundesoje:

- rezervim sasie nga stoku i disponueshem;
- rezervim sipas LOT-it kur eshte e nevojshme;
- prioritet sipas afatit te skadences/FEFO;
- ndarje per automjet/rute;
- lirim rezervimi nese porosia anulohet;
- ndalim i dyfishimit te se njejtes sasi per dy porosi;
- raport "available", "reserved", "allocated", "picked", "loaded", "delivered".

Per MVP kjo mund te filloje si alokim manual i kontrolluar; por modeli duhet te jete i gatshem per ATP serioz.

## 9.7 Proof of Delivery (POD)

Dorezimi duhet te kete konfirmim te pavarur nga vizita e merchandising-ut.

POD duhet te ruaje:

- klientin/lokacionin;
- faturen ose dokumentin e dorezimit;
- data/ora;
- punetorin/automjetin;
- GPS dhe accuracy;
- emer personi pranues;
- nenshkrim digjital kur perdoret;
- foto opsionale e mallit/dokumentit;
- komente/refuzime/diferenca;
- status: dorezuar, pjeserisht dorezuar, refuzuar, kthyer.

POD ndihmon ne konteste me klientin, kontroll te punetorit dhe lidhje te sakte mes fatures, stokut dhe kthimeve.

## 10. Kontrolli i rafteve dhe vizitat ne terren

Punetori qe kontrollon raftet duhet te kete modul vizitash.

Per cdo vizite:

- klienti/lokacioni
- data/ora automatike
- punetori
- GPS automatik
- accuracy e GPS
- foto para
- foto pas
- komente
- produkte qe mungojne
- produkte afer skadences
- porosi e krijuar nga vizita

Rregull i rendesishem:

- foto vetem nga kamera
- jo upload nga galeria
- lokacioni dergohet automatikisht nga pajisja
- punetori nuk zgjedh manualisht lokacionin nga harta

## 11. Porosite nga punetori i rafteve

Punetori i rafteve mund te krijoje porosi per klientin.

Rrjedha:

Vizite -> kontroll rafti -> porosi -> fabrika e pranon -> picking/packing -> ngarkim -> dorezim/faturim.

Statuset:

- draft
- derguar ne fabrike
- aprovuar
- ne pergatitje
- ngarkuar
- derguar
- faturuar
- anuluar

## 12. Kthimet, note krediti dhe write-off

Kthimet duhet te klasifikohen me arsye:

- afat i skaduar
- afat afer skadimit
- demtuar
- gabim ne porosi
- klienti refuzoi
- nderrim produkti
- kthim ambalazhi/gajbe

Efekti duhet te jete i qarte:

- kthim ne stok te shitshem
- kthim ne stok karantine
- write-off
- note krediti
- korrigjim financiar

## 13. Ambalazhi / gajbet

KABI perdor gajbe/ambalazh kthyes.

Gajbet mund te kene kapacitete te ndryshme:

- 24 cope
- 20 cope
- kapacitete tjera sipas llojit te gajbes/produktit

Duhet kartela e ambalazhit:

- sa i jane dhene klientit
- sa jane kthyer
- sa i ka punetori ne automjet
- sa mungojne
- a faturohen
- a mbahen si detyrim

Duhet te ruhen edhe:

- tipi i gajbes
- kapaciteti i gajbes
- produktet qe mund te futen ne ate gajbe
- depo/automjet/klient ku ndodhet gajbja
- gjendja: ne qarkullim, kthyer, demtuar, humbur

Ambalazhi nuk duhet te perzihet me stokun e produktit; duhet levizje dhe kartela te veta.

## 14. Multi-UoM

KABI duhet te mbeshtese njesi te shumefishta:

- litra
- kg
- cope
- pako
- gajbe
- palete

Duhet konvertime te kontrolluara:

- litra -> kg
- kg -> cope/pako
- cope -> pako/gajbe

Konvertimet duhet te ruhen me histori kur ndryshojne.

## 14.1 Peshe variabile / catch-weight

Disa produkte, sidomos djathi, mund te shiten me kg por te ekzistojne si cope fizike me peshe te ndryshme.

Sistemi duhet te mbeshtese catch-weight:

- prodhim i copave me peshe reale;
- lidhje cope/pako me LOT;
- peshim ne prodhim;
- peshim ne paketim;
- peshim ne shitje/dorezim kur duhet;
- barcode/etikete me peshe reale;
- cmim per kg;
- sasi ne cope dhe kg paralelisht;
- diferenca mes peshes teorike dhe peshes reale;
- ndikim ne stok, kosto dhe fature.

Shembull: 1 cope djath mund te jete 0.82 kg, tjetra 1.13 kg. Sistemi nuk duhet ta trajtoje te dyja si "1 cope = 1 kg".

## 15. Kostoja e prodhimit per batch/produkt

Kostoja nuk duhet te mbetet vetem si mesatare e thjeshte operative.

Per prodhim duhet te llogaritet kosto per batch:

- kosto qumeshti/lende e pare
- kosto perberesish
- kosto paketimi
- pune
- energji
- kosto tjera indirekte sipas rregullave te aprovuara

Duhet raport:

- kosto plan
- kosto reale
- variance
- kosto per produkt/batch

Metoda zyrtare e vleresimit te inventarit per pasqyra financiare duhet te vendoset me kontabilistin, p.sh. FIFO, mesatare e ponderuar ose metode tjeter e lejuar. Sistemi duhet te ruaje detaje te mjaftueshme per batch costing edhe nese raportimi financiar perdor metode tjeter.

## 16. Kontabiliteti dhe postimet

KABI ka te njejten baze financiare/kontabel si platforma:

- GL double-entry
- AR/AP
- blerje vendore/import
- shitje
- pagesa
- banka/arka
- TVSH
- paga
- asete
- mbyllje periudhe

Rregullat specifike te KABI:

- pranimi i qumeshtit krijon detyrim ndaj fermerit/furnitorit
- qumeshti hyn si lende e pare
- prodhimi konsumon lenden e pare dhe krijon produkt final
- shitja krijon te hyra, TVSH dhe arketueshme/cash
- COGS del nga kostoja e batch-it/produktit
- kthimi krijon note krediti, kthim stoku ose write-off sipas rastit

## 16.1 Raportet/outputet ligjore dhe financiare

Sistemi duhet te prodhoje jo vetem postime, por edhe outpute:

- libri i shitjes
- libri i blerjes
- deklarata mujore e TVSH
- kartela klienti
- kartela furnitori/fermeri
- AR/AP aging
- raport arka/banka
- raport stoku dhe levizje stoku
- raport COGS
- pasqyra financiare bazike
- eksport per kontabilist/ATK sipas formatit te kerkuar

Keto duhet te lidhen me kerkesat kontabel-administrative të aprovuara jashtë repo-s.

## 16.2 Bank reconciliation

Duhet modul ose se paku workflow per barazim bankar:

- import/lexim i ekstrakteve bankare
- lidhje me pagesa/fatura
- pagesa te papajtuara
- diferenca
- audit log i barazimit

## 16.3 Inter-company KABI -> PLANET

KABI mund ta furnizoje PLANET-in.

Duhet te vendoset nese kjo trajtohet si:

- shitje e KABI me fature fiskale drejt PLANET
- blerje/stok hyrje te PLANET
- transaksion inter-company brenda grupit

Edhe pse jane ne nje software, bizneset duhet te jene te ndara financiarisht. Nese ka raport konsolidimi, eliminimet inter-company jane raportim i vecante, jo perzierje e dokumenteve baze.

## 17. TVSH

Tarifat duhet te jene te konfigurueshme per tenant dhe per artikull.

Hipoteze per diskutim:

- disa produkte qumeshtore mund te jene T8
- disa artikuj mund te jene T18

Kjo nuk duhet te hardcode-ohet. Duhet te konfirmohet me kontabilistin dhe burimin zyrtar tatimor para konfigurimit final.

Pika qe duhen konfirmuar:

- TVSH per produktet e qumeshtit ne shitje
- TVSH per ambalazh/gajbe nese faturohet
- TVSH per write-off/asgjesim
- TVSH per kthime/note krediti
- trajtimi i qumeshtit nga fermeret e regjistruar dhe te paregjistruar
- a ka ndonje skeme specifike bujqesore

## 18. Mbyllja ditore e punetorit/automjetit

Ne fund te dites punetori duhet te mbylle raportin:

- mall i ngarkuar
- mall i shitur
- mall i kthyer
- mall i mbetur ne automjet
- cash i pranuar
- pagesa tjera
- borxhe te krijuara
- fatura te leshuara
- diferenca
- aprovim nga menaxheri

Pa mbyllje ditore nuk duhet te lejohet mbyllja e plote e dites operative.

## 19. Offline dhe sinkronizimi

Pajisjet ne terren mund te punojne pa internet.

Duhet te mbeshtetet:

- porosi offline
- fature/dokument i brendshem offline sipas rregullave ligjore
- UUID per cdo veprim
- outbox
- sync pa dublim
- status "ne pritje per sinkronizim"
- rregulla per konfliktet e stokut

Fiskalizimi offline duhet te konfirmohet me ATK/ofruesin fiskal para vendimit final.

Kerkesa operative e KABI: faturimi ne terren duhet te funksionoje edhe offline.

Hulumtimi fillestar ne dokumentacionin zyrtar te ATK/SEF tregon se kuponet mund te leshohen ne modalitet jashtë linje/offline kur ka nderprerje te lidhjes elektronike/internetit, por duhet te fiskalizohen/dorezohen ne ATK pas rikthimit te lidhjes dhe brenda afateve te percaktuara. Shih: `docs/canonical/05-sef-atk/ATK_SEF_Offline_Faturimi_Findings.md`.

Vendimet arkitekturore paraprake per offline/online, stack, local hub, pajisje, sync engine dhe fiscal outbox jane dokumentuar ne: `docs/Arkitektura_Offline_Online_Stack_Draft.md`.

Duhet te specifikohet qarte:

- cfare dokumenti krijohet kur pajisja eshte offline
- a leshohet kupon/fature fiskale offline nga software/pajisja fiskale
- si ruhet QR/numri fiskal kur nuk ka internet
- a behet queue dhe fiskalizim kur kthehet lidhja
- afati maksimal ligjor per sinkronizim/fiskalizim
- cfare ndodh nese fiskalizimi deshton pas kthimit te internetit
- cfare sheh klienti ne dokumentin offline
- si parandalohet dublimi i faturave

Kjo eshte pike kritike dhe nuk duhet te supozohet pa verifikim zyrtar.

## 19.1 Cutover dhe saldot hapese

Kur KABI kalon ne sistemin e ri, duhet plan i qarte cutover.

Nese nuk migrohet krejt historiku, duhet te futen saldot hapese:

- stok aktual per produkt/batch/afat
- stok automjetesh
- gajbe/ambalazh ne klient/automjet/depo
- AR - borxhet e klienteve
- AP - detyrimet ndaj fermereve/furnitoreve
- avanset/huate e fermereve
- arka/banka
- GL opening balances
- fatura te hapura
- porosi te hapura

Pa cutover te sakte, sistemi mund te jete teknikisht funksional por financiarisht i gabuar.

## 19.2 Rruge, territore dhe frekuence vizitash

Duhet te modelohen rruget/territoret:

- ruta
- punetori/agjenti
- klientet ne rute
- frekuenca vizites, p.sh. cdo te hene
- dita/ora e preferuar
- prioriteti
- statusi i vizites

Kjo lidhet me kontrollin e rafteve, porosite dhe shperndarjen.

## 19.3 Rolet specifike te KABI

Rolet qe duhet te diskutohen:

- administrator
- menaxher
- kontabilist
- operator pranimi qumeshti
- kontrollor cilesie
- operator prodhimi
- magazinier
- ambulant/shites ne terren
- kontrollues rafti/merchandiser
- shofer
- arketar

Cdo rol duhet te kete leje te qarta per krijim, ndryshim, anulim, aprovim, zbritje, kthim, fiskalizim dhe raporte.

## 19.3.1 2FA, qeverisje qasjeje dhe matrica e aprovimeve

Administrimi duhet te jete eksplicit, sepse KABI do te kete veprime me ndikim financiar, stok dhe fiskal.

Duhet te mbeshtetet:

- 2FA per role kritike;
- menaxhim sesionesh;
- bllokim/reset qasjeje;
- RBAC granular per modul, depo, automjet, lokacion dhe raport;
- audit log per cdo veprim kritik;
- matrice aprovimesh.

Veprime qe duhet te kene aprovim ose kufizim sipas rolit:

- ndryshim cmimi;
- zbritje jashte limitit;
- override i limitit te kreditit;
- write-off/skadence;
- kthim me note krediti;
- bllokim/zhbllokim LOT-i;
- aprovim prodhimi final;
- korrigjim stoku;
- cutover/saldo hapese;
- anulim/fiskalizim/deshtim fiskal;
- ndryshim marrëveshjeje komerciale.

Kjo duhet te trajtohet si pjese e produktit, jo si konfigurim i thjeshte teknik.

## 19.4 Etiketa, barcode dhe skanim

Per gjurmim serioz duhet te diskutohet printimi/skanimi i etiketave:

- barcode/QR per produkt
- batch/LOT
- afat skadence
- data prodhimit
- sasia/njesia
- etiketa per gajbe/pako/palete

Kjo eshte shume e rendesishme per FEFO, ngarkim automjeti, kthime dhe recall.

## 19.5 KPI dhe analitika menaxheriale

Raportet menaxheriale qe duhen:

- humbje/skadence per produkt
- kthime per klient/shites/rute
- fitim per produkt
- fitim per klient
- fitim per rute/automjet/shites
- yield variance
- shrinkage/diferenca stoku
- aging i klienteve
- performanca e fermereve
- cilesia mesatare e qumeshtit per fermer

## 19.6 Cold chain dhe temperatura

Cold chain duhet te jete se paku ne roadmap.

Pika per diskutim:

- temperature ne pranim
- temperature ne depo/frigorifer
- temperature ne transport
- temperature te klienti ne pranim
- alarme per devijim

Per MVP mund te regjistrohet manualisht; me vone mund te lidhet me pajisje/sensor.

## 19.7 Recall / terheqje nga tregu

Gjurmueshmeria duhet te kthehet ne workflow veprues.

Procesi:

1. Identifiko batch/LOT problematik
2. Gjej klientet/faturat ku eshte derguar
3. Njofto punetoret/menaxheret
4. Blloko shitjen e lot-it
5. Terhiq mallin
6. Regjistro kthim/note krediti/write-off
7. Dokumento mbylljen e rastit

Kjo eshte e rendesishme per siguri ushqimore dhe per auditim.

## 20. Rregulla te pakalueshme

- Nuk fshihet histori financiare/stoku pas postimit.
- Korrigjimet behen me reversal/anulim/note krediti.
- Cdo veprim kritik ka audit log.
- Cdo dokument ka numerim te kontrolluar.
- Debit = kredit per cdo postim GL.
- Stoku nuk leviz pa dokument burim.
- Cdo levizje batch/LOT ruan gjurmueshmeri.
