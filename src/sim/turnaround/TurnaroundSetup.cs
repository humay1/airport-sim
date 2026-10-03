namespace AirportSim.Sim.Turnaround
{
    /// <summary>The construction data of sim.turnaround (13-interfaces-turnaround.md §13.10a).</summary>
    public readonly struct TurnaroundSetup
    {
        /// <summary>The job catalogue.</summary>
        public TurnaroundCatalogue Catalogue { get; }

        /// <summary>The vehicle fleet.</summary>
        public TurnaroundFleet Fleet { get; }

        /// <summary>Creates a setup.</summary>
        public TurnaroundSetup(TurnaroundCatalogue catalogue, TurnaroundFleet fleet)
        {
            Catalogue = catalogue;
            Fleet = fleet;
        }
    }
}
