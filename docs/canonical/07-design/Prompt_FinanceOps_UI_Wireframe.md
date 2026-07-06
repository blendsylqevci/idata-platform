# Prompt — Finance Operations UI (wireframe / screen breakdown) për Claude Code Design / Figma

> **Faza:** vetëm **UI/UX prototype / design exploration** — **mock data**, pa backend/DB/business-logic. Output: **hi-fi wireframe-ekrane + komponentë + mini style-guide**, **desktop-first** (financa punohet në PC), me variante responsive. **APLIKACION (in-product), JO website/landing.**
> Burimi i specës: `iData_Plus_Faza0/08_Finance_Operations_SCOPE` + `09_Finance_Operations_Workflow_Spec`. Roli: **Financë Operative (Financë Operative)** — i ndarë nga kontabiliteti.

## 1. Konteksti
Moduli **Finance Operations** i platformës **iData+** (multi-company): kontrolli ditor i parasë për **PLANET** (restorant) dhe **KABI** (fabrikë). NUK është kontabilitet (GL/TVSH) — është shtresa operative para-GL: arka, barazime, POS/banka, shpenzime, pagesa furnitorësh, handover te kontabilisti. **Gjuha e UI: Shqip.**

## 2. Design system (I KYÇUR — mos e ndrysho)
- **Font:** Inter Tight (UI) · numra/ID me **tabular-nums** (JetBrains Mono opsionale për ID/kode).
- **Paleta:** sidebar/hero **Midnight Velvet `#01040C`** · sfond **`#F5F5F5`** · karta **`#FFFFFF`** · primar/aksent **Azure `#2463EB`→`#609FFF`** (gradient vetëm azure) · text `#111827`/`#6B7280` · border `#E5E7EB` · success `#16A34A` · warning `#F59E0B` · error/destruktiv `#DC2626`.
- **Light default + Dark toggle** (dizajno të dyja; kontrast AA në të dyja).
- **Dy regjistra:** **ajror** (dashboard/karta/KPI) + **i dendur modern** (tabela financiare: header i grupuar, rresht totali, numra djathtas tabular-nums, toolbar+status-bar — serioz, jo Excel i vjetër).
- Ikona **lucide** (pa emoji); radius karta 16px, butona ~10px; hije të buta `0 4px 16px rgba(2,6,23,.05)`.
- **Statuset (badge):** Hapur/Open · Barazuar/Reconciled · Dorëzuar/Handed-over · Në pritje aprovimi · Diferencë · Anuluar · Korrigjuar.

## 3. Shell (FO-00)
Sidebar Midnight me modulet (Dashboard, Dita financiare, Barazime, Arka/Zyra, POS/Banka, Shpenzime, Furnitorë, Handover, Aprovime/Audit, Raporte); topbar i bardhë me: **Company switcher** (PLANET/KABI/njësi), **konteksti i ditës financiare** (data + status Open/Closed), Universal Search, dark toggle, përdoruesi. Mbanderolë "offline" kur s'ka rrjet.

## 4. Screen breakdown (FO-01 … FO-13)
Për secilin: **Qëllimi · Layout (rajone) · Komponentët · Statuset (empty/loading/error) · Regjistri**.

- **FO-01 Daily Finance Dashboard** *(ajror)* — KPI cards: Cash në arka, Kartë/POS, Bankë, Shitje ditore, Shpenzime, **Diferenca**; lista "tavolina hapur/mbyllur", "barazime pending", alarme (>X). Chart i thjeshtë trend ditor (line) + ndarja cash/card/bank (bar). Empty: "S'ka të dhëna për këtë ditë".
- **FO-02 Dita Financiare** — hap/mbyll ditën operative; status timeline; kush e hapi/mbylli; lock pas mbylljes (rihapje = aprovim).
- **FO-03 Saldo/Float** — cakto float per kamarier/arkë (tabelë e dendur: punëtor, float dhënë, arkë, kohë); modal "Cakto float".
- **FO-04 Barazime — listë** *(i dendur)* — rresht per kamarier/arkatar: shitje, cash, kartë, bankë, **cash që duhet dorëzuar**, diferencë, status; filtër data; rresht **totali**; veprim "Hap barazimin".
- **FO-05 Barazimi i kamarierit — detaj** — drilldown **tavolinat → porositë** brenda tavolinës; ndarja e pagesave (cash/kartë/bankë); zbritje/anulime (me arsye); saldo/float; **cash due**; diferencë; veprime: dorëzo cash, shëno diferencë, **kërko korrigjim** (PIN/aprovim). Timeline "What happened?".
- **FO-06 Shank/Banak** — barazim per banak (i brendshëm/jashtëm), printer/POS/lokacion; aktivizim banak i jashtëm.
- **FO-07 POS Terminal Reconciliation** *(i dendur)* — pagesat me kartë sipas **terminalit/bankës**; përputhje me batch banke; flag **të pa-barazuara**; veprim "Përputh".
- **FO-08 Arka / Zyra e Parave (Safe)** — pozicioni i cash-it; hyrje/dalje; **kush mori para + arsye + aprovim**; transfer arkë↔zyra.
- **FO-09 Shpenzime / Cash Out** — listë + modal "**Regjistro shpenzim**" (shumë, kategori, arsye, foto/PDF, aprovim); statuset draft→aprovuar→postuar (postimi = anë e kontabilitetit, vetëm tregohet).
- **FO-10 Pagesa Furnitorësh (ops)** — fatura pa-paguara/pjesërisht; **paguaj një ose disa bashkë**; cash/bankë; kush pagoi; foto/PDF; histori çmimi.
- **FO-11 Bank Deposit / Cash Handover** — depozitim cash në bankë / transfer; gjurmë (kush/sa/kur/arsye).
- **FO-12 Aprovime & Audit** — radha e aprovimeve (zbritje/anulim/korrigjim/rihapje me PIN); **audit log append-only** (kush/çka/kur/para→pas) me filtër.
- **FO-13 Raporte** — **filtër datash**; cash/kartë/bankë; sipas kamarierit/shankut/tavolinës/artikullit; top produkte; anulime; zbritje; diferenca; furnitorë të papaguar; **Eksporto (Excel/PDF/CSV)**; rresht totali.

## 5. Komponentët (mini-libra)
KPI card · **data-table e dendur** (sortable me aria-sort, sticky header, **tabular-nums**, rresht totali, zebra opsionale, virtualizim për 50+ rreshta) · date-range filter · drilldown master→detail · status badges · modal (regjistro shpenzim / aprovim me PIN / cakto float) · confirm dialog (veprime destruktive me kuq) · command/search · audit timeline · empty/loading (skeleton) / error states · toolbar + status bar.

## 6. Rregulla UX (të detyrueshme)
- Çdo ekran: **një CTA primare** (azure), sekondaret të nënrenditura.
- **Para:** shfaq € të formatuar (p.sh. `€18.20`) me **tabular-nums**; numra djathtas; rresht totali. *(Shkalla teknike e SEF — items €0.0001 / totals cents — është shtresë e të dhënave, JO në UI.)*
- Tabela: sortable (aria-sort), empty-state kuptimplotë, **mos** Excel i ngjeshur.
- Forma: label të dukshme, validim on-blur, gabim afër fushës, helper text; required me yll.
- Veprime destruktive (anulim/diferencë/rihapje): **konfirmim + PIN/aprovim**, ngjyrë danger, audit.
- Loading >300ms → skeleton; feedback brenda 100ms te klikimet.
- Dark mode: kontrast i testuar veçmas; border/divider të dukshme në të dyja.
- Accessibility: kontrast ≥4.5:1, focus-rings, ikona me label, ngjyra jo e vetmja shenjë (shto ikonë/tekst te diferenca/status).
- Responsive: desktop-first (≥1280), por tablet/landscape të lexueshëm; pa horizontal-scroll në mobile.

## 7. Variantet KABI (të njëjtët komponentë)
Driver/Van Settlement (ngarkim→shitje→kthime→cash→kredi→mbetje→diferenca) · Cash Collection from Field · Mobile POS recon (offline+fiscal status) · Daily Factory Cash Office · Farmer Advances (referencë; barazim mujor = accounting).

## 8. Output i pritur
Hi-fi wireframe-ekrane për FO-00…FO-13 (desktop + variant tablet ku ka kuptim) + **mini style-guide** (colors/typography/buttons/cards/table/badges/states) + komponentët e §5. **Mock data shqip.** Pa backend/DB. Qëllimi: të vlerësojmë nëse pamja është **premium SaaS finance dashboard**, e qartë, e dendur ku duhet, ajrore ku duhet — jo website, jo ERP i vjetër.

## 9. Anti-patterns (mos)
Website/landing marketing · gradient purple/pink "AI" · admin-template i lirë · tabela si Excel i vjetër · emoji si ikona · ekran që përzien financën operative me GL/TVSH/deklarata (ato janë moduli **Accounting**, ekran tjetër).
