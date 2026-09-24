namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.9.</summary>
    public interface ICheckpointSink
    {
        void Record(in Checkpoint cp);
    }
}
