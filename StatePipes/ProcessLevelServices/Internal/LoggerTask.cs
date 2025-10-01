using StatePipes.Common;

namespace StatePipes.ProcessLevelServices.Internal
{
    internal class LoggerTask : TaskWrapper<string>
    {
        public LoggerConfiguration Configuration { get; }
        private string _logFileName = string.Empty;
        private string _logFileDirectory = string.Empty;
        private const string LogFileExtension = ".log";
        private int _linesLoggedToCurrentFile;
        private int _fileCounter;
        internal LoggerTask() 
        {
            Configuration = ConfigurationSettingsHelper.Instance<LoggerConfiguration>();
            ConfigurationSettingsHelper.Save(Configuration);
        }
        private void PurgeLogFiles()
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(_logFileDirectory);
            FileInfo[] files = directoryInfo.GetFiles()
                                           .OrderBy(f => f.CreationTime)
                                           .ToArray();
            for (int i = 0; i < files.Length - Configuration.NumberOfLogFilesToKeep; i++)
            {
                File.Delete(files[i].FullName);
            }
        }
        private void StartNewLogFile()
        {
            if(string.IsNullOrEmpty(_logFileDirectory)) _logFileDirectory = DirHelper.GetProductDataCategoryDirectoryForProcess(DirHelper.FileCategory.Log);
            var compoundName = DirHelper.GetProcessName();
            var logFilePath = Path.Combine(_logFileDirectory, compoundName);
            var uniquenessPostfix = (++_fileCounter).ToString("D3");
            _logFileName = $"{logFilePath}{DateTime.Now.ToString("_yyyyMMdd_HHmmss")}_{uniquenessPostfix}{LogFileExtension}";
            if (!Directory.Exists(_logFileDirectory))
            {
                Directory.CreateDirectory(_logFileDirectory);
            }

            PurgeLogFiles();
            _linesLoggedToCurrentFile = 0;
        }
        private void SendToFile(List<string> lines)
        {
            _linesLoggedToCurrentFile += lines.Count;
            File.AppendAllLines(_logFileName, lines);
            if (_linesLoggedToCurrentFile >= Configuration.MaxLinesPerFile) StartNewLogFile();
            PerformCancellation();
        }
        protected override void DoWork()
        {
            StartNewLogFile();
            while (true)
            {
                PerformCancellation();
                var lines = WaitGetAll(Configuration.FlushTimeoutMilliseconds);
                if (lines != null)
                {
                    var currentFileCapcity = Configuration.MaxLinesPerFile - _linesLoggedToCurrentFile;
                    while (lines.Count > 0)
                    {
                        if (lines.Count > currentFileCapcity)
                        {
                            var linesForCurrentFile = lines.GetRange(0, currentFileCapcity);
                            lines = lines.GetRange(currentFileCapcity, lines.Count - currentFileCapcity);
                            SendToFile(linesForCurrentFile);
                            currentFileCapcity = Configuration.MaxLinesPerFile - _linesLoggedToCurrentFile;
                        }
                        else
                        {
                            SendToFile(lines);
                            break;
                        }
                    }
                   
                }
            }
        }
    }
}
