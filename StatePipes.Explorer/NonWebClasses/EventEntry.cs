using StatePipes.Interfaces;

namespace StatePipes.Explorer.NonWebClasses
{
    public class EventEntry(string fullName, IEvent? obj)
    {
        private IEvent? _obj = obj;
        private string _timestamp = DateTime.Now.ToString();
        public string FullName { get; } = fullName;
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
    }
}
