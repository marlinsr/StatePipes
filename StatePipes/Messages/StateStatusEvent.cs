using StatePipes.Interfaces;

namespace StatePipes.Messages
{
    public class StateStatusEvent : IEvent
    {
        public string StateMachineName { get; }
        public string State { get; }

        public StateStatusEvent(string state, string stateMachineName)
        {
            State = state;
            StateMachineName = stateMachineName;
        }
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = string.IsNullOrEmpty(State) ? 0 : State.GetHashCode() ;
                hashCode = string.IsNullOrEmpty(StateMachineName) ? hashCode : hashCode * 397 ^ StateMachineName.GetHashCode();
                return hashCode;
            }
        }
        public override string ToString() => base.ToString() + " - " + StateMachineName + " - " + State;
        public override bool Equals(object? obj)
        {
            if (obj == null) return false;
            return obj is StateStatusEvent && Equals(obj);
        }
        protected bool Equals(StateStatusEvent other) => State == other.State && StateMachineName == other.StateMachineName;
    }
}
