namespace StatePipes.Interfaces
{
    public interface IStatePipesProxyFactory
    {
        List<string> GetProxyConfigNames();
        IStatePipesProxy? GetStatePipesProxy(string name, bool connect = true);
        List<IStatePipesProxy> GetAllClientProxies();
    }
}
