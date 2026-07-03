# Faza 0 — Accounting Compatibility Matrix (PRO DATA → iData+)

**Versioni:** 1.3 · **Data:** 28 Qershor 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.6 §4, §8
**Qëllimi:** kontabilisti i PLANET-it **të mos humbasë asgjë** nga PRO DATA. Çdo artefakt: çfarë riprodhojmë, nga e kemi baseline-in, statusi, kush validon.
**Legjenda statusi:** ✅ baseline i nxjerrë · 🟡 pjesërisht · ⏳ pending · 🔁 validim me kontabilistin

| # | Artefakt | Çfarë riprodhojmë në iData+ | Baseline / burimi | Status | Validues |
|---|---|---|---|---|---|
| 1 | **Libri i Blerjes** | format 29-kolonësh 1:1 + harta e kutive të Deklaratës ([43]/[K1]/[45]/[51]/[67]) | nxjerrë & verifikuar nga eksporti real PLANET2 | ✅ + 🔁 | Kontabilisti |
| 2 | **Libri i Shitjes** | format 1:1 + **harta zyrtare e kutive** ([9]/[10a-c]/[10]/[11] të liruara; [12]→18%; [14]→8%; [16-28]; [K1]/[K2]; Totali [30]) | **template zyrtar ATK (28.06)** `reference_atk/` + baseline PRO DATA | ✅ format | Kontabilisti |
| 3 | **Deklarata e TVSH-së (përmbledhja mujore)** | auto-gjenerim nga **TË DY librat** (input+output box-maps të plota) | template zyrtar + përmbledhja PRO DATA | ✅ hartë; 🔁 muaj real | Kontabilisti |
| 4 | **Plan kontabël** | klasat 1/2/4/6-7/8 + llogari kyçe; KABI zgjeron Klasa 9 | referencë PRO DATA (142/145 llogari) | ✅ | Kontabilisti |
| 5 | **GL / Ditari** | postim automatik nga dokumenti (debit=kredit) | ekstrakti i skemës së kontimit PRO DATA | ✅ | Codex + KO |
| 6 | **AR (blerës) + aging** | kartela klienti, saldo, aging | nga GL/dokumentet | 🟡 | KO |
| 7 | **AP (furnitorë) + aging** | kartela furnitori, saldo, aging | nga GL/dokumentet | 🟡 | KO |
| 8 | **Arka** | hyrje/dalje, saldo ditore, rakordim | PRO DATA | 🟡 | KO |
| 9 | **Banka** | rakordim, identifikim POS/terminal | PRO DATA + ekstrakte bankare | ⏳ (#6 te SSOT) | KO |
| 10 | **Artikuj + çmimore + UoM** | multi-UoM, catch-weight, kategori, TVSH | master-data PRO DATA | 🟡 | TA/financa |
| 11 | **Furnitorë** | regjistër + mapping emër↔artikull (smart matching) | master-data PRO DATA | 🟡 | TA |
| 12 | **Klientë** | regjistër + limite kredie | master-data PRO DATA | 🟡 | TA |
| 13 | **Kategori POS** | kategori produktesh PLANET | PRO DATA | 🟡 | TA |
| 14 | **Pagesa & alokim** | cash/kartë/bankë, alokim te faturat | PRO DATA | 🟡 | KO |
| 15 | **Stok / gjendje** | multi-depo/restorant/shank, lëvizje, korrigjime | inventar fillestar | ⏳ saldo hapëse | MG/KO |
| 16 | **COGS** | default mesatare e ponderuar + batch costing KABI | — | ⏳ (#9 te SSOT) 🔁 | Kontabilisti |
| 17 | **Kartela analitike** | per llogari/partner/artikull | PRO DATA | 🟡 | KO |
| 18 | **Saldo hapëse** | vetëm saldo në cutover 1 Jan 2027 (pa histori) | fabrika/kontabilisti | ⏳ | KO + Fabrika |
| 19 | **Normë e sheshtë [51]** | akt-blerje fermeri → kutia [51] | vërejtur te Libri real | 🟡 paraprak 🔁 | Kontabilisti/ATK |
| 20 | **Inter-company KABI→PLANET** | shitje/blerje normale fiskale 8% (jo doc special) | vërejtur (NUI KABI te blerjet 8%) | ✅ | Kontabilisti |
| 21 | **Forma zyrtare e Deklaratës TVSH** | për validim 1:1 | template marrë; mbetet **1 muaj REAL i plotësuar** | 🟡 | Kontabilisti |

## Rrjedha që duhet sqaruar me kontabilistin (SSOT #6)
BLV → blerje → bartja te librat → faturat e paguara me bankë → marrja e ekstraktit bankar → raportimi i pagesës + data + të dhënat në program. *(Formulimi i paqartë — sqarohet të mërkurën.)*

## Plani i validimit
- Baseline nga PRO DATA = **i vlefshëm për të nisur** (foundation scaffold s'bllokohet).
- **Test paralel 1 muaj** para go-live: librat e iData+ krahasohen 1:1 me PRO DATA + Deklarata e TVSH-së.
- **Firma e kontabilistit** për kolonat kyçe (#1–#5, #16, #18, #19) kërkohet **para Accounting Core / Go-live** — **JO bllokues për foundation scaffold (Faza 1)**.
- **Kodet e taksës (SEF):** **A=exempt** / C=0% / D=8% / E=18% (harta: T0→C, T8→D, T18→E, **TL→A** — konfirmo me kontabilist/ATK); librat ATK = kutitë [43]/[45]/[51]. Shih `06_SEF_Test_Phase_Active.md`.
