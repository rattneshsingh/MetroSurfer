namespace Runner.Config
{
    /// <summary>
    /// How an obstacle is physically built. This is the axis that actually needs code — a new
    /// obstacle only requires a new form if it is shaped unlike anything already here.
    /// Everything else is a new entry in the catalogue.
    /// </summary>
    public enum ObstacleForm
    {
        /// <summary>Solid box, lethal throughout. Ground barriers and overhead slabs both.</summary>
        Barrier = 0,

        /// <summary>Roof you can stand on, with the lethal volume inset below it.</summary>
        Landable = 1,

        /// <summary>Sloped surface that raises the ground. Never lethal.</summary>
        Ramp = 2,

        /// <summary>Flat hazard on the road with a shallow kill volume above it.</summary>
        Pit = 3
    }
}
