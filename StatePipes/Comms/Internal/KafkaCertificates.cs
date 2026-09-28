using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace StatePipes.Comms.Internal
{
    internal static class KafkaCertificates
    {
        /// <summary>Reads a .p12 from disk and returns its CA certificates as one PEM string.</summary>
        public static string ExtractCaChainPem(string p12Path, string password) =>
            ExtractCaChainPem(File.ReadAllBytes(p12Path), password);

        /// <summary>Reads a .p12 from a stream (MemoryStream, embedded resource, secret store, ...).</summary>
        public static string ExtractCaChainPem(Stream p12Stream, string password)
        {
            using var buffer = new MemoryStream();
            p12Stream.CopyTo(buffer);
            return ExtractCaChainPem(buffer.ToArray(), password);
        }

        /// <summary>Returns every certificate in the bundle that is not the leaf we hold the private key for.</summary>
        public static string ExtractCaChainPem(byte[] p12Bytes, string password)
        {
            // EphemeralKeySet keeps the private key out of the Windows key store -- we only want the public CA certificates here, so there is no reason to persist anything.
            var bundle = X509CertificateLoader.LoadPkcs12Collection(p12Bytes, password, X509KeyStorageFlags.EphemeralKeySet);
            var pem = new StringBuilder();
            var count = 0;
            foreach (var certificate in bundle)
            {
                using (certificate)
                {
                    // The entry we have a private key for is our own client identity, not a trust anchor.
                    if (certificate.HasPrivateKey) continue;
                    pem.AppendLine(certificate.ExportCertificatePem());
                    count++;
                }
            }
            if (count == 0) throw new InvalidOperationException(
                    "The PKCS#12 bundle contains no CA certificates. Re-issue it with the issuing chain included " + "(DockerInfrastructureStart.ps1 passes --ca intermediate_ca.crt --ca root_ca.crt to 'step certificate p12').");
            return pem.ToString();
        }
    }
}
