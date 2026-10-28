using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationOperatorReadinessInteropTests
{
    [TestMethod]
    public async Task DeployHelperUsesActualLoginContractOverTrustedTlsAndKeepsBusinessApiBlocked()
    {
        await using var fixture = await OperatorHostFixture.StartAsync();

        using (var client = fixture.CreateHttpClient())
        {
            using var wrongUser = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "operator",
                ["password"] = fixture.ApiKey,
                ["ReturnUrl"] = $"/settings/horse-id-migration?operationId={fixture.OperationId:D}",
            }));
            Assert.AreEqual(HttpStatusCode.Redirect, wrongUser.StatusCode);
            Assert.IsFalse(wrongUser.Headers.Contains("Set-Cookie"),
                "A noncanonical username must not issue the operator cookie.");

            using var wrongKey = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "user",
                ["password"] = "wrong-test-only-key",
                ["ReturnUrl"] = $"/settings/horse-id-migration?operationId={fixture.OperationId:D}",
            }));
            Assert.AreEqual(HttpStatusCode.Redirect, wrongKey.StatusCode);
            Assert.IsFalse(wrongKey.Headers.Contains("Set-Cookie"),
                "An incorrect API key must not issue the operator cookie.");

            using var noKey = await client.GetAsync(
                $"/api/v2/admin/horse-id-migration-operations/{fixture.OperationId:D}");
            Assert.AreEqual(HttpStatusCode.Unauthorized, noKey.StatusCode,
                "The helper login cookie must not replace API-key protection.");

            using var blockedBusinessApi = new HttpRequestMessage(HttpMethod.Get, "/api/v2/admin/collection/pipeline-state");
            blockedBusinessApi.Headers.Add("X-Api-Key", fixture.ApiKey);
            using var blockedResponse = await client.SendAsync(blockedBusinessApi);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, blockedResponse.StatusCode,
                "Operator UI recovery must leave collection business APIs blocked.");
            StringAssert.Contains(await blockedResponse.Content.ReadAsStringAsync(),
                "Application startup is blocked by migration state.");
        }

        var helper = await fixture.RunActualReadinessHelperAsync();
        Assert.IsFalse(helper.TimedOut, "The helper must finish well inside its bounded child timeout.");
        Assert.AreEqual(0, helper.ExitCode,
            $"The actual Bash/Python readiness helper must log in and read the bound operation HTML. {helper.Output}");
        Assert.IsFalse(helper.Output.Contains(fixture.ApiKey, StringComparison.Ordinal),
            "The ephemeral API key must never appear in helper stdout or stderr.");
    }

    private sealed class OperatorHostFixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "hrp-horse-id-operator-readiness-" + Guid.NewGuid().ToString("N"));
        private readonly int _port;
        private readonly string _certificatePassword = Guid.NewGuid().ToString("N");
        private Process? _apiProcess;
        private Task<string>? _apiStdout;
        private Task<string>? _apiStderr;

        private OperatorHostFixture()
        {
            Directory.CreateDirectory(_root);
            StateDirectory = Path.Combine(_root, "appstate");
            Directory.CreateDirectory(StateDirectory);
            EventStorePath = Path.Combine(_root, "eventstore.db");
            CollectionStorePath = Path.Combine(StateDirectory, "collection-platform.db");
            ScheduleStorePath = Path.Combine(_root, "schedule", "prediction-executions.db");
            QueueStorePath = Path.Combine(_root, "queue.db");
            ApiKey = "test-only-" + Guid.NewGuid().ToString("N");
            OperationId = Guid.NewGuid();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        private string StateDirectory { get; }
        private string EventStorePath { get; }
        private string CollectionStorePath { get; }
        private string ScheduleStorePath { get; }
        private string QueueStorePath { get; }
        internal string ApiKey { get; }
        internal Guid OperationId { get; }
        private string RootCertificatePath => Path.Combine(_root, "test-root-ca.pem");
        private string ServerCertificatePath => Path.Combine(_root, "test-server.pfx");
        private string BaseUrl => $"https://127.0.0.1:{_port}";

        internal static async Task<OperatorHostFixture> StartAsync()
        {
            var fixture = new OperatorHostFixture();
            try
            {
                fixture.CreateTrustedServerCertificate();
                var sourcePaths = new[]
                {
                    fixture.EventStorePath,
                    fixture.CollectionStorePath,
                    fixture.ScheduleStorePath,
                    fixture.QueueStorePath,
                };
                var journal = new HorseIdentityMigrationJournal(fixture.StateDirectory, sourcePaths);
                var manifest = new HorseIdentityMigrationManifest(
                    [new HorseIdentityMigrationManifestEntry(
                        "horse-11111111-1111-4111-8111-111111111111", "jra-horse:1234567890", true)]);
                Assert.IsTrue((await journal.StageAsync(fixture.OperationId, manifest, DateTimeOffset.UtcNow)).Succeeded,
                    "The real migration journal must stage the operation served by the blocked API host.");

                fixture.StartApiProcess();
                await fixture.WaitForHealthAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal HttpClient CreateHttpClient()
        {
            var expectedThumbprint = X509CertificateLoader.LoadPkcs12FromFile(ServerCertificatePath, _certificatePassword)
                .Thumbprint;
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                    string.Equals(certificate?.GetCertHashString(), expectedThumbprint, StringComparison.OrdinalIgnoreCase),
            };
            return new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        }

        internal async Task<HelperResult> RunActualReadinessHelperAsync()
        {
            var helperPath = Path.Combine(FindRepositoryRoot(), "deploy", "horse-id-migration-deploy.sh");
            var command = $"set -euo pipefail; . {QuoteBash(helperPath)}; "
                + $"horse_id_migration_assert_operator_ui_ready {QuoteBash(BaseUrl)} "
                + $"\"$HRP_TEST_API_KEY\" {QuoteBash(OperationId.ToString("D"))}";
            var process = new Process
            {
                StartInfo = new ProcessStartInfo("bash")
                {
                    WorkingDirectory = FindRepositoryRoot(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true,
            };
            process.StartInfo.ArgumentList.Add("--noprofile");
            process.StartInfo.ArgumentList.Add("--norc");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add(command);
            process.StartInfo.Environment["HRP_TEST_API_KEY"] = ApiKey;
            process.StartInfo.Environment["SSL_CERT_FILE"] = RootCertificatePath;
            process.StartInfo.Environment["HRP_PYTHON3"] = "python3";

            if (!process.Start()) throw new InvalidOperationException("The actual readiness helper process did not start.");
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                timedOut = true;
                process.Kill(entireProcessTree: true);
                using var reapTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(reapTimeout.Token);
            }

            return new(process.ExitCode, timedOut, (await stdout) + (await stderr));
        }

        internal static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
        }

        private void CreateTrustedServerCertificate()
        {
            var now = DateTimeOffset.UtcNow;
            using var caKey = RSA.Create(2048);
            var caRequest = new CertificateRequest("CN=HorseRacingPrediction isolated test CA", caKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            using var ca = caRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(2));
            File.WriteAllText(RootCertificatePath, ca.ExportCertificatePem());

            using var serverKey = RSA.Create(2048);
            var serverRequest = new CertificateRequest("CN=127.0.0.1", serverKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            var extendedUsage = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
            serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(extendedUsage, true));
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback);
            names.AddDnsName("localhost");
            serverRequest.CertificateExtensions.Add(names.Build());
            using var publicCertificate = serverRequest.Create(ca, now.AddMinutes(-2),
                now.AddHours(1), RandomNumberGenerator.GetBytes(16));
            using var serverCertificate = publicCertificate.CopyWithPrivateKey(serverKey);
            File.WriteAllBytes(ServerCertificatePath,
                serverCertificate.Export(X509ContentType.Pfx, _certificatePassword));
        }

        private void StartApiProcess()
        {
            var apiAssembly = typeof(HorseIdentityMigrationRuntimeMode).Assembly.Location;
            var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = FindRepositoryRoot(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true,
            };
            process.StartInfo.ArgumentList.Add(apiAssembly);
            process.StartInfo.ArgumentList.Add("--urls");
            process.StartInfo.ArgumentList.Add(BaseUrl);
            process.StartInfo.Environment["CollectionPlatform__StateDirectory"] = StateDirectory;
            process.StartInfo.Environment["ConnectionStrings__EventStore"] = $"Data Source={EventStorePath}";
            process.StartInfo.Environment["PredictionScheduling__StateDirectory"] = Path.GetDirectoryName(ScheduleStorePath)!;
            process.StartInfo.Environment["PredictionScheduling__StoreFileName"] = Path.GetFileName(ScheduleStorePath);
            process.StartInfo.Environment["CollectionQueue__Provider"] = "Local";
            process.StartInfo.Environment["CollectionQueue__LocalDatabasePath"] = QueueStorePath;
            process.StartInfo.Environment["ApiKey__HeaderName"] = "X-Api-Key";
            process.StartInfo.Environment["ApiKey__Key"] = ApiKey;
            process.StartInfo.Environment["HRP_HORSE_IDENTITY_OPERATOR_ONLY"] = "true";
            process.StartInfo.Environment["HRP_HORSE_IDENTITY_OPERATOR_OPERATION_ID"] = OperationId.ToString("D");
            process.StartInfo.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = ServerCertificatePath;
            process.StartInfo.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = _certificatePassword;
            process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";

            if (!process.Start()) throw new InvalidOperationException("The operator-only API host did not start.");
            process.StandardInput.Close();
            _apiStdout = process.StandardOutput.ReadToEndAsync();
            _apiStderr = process.StandardError.ReadToEndAsync();
            _apiProcess = process;
        }

        private async Task WaitForHealthAsync()
        {
            using var client = CreateHttpClient();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!deadline.IsCancellationRequested)
            {
                if (_apiProcess!.HasExited)
                {
                    var output = await Task.WhenAll(_apiStdout!, _apiStderr!);
                    throw new InvalidOperationException(
                        $"The actual operator-only API host exited before TLS health was ready ({_apiProcess.ExitCode}). {output}");
                }
                try
                {
                    using var response = await client.GetAsync("/health", deadline.Token);
                    if (response.StatusCode == HttpStatusCode.OK) return;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!deadline.IsCancellationRequested) { }
                await Task.Delay(100, deadline.Token);
            }
            throw new TimeoutException("The actual operator-only API host did not serve HTTPS health within 20 seconds.");
        }

        private static string QuoteBash(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

        public async ValueTask DisposeAsync()
        {
            if (_apiProcess is not null)
            {
                if (!_apiProcess.HasExited) _apiProcess.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _apiProcess.WaitForExitAsync(timeout.Token);
                var output = string.Join(Environment.NewLine, await Task.WhenAll(_apiStdout!, _apiStderr!));
                if (!string.IsNullOrEmpty(ApiKey) && output.Contains(ApiKey, StringComparison.Ordinal))
                    throw new InvalidOperationException("The ephemeral API key appeared in API-host output.");
                _apiProcess.Dispose();
            }
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record HelperResult(int ExitCode, bool TimedOut, string Output);
}
