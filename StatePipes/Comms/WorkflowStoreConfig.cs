using Newtonsoft.Json;

namespace StatePipes.Comms
{
    /// <summary>
    /// PostgreSQL store for workflow instances and timers (spec §5.5). Authentication is by client certificate only:
    /// <see cref="Username"/> must equal the certificate's CN.
    /// </summary>
    [method: JsonConstructor]
    public class WorkflowStoreConfig(string host, string database, string username, string clientCertFileName,
        int port = 5432, string passwordFileName = "CommonPassword.txt", int lockedMaxRetries = 10,
        TimeSpan? lockedRetryDelay = null, TimeSpan? timerPollInterval = null)
    {
        public string Host { get; } = host;
        public int Port { get; } = port;
        public string Database { get; } = database;
        public string Username { get; } = username;
        public string ClientCertFileName { get; } = clientCertFileName;
        public string PasswordFileName { get; } = passwordFileName;
        public int LockedMaxRetries { get; } = lockedMaxRetries;
        public TimeSpan LockedRetryDelay { get; } = lockedRetryDelay ?? TimeSpan.FromMilliseconds(50);
        public TimeSpan TimerPollInterval { get; } = timerPollInterval ?? TimeSpan.FromSeconds(1);
    }
}
