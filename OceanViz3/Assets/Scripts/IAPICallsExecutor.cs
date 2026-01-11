/// <summary>
/// Contract for components that can execute scripted SimulationAPI calls.
/// Implementors are discovered by SimulationAPI and executed once per scene/location load
/// (when the simulation and location become ready), using the API's queued call system.
/// </summary>
public interface IAPICallsExecutor
{
    void Execute(SimulationAPI api);
}


