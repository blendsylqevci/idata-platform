# Prompt për Claude Code — Prototip UI/UX (KABI & PLANET)

**Qëllimi:** ndërto një **prototip të klikueshëm UI/UX** (vetëm pamje, **mock data, pa backend**) që të shohim si duket platforma — web (dashboard + kontabilitet + master-data), mobile (kamarier + shofer/ambulant) dhe KDS.
**Stili = MIX:** bukuria moderne (WareHub: Inter Tight, Midnight Velvet + azure gradient, ajror) **+ soliditeti enterprise** (.NET/Java) te tabelat kontabël.

**Si ta përdorësh:** hap Claude Code te një folder bosh. Jep **PROMPT A**, prit të ndërtohet, ekzekuto `npm run dev` dhe shiko. Pastaj **PROMPT B**, pastaj **PROMPT C**.

---

## Design System (referencë për të gjitha promptet)

```
Stack: Next.js (App Router) + TypeScript + Tailwind CSS + shadcn/ui + lucide-react + recharts.
VETËM UI/prototip: mock data (fixtures TS/JSON), pa Supabase/NestJS/DB. Pa logjikë biznesi reale.

Font: "Inter Tight" (UI), "JetBrains Mono" (ID/kode teknike). tabular-nums për ÇDO numër (shuma, NUI, POS ID, nr. fature, journal ID, sasi).

Ngjyrat (tokens):
  --midnight #01040C   (sidebar / hero / footer — i errët)
  --bg       #F5F5F5   (sfondi i app-it)
  --card     #FFFFFF   --border #E2E8F0 / #EEF1F5
  --azure    #2463EB   --azure-2 #609FFF   (PRIMARY + gradient: linear-gradient(135deg,#2463EB,#609FFF))
  --text     #0F172A   --text-2 #475569
  --success  #16A34A   --warning #F59E0B   --error #DC2626

DY REGJISTRA (përdori sipas ekranit):
  • AJROR  → dashboard, përmbledhje, mobile, hero: karta rounded-16, hije shumë e butë
    (0 4px 16px rgba(2,6,23,.05)), hapësirë e bollshme, gradient azure te hero + butoni primar,
    ikona të holla.
  • DENDUR (enterprise) → tabela kontabël (Libri Blerjes/Shitjes, ditarë, stok): rreshta ~30px,
    header i grupuar, numra djathtas me tabular-nums, rresht TOTALI (navy/azure), header sticky,
    toolbar (filtra/eksport), status bar poshtë. Solidi si .NET/Java.

Rregulla: light DEFAULT (dark vetëm toggle i mëvonshëm); sidebar i errët #01040C me item aktiv azure;
ikona lucide (pa emoji); accessibility (kontrast, focus, tastierë); UI në SHQIP.
Terminologjia fiskale: fiskalizuar / në pritje fiskalizimi / lëshuar offline / dështuar / korrigjuar /
anuluar / audit log. (Mos: hidden/undeclared/mos-deklarim.)
White-label: accent azure si default; ndryshim i vogël theksi për subjekt, baza e njëjtë.
```

---

## PROMPT A — Scaffold + Design System + Shell + Auth + Dashboard

```
Krijo një projekt Next.js (App Router, TypeScript, Tailwind, shadcn/ui, lucide-react, recharts) si
PROTOTIP UI/UX me MOCK DATA (pa backend). Konfiguro design system-in sipas tokens-ave:
font Inter Tight + JetBrains Mono, ngjyrat (#01040C, #F5F5F5, #FFFFFF, #2463EB, #609FFF, #0F172A,
#475569, #E2E8F0, success/warning/error), tabular-nums për numra. Light default.

Ndërto:
1) Faqe "/" = INDEX i prototipit (galeri): kartela që linkojnë te çdo ekran (Login, Dashboard,
   Libri i Blerjes, Artikujt, Mobile Kamarieri, Mobile Shoferi, KDS) — që të shihen të gjitha shpejt.
2) /login — split screen: majtas panel i errët #01040C me gradient azure + logo "iCode" + slogan;
   djathtas formë e bardhë (Email, Fjalëkalim me toggle, "Më mbaj të kyçur", "Keni harruar…", buton
   primar me gradient azure "Kyçu"). Pa backend — submit → ridrejto te /select-business.
3) /select-business — dy karta (KABI / PLANET) me NUI dhe rolin; klik → /dashboard.
4) App Shell: sidebar i errët #01040C (logo + nav: Paneli, Pranimi i qumështit, Prodhimi, Stoku,
   Fermerët, Shitjet, Blerjet, Kontabiliteti, Raportet, Administrimi) me item aktiv azure; topbar i
   bardhë (kërkim, subjekti aktiv, notifikime, avatar). Responsive (sidebar → drawer në mobile).
5) /dashboard (REGJISTRI AJROR): hero me gradient azure ("Mirëmëngjes, Pronari" + qumësht i pranuar sot),
   4 KPI cards (rounded-16, soft shadow, tabular-nums, trend ▲/▼), një grafik recharts (pranimi i
   qumështit 7 ditë, shtylla azure), dhe një kartë "Statuset fiskale" me badge (Fiskalizuar/Në pritje/
   Lëshuar offline/Korrigjuar).

Mock data në /lib/mock (TS): user, subjektet me identifikues sintetikë (p.sh. KABI-MOCK, PLANET-MOCK), KPI, seritë e grafikut. Mos përdor NUI real në UI prototype.
Acceptance: `npm run dev` hap prototipin; navigimi punon; Inter Tight + azure gradient + light; pa backend.
```

---

## PROMPT B — Kontabiliteti (dense) + Master-data

```
Shto ekranet kontabël (REGJISTRI DENDUR ENTERPRISE) dhe master-data. Mock data.

1) /kontabiliteti/libri-blerjes — grid i dendur enterprise:
   - Toolbar: zgjedhës periudhe (muaj test), kërkim shitësi, Filtro, "Eksporto Excel",
     "Deklarata e TVSH-së", "+ Faturë blerje".
   - Tabelë me header të GRUPUAR: Identifikim (Nr, Data, Nr. fature, Shitësi, Nr. fiskal) |
     "Të tatueshme 18%" (Bazë, TVSH) | "Të tatueshme 8%" (Bazë, TVSH) | Totali TVSH.
   - Numrat djathtas, tabular-nums; rreshta ~30px; header sticky; hover.
   - Rresht TOTALI (sfond navy/azure): Bazë 18% XX,XXX.XX · TVSH 18% X,XXX.XX · Bazë 8% X,XXX.XX ·
     TVSH 8% XXX.XX · Totali XX,XXX.XX.
   - Poshtë: strip me kutitë e Deklaratës (Bazë18→[43], TVSH18→[K1], Bazë8→[45], Fermerë normë e
     sheshtë→[51], Totali→[67]) + status bar (XX fatura · XX shitës · periudhë test).
   - Mock rows neutrale: Furnitor A, Furnitor B, Furnitor C, Furnitor inter-company (8%, theksuar),
     Furnitor D, Furnitor E. Mos përdor emra realë furnitorësh ose vlera reale.
2) /kontabiliteti/fature-blerje — formë + rreshta artikujsh (mock), me nëntotale/TVSH/total (tabular-nums).
3) /artikuj — listë (kërkim/filtra) + panel detaji me tabs: Të dhëna, Çmimore (3 nivele), Normativ
   (me skrap %), Njësitë (konvertim UoM), Atributet (LOT/skadencë). Mock ~10 produkte KABI (Jogurt 1L,
   Djathë 800g [catch-weight], Ajran 0.5L, Kos 0.45L, Gjizë 800g…).
4) /partnere — listë partnerësh (furnitor/blerës/agjent) me NUI, limit kredie, çmimore; klientë zinxhir
   (qendër + lokacione).

Acceptance: tabela e Librit të Blerjes duket si sistem real kontabël (dendur, totale, kuti); master-data
me detaje; tabular-nums kudo; pa backend.
```

---

## PROMPT C — Mobile (kamarier + shofer) + KDS

```
Shto ekranet mobile dhe KDS. Mock data. Në desktop, shfaqi brenda kornizave telefoni (device frame) te
një faqe /mobile, dhe gjithashtu bëj routet realisht responsive.

1) /mobile/kamarieri (PLANET): header navy "Tavolina 12 · Sala A · 4 mysafirë"; kategori (Ushqime/Pije/
   Ëmbëlsira); lista e porosisë me stepper sasie (− n +) dhe çmime (tabular-nums); "Shto artikull";
   bottom bar me Total + buton primar gradient "Dërgo te kuzhina"; bottom nav (Tavolinat/Menu/Llogaria/Profili).
2) /mobile/shoferi (KABI): banner offline (azure) "Pa internet · 2 kuponë lëshuar offline · sinkronizohen
   automatikisht"; header "Klient zinxhir — Lokacion test · Pika 3/8 · GPS ✓"; artikuj me LOT + afat skadence +
   peshë reale (catch-weight); chips pagese (Para të gatshme/Borxh/Bankë); POD (Foto/Nënshkrim/GPS);
   Total (me TVSH 8%) + buton "Lësho faturën/kuponin".
3) /kds (PLANET): header navy me tabs Grill/Pizzeria/Expo + ora + numri i vonesave; grid tiketash:
   tavolina + kohëmatës (mono) + artikuj me modifiera + status, me kodim ngjyrash (E re #16A34A /
   Në punë #2463EB / Paralajmërim #F59E0B / Vonesë #DC2626); butona Nis/Gati; legjendë + status bar.

Stili: shell/mobile = ajror (rounded, soft, gradient azure te butonat primarë); KDS = i qartë, kontrast i mirë.
Acceptance: të tre ekranet duken realë dhe të klikueshëm; offline/LOT/POD/catch-weight të dukshëm; pa backend.
```

---

## Anti-patterns (mos i bëj)
- Mos e bëj të duket "website template" — është aplikacion biznesi (shell + të dhëna).
- Pa gradient purple/pink; gradient vetëm azure (#2463EB→#609FFF). Pa emoji (vetëm lucide).
- Numra pa tabular-nums; tabela kontabël "ajrore" (duhet dendur me totale).
- Pa backend/DB në këtë fazë — vetëm mock data dhe UI.

*Pas prototipit dhe miratimit të stilit, e kthejmë në Prompt-Pack v3 për ndërtimin real (me arkitekturën NestJS/Supabase).*
