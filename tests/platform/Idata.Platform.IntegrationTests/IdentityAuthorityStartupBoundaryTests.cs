using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// A1 (C1) — kufiri i startup-it në nivel PROCESI: host-i REAL i API-së nën
/// `Production` me credentials persistent të vlefshme + `signing[1]` të
/// pangarkueshëm duhet të dalë me exit code jo-zero PARA se të lidhë ndonjë
/// port (zero "Now listening"), PAVARËSISHT emrit të DLL-së së entry-t —
/// përfshirë kopjet byte-identike me emrat e tool-it të OpenAPI-t
/// (`GetDocument.Insider.dll`, `dotnet-getdocument.dll`). Zero përjashtim
/// runtime sipas identitetit të procesit. Rasti "credentials të munguara"
/// NUK përdoret (një dështim tjetër i OpenIddict do ta fshihte bypass-in).
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class IdentityAuthorityStartupBoundaryTests
{
    private const string PfxPassphrase = "synthetic-process-pfx-passphrase";

    private static readonly string[] EntryNames = ["Idata.Platform.Api.dll", "GetDocument.Insider.dll", "dotnet-getdocument.dll"];

    [Theory]
    [InlineData("Idata.Platform.Api.dll", "signing1-unloadable")]
    [InlineData("GetDocument.Insider.dll", "signing1-unloadable")]
    [InlineData("dotnet-getdocument.dll", "signing1-unloadable")]
    [InlineData("Idata.Platform.Api.dll", "jose-incoherent")]
    [InlineData("GetDocument.Insider.dll", "jose-incoherent")]
    [InlineData("dotnet-getdocument.dll", "jose-incoherent")]
    public async Task Production_del_jo_zero_para_bind_it_pavaresisht_emrit_te_entry_dll(string entryName, string scenario)
    {
        // Dy skenarë me burim dështimi TË NDRYSHËM: (a) `signing[1]` i pangarkueshëm →
        // snapshot-i i regjistrimit (validatori 1); (b) credentials valide + allowlist
        // firme PS256 ndaj certifikatës RSA → koherenca JOSE mbi credentials REALE
        // (validatori 2) — dështimi mund të vijë VETËM prej andej.
        Assert.Contains(entryName, EntryNames);
        var joseIncoherent = scenario == "jose-incoherent";
        using var signing = SyntheticCertificates.Signing("CN=synthetic-process-signing");
        using var encryption = SyntheticCertificates.Encryption("CN=synthetic-process-encryption");
        var root = Directory.CreateTempSubdirectory("idata-b2a-startup-boundary-");
        try
        {
            var appDir = PrepareByteIdenticalCopy(root.FullName, entryName);
            var port = FreeTcpPort();
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["DOTNET_ENVIRONMENT"] = "Production",
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
                ["Identity__Authority__Issuer"] = "https://localhost/",
                ["Identity__Authority__Audiences__0"] = "synthetic-api",
                ["Identity__Authority__Jose__SignatureAlgorithms__0"] = joseIncoherent ? "PS256" : "RS256",
                ["Identity__Authority__Jose__KeyManagementAlgorithms__0"] = "RSA-OAEP",
                ["Identity__Authority__Jose__ContentEncryptionAlgorithms__0"] = "A256CBC-HS512",
                ["Identity__Authority__ClockSkewSeconds"] = "0",
                ["Identity__Authority__Credentials__Signing__0__Pfx"] = SyntheticCertificates.ToPfxBase64(signing, PfxPassphrase),
                ["Identity__Authority__Credentials__Signing__0__PfxPassword"] = PfxPassphrase,
                ["Identity__Authority__Credentials__Encryption__0__Pfx"] = SyntheticCertificates.ToPfxBase64(encryption, PfxPassphrase),
                ["Identity__Authority__Credentials__Encryption__0__PfxPassword"] = PfxPassphrase,
            };

            if (!joseIncoherent)
            {
                // signing[1] i PANGARKUESHËM — dështimi duhet të vijë nga snapshot-i i regjistrimit.
                environment["Identity__Authority__Credentials__Signing__1__Pfx"] = "this-is-not-base64!!";
                environment["Identity__Authority__Credentials__Signing__1__PfxPassword"] = PfxPassphrase;
            }

            var expectedClass = joseIncoherent ? "jose-signature-algorithm-mismatch:signing[0]:RS256" : "signing[1]";
            var (exitCode, exited, output, portOpened) = await RunAsync(appDir, entryName, environment, port, TimeSpan.FromSeconds(60));

            Assert.False(portOpened, $"Procesi '{entryName}' LIDHI portin {port} (u pranua lidhje TCP):{Environment.NewLine}{Redact(output)}");
            Assert.True(exited, $"Procesi '{entryName}' NUK doli brenda afatit (u vra) — host-i vazhdoi të ekzekutohej:{Environment.NewLine}{Redact(output)}");
            Assert.True(exitCode != 0, $"Procesi '{entryName}' doli me exit 0:{Environment.NewLine}{Redact(output)}");
            Assert.True(!output.Contains("Now listening", StringComparison.Ordinal), $"Procesi '{entryName}' LIDHI port para dështimit:{Environment.NewLine}{Redact(output)}");
            Assert.True(output.Contains("identity-credentials-policy", StringComparison.Ordinal), $"Dështimi i '{entryName}' nuk vjen nga policy-ja:{Environment.NewLine}{Redact(output)}");
            Assert.True(output.Contains(expectedClass, StringComparison.Ordinal), $"Dështimi i '{entryName}' nuk mbart klasën e pritur '{expectedClass}':{Environment.NewLine}{Redact(output)}");
            Assert.DoesNotContain(PfxPassphrase, output, StringComparison.Ordinal);
            Assert.DoesNotContain("this-is-not-base64", output, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Kopje byte-identike e host-it: të gjitha assemblies e test bin-it (përfshin
    /// çdo varësi të API-së), entry-i i emërtuar sipas rastit, runtimeconfig-u i
    /// API-së i kopjuar nën emrin e entry-t; pa deps.json (host-i probe-on
    /// direktorinë). `Idata.Platform.Api.dll` origjinal NUK kopjohet krahas
    /// entry-t të riemërtuar (identiteti i assembly-t mbetet i vetëm).
    /// </summary>
    private static string PrepareByteIdenticalCopy(string root, string entryName)
    {
        var source = AppContext.BaseDirectory;
        var apiDll = Path.Combine(source, "Idata.Platform.Api.dll");
        Assert.True(File.Exists(apiDll), $"Mungon {apiDll}");
        var appDir = Path.Combine(root, Path.GetFileNameWithoutExtension(entryName).Replace('.', '_'));
        Directory.CreateDirectory(appDir);
        foreach (var file in Directory.EnumerateFiles(source, "*.dll"))
        {
            if (string.Equals(Path.GetFileName(file), "Idata.Platform.Api.dll", StringComparison.Ordinal))
            {
                continue;
            }

            File.Copy(file, Path.Combine(appDir, Path.GetFileName(file)));
        }

        File.Copy(apiDll, Path.Combine(appDir, entryName));
        var entryStem = Path.GetFileNameWithoutExtension(entryName);
        File.Copy(Path.Combine(source, "Idata.Platform.Api.runtimeconfig.json"), Path.Combine(appDir, entryStem + ".runtimeconfig.json"));

        // Byte-identik: provohet, jo supozohet.
        Assert.Equal(File.ReadAllBytes(apiDll), File.ReadAllBytes(Path.Combine(appDir, entryName)));
        return appDir;
    }

    private static int FreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Provë e pavarur nga log-et: a pranon porti ndonjë lidhje TCP gjatë jetës së procesit?</summary>
    private static async Task<bool> ProbePortUntilAsync(int port, Task exit)
    {
        while (!exit.IsCompleted)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
                return true;
            }
            catch (System.Net.Sockets.SocketException)
            {
            }

            await Task.WhenAny(exit, Task.Delay(50));
        }

        return false;
    }

    private static async Task<(int ExitCode, bool Exited, string Output, bool PortOpened)> RunAsync(
        string appDir, string entryName, IReadOnlyDictionary<string, string?> environment, int port, TimeSpan timeout)
    {
        var info = new ProcessStartInfo(ResolveDotnetHost())
        {
            WorkingDirectory = appDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(Path.Combine(appDir, entryName));
        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }

        // Asnjë trashëgim i konfigurimit identity/DB të procesit të testit.
        foreach (var inherited in info.Environment.Keys.Where(k => k.StartsWith("Identity__", StringComparison.Ordinal) && !environment.ContainsKey(k)).ToArray())
        {
            info.Environment.Remove(inherited);
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        var exited = true;
        var exit = process.WaitForExitAsync(cts.Token);
        var probe = ProbePortUntilAsync(port, exit);
        try
        {
            await exit;
        }
        catch (OperationCanceledException)
        {
            exited = false;
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var portOpened = await probe;
        var output = new StringBuilder().Append(await stdout).AppendLine().Append(await stderr).ToString();
        return (process.ExitCode, exited, output, portOpened);
    }

    /// <summary>
    /// Host-i `dotnet` REAL (jo procesi i testit): `DOTNET_HOST_PATH` (i vendosur nga
    /// SDK-ja për procesin e testit), ndryshe `dotnet` nga PATH-i.
    /// </summary>
    private static string ResolveDotnetHost()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
        {
            return hostPath;
        }

        var processPath = Environment.ProcessPath;
        if (processPath is not null && Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        return "dotnet";
    }

    private static string Redact(string output)
        => output.Length > 6000 ? output[..6000] + "…" : output;
}
