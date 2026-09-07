using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Idata.Platform.Modules.Identity.Infrastructure;

/// <summary>
/// Politikë e KUFIZUAR e log-eve për sipërfaqen e protokollit Identity (B.2c — correction C1).
///
/// **Çfarë është:** **SUPPRESSION e EVENT-EVE** (jo redaktim, jo maskim) — çdo event nën nivelin e
/// ruajtur të kategorive të bibliotekës së protokollit nuk lëshohet fare drejt asnjë sink-u të
/// konfiguruar. Arsyeja: ato kategori emetojnë dump-e të PLOTA kërkese/përgjigjeje të protokollit
/// (p.sh. kërkesa e token-it me `code_verifier` dhe `code`), ndërsa redaktimi i bibliotekës nuk i
/// mbulon të gjithë parametrat.
///
/// **Si imponohet (determinist, jo shpresë):** rregullat e filtrit vlerësohen me përparësi
/// (a) provider-specific mbi provider-agnostic, pastaj (b) kategoria më e gjatë, pastaj (c) rregulli
/// i shtuar më vonë. Prandaj politika (1) HEQ çdo rregull ekzistues që prek kategoritë e mbrojtura —
/// për çdo provider, përfshirë rregullat me wildcard `*` që mund t'i prekin — dhe (2) shton rregullin
/// autoritativ provider-agnostic PLUS një rregull për SECILIN provider që ka rregulla në konfigurimin
/// efektiv, që asnjë rregull provider-specific me kategori më të shkurtër (p.sh. `null`) të mos e
/// mposhtë. Ekzekutohet si <c>PostConfigure</c>, pra PAS çdo <c>Configure</c> (appsettings,
/// `SetMinimumLevel`, `AddFilter`).
///
/// **Nuk e zbut një politikë më strikte:** niveli i ruajtur është MAKSIMUMI midis
/// <see cref="MinimumRetainedLevel"/>, nivelit më strikt të rregullave të hequra për këto kategori
/// dhe default-it global (rregulli me kategori `null`) — pra një operator që ka vendosur `Error`
/// ose `None` nuk zbutet në `Warning`.
///
/// **Kufijtë e DEKLARUAR (jo garanci universale):**
///  - ÇDO <c>PostConfigure&lt;LoggerFilterOptions&gt;</c> i regjistruar PAS kësaj politike që shton një
///    rregull me kategori të barabartë ose më të gjatë (ose provider-specific) e mposht atë — rendi i
///    regjistrimit vendos; kjo politikë nuk pretendon të jetë e fundit;
///  - mbulon VETËM prefiksin e kategorive <see cref="SuppressedCategoryPrefixes"/>. Nuk preken:
///    hosting/request diagnostics (rreshti standard `Request starting … {QueryString}` — një kredencial
///    i vendosur nga klienti në URL logohet nga host-i PARA se protokolli ta refuzojë; prandaj B.2c C1
///    e kufizon transportin e UserInfo te `Authorization: Bearer`, shih <see cref="UserInfoTransportGuard"/>),
///    EF Core command logging, ASP.NET Core Identity/Authentication/DataProtection dhe kategoritë e aplikacionit;
///  - reverse proxy-t dhe sink-et e jashtme mbeten jashtë kufirit të provuar;
///  - telemetria e dobishme RUHET: `Warning`/`Error`/`Critical` (përfshirë paralajmërimin e kufirit të
///    dështimit infrastrukturor) vazhdojnë të lëshohen. *(Efekt anësor i pranuar: log-et `Debug`/`Information`
///    të bibliotekës nuk dalin as në diagnostikë.)*
/// </summary>
internal static class IdentityProtocolLoggingPolicy
{
    /// <summary>Kategoritë e bibliotekës së protokollit që emetojnë dump-e kërkese/përgjigjeje.</summary>
    internal static readonly ImmutableArray<string> SuppressedCategoryPrefixes = ["OpenIddict"];

    /// <summary>Niveli më i ulët që MBETET i lejuar për ato kategori (kufi i poshtëm, jo i sipërm).</summary>
    internal const LogLevel MinimumRetainedLevel = LogLevel.Warning;

    internal static IServiceCollection AddIdentityProtocolLoggingPolicy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddLogging();
        services.AddSingleton<IPostConfigureOptions<LoggerFilterOptions>, Applier>();
        return services;
    }

    /// <summary>Zbatimi i politikës mbi opsionet REALE të filtrit (i testueshëm i izoluar).</summary>
    internal static void Apply(LoggerFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // (0) Provider-at që kanë rregulla në konfigurimin efektiv — një rregull provider-specific
        // me kategori `null` do ta mposhtte një rregull provider-agnostic me kategori të gjatë.
        var providers = options.Rules
            .Select(rule => rule.ProviderName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // (1) Niveli i ruajtur nuk e ZBUT një politikë ekzistuese më strikte: merret maksimumi midis
        // kufirit tonë, rregullave të mbrojtura që po hiqen dhe default-it global (kategori `null`).
        var retained = MinimumRetainedLevel;
        foreach (var rule in options.Rules)
        {
            if (rule.LogLevel is { } level && level > retained
                && (IsProtectedRuleCategory(rule.CategoryName) || rule.CategoryName is null))
            {
                retained = level;
            }
        }

        if (options.MinLevel > retained)
        {
            retained = options.MinLevel;
        }

        // (2) Hiq çdo rregull që prek kategoritë e mbrojtura (çdo provider, çdo nivel, përfshirë wildcard).
        for (var index = options.Rules.Count - 1; index >= 0; index--)
        {
            if (IsProtectedRuleCategory(options.Rules[index].CategoryName))
            {
                options.Rules.RemoveAt(index);
            }
        }

        // (3) Rregulli autoritativ + një rregull për secilin provider ekzistues.
        foreach (var prefix in SuppressedCategoryPrefixes)
        {
            options.Rules.Add(new LoggerFilterRule(null, prefix, retained, null));
            foreach (var provider in providers)
            {
                options.Rules.Add(new LoggerFilterRule(provider, prefix, retained, null));
            }
        }
    }

    /// <summary>A i përket kategoria e KËRKESËS sipërfaqes së mbrojtur (pa wildcard)?</summary>
    internal static bool IsProtectedCategory(string? category)
        => category is not null
           && SuppressedCategoryPrefixes.Any(prefix => category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A mund ta prekë kategoria e një RREGULLI sipërfaqen e mbrojtur? Trajton edhe semantikën
    /// `*` të framework-ut (prefiks/suffiks) dhe është KONSERVATIVE: nëse rregulli mund të përputhet
    /// me një kategori të mbrojtur, ai hiqet.
    /// </summary>
    internal static bool IsProtectedRuleCategory(string? ruleCategory)
    {
        if (ruleCategory is null)
        {
            return false; // rregulli global mbetet — nuk është i yni për ta hequr.
        }

        var wildcard = ruleCategory.IndexOf('*', StringComparison.Ordinal);
        if (wildcard < 0)
        {
            return SuppressedCategoryPrefixes.Any(prefix =>
                ruleCategory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || prefix.StartsWith(ruleCategory, StringComparison.OrdinalIgnoreCase));
        }

        var head = ruleCategory[..wildcard];
        return SuppressedCategoryPrefixes.Any(prefix =>
            head.Length == 0
            || prefix.StartsWith(head, StringComparison.OrdinalIgnoreCase)
            || head.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Applier : IPostConfigureOptions<LoggerFilterOptions>
    {
        public void PostConfigure(string? name, LoggerFilterOptions options) => Apply(options);
    }
}
