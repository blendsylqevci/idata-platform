# Faza 0 — RBAC / Permission Matrix (iData+)

**Versioni:** 1.0 · **Data:** 27 Qershor 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.3 §9
**Parime:** deny-by-default · per-feature configurable nga Tenant-Admin · **"Fshirje" s'ekziston për dokumente financiare** (vetëm Anulim/Korrigjim) · çdo veprim kritik → audit log · 2FA i detyrueshëm për admin & kontabilist.

## Veprimet (verbs)
`R` Lexim · `C` Krijim · `U` Modifikim · `X` Anulim/Korrigjim · `P` Shtypje · `A` Aprovim · **`E` Eksport · `Cl` Mbyllje (ditë/periudhë) · `Ro` Rihapje** · `—` pa qasje · `cfg` konfigurueshme

## Rolet
| Kodi | Roli | Shënim |
|---|---|---|
| PO | Platform Owner (iCode) | tenant + config teknik; **JO të dhëna biznesi** në punë normale |
| GO | Group Owner | raporte të konsoliduara KABI+PLANET; Company Switcher |
| TA | Tenant-Admin | qasje maksimale brenda një biznesi |
| KO | Kontabilist | GL, libra, TVSH, banka/arka, periudha |
| MN | Menaxher (sallë/njësi) | operim ditor + aprovime në pikë |
| MG | Magazinier | stok/depo, hyrje/dalje |
| TQ | Teknolog/QC (KABI) | pranim qumështi, laborator, prodhim |
| SH | Shofer/Shitës terreni (KABI) | van sales, POD, arkëtim |
| KM | Kamarier (PLANET) | porosi, tavolina |
| KU | Kuzhinier/KDS (PLANET) | bileta KDS |
| BR | Bar/Shank (PLANET) | porosi pijesh |
| AR | Arkatar (PLANET) | arka, pagesa, Z-raport |
| FO | **Financë Operative** (PLANET) | cash/barazime/POS/shpenzime/pagesa-ops/raporte ditore; **JO GL/TVSH/mbyllje** |
| IS | iCode Support | **vetëm break-glass** (shih më poshtë) |

---

## Matrica A — Bërthama / Kontabiliteti / Shared
| Moduli | PO | GO | TA | KO | MN | IS |
|---|---|---|---|---|---|---|
| Dashboard / raporte grupi | R | R | R(biznesi) | R | R | — |
| Master Data (artikuj, partnerë, çmime) | — | R | RCU | R | RC(cfg) | — |
| Plan kontabël / GL (ditari) | — | R | R | RCUX | — | — |
| Libra ATK (Shitje/Blerje) + Deklaratë TVSH | — | R | R | RCUXP | — | — |
| Arka / Banka (rakordim) | — | R | R | RCUXPA | R | — |
| Pagesa & alokim / aging (AR/AP) | — | R | R | RCUXP | R(cfg) | — |
| Mbyllje/hapje periudhe | — | — | — | **A (vetëm KO kryesor)** | — | — |
| Përdorues & Role (RBAC) | RCU(tenant) | R | RCU(biznesi) | — | — | — |
| SEF / Fiskalizim (outbox, statuse) | R(config) | R | R | RUXP(rikuponim) | R | — |
| Audit log | R | R | R | R | R | R |
| Cilësime sistemi/teknike | RCU | — | R(biznesi) | — | — | — |

## Matrica B — Operacionet KABI
| Moduli | TA | MN | MG | TQ | SH |
|---|---|---|---|---|---|
| Pranim qumështi / QC laborator | R | R | R | **RCU+A (Pass/Fail)** | — |
| Fermerë / Punki / kartela | RCU | R | — | RC(pranime) | — |
| Urdhër prodhimi / batch / pasterizim | R | R | R | RCU | — |
| Recetura / normativ (BOM) | RCU(A) | R | — | RC(propozim) | — |
| Stok / depo / transfer | R | R | RCUX | R | — |
| Stok automjeti (van) / ngarkim | R | R(A) | RC(ngarkim) | — | R(vetja) |
| Shitje terreni / faturim / POD | R | A(zbritje/kredi) | — | — | RC P |
| Çmime / zbritje (override) | A | **A (PIN)** | — | — | — |
| Write-off / korrigjim stoku | A | A | C(propozim) | — | — |

## Matrica C — Operacionet PLANET
| Moduli | TA | MN | KM | KU | BR | AR |
|---|---|---|---|---|---|---|
| POS / porosi / tavolina | R | RU(A) | RC(vetja) | — | RC(pije) | R |
| Anulim artikulli i dërguar | A | **A (PIN)** | C(propozim+arsye) | — | C(propozim) | — |
| Transfer / bashkim tavoline | A | **A (çdo orë)** | C(kërkesë) | — | — | — |
| Zbritje | A | **A (PIN)** | — | — | — | C(kërkesë) |
| KDS (bileta, statuse) | R | R | R(njoftim) | RCU(vetja) | RCU(shank) | — |
| Shfaq faturën / Paguaj / mbyll | R | A(rihapje) | C(paguaj) | — | C | RC P |
| Arka / Z-raport / barazim | R | A | — | — | — | **RCUXP** |
| Stok restorant/shank / recetura | R | R(A) | — | — | RU(shank) | — |

---

## Financë Operative (FO) — rol i veçantë nga Kontabilisti
Vepron kryesisht në modulin **Finance Operations** (`08_Finance_Operations_SCOPE`): **R/C/U + Aprovim(ops)** mbi arka/cash, barazim kamarierë/shank, POS/banka, shpenzime (regjistrim), pagesa furnitorësh (ops), raporte menaxheriale, cash handover/deposit, audit(R). **JO** GL/journal, libra ATK, deklaratë TVSH, mbyllje periudhe, postim/reversal (= **KO**). Korrigjim barazimi: kryen-me-audit **ose** propozon (pyetja #13). Handoff: kamarieri mbyll → FO barazim → KO poston.

## Matrica e aprovimeve (default)
| Veprimi | Kush aprovon |
|---|---|
| Ndryshim çmimi / zbritje / anulim / kthim | **PIN i menaxherit** (POS/terren) |
| Write-off / korrigjim stoku | **Admin ose Kontabilist** |
| Rihapje fature e mbyllur (PLANET) | Menaxher/Pronar |
| Hapje/mbyllje periudhe kontabël | **Vetëm Kontabilisti kryesor** |
| Ndryshim çmimi master (jo offline) | Vetëm nga cloud (TA/financa) |

## Break-glass (iCode Support)
Qasja e iCode në të dhënat e biznesit **kërkon**: (1) aprovim i Group Owner, (2) arsye e shkruar, (3) **kohë e kufizuar** (skadon vetë), (4) **audit log** i plotë. **Pa backdoor.** Default: IS s'ka asnjë qasje në të dhëna biznesi.

## Shënime zbatimi
- Çdo qelizë është **default**; Tenant-Admin e rregullon per-feature (R/C/U/X/P/A).
- "Vetja" = sheh/vepron vetëm mbi të vetat (p.sh. kamarieri tavolinat e veta) — konfigurueshme te "tërë salla".
- Dokumentet financiare: asnjë `Delete`; vetëm `X` (anulim/korrigjim/notë krediti) me audit.
