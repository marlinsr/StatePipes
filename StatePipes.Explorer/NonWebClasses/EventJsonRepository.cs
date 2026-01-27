using StatePipes.Interfaces;
using StatePipes.SelfDescription;

namespace StatePipes.Explorer.NonWebClasses
{
    internal class EventJsonRepository
    {
        private readonly List<EventEntry> _eventJsonRepo = [];
        public EventJsonRepository() { }
        public void SetJsonString(IEvent ev)
        {
            string name = ev.GetType().FullName ?? string.Empty;
            if(string.IsNullOrEmpty(name)) return;
            lock (_eventJsonRepo)
            {
                var eventEntry = _eventJsonRepo.FirstOrDefault(e => e.FullName == name);
                if (eventEntry != null)
                {
                    eventEntry.Obj = ev;
                }
                else
                {
                    _eventJsonRepo.Add(new EventEntry(name, ev));
                }
            }
        }
        public void SetEmptyJsonStrings(List<TypeSerializationJsonHelper> tList)
        {
            lock (_eventJsonRepo)
            {
                foreach (var t in tList)
                {
                    string name = t.ThisType?.FullName ?? string.Empty;
                    var eventJsonRepoEvent = _eventJsonRepo.FirstOrDefault(t => t.FullName == name);
                    if (eventJsonRepoEvent == null) _eventJsonRepo.Add(new EventEntry(name, null));
                }
                List<EventEntry> eventListToRemove = [];
                foreach (var eventRepoEntry in _eventJsonRepo)
                {
                    if (tList.FirstOrDefault(tEvent => tEvent.ThisType?.FullName == eventRepoEntry.FullName) == null) 
                        eventListToRemove.Add(eventRepoEntry);
                }
                eventListToRemove.ForEach(removeEventEntry => _eventJsonRepo.Remove(removeEventEntry));
            }
        }
        public List<EventEntry> GetEventJsons()
        {
            List<EventEntry> ret = [];
            lock (_eventJsonRepo)
            {
                _eventJsonRepo.ForEach(i => ret.Add(i));
            }
            return ret;
        }
    }
}
