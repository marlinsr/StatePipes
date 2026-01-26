using System.Reflection;

namespace StatePipes.ServiceCreatorTool
{
    internal class GeneratorHelper
    {
        private readonly DirectoryHelper _dm;
        private readonly MonikerSubstitution _monikers;
        public GeneratorHelper(DirectoryHelper dm, MonikerSubstitution monikers)
        {
            _dm = dm;
            _monikers = monikers;
        }
        private string FullEmbeddedResourceName(string resourceName) => $"{typeof(GeneratorHelper).Namespace}.Resources.{resourceName}";
        private string ReadEmbeddedTextFile(string resourceName)
        {
            resourceName = FullEmbeddedResourceName(resourceName);
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) throw new ArgumentException($"Resource '{resourceName}' not found.");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
        private byte[] ReadEmbeddedBinaryFile(string resourceName)
        {
            resourceName = FullEmbeddedResourceName(resourceName);
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) throw new ArgumentException($"Resource '{resourceName}' not found.");
            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            return buffer;
        }
        public void SaveTextFile(string sampleFileName, string fileName)
        {
            fileName = _monikers.Replace(fileName);
            string outputPath = Path.Combine(_dm.GetCurrentDirectory(), fileName);
            string contents = ReadEmbeddedTextFile(sampleFileName);
            contents = _monikers.Replace(contents);
            File.WriteAllText(outputPath, contents);
        }
        public void SaveBinaryFile(string sampleFileName, string fileName)
        {
            fileName = _monikers.Replace(fileName);
            string outputPath = Path.Combine(_dm.GetCurrentDirectory(), fileName);
            var contents = ReadEmbeddedBinaryFile(sampleFileName);
            File.WriteAllBytes(outputPath, contents);
        }
        public void Inject(string sampleInjectionFile, string fileName, string moniker, bool oneTime = false)
        {
            string injectionContents = ReadEmbeddedTextFile(sampleInjectionFile);
            injectionContents = _monikers.Replace(injectionContents);
            if (!oneTime)
            {
                injectionContents += $"\n{moniker}";
            }

            fileName = _monikers.Replace(fileName);
            string filePath = Path.Combine(_dm.GetCurrentDirectory(), fileName);
            string contents = File.ReadAllText(filePath);
            contents = contents.Replace(moniker, injectionContents);

            File.WriteAllText(filePath, contents);
        }
        public void MoveToRootDirectory() => _dm.MoveToRootDirectory(); 
        public void MoveUp() => _dm.MoveUp(); 
        public void MoveTo(string directoryName)
        {
            directoryName = _monikers.Replace(directoryName);
            _dm.MoveTo(directoryName);
        }
        public static Assembly GetTopLevelAssembly()
        {
            var assembly = Assembly.GetEntryAssembly();
            if (assembly == null) assembly = Assembly.GetCallingAssembly();
            if (assembly == null) throw new Exception("Top Level Assembly Not Found");
            return assembly;
        }
        public static string FileName()
        {
            Assembly _objParentAssembly = GetTopLevelAssembly();

            if (File.Exists(_objParentAssembly.Location))
                return _objParentAssembly.Location;
            if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + AppDomain.CurrentDomain.FriendlyName))
                return AppDomain.CurrentDomain.BaseDirectory + AppDomain.CurrentDomain.FriendlyName;
            if (File.Exists(Assembly.GetExecutingAssembly().Location))
                return Assembly.GetExecutingAssembly().Location;

            throw new IOException("Assembly not found");
        }
        public static string MyPath()
        {
            var path = Path.GetDirectoryName(FileName());
            if (path == null) throw new IOException("Path Not Fount"); 
            return path;
        }
    }
}
