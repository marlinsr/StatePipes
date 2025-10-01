namespace StatePipes.ProcessLevelServices
{
    public class ArgsHolder
    {
        public static ServiceArgs? Args { get; private set; }
        public static void InitalizeArgs(ServiceArgs args)
        {
            if (Args == null) Args = args;
        }
        public static string? GetArgValue(string argNamePrefix) => Args?.GetArgValue(argNamePrefix);
    }
}
