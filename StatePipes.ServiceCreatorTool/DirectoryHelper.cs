using System.Runtime.InteropServices;

namespace StatePipes.ServiceCreatorTool
{
    internal class DirectoryHelper
    {
        private readonly string _rootDirectory;
        private readonly string _directoryDelimeter;
        private readonly string _driveDelimeter;

        private string _currentDirectory = string.Empty;

        public DirectoryHelper(string rootDirectory)
        {
            _directoryDelimeter = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "/" : "\\";
            _driveDelimeter = ":";
            _rootDirectory = rootDirectory;
            if (!Directory.Exists(_rootDirectory))
            {
                Directory.CreateDirectory(_rootDirectory);
            }
        }

        public string GetCurrentDirectory()
        {
            return Path.Combine(_rootDirectory, _currentDirectory);
        }

        public string GetCurrentDirectoryRelative()
        {
            return _currentDirectory;
        }

        public void MoveToRootDirectory()
        {
            _currentDirectory = string.Empty;
        }

        public void MoveUp()
        {
            if (_currentDirectory != string.Empty)
            {
                var index = _currentDirectory.LastIndexOf(_directoryDelimeter);
                if (index > 0)
                {
                    _currentDirectory = _currentDirectory.Substring(0, index);
                }
                else
                {
                    _currentDirectory = string.Empty;
                }
            }
        }

        public void MoveTo(string directoryName)
        {
            if (directoryName.Contains(_directoryDelimeter))
            {
                throw new Exception($"DirectoryManager.MoveTo cannot contain {_directoryDelimeter} for directoryName {directoryName}");
            }
            if (directoryName.Contains(_driveDelimeter))
            {
                throw new Exception($"DirectoryManager.MoveTo cannot contain {_driveDelimeter} for directoryName {directoryName}");
            }
            var tempDirectory = Path.Combine(GetCurrentDirectory(), directoryName);
            if (!Directory.Exists(tempDirectory))
            {
                Directory.CreateDirectory(tempDirectory);
            }
            _currentDirectory = tempDirectory;
        }
    }
}
