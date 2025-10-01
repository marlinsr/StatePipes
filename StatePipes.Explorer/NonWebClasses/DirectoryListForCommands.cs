namespace StatePipes.Explorer.NonWebClasses
{
    public class DirectoryListForCommands
    {
        public string? DirectoryName { get; private set; }
        public List<DirectoryListForCommands> Subdirectories { get; } = new List<DirectoryListForCommands>();
        public CommandEntry? Command { get; }
        public string? Namespace
        {
            get
            {
                if (Command != null) return Command.FullName;
                int indx = Subdirectories[0].Namespace?.LastIndexOf(".") ?? -1;
                if (indx <= 0) return Subdirectories[0].Namespace;
                return Subdirectories[0].Namespace?.Substring(0, indx);
            }
        }
        private void CreateUniqueSubDirectories(string? directoryName, List<CommandEntry> childrenCommandEntryList)
        {
            List<string> uniqueSubDirectories = new List<string>();
            foreach (var subDirectory in childrenCommandEntryList)
            {
                var subDirectoryDirectoryName = string.IsNullOrEmpty(directoryName) ? subDirectory.FullName.Split('.')[0]
                    : directoryName + "." + subDirectory.FullName.Replace(directoryName + ".", string.Empty).Split('.')[0];
                if (!uniqueSubDirectories.Contains(subDirectoryDirectoryName))
                {
                    uniqueSubDirectories.Add(subDirectoryDirectoryName);
                    Subdirectories.Add(new DirectoryListForCommands(childrenCommandEntryList, subDirectoryDirectoryName));
                }
            }
        }
        public DirectoryListForCommands(List<CommandEntry>? commandList, string? directoryName = null)
        {
            DirectoryName = directoryName;
            if (commandList == null || commandList.Count == 0) return;
            Command = GetCommandEntry(commandList, directoryName);
            if(Command != null) return;
            var childrenCommandEntryList = commandList;
            if (directoryName != null) 
                childrenCommandEntryList = commandList.Where(e => e.FullName.StartsWith(directoryName, StringComparison.OrdinalIgnoreCase)).ToList();
            CreateUniqueSubDirectories(directoryName, childrenCommandEntryList);
            if (string.IsNullOrEmpty(directoryName))
            {
                while (Compact(null)) ;
                Shorten(string.Empty);
            }
        }
        private CommandEntry? GetCommandEntry(List<CommandEntry> commandList, string? directoryName)
        {
            if (!string.IsNullOrEmpty(directoryName))
            {
                var commandEntry = commandList.FirstOrDefault(e => e.FullName == directoryName);
                if (commandEntry != null) return commandEntry;
            }
            return null;
        }
        private bool CompactSubdirectories(List<DirectoryListForCommands> subdirectories)
        {
            foreach (var subDirectory in Subdirectories)
            {
                if (subDirectory.Compact(this)) return true;
            }
            return false;
        }
        private bool Compact(DirectoryListForCommands? parentDirectory)
        {
            if (parentDirectory != null && Subdirectories.Count == 0 && Command == null)
            {
                parentDirectory.Subdirectories.Remove(this);
                return true;
            }
            else if (parentDirectory != null && Subdirectories.Count == 1 && Subdirectories[0].Command == null)
            {
                parentDirectory.Subdirectories.Add(Subdirectories[0]);
                parentDirectory.Subdirectories.Remove(this);
                return true;
            }
            else
            {
                return CompactSubdirectories(Subdirectories);
            }
        }
        private void Shorten(string parentDirectoryName)
        {
            foreach (var subDirectory in Subdirectories)
            {
                subDirectory.Shorten(DirectoryName ?? string.Empty);
            }
            DirectoryName = DirectoryName?.Substring(parentDirectoryName.Length > 0 ? parentDirectoryName.Length + 1 : 0);
        }
    }
}
