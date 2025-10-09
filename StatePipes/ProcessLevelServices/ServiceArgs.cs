namespace StatePipes.ProcessLevelServices
{
    public class ServiceArgs
    {
        public const char NameValueDelimiter = '=';
        public const string LogLevelArg = "--loglevel";
        public const string PostFix = "--postfix";
        public const string CompanyName = "--companyname";
        public const string UseDefaultConfig = "--usedefaultconfig";
        public IReadOnlyList<string>? Args { get; }
        public ServiceArgs(IReadOnlyList<string>? args)
        {
            Args = args;
            args?.ToList().ForEach(a => { if(!a.Contains(NameValueDelimiter)) throw new ArgumentException($"Arg {a} doesn't contain delimeter {NameValueDelimiter}"); });
        }
        public string? GetArgValue(string argNamePrefix)
        {
            if (Args == null || string.IsNullOrEmpty(argNamePrefix)) return null;
            var argNamePlusDelimeter = argNamePrefix + NameValueDelimiter;
            var arg = Args.FirstOrDefault(a => a.StartsWith(argNamePlusDelimeter, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(arg)) return null;
            return arg.Substring(argNamePlusDelimeter.Length);
        }
        public ServiceArgs Remove(string argName) => new((Args?.Where(s => !s.StartsWith(argName + NameValueDelimiter)))?.ToList());
        public bool ContainsArgName(string argName) => Args?.Any(a => a.StartsWith(argName + NameValueDelimiter)) ?? false;
        private static string GetArgName(string argName) => argName.Split(NameValueDelimiter)[0];
        public ServiceArgs GetArgsNotFoundIn(ServiceArgs other) => new(Args?.ToList().Where(a => !other.ContainsArgName(a)).ToList());
        public ServiceArgs Concat(ServiceArgs other)
        {
            if(Args == null) return new ServiceArgs(other.Args);
            if (other.Args == null) return new ServiceArgs(Args);
            var conCatList = Args.ToList();
            conCatList.AddRange(other.Args);
            return new ServiceArgs(conCatList);
        }
        public ServiceArgs Merge(ServiceArgs dominantArgs)
        {
            if(Args == null) return dominantArgs;
            if(dominantArgs.Args == null) return new ServiceArgs(Args.ToList());
            var argsNotContainedInDominant = GetArgsNotFoundIn(dominantArgs);
            return dominantArgs.Concat(argsNotContainedInDominant);
        }
    }
}
