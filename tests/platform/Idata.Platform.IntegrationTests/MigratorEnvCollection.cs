using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Testet që mutojnë env global (PLATFORM_PGDATABASE/DOTNET_ENVIRONMENT/opt-in)
/// dhe target DB-në reale ekzekutohen SERIALISHT — jo në paralel.
/// </summary>
[CollectionDefinition(nameof(MigratorEnvCollection), DisableParallelization = true)]
public sealed class MigratorEnvCollection;
