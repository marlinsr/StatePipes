using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    public class SelfDescriptionEvent(TypeSerializationList typeList) : IEvent
    {
        public TypeSerializationList TypeList { get; } = typeList;
    }
}
