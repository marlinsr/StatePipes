using StatePipes.Interfaces;

namespace StatePipes.Explorer.NonWebClasses
{
    public class EventEntry
    {
        private IEvent? _obj;
        private string _timestamp;
        public string FullName { get; }
        public IEvent? Obj
        {
            get => _obj;
            set
            {
                _obj = value;
                _timestamp = DateTime.Now.ToString();
            }
        }
        public string Timestamp => _timestamp;
        public EventEntry(string fullName, IEvent? obj)
        {
            _obj = obj;
            _timestamp = DateTime.Now.ToString();
            FullName = fullName;
        }
    }
}
