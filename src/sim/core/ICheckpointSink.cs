namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.9.</summary>
    public interface ICheckpointSink
    {
        /// <summary>Records a checkpoint produced in phase 4 of a tick.</summary>
        void Record(in Checkpoint cp);
    }
}
