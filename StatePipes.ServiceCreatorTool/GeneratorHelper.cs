using System.Reflection;

namespace StatePipes.ServiceCreatorTool
{
    internal class GeneratorHelper(DirectoryHelper dm, MonikerSubstitution monikers)
    {
        private static string FullEmbeddedResourceName(string resourceName) => $"{typeof(GeneratorHelper).Namespace}.Resources.{resourceName}";
        private static string ReadEmbeddedTextFile(string resourceName)
        {
            resourceName = FullEmbeddedResourceName(resourceName);
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream(resourceName) ?? throw new ArgumentException($"Resource '{resourceName}' not found.");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
        private static byte[] ReadEmbeddedBinaryFile(string resourceName)
        {
            resourceName = FullEmbeddedResourceName(resourceName);
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream(resourceName) ?? throw new ArgumentException($"Resource '{resourceName}' not found.");
            byte[] buffer = new byte[stream.Length];
            stream.ReadExactly(buffer, 0, buffer.Length);
            return buffer;
        }
        public void SaveTextFile(string sampleFileName, string fileName)
        {
            fileName = monikers.Replace(fileName);
            string outputPath = Path.Combine(dm.GetCurrentDirectory(), fileName);
            string contents = ReadEmbeddedTextFile(sampleFileName);
            contents = monikers.Replace(contents);
            File.WriteAllText(outputPath, contents);
        }
        public void SaveBinaryFile(string sampleFileName, string fileName)
        {
            fileName = monikers.Replace(fileName);
            string outputPath = Path.Combine(dm.GetCurrentDirectory(), fileName);
            var contents = ReadEmbeddedBinaryFile(sampleFileName);
            File.WriteAllBytes(outputPath, contents);
        }
        public void Inject(string sampleInjectionFile, string fileName, string moniker, bool oneTime = false)
        {
            string injectionContents = ReadEmbeddedTextFile(sampleInjectionFile);
            injectionContents = monikers.Replace(injectionContents);
            if (!oneTime)
            {
                injectionContents += $"\n{moniker}";
            }

            fileName = monikers.Replace(fileName);
            string filePath = Path.Combine(dm.GetCurrentDirectory(), fileName);
            string contents = File.ReadAllText(filePath);
            contents = contents.Replace(moniker, injectionContents);

            File.WriteAllText(filePath, contents);
        }
        public void MoveToRootDirectory() => dm.MoveToRootDirectory(); 
        public void MoveUp() => dm.MoveUp(); 
        public void MoveTo(string directoryName)
        {
            directoryName = monikers.Replace(directoryName);
            dm.MoveTo(directoryName);
        }
        public static Assembly GetTopLevelAssembly()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
            return assembly ?? throw new Exception("Top Level Assembly Not Found");
        }
        public static string FileName()
        {
            Assembly _objParentAssembly = GetTopLevelAssembly();

            if (File.Exists(_objParentAssembly.Location))
                return _objParentAssembly.Location;
            if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + AppDomain.CurrentDomain.FriendlyName))
                return AppDomain.CurrentDomain.BaseDirectory + AppDomain.CurrentDomain.FriendlyName;
            return File.Exists(Assembly.GetExecutingAssembly().Location)
                ? Assembly.GetExecutingAssembly().Location
                : throw new IOException("Assembly not found");
        }
        public static string MyPath()
        {
            var path = Path.GetDirectoryName(FileName()) ?? throw new IOException("Path Not Fount");
            return path;
        }
    }
}
