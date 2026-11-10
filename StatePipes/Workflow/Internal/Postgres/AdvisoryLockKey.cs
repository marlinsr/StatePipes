using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace StatePipes.Workflow.Internal.Postgres
{
    /// <summary>
    /// The pg_try_advisory_xact_lock key for one workflow instance. Must be identical in every process on every machine,
    /// which string.GetHashCode (randomized per process) is not. A collision only costs a spurious "locked" and a retry.
    /// </summary>
    internal static class AdvisoryLockKey
    {
        public static long For(string workflowType, string correlationId)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(workflowType + "\0" + correlationId));
            return BinaryPrimitives.ReadInt64LittleEndian(hash);
        }
    }
}
