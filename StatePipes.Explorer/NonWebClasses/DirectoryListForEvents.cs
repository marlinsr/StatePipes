namespace StatePipes.Explorer.NonWebClasses
{
    public class DirectoryListForEvents
    { 
        public string? DirectoryName { get; private set; }
        public List<DirectoryListForEvents> Subdirectories { get; } = [];
        public EventEntry? Event { get; }
        public string? Namespace
        {
            get
            {
                if (Event != null) return Event.FullName;
                int indx = Subdirectories[0].Namespace?.LastIndexOf(".") ?? -1;
                if (indx <= 0) return Subdirectories[0].Namespace;
                return Subdirectories[0].Namespace?.Substring(0,indx);
            }
        }
        private void CreateUniqueSubDirectories(string? directoryName, List<EventEntry> childrenEventEntryList)
        {
            List<string> uniqueSubDirectories = [];
            foreach (var subDirectory in childrenEventEntryList)
            {
                var subDirectoryDirectoryName = string.IsNullOrEmpty(directoryName) ? subDirectory.FullName.Split('.')[0]
                    : directoryName + "." + subDirectory.FullName.Replace(directoryName + ".", string.Empty).Split('.')[0];
                if (!uniqueSubDirectories.Contains(subDirectoryDirectoryName))
                {
                    uniqueSubDirectories.Add(subDirectoryDirectoryName);
                    Subdirectories.Add(new DirectoryListForEvents(childrenEventEntryList, subDirectoryDirectoryName));
                }
            }
        }
        public DirectoryListForEvents(List<EventEntry>? eventList, string? directoryName = null)
        {
            DirectoryName = directoryName;
            if (eventList == null || eventList.Count == 0) return;
            if (!string.IsNullOrEmpty(directoryName))
            {
                var childEvent = eventList.FirstOrDefault(e => e.FullName == directoryName);
                if (childEvent != null)
                {
                    Event = childEvent;
                    return;
                }
            }
            Event = null;
            var childrenEventList = eventList;
            if (directoryName != null)
            {
                childrenEventList = eventList.Where(e => e.FullName.StartsWith(directoryName, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            CreateUniqueSubDirectories(directoryName, childrenEventList);
            if (string.IsNullOrEmpty(directoryName))
            {
                while (Compact(null));
                Shorten(string.Empty);
            }
        }
        private bool CompactSubdirectories(List<DirectoryListForEvents> subdirectories)
        {
            foreach (var subDirectory in Subdirectories)
            {
                if (subDirectory.Compact(this)) return true;
            }
            return false;
        }
        private bool Compact(DirectoryListForEvents? parentDirectory)
        {
            if (parentDirectory != null && Subdirectories.Count == 0 && Event == null)
            {
                parentDirectory.Subdirectories.Remove(this);
                return true;
            }
            else if (parentDirectory != null && Subdirectories.Count == 1 && Subdirectories[0].Event == null)
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
            foreach(var subDirectory in Subdirectories)
            {
                subDirectory.Shorten(DirectoryName ?? string.Empty);
            }
            DirectoryName = DirectoryName?.Substring(parentDirectoryName.Length > 0 ? parentDirectoryName.Length + 1 : 0);
        }

    }
}
