# iData+ — Design Brief & Prompt (UI/UX Prototype)

> **Faza:** vetëm **UI/UX prototype / design exploration**. JO implementim final, JO backend, JO database, JO Supabase real, JO business logic. Qëllimi: pamja + përvoja e platformës. Output = **dizajn-ekrane + komponentë + mini style guide**, desktop + mobile, me **mock data**.
>
> ⚠️ **KJO ËSHTË UI E APLIKACIONIT (in-product), JO website / landing marketing.** Fillo nga ekranet brenda produktit (Login → Zgjedhja e biznesit → Dashboard → modulet). **MOS ndërto faqe marketingu/landing.**

---

## 1. Produkti
- **Emër (i përkohshëm):** iData+
- Platformë **multi-company / multi-tenant** për Kosovë; **një produkt i vetëm**, jo dy.
- Bizneset e para: **KABI** (fabrikë qumështi + fermerë + prodhim + stok automjeti + distribuim) dhe **PLANET** (restorant + POS + KDS + stok + kontabilitet).
- **Gjuha e UI: Shqip.**

## 2. Drejtimi vizual (përshkrim i referencës — imazhet nuk janë në projekt)
Referencat janë nga një **Warehouse/Inventory SaaS modern** (stil "WareHub"). Përdore vetëm si **drejtim**, mos i kopjo 1:1. Karakteristikat e tij:
- **Hero i errët** (near-black `#01040C`) me një **gradient të butë sky/azure** dhe një **screenshot të platformës** që noton mbi të.
- Seksione të bardha/gri të çelët me **karta të bardha**, border shumë i lehtë, **hije shumë të buta**, qoshe të **rrumbullakosura të mëdha**.
- **Shumë whitespace**, ikona **lineare minimaliste**, tipografi **Inter Tight** e fortë.
- Një kartë çmimi e theksuar me **azure**. Footer me wordmark të madh.
- Ndjesi **premium, e pastër, moderne, mobile-first** — JO admin-template i lirë, JO ERP klasik .NET/Java me tabela të ngjeshura.

## 3. Paleta + Tipografia
```
Midnight Velvet  #01040C   (sidebar, header blocks, premium contrast)
Ivory / White    #FFFFFF   (karta)
Moonlit Gray     #F5F5F5   (sfondi i app-it)
Azure Dream      #2463EB   (PRIMARY: veprime, charts, theks)
Light Azure      #609FFF   (fundi i gradientit: linear-gradient(135deg,#2463EB,#609FFF))
Text dark        #111827   Text muted #6B7280   Border subtle #E5E7EB
Success #16A34A  Warning #F59E0B  Error #DC2626
```
- **Font kryesor:** Inter Tight (look modern SaaS). Body/tabela: Inter ose Geist Sans nëse lexohet më mirë.
- **tabular numbers** për: shuma, NUI, POS ID, numër fature, journal ID, sasi.
- Tabelat kontabël: **moderne dhe të lexueshme**, jo Excel i vjetër — por **ruaj seriozitetin kontabël** (header i grupuar, numra djathtas, rresht totali).
- Gradient **vetëm azure** (kurrë purple/pink AI).

## 4. Mini style guide / komponentët (dy regjistra)
**Komponentë:** cards, tables, badges, forms, modal, sidebar, topbar, tabs, filters, command/search, charts, mobile bottom actions.
- **Buttons:** primar = gradient azure (radius ~10px); sekondar = i bardhë me border `#E5E7EB`; destruktiv = `#DC2626`.
- **Cards:** të bardha, radius 16px, border `#E5E7EB`, hije `0 4px 16px rgba(2,6,23,.05)`.
- **Status badges:** Fiskalizuar (success), Në pritje fiskalizimi (warning), Lëshuar offline (azure), Dështuar (error), Korrigjuar/Anuluar (gri).
- **Sidebar:** Midnight `#01040C`, item aktiv azure; topbar i bardhë.
- **DY REGJISTRA:**
  - **Ajror** (dashboard, përmbledhje, mobile, hero): rounded, soft, gradient azure, whitespace.
  - **I dendur por modern** (tabela kontabël/stok): header i grupuar, tabular-nums, rresht totali, toolbar + status bar — serioz, jo Excel i vjetër.

## 5. Ekranet (fillo nga APLIKACIONI, jo landing)
> Fillo gjithmonë nga **Login** dhe ekranet brenda produktit. Landing-u është **OPSIONAL dhe i fundit** — jo website tani.

1. **(OPSIONALE — e fundit, JO tani) Landing / hero:** vetëm nëse kërkohet shprehimisht më vonë; nuk është prioritet dhe nuk është website marketing në këtë fazë.
2. **Login + 2FA:** premium, clean, i thjeshtë — email, fjalëkalim, kod 2FA. Jo formë e vjetër.
3. **Zgjedhja e biznesit:** karta KABI / PLANET; subjekt aktiv, NUI, rol, njësi/depo; shumë e pastër.
4. **Main Dashboard:** sidebar dark/navy + content i bardhë; karta për Shitje, Blerje, TVSH, SEF, Stok, Detyra kritike; charts moderne; status badges.
5. **Inventory / Warehouse Dashboard:** stil i referencës; karta për stok total, lëvizje, produkte me skadencë, rezervime, automjete; grafikë + progress bars + stock status.
6. **KABI Mobile — Pranim qumështi:** nga fermeri / punëtori / **Punki**; litra, yndyrë, temperaturë, tank, status; app modern mobile, big touch targets.
7. **KABI — Tank & Prodhim:** tank 10,000L; teknologu vendos alokimin (jogurt/djathë/kos/ajran); batch/LOT, pasterizim, yield, skadencë; vizualisht intuitiv.
8. **KABI — Stok Automjeti / Ambulant:** automjeti = depo mobile; ngarkim, shitje, kthime, stok i mbetur, mbyllje ditore; status offline; POD/foto/GPS si badges.
9. **Accounting Dashboard:** modern por serioz; debit/kredit, TVSH input/output, AR/AP, periudha kontabël, raporte mujore.
10. **Libri i Blerjes (ATK):** summary cards + tabelë moderne; kolona: data, nr fature, shitësi, nr fiskal, nr TVSH, blerje 18%, TVSH 18%, blerje 8%, TVSH 8%, total TVSH; buton Eksporto Excel; filtër periudhe; rresht totali.
11. **SEF Dashboard:** fiscal outbox; statuset (në pritje fiskalizimi / fiskalizuar / dështuar / lëshuar offline); alert 48h; POS ID, NUI, statusi i certifikatës; preview kuponi me QR.
12. **PLANET POS:** plan tavolinash, porosi aktive, pagesa, fiskalizim; pamje moderne për tablet/desktop.
13. **PLANET KDS:** sektorë Grill / Pizzeria / Expo; karta porosish; timer; statuse (e re / në përgatitje / gati / dorëzuar).

> Bëj **shembuj desktop + mobile** për ekranet ku ka kuptim.

## 6. Terminologjia + Anti-patterns
- UI **shqip**; terma legalë: **fiskalizuar · në pritje fiskalizimi · lëshuar offline · dështuar · korrigjuar · anuluar · audit log**. Mos përdor "hidden/undeclared/non-declared/mos deklarim".
- **Mos:** gradient purple/pink AI; stil "admin template i lirë"; pamje e ngurtë .NET/Java; tabela të ngjeshura si Excel i vjetër; emoji si ikona.

## 7. Output
- Dizajn-ekrane (hi-fi) + komponentë + **mini style guide** (colors, typography, buttons, cards, table style, status badges).
- Desktop + mobile. **Mock data**, pa backend/DB/Supabase/business logic.
- Qëllimi: të vlerësojmë nëse pamja është premium, e bukur, moderne, SaaS inventory/ERP enterprise — jo ERP klasik i vjetër.
