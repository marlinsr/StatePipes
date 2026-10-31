namespace StatePipes.ServiceCreatorTool
{
    /// <summary>
    /// Proxy generator inputs given on the command line. A supplied value replaces the dialog that would ask for it,
    /// so a proxy can be generated without user interaction. When Headless is set a missing value is an error, never a dialog.
    /// </summary>
    internal sealed record ProxyInputs(string? Moniker = null, string? DllPath = null, string? BrokerUri = null, string? ExchangeName = null, string? CertPath = null, string? CertPasswordPath = null, bool Headless = false, string? StateMachine = null)
    {
        public bool MissingInHeadless(string? value, string argName)
        {
            if (!Headless || !string.IsNullOrEmpty(value)) return false;
            Console.WriteLine($"Headless mode needs {argName}.");
            return true;
        }
        public bool HasLiveConnection =>
            !string.IsNullOrEmpty(BrokerUri) && !string.IsNullOrEmpty(ExchangeName) && !string.IsNullOrEmpty(CertPath) && !string.IsNullOrEmpty(CertPasswordPath);
    }
}
