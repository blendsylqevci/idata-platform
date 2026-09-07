using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Idata.Platform.Modules.Identity.Infrastructure;

/// <summary>
/// Politikë e KUFIZUAR e log-eve për sipërfaqen e protokollit Identity (B.2c — correction C1,
/// e ridizajnuar në correction C2).
///
/// **Çfarë është:** një DYSHEME <see cref="MinimumRetainedLevel"/> (SUPPRESSION e event-eve nën
/// `Warning`, jo redaktim, jo maskim) VETËM për kategoritë e mbrojtura
/// (<see cref="ProtectedCategoryPrefixes"/>):
///  - `OpenIddict*` — biblioteka e protokollit emeton dump-e të plota kërkese në `Information`
///    (p.sh. kërkesa e token-it me `code_verifier`), dhe redaktimi i saj nuk e mbulon atë parametër;
///  - `Microsoft.AspNetCore.Hosting.Diagnostics` — host-i logon në `Information` rreshtat
///    `Request starting …{QueryString}` (EventId 1) dhe `Request finished …{QueryString}` (EventId 2),
///    pra URL-në e PLOTË të çdo kërkese PARA se protokolli ta refuzojë; një kredencial i vendosur
///    nga klienti në URL do të shkruhej te sink-et e host-it edhe kur kërkesa refuzohet me 400.
///    Të dy event-et janë të mbrojtura nga `IsEnabled(Information)` në thirrës (ASP.NET Core 10.0.10,
///    `HostingApplicationDiagnostics`), prandaj me dyshemenë as objekti i state-it nuk krijohet.
///    **Trade-off i deklaruar:** humbet telemetria e përmbledhjes së kërkesave (request summary) e
///    kësaj kategorie, si dhe event-i `Information` i kërkesave të patrajtuara (EventId 16, vetëm path);
///    event-et e saj `Warning`/`Critical` ruhen (sipas burimit të pinuar kategoria nuk emeton asnjë
///    event `Warning+` per-request — vetëm në startup).
///
/// **Si imponohet (mbi semantikën e pinuar të `LoggerRuleSelector`, .NET 10.0.10):** përzgjedhja e
/// rregullit efektiv për (provider, kategori) varet VETËM nga (`ProviderName`, `CategoryName`, pozicioni
/// në listë): provider-specific mbi provider-agnostic, pastaj kategoria më e gjatë, pastaj rregulli i
/// shtuar më vonë; rregulli fitues jep NJËKOHËSISHT nivelin dhe callback-un e filtrit (niveli
/// kontrollohet i pari, pastaj filtri). Politika NUK heq, NUK riorganizon dhe NUK ndryshon nivelin e
/// asnjë rregulli; ajo:
///  (1) mbështjell ÇDO rregull ekzistues NË VEND — i njëjti provider, e njëjta kategori, i njëjti nivel,
///      i njëjti pozicion (pra e NJËJTA përparësi) — duke i shtuar filtrit të tij dyshemenë: për një
///      kategori të mbrojtur event-i kalon vetëm nëse niveli ≥ `Warning` DHE filtri origjinal (nëse ka)
///      e lejon; për çdo kategori tjetër filtri i mbështjellë kthen saktësisht atë që kthente më parë;
///  (2) fut TË PARIN një rregull ekuivalent me rastin “asnjë rregull nuk përputhet” (kategori `null`,
///      niveli = `MinLevel`, vetëm dyshemeja), që çdo rregull `Default` i operatorit — i shtuar më vonë —
///      ta mposhtë sipas rendit normal, dhe që dyshemeja të vlejë edhe kur operatori s'ka asnjë rregull.
/// Ekzekutohet si <c>PostConfigure</c>, pra PAS `appsettings`/`SetMinimumLevel`/`AddFilter` (të gjitha
/// `Configure`), dhe ri-aplikohet mbi çdo instancë të re të opsioneve (reload i konfigurimit).
///
/// **Invariantet që rrjedhin nga ndërtimi (jo nga një maksimum global):**
///  - jashtë kategorive të mbrojtura vendimi efektiv është IDENTIK për çdo (provider, kategori, nivel):
///    pragjet, callback-et e filtrit dhe përparësia e rregullave ruhen — asnjë rregull i përbashkët
///    (p.sh. `Microsoft.AspNetCore`, `*`, `Microsoft.AspNetCore.Hosting.*`) nuk hiqet dhe nuk preket;
///  - brenda kategorive të mbrojtura politika VETËM kufizon event-et nën `Warning`: nuk riaktivizon
///    asnjë event që operatori e kishte ndaluar (`Error`/`None` mbeten `Error`/`None`) dhe nuk transferon
///    kufizimin e një provider-i/kategorie te një tjetër, sepse asnjë nivel nuk llogaritet as transferohet
///    mes provider-ëve/kategorish — i vetmi nivel i kopjuar është `MinLevel` efektiv, në rregullin fallback
///    (i ri-lexuar në çdo instancë të re të opsioneve).
///
/// **Kufijtë e DEKLARUAR (jo garanci universale):**
///  - një <c>PostConfigure&lt;LoggerFilterOptions&gt;</c> i regjistruar PAS kësaj politike që SHTON rregulla
///    të pambështjella mund ta anashkalojë, dhe një që NDRYSHON `MinLevel` (jo rregullat) nuk reflektohet
///    në rregullin fallback (rendi i regjistrimit vendos); konfigurimi normal i mbështetur (`appsettings`,
///    `AddFilter`, `SetMinimumLevel`, seksione per-provider, wildcard nga kodi ose nga konfigurimi) është
///    fazë `Configure` dhe nuk e anashkalon;
///  - dyshemeja vepron PARA shkrimit VETËM te sink-et e regjistruara përmes `ILoggerFactory`; kod që
///    zëvendëson infrastrukturën e log-eve pas saj (fabrikë tjetër logger-ësh, sink-e që nuk kalojnë nga
///    filtri i saj) është jashtë kufirit;
///  - mbulohen VETËM dy prefikset e mësipërme; çdo kategori tjetër ruan sjelljen e vet (p.sh.
///    `Microsoft.AspNetCore.Http.Result.RedirectResult` logon URL-në e destinacionit të redirect-eve në
///    `Information` — regjistruar si follow-up, jashtë këtij zarfi);
///  - reverse proxy-t, ingress-i dhe platformat e jashtme të log-eve nuk certifikohen këtu.
/// </summary>
internal static class IdentityProtocolLoggingPolicy
{
    /// <summary>Prefikset e kategorive të mbrojtura (përputhje prefiksi, case-insensitive si te framework-u).</summary>
    internal static readonly ImmutableArray<string> ProtectedCategoryPrefixes =
        ["OpenIddict", "Microsoft.AspNetCore.Hosting.Diagnostics"];

    /// <summary>Niveli më i ulët që MBETET i lejuar për kategoritë e mbrojtura (dysheme, jo mbivendosje).</summary>
    internal const LogLevel MinimumRetainedLevel = LogLevel.Warning;

    internal static IServiceCollection AddIdentityProtocolLoggingPolicy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddLogging();
        // Një regjistrim i vetëm (idempotent ndaj thirrjeve të përsëritura të composition root-it);
        // `Apply` është gjithsesi idempotente semantikisht mbi të njëjtën instancë.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<LoggerFilterOptions>, Applier>());
        return services;
    }

    /// <summary>Zbatimi i politikës mbi opsionet REALE të filtrit (provuar përmes host-it real; testet nuk shohin tipat `internal`).</summary>
    internal static void Apply(LoggerFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // (1) Mbështjellje NË VEND: provider/kategori/nivel/pozicion të pandryshuar → përparësi e pandryshuar.
        for (var index = 0; index < options.Rules.Count; index++)
        {
            var rule = options.Rules[index];
            options.Rules[index] = new LoggerFilterRule(rule.ProviderName, rule.CategoryName, rule.LogLevel, WithFloor(rule.Filter));
        }

        // (2) Ekuivalenti i rastit "asnjë rregull nuk përputhet" (MinLevel, pa filtër) + dyshemeja, I PARI
        //     në listë: çdo rregull `Default` i operatorit (i shtuar më vonë) e mposht sipas rendit normal.
        options.Rules.Insert(0, new LoggerFilterRule(null, null, options.MinLevel, WithFloor(null)));
    }

    /// <summary>A i përket kategoria e një logger-i sipërfaqes së mbrojtur? (loop pa closure — thirret në çdo `IsEnabled` nën `Warning`)</summary>
    internal static bool IsProtectedCategory(string? category)
    {
        if (category is null)
        {
            return false;
        }

        foreach (var prefix in ProtectedCategoryPrefixes)
        {
            if (category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Dyshemeja e kombinuar me filtrin origjinal: për kategori të mbrojtur kërkon nivel ≥ `Warning`;
    /// për çdo kategori tjetër është identike me filtrin origjinal (ose `true` kur ai mungon).
    /// </summary>
    internal static Func<string?, string?, LogLevel, bool> WithFloor(Func<string?, string?, LogLevel, bool>? inner)
        => (provider, category, level)
            => (level >= MinimumRetainedLevel || !IsProtectedCategory(category))
               && (inner is null || inner(provider, category, level));

    private sealed class Applier : IPostConfigureOptions<LoggerFilterOptions>
    {
        public void PostConfigure(string? name, LoggerFilterOptions options) => Apply(options);
    }
}
