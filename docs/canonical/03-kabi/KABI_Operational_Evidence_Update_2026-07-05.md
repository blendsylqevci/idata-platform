# KABI - Operational Evidence Update

Data: 2026-07-05  
Status: Sanitized working evidence  
Burimi: pergjigje nga KABI + dokumente te skanuara operative  
Privatesia: dokumentet burimore mbeten private-local; ky dokument nuk permban emra fermeresh, emra klientesh, emra furnitoresh, barkode, cmime reale, NUI/NIF personale, IBAN, apo vlera reale financiare.

## 1. Qellimi

Ky dokument mbyll dhe perditeson pikat operative te KABI-t qe ndikojne ne:

- pranimin e qumeshtit;
- analizat laboratorike;
- formulen USAID dhe pagesen e fermereve;
- prodhimin, normativat dhe repromaterialin;
- artikujt, TVSH-ne dhe etiketat;
- shitjen, kthimet dhe shperndarjen;
- kontabilitetin dhe raportimin;
- pyetjet qe mbeten te hapura per verifikim ligjor ose teknologjik.

Ky dokument eshte burim pune per specifikim dhe dizajn sistemi. Nuk zevendeson dokumentet zyrtare, marreveshjet me fermeret, raportet e AUV/AVUK, deklarimet tatimore, apo konfirmimet zyrtare te ATK-se.

Ky dokument perditeson `KABI_Logjika_Operacionale_Kontabiliteti_Draft.md` (draft/referenca legacy) me evidencen e KONFIRMUAR nga KABI (05.07). Per trajtimin tatimor te fermerëve shih `Accountant_Confirmation_TVSH_e_Sheshte_Fermere_KABI_2026-07-05.md`.

## 2. Gjetje kryesore - te konfirmuara nga KABI

### 2.1 Gjurmueshmeria e qumeshtit

Kur qumeshti nga disa fermere perzihet, gjurmueshmeria ruhet me:

- regjionin/piken e pranimit;
- numrin/kodin e fermerit;
- lotin e qumeshtit;
- daten e pranimit;
- sasine;
- rezultatet e analizave;
- regjistrimin ne dokumente.

Per sistemin kjo do te thote se pranimi nuk eshte vetem "sasi qumeshti", por nje event i gjurmueshem me identitet fermeri, moster, rezultat laboratori dhe lidhje me lot/pool/tank.

### 2.2 Mostra per fermer

KABI konfirmoi se per secilin fermer merret moster qumeshti per kontroll nga teknologu/laboratori.

Kjo eshte kerkese funksionale e detyrueshme:

- pa moster nuk duhet te mbyllet pranimi si i plote;
- pa aprovim laboratori nuk duhet te kalohet ne shlyerje finale;
- mostra duhet te kete status, rezultat, aprovues, date/kohe dhe audit trail.

### 2.3 Testet reale te cilesise ne pranim

KABI ben ose regjistron keto kontrolle:

- pamje dhe aroma;
- aciditet / pH / freski;
- dendesia;
- yndyra;
- proteina / SNF;
- sasia e ujit te shtuar;
- temperatura;
- testi per antibiotike;
- qelizat somatike nuk testohen direkt ne menyre rutine, por perdoren filtra per reduktim;
- kontaminimet mikrobiologjike nuk testohen aktualisht ne pranim.

Pajisje/metoda:

- yndyra, SNF/proteina dhe disa parametra dalin nga Lacto scan;
- dendesia matet me densimeter ose analizator;
- aciditeti/freskia me pH-meter ose alcohol test;
- disa rezultate dalin te shtypura nga pajisja, disa regjistrohen manualisht.

Laboratori aprovon rezultatet.

### 2.4 Vlera orientuese te pranimit

KABI dha keto vlera orientuese:

- yndyra zakonisht rreth 3.2-3.5%, jo nen pragun e pranimit;
- temperatura e qumeshtit te ftohur zakonisht rreth 4-6 C;
- antibiotiku duhet te jete negativ;
- uji i shtuar nuk duhet te kaloje pragun e lejuar;
- qelizat somatike duhet te jene sa me te ulta.

Shenim i rendesishem: vlerat per proteina/SNF dhe disa pragje duhet te konfirmohen me teknologun per njesine/formatin e sakte, sepse mund te varen nga pajisja dhe menyra e leximit te raportit.

### 2.5 Formula USAID dhe pagesa e fermereve

KABI konfirmoi:

- formula USAID eshte skema zyrtare e pageses se fermereve;
- formula eshte skema aktuale e KABI-t (kohezgjatja e perdorimit per t'u konfirmuar) dhe nuk ndryshohet ne logjike;
- nuk ka cmim te njejte per te gjithe fermeret;
- cmimi ndryshon sipas cilesise/parametrave qe hyjne ne formule;
- teknologu/laboratori cakton vlerat e analizes per fermerin nga mostra;
- sistemi duhet te llogarise automatikisht cmimin per liter dhe totalin;
- pagesat behen mujore;
- fermeret marrin avanse;
- avanset duhet te zbriten ne shlyerje;
- penalty ekziston;
- bonus nuk eshte praktikuar zakonisht, por duhet te ekzistoje si opsion;
- inpute/hua per fermeret nuk jepen ne procesin aktual.

Vendim funksional:

- formula implementohet 1:1 me Excel-in e validuar;
- parametrat reale te formulas mbeten private-local/tenant config;
- ne dokumentet publike/canonical nuk publikohen konstantet reale, emrat, litrat, cmimet, apo totalet;
- sistemi duhet te ruaje historikun e parametrave sipas periudhes.

### 2.6 Fermeret dhe dokumentet

KABI konfirmoi:

- ka marrëveshje individuale me fermerë;
- fermeret nuk leshojne fatura;
- dokumentet/raportet per pranim dhe barazim i mban KABI;
- kartela e fermerit duhet te tregoje pranimet, analizat, avanset, penalty/bonus, pagesat dhe bilancin.

Pika ligjore/tatimore per fermeret mbetet gate me ATK/kontabilist: trajtimi i TVSH-se se sheshte, NIF dhe raportimi ne libra.

### 2.7 Produktet, TVSH dhe etiketat

KABI konfirmoi:

- qumeshti dhe ajka 1kg trajtohen me TVSH 18%;
- produktet e tjera trajtohen me TVSH 8%;
- barkodet ekzistojne ne listen operative;
- afati i skadences ndryshon sipas produktit;
- etiketat/barcodet printohen sipas porosise.

Dokumenti i produkteve tregon se sistemi duhet te kete keto fusha ne master data:

- kodi;
- barcode;
- pershkrimi;
- njesia;
- paketimi;
- cmimi i shitjes;
- norma e TVSH-se;
- status aktiv/pasiv;
- afati i skadences;
- a shitet me peshe;
- normativi/BOM;
- a printohet etiketa.

Cmimet, barkodet dhe kodet reale nuk duhet te publikohen ne GitHub/Notion/Slack pa arsye dhe pa sanitizim.

### 2.8 Prodhimi, normativat dhe repromateriali

Dokumentet e skanuara konfirmojne se KABI ka:

- lista recepturash/normativash per produkte;
- repromaterial/ambalazh te lidhur me artikuj prodhimi;
- linja prodhimi me kapacitete operative;
- afate dhe paketim te ndryshem sipas produktit.

Sistemi duhet te mbeshtese:

- BOM/normativa per produkt;
- komponentet/repromaterialin;
- versionimin e recepturave;
- lidhjen e prodhimit me lot/batch;
- konsumin e qumeshtit dhe repromaterialit;
- raportin plan vs real;
- kapacitetin e linjave prodhuese;
- etiketa sipas porosise dhe produktit.

### 2.9 Gajbe, kartona dhe paketim

KABI dha shembuj per sasi ne gajbe/karton:

- disa produkte paketohen ne gajbe me numer te caktuar njesish;
- disa ne karton;
- paketimi ndryshon sipas produktit.

Sistemi duhet te kete paketime te konfigurueshme per produkt dhe te mos e supozoje nje raport fiks per te gjitha produktet.

### 2.10 Shitja, shperndarja dhe automjetet

KABI konfirmoi:

- ka shperndares te brendshem dhe shperndares te jashtem;
- disa shperndares te jashtem punojne me rabat/kompensim sepse mbulojne shpenzimet e veta;
- ngarkimi behet sipas porosise/listes;
- ka edhe model ambulant ku shoferi ngarkohet dhe shet ne pika;
- batch/LOT duhet te dihet gjate ngarkimit;
- faturimi behet ne vend;
- ne fund te dites shoferi barazohet, shkarkohet dhe krahasohet malli i ngarkuar me shitjet/kthimet/mbetjen;
- diferencat aprovohen nga roli pergjegjes;
- FEFO duhet te aplikohet detyrimisht, edhe nese sot nuk eshte i formalizuar;
- pranimi i pjesshem nga klienti dhe refuzimi i LOT-it korrigjojne faturen.

Sistemi duhet te kete:

- depo mobile per automjet;
- ngarkim/shkarkim;
- shitje ne terren;
- fature ne vend;
- kthime;
- barazim shoferi;
- aprovim diferencash;
- FEFO ne terren;
- korrigjim fature per pranim te pjesshem ose LOT te refuzuar.

### 2.11 Kthimet nga klientet

KABI konfirmoi:

- kthimet behen me flet-kthim;
- financiarisht zbriten nga kartela e klientit;
- per kthime duhet dokument/arsye;
- note krediti kerkohet per kthim, me arsye te qarte.

Dokumenti i kthimit i skanuar konfirmon se kthimet permbajne sasi negative/korrigjim, TVSH dhe nenshkrim/vulosje.

### 2.12 Klientet, furnitoret dhe situacionet

Dokumentet e skanuara tregojne se KABI ka situacione/lista per:

- furnitore;
- klienta;
- karta/llogari analitike;
- dokumente operative per pranim dhe situacion.

Keto dokumente jane private-sensitive sepse permbajne emra subjektesh dhe kode/llogari. Ne sistem duhet te modelohen si master data dhe kartela analitike, por raw listat nuk duhet te publikohen.

### 2.13 Kontabiliteti dhe raportimi

KABI konfirmoi:

- KABI -> PLANET trajtohet si furnizim normal;
- raportet per ATK perfshijne librin e shitjes dhe librin e blerjes;
- kthimet ndikojne ne kartela;
- pagat/kontributet duhet te jene ne sistem ne fazen e pare;
- sezonet me volum me te larte jane vere dhe dhjetor.

Vendim funksional:

- Core Accounting + KABI mbetet scope i dites se pare;
- payroll/kontribute nuk duhet te lihet jashte fazes se pare;
- raportet ATK per KABI duhet te dalin nga i njejti engine kontabel, me rregulla tenant-specific per TVSH dhe fermeret.

### 2.14 TVSH e sheshte per fermeret - baze zyrtare

Burim zyrtar i gjetur: Gazeta Zyrtare e Republikes se Kosoves, Udhëzimi Administrativ MF-Nr. 03/2015 per zbatimin e Ligjit Nr. 05/L-037 per TVSH, Neni 76.

Gjetjet kryesore:

- Skema e normes se sheshte zbatohet per produktet bujqesore te furnizuara nga bujqit/prodhuesit bujqesor.
- "Prodhues bujqesor" eshte person qe zhvillon aktivitet ne ekonomi bujqesore, pyjore ose peshkatari ne Kosove.
- Certifikata nënkupton dokumentin e regjistrimit te personit me numer identifikues si fermer prane Ministrise se Bujqesise, Pylltarise dhe Zhvillimit Rural.
- Kushtet kryesore: qarkullimi vjetor i prodhuesit nuk tejkalon pragun e regjistrimit per TVSH; furnizon mallra me prejardhje bujqesore; mallrat u furnizohen personave te tatueshem qe nuk perfitojne nga skema; prodhuesi eshte i pajisur me certifikate te leshuar nga Ministria perkatese.
- Norma e sheshte per prodhuesit bujqesor eshte 8%.
- Perllogaritja behet nga bleresi - person i tatueshem.
- Norma e sheshte konsiderohet brenda cmimit te perllogaritur te blerjes.
- Bleresi duhet te leshoje fature tatimore per shitesin/prodhuesin bujqesor.
- TVSH e perllogaritur brenda vleres se paguar prodhuesit bujqesor eshte TVSH e zbritshme per bleresin, sipas kushteve te ligjit.
- Fatura duhet te permbaje, nder te tjera, te dhenat e bleresit, te dhenat e prodhuesit bujqesor, numrin e certifikates nga MBPZHR dhe numrin identifikues fiskal te prodhuesit bujqesor, pershkrimin/sasine/njesine, vleren pa TVSH, TVSH-ne e perllogaritur dhe totalin me TVSH.
- Lista e produkteve te perfshira ne skeme perfshin qumeshtin dhe nen-produktet e qumeshtit.

Vendim per sistem:

- Gate-i ligjor per ekzistencen e skemes se normes se sheshte 8% per furnizimet bujqesore tani ka burim zyrtar.
- Ne sistem duhet te kete dokument blerjeje/settlement per fermerin qe gjenerohet nga KABI si bleres, jo nga fermeri.
- Kartela e fermerit duhet te ruaje certifikaten/numrin identifikues te fermerit dhe NIF-in/fiscal ID kur aplikohet kjo skeme.
- Posting final dhe raportimi ne libra mbetet per sign-off te kontabilistit: cilat konto, cilat fusha te librit te blerjes dhe si shfaqet ne deklarimin mujor.

## 3. Gate te mbyllura

| Gate | Status | Koment |
| --- | --- | --- |
| Formula USAID si skeme zyrtare pagese | Mbyllur | Implementim 1:1 me Excel-in e validuar; vlerat reale private-local. |
| Mostra individuale per fermer | Mbyllur | E detyrueshme per pranim dhe shlyerje. |
| Laboratori aprovon rezultatet | Mbyllur | Lab/QC role duhet te jete gate para shlyerjes finale. |
| Pagesa mujore e fermerit | Mbyllur | Shlyerje mujore me avanse/penalty/bonus opsional. |
| Fermeret nuk leshojne fatura | Mbyllur si proces biznesi | Trajtimi tatimor mbetet gate ligjor. |
| Inpute/hua per fermeret | Mbyllur per scope aktual | Nuk jepen aktualisht. |
| TVSH produktesh KABI | Mbyllur nga KABI/kontabilisti | Qumesht + ajke 1kg = 18%; produktet tjera = 8%. |
| Kthime me arsye/note krediti | Mbyllur | Duhet dokument kthimi + arsye + efekt ne kartela. |
| FEFO ne terren | Mbyllur si kerkese | Duhet aplikuar, edhe nese sot nuk eshte i formalizuar. |
| Payroll ne fazen e pare | Mbyllur | I detyrueshem sipas pergjigjes se KABI/kontabilistit. |

## 4. Gate te hapura

| Gate | Status | Pse mbetet hapur |
| --- | --- | --- |
| TVSH e sheshte per fermeret / NIF | Baze zyrtare u gjet; kerkohet sign-off kontabilist | UA MF 03/2015, Neni 76 konfirmon skemen, normen 8%, faturen nga bleresi dhe kushtet. Kontabilisti duhet te mbylle posting/konto/liber blerje. |
| Njesite/pragjet e sakta per proteine/SNF | Kerkohet teknologu | Vlerat e dhena duhet te konfirmohen ne formatin e pajisjes. |
| Rounding i pageses se fermerit | Kerkohet kontabilist/KABI | Duhet vendim cent-per-cent per shlyerje. |
| Posting accounting per fermeret | Kerkohet pas gate-it tatimor | Varet nga trajtimi i TVSH-se se sheshte dhe dokumentit te blerjes. |
| OCR/strukturim i plote i listave te skanuara | Kerkohet vendim | Raw scans jane private; per sistem duhen import templates te sanitizuara. |
| Pajisja Lacto scan integration | Kerkohet verifikim teknik | Duhet pare a eksporton file, port, printer output, apo vetem letre. |

## 5. Ndikimi ne module dhe faza

### Faza 0 / Evidence + PoC

- Ruaj vendimin qe formula USAID eshte zyrtare dhe e pandryshueshme ne logjike.
- Mbaj parametrat reale jashte GitHub/Notion/Slack.
- Shto test fixtures private per validim 1:1 me Excel (te gjithe rreshtat aktive; numri i sakte private-local).
- Mbyll bazen ligjore per TVSH e sheshte/NIF dhe hap vetem sign-off kontabel per posting/konto/liber blerje.
- Pergatit modelin per pranim, moster, lab approval, shlyerje mujore.

### KABI MVP

- Master data: fermer, marrëveshje fermeri, produkt, repromaterial, recepturë, klient, furnitor, automjet.
- Pranim qumeshti me moster.
- Lab/QC me aprovime dhe arsye refuzimi.
- Formula USAID per shlyerje.
- Avanse, penalty, bonus opsional.
- Faturim ne terren.
- Kthime me arsye/note krediti.
- Depo mobile dhe barazim shoferi.
- FEFO dhe LOT/batch.
- Raporte ATK dhe payroll/kontribute.

### KABI Advanced

- Integrim direkt me Lacto scan nese teknikisht e mundshme.
- Import OCR/scan per dokumente te vjetra.
- Traceability recall nga fermer/lot/tank/batch/klient.
- KPI per cilesi qumeshti, prodhim, humbje dhe kthime.

## 6. Rregulla privatësie per kete evidence

- Raw PDF scans nuk futen ne GitHub.
- Listat me emra klientesh/furnitoresh/fermeresh mbeten private-local.
- Cmimet, barkodet, IBAN, NUI/NIF personal, dhe numrat reale nuk publikohen ne Slack/Notion.
- Ne Notion/Slack lejohet vetem statusi i sanitizuar: cilat gates u mbyllen, cilat mbeten hapur, dhe cfare ndikon ne modul.
- Per implementim, te dhenat reale futen vetem ne tenant config / private seed / import te kontrolluar.

## 7. Vendimi i ketij update-i

KABI operational evidence tani eshte mjaftueshem e forte per te kaluar nga pyetje te hapura te pergjithshme ne specifikim konkret te modulit KABI:

1. Pranim qumeshti + moster per fermer.
2. Lab/QC approval.
3. Formula USAID zyrtare.
4. Shlyerje mujore fermeri.
5. Master data produkte/repromaterial/BOM.
6. Van sales + depo mobile + barazim.
7. Kthime + note krediti + kartela.
8. ATK books + payroll si pjese e fazes se pare.

Gate kryesor i mbetur para posting-ut final kontabel per fermeret nuk eshte me ekzistenca e skemes; ajo u gjet ne burim zyrtar. Mbetet sign-off i kontabilistit per konto, dokument blerjeje, fusha te librit te blerjes dhe raportim mujor.
