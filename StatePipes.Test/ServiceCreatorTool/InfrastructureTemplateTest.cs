using StatePipes.Test.ClaudePlugin;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace StatePipes.Test.ServiceCreatorTool
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class InfrastructureTemplateTest
    {
        private static string Sample(string name) => File.ReadAllText(Path.Combine(PluginPaths.Resources, name));

        /// <summary>Substitutes the generator monikers, then asks PowerShell's parser for syntax errors.</summary>
        private static int ParseErrors(string sampleName)
        {
            var text = Regex.Replace(Sample(sampleName), @"@#\$(\w+)@#\$", "$1");
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.ps1");
            File.WriteAllText(path, text);
            try
            {
                var psi = new ProcessStartInfo("pwsh", ["-NoProfile", "-Command",
                    $"$e=$null; [System.Management.Automation.Language.Parser]::ParseFile('{path}', [ref]$null, [ref]$e) | Out-Null; $e.Count"])
                { RedirectStandardOutput = true, UseShellExecute = false };
                using var p = Process.Start(psi)!;
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return int.Parse(output);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        [DataRow("DockerInfrastructureStart_ps1.sample")]
        [DataRow("DockerInfrastructureStop_ps1.sample")]
        [DataRow("DockerStart_ps1.sample")]
        public void Script_ParsesAsPowerShell(string sample) => Assert.AreEqual(0, ParseErrors(sample));

        [TestMethod]
        public void InfrastructureStart_ContainsPostgresEssentials()
        {
            var s = Sample("DockerInfrastructureStart_ps1.sample");
            foreach (var expected in new[] { "postgres:17", "hostssl  all  all       0.0.0.0/0 cert", "hostssl  all  all       ::/0      cert",
                "ssl_min_protocol_version=TLSv1.3", "chmod 600", "postgres-db-data", "statepipes.postgres-db.client.p12", "pg_isready",
                "CREATE ROLE statepipes LOGIN;", "ALTER DATABASE statepipes OWNER TO statepipes;", "CN=postgres-db",
                "docker volume rm postgres-db-data", "127.0.0.1 amqp09-broker kafka-broker postgres-db statepipes.explorer" })
                StringAssert.Contains(s, expected);
            Assert.IsFalse(Regex.IsMatch(s, @"^\s*""host\s", RegexOptions.Multiline), "pg_hba must not contain non-TLS host lines");
        }

        [TestMethod]
        public void InfrastructureStop_StopsPostgresKeepsVolume()
        {
            var s = Sample("DockerInfrastructureStop_ps1.sample");
            StringAssert.Contains(s, "postgres-db");
            Assert.IsFalse(s.Contains("volume rm"));
        }

        [TestMethod]
        public void DockerStart_CopiesPostgresClientCert() =>
            StringAssert.Contains(Sample("DockerStart_ps1.sample"), @"step-ca\statepipes.postgres-db.client.p12");

        [TestMethod]
        public void ServiceCsproj_CopiesPostgresCertGuarded() =>
            StringAssert.Contains(Sample("Service_csproj.sample"),
                @"if exist $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 copy $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 $(OutDir)\certs\statepipes.postgres-db.client.p12");
    }
}
