using StatePipes.Interfaces;
using StatePipes.Messages;
using StatePipes.ProcessLevelServices;
using static StatePipes.ProcessLevelServices.LoggerHolder;
namespace StatePipes.Comms.Internal
{
    internal class GetLogFileTailCommandHandler(IStatePipesService bus) : IMessageHandler<GetLogFileTailCommand>
    {
        private string ReadTail(string logFileName)
        {
            const long maxCharactersToRead = 500000;
            const long negativeMaxCharactersToRead = -500000;
            char[] logFileLines;
            using (var fs = new FileStream(logFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (var sr = new StreamReader(fs))
                {
                    long bytesToRead = sr.BaseStream.Length;
                    if (bytesToRead > maxCharactersToRead)
                    {
                        bytesToRead = maxCharactersToRead;
                        sr.BaseStream.Seek(negativeMaxCharactersToRead, SeekOrigin.End);
                    }
                    logFileLines = new char[bytesToRead];
                    int bytesRead = 0;
                    while (bytesRead < bytesToRead)
                    {
                        int readRet = sr.Read(logFileLines, bytesRead, (int)bytesToRead - bytesRead);
                        if (readRet <= 0)
                        {
                            var readErrorStr = $"Log file {logFileName} Read error {readRet}";
                            Log?.LogError(readErrorStr);
                            return readErrorStr;
                        }
                        bytesRead += readRet;
                    }
                }
            }
            return new string(logFileLines);
        }
        private string GetNewestLogFile(out string errString)
        {
            string logFileDirectory = DirHelper.GetProductDataCategoryDirectoryForProcess(DirHelper.FileCategory.Log);
            string compoundName = DirHelper.GetProcessName();
            string logFilePath = Path.Combine(logFileDirectory, compoundName);
            string? path = Path.GetDirectoryName(logFilePath);
            if (path == null)
            {
                errString = $"Couldn't get directory name for path {logFilePath}";
                Log?.LogError(errString);
                return string.Empty;
            }
            string searchPattern = Path.GetFileNameWithoutExtension($@"{logFilePath}*.csv");
            string[] files = Directory.GetFiles(path, searchPattern);
            if (files.Length <= 0)
            {
                errString = $"No log files fit search pattern {searchPattern} in path {path}";
                Log?.LogInfo(errString);
                return string.Empty;
            }
            Dictionary<DateTime, string> fileCreationDateDictionary = [];
            files.ToList().ForEach(file => fileCreationDateDictionary.Add(File.GetCreationTime(file), file));
            var newestCreationDate = fileCreationDateDictionary.Keys.OrderByDescending(k => k).First();
            var newestLogFile = fileCreationDateDictionary[newestCreationDate];
            errString = string.Empty;
            return newestLogFile;
        }
        public void HandleMessage(GetLogFileTailCommand message, BusConfig? responseInfo, bool isResponse)
        {
            if (responseInfo == null) return;
            var newestLogFile = GetNewestLogFile(out string err);
            if (!string.IsNullOrEmpty(err))
            {
                bus.SendResponse(new LogFileTailEvent(err), responseInfo);
                return;
            }
            string logFileTailContents = ReadTail(newestLogFile);
            bus.SendResponse(new LogFileTailEvent(logFileTailContents), responseInfo);
        }
    }
}
