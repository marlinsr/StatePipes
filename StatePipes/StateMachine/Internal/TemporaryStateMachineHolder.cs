namespace StatePipes.StateMachine.Internal
{
    internal class TemporaryStateMachineHolder
    {
        internal static BaseStateMachine? BaseStateMachine;
        internal static object StateMachineLock = new object();
    }
}
