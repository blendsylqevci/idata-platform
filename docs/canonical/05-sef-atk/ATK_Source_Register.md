# ATK Source Register — regjistri i burimeve normative SEF

**Versioni:** 1.0 · **Data:** 2026-07-10 · **Pronari:** Blend · **Statusi:** canonical
**Qëllimi:** një regjistër i vetëm i burimeve zyrtare (normative + teknike) mbi të cilat mbështetet pajtueshmëria SEF e iData+. Çdo audit/PR normativ verifikohet kundrejt këtij regjistri (shih §4).

---

## 1. Enumerimi i portalit (leximi FRESH: 10.07.2026)

**Portali:** `https://apps.atk-ks.org/sefaplikimi` — më 10.07.2026 lista përmbante **7 dokumente gjithsej** (kategoritë e portalit: Ligj · UdhezimAdministrativ · SpecifikaTeknike · TeTjera). Datat "Portali" më poshtë janë **të lexuara FRESH nga vetë portali gjatë ekzekutimit**; datat e nënshkrimit/fuqisë janë të lexuara nga vetë PDF-të (faqet e skanuara u renderuan). Hash-et sha256 janë llogaritur nga shkarkimi i freskët i të njëjtës ditë dhe shfaqen **të grupuara në blloqe 8-karakterëshe** (stil fingerprint-i; edhe shmang false-positive të skanerëve të shifrave) — **për krahasim me `shasum -a 256` hiqi hapësirat**.

| # | Titulli zyrtar | URL (`https://apps.atk-ks.org/sefaplikimi/uploads/documents/…`) | sha256 | Faqe | Portali | Nënshkruar / në fuqi | Supersedes / amends |
|---|---|---|---|---|---|---|---|
| 1 | **Ligji Nr. 08/L-257 "PËR ADMINISTRIMIN E PROCEDURAVE TATIMORE" (LAPT)** — titulli i portalit "Ligji Nr.08/L-257 – LIGJ PËR ATK" është **i pasaktë/i shkurtuar**; titulli zyrtar lexohet nga vetë PDF-i (Gazeta Zyrtare) | `…/50ac31d7-30ef-4f8b-9cec-a321f8f8a046.pdf` | `2b8de966 a3753629 abcb1cab 72e2456d 3f25a56b e79cc0e7 e9f7f0ba 6400c685` | 69 | 31.03.2026 (datë ngarkimi në portal) | **Publikuar juridikisht 12.01.2024** — Gazeta Zyrtare Nr. 5/2024 | — (ligji bazë; **këtu referojnë sanksionet e UA N49**) |
| 2 | **Udhëzim Administrativ (MF) Nr. 01/2026 për Shfrytëzimin e PEF/SF/SEF** | `…/87a43adf-6ee2-42e5-a5af-043c031f514f.pdf` | `70792e81 833bda72 285ee088 582e7e97 98c03340 58afb785 a105c4ae 68a52978` | 40 | 14.05.2026 | — (shih vetë UA-në për datën e miratimit) | — |
| 3 | **Kërkesat specifike teknike dhe funksionale për PEF/SF/SEF ("KT", Dok. Nr. 01-06-1967)** | `…/150ab6cc-084f-490d-b5fc-d8639d0287dc.pdf` | `69ac172e 1903dfca 9dac4f10 b4773f96 458bcb68 96da3b88 c49d4418 03340962` | 88 | 01.06.2026 | **Miratuar 29.05.2026** (sipas Nenit 1 të amendamentit — "Dokument bazë") | **Amendohet nga #6** (Nenet 25 dhe 26.4) |
| 4 | **Udhëzues i përdorimit të sistemit për Testim dhe Aplikim për Certifikim të SEF** | `…/1e869fe5-02a2-4471-95a8-0ec6844d6f12.pdf` | `3dc47ab1 0d5da241 8a0b6eab 9ae6f1b0 0afa2b59 6c8a1abc 51c0d3eb 661e39e7` | 5 | 04.06.2026 | — | — |
| 5 | **KËRKESË PËR CERTIFIKIM DHE MIRËMBAJTJE TË SOFTUERËVE ELEKTRONIK FISKAL (SEF)** (forma e aplikimit) | `…/1ae65d61-178f-4669-8b8a-c7056811a3ba.pdf` | `b7fbf2ec 7d2e247d 0321c582 3602f610 849e75ac fe2d800b a0370fed 00854df8` | 1 | 04.06.2026 | — | — |
| 6 | **Ndryshim-Plotësimi i Kërkesave Specifike, Teknike dhe Funksionale për PEF/SF/SEF** ("Ndryshim-plotësimi i Dokumentit Nr. 01-06-1967 i datës 29.05.2026"; vula e arkivës Nr. Prot. **01-06-2194**) | `…/4994a4cd-75c8-46fd-bb7c-d541e4aa0e40.pdf` | `1d2aa83c d389e095 8d6c84f0 77bb8610 8e8176e4 8f976fcd e4a782af 486797e2` | 3 | 22.06.2026 | **Nënshkruar 22.06.2026; protokolluar 24.06.2026; NË FUQI nga 22.06.2026** (Neni 5: hyn në fuqi në datën e nënshkrimit nga Drejtori i Përgjithshëm i ATK-së) | **Amendon #3**: Neni 25 (Instalimi i SEF) + Neni 26 par. 4 (Ruajtja e të Dhënave); Neni 4 shfuqizon pikat përkatëse të Dokumentit bazë |
| 7 | **Kushtet dhe procedurat për aplikimin, certifikimin dhe mirëmbajtjen e SEF** | `…/0d8c2056-98ad-403a-a685-57e8cf33d1cc.pdf` | `38e3e086 18807c52 990b5dc1 2e1be63f 738cfc5c 93e7bb8d 50df9045 24f8b9e2` | 7 | **03.07.2026** (leximi FRESH 10.07.2026) — **⚑ MOSPËRPUTHJE E FLAMURUAR:** snapshot-e të pavarura të së njëjtës ditë (10.07.2026) kanë raportuar data të ndryshme liste (01.06 vs 03.07); regjistrohet çfarë u lexua në ekzekutim, **pa korrigjim me hamendje** — rilexohet në enumerimin e radhës | **Nënshkruar + NË FUQI 03.07.2026** (Neni 9: hyn në fuqi në datën e nënshkrimit nga Drejtori i Përgjithshëm; vula e arkivës me datë 03.07.2026) | — (dokumenti procedural i certifikimit; Neni 8: ATK verifikon funksionimin pas instalimit; Komisioni pezullon/tërheq/anulon certifikatën) |

*Shënim:* portali shfaq edhe numëratorë shikimesh per dokument — vlera **jo të qëndrueshme**, nuk regjistrohen. **MOS fut hamendje** për boshllëqe të numërimit "Download/N" apo për dokumente që s'shfaqen në listë.

## 2. Burimi TEKNIK (veçmas nga burimet normative)

| Burimi | Çfarë jep | Pin-i aktual |
|---|---|---|
| **`github.com/fiskalizimi`** (repo zyrtare teknike e ATK-së) | protokolli i integrimit: `models.proto`, shembulli `pos-csharp`, nënshkrimi ECDSA P-256, QR, endpoints | `pos-csharp @ a4060ef08838` · `models.proto` sha256 `fc42f76e 353d23b5 75c989d1 86b05671 a340f584 5fc83043 06bdfac0 5cf68efb` (i vendor-uar në `packages/database/proto/sef-models.proto` me drift-test) |

Ky burim është **teknik** (si integrohesh), jo normativ (çfarë detyrohesh) — konfliktet normative zgjidhen nga dokumentet e §1 sipas hierarkisë së burimeve në `PROJECT_RULES.md` §6.

## 3. Përmbajtja e amendamentit (dok. #6) — citim besnik

*Tekstet më poshtë janë citime besnike nga PDF-i i skanuar (faqet u renderuan gjatë ekzekutimit); drejtshkrimi i origjinalit ruhet [sic].*

**Neni 2 — tek Neni 25 i Dokumentit bazë, "Instalimi i SEF":** formulimi i vjetër (*"SEF duhet të ketë mundësi të instalohet në pajisje të ndryshme, duke përfshirë kompjuterë desktop, laptopë, tableta dhe telefona mobilë etj., me ndërfaqe të përshtatura për secilën pajisje përkatëse"*) **ndryshohet dhe plotësohet me formulimin e ri:**

> *"Instalimi i SEF: SEF duhet të mundësojë instalimin dhe përdorimin së paku në njërën nga pajisjet apo ambientet e punës, duke përfshirë, por pa u kufizuar në: kompjuter personal (PC), laptopë, tablet, telefon mobil të mençur, apo pajisje të tjera elektronike të përshtatshme për funksionimin e tij."*

**Neni 3 — tek Neni 26, paragrafi 4 (*Ruajtja e të Dhënave*):** përmbajtja e vjetër (4 pika — përfshirë *"Të gjitha të dhënat që ruhen lokalisht për arsye të ndryshme, duhet të ruhen të enkriptuara duke përdorur standardet e avancuara të enkriptimit."* dhe *"Qasja në të dhëna e pa transmetuara duhet të mundësohet vetëm me kërkesë të ATK-së apo ndonjë autoriteti tjetër shtetëror."*) **ndryshohet dhe plotësohet me tekstin si në vijim (3 pika):**

> - *"SEF duhet të ruajë të dhënat e transaksioneve në mënyrë të mbrojtur, duke garantuar integritet te te dhënave në rast defekti ose ndërprerjeje të papritur të funksionimit;"*
> - *"Në rast mungese të internetit, pajisja ruan të dhënat dhe i dërgon automatikisht sapo të rikthehet lidhja;"*
> - *"Të dhënat e pa transmetuara ne Sistemi Informativ i ATK-së duhet të mundësohet te eksportohen ne formate te përshtatshme si: CSV, PDF vetëm me kërkesë të ATK-së apo ndonjë autoriteti tjetër shtetëror."*

**Pasoja normative:** (i) **enkriptimi at-rest NUK është më kërkesë eksplicite e KT-së** — kërkesa e re = **integriteti i garantuar** i të dhënave në defekt/ndërprerje; (ii) dërgimi automatik pas rikthimit mbetet; (iii) **kërkesë e RE**: **eksport CSV/PDF i të patransmetuarave VETËM me kërkesë** të ATK-së/autoritetit shtetëror. (Neni 4 i amendamentit shfuqizon pikat përkatëse të Dokumentit bazë.) Zbatimi → `ATK_Compliance_Gate.md`.

## 4. Rregulli i procesit (i detyrueshëm)

> **Para çdo auditi/PR-je normative:** enumero portalin **komplet**; krahaso snapshot-in (titull/URL/hash/datë) me këtë regjistër; **çdo ndryshim = gjetje e re** (dokument i ri, hash i ndryshuar, datë e zhvendosur, titull i ndryshuar) — regjistrohet këtu me datën e leximit dhe trajtohet në PR-in përkatës. Mospërputhjet mes snapshot-eve **flamurohen, nuk korrigjohen me hamendje**.

Zbatimi në procesin e review-t: `PM_REVIEW_PROTOCOL.md` (checklist SEF v1.3).

## 5. Lidhjet

`ATK_Compliance_Gate.md` (regjistri i kërkesave me gate) · `Kupon_Print_Spec.md` · SSOT §7 · Tracker (`ATK-005`, `ATK-010`, `ATK-011`).
