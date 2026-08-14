namespace RouteJumper.Models
{
    /// <summary>
    /// A star system's position in the galaxy, in light-years - the same [x, y, z] space Elite's
    /// own journal (StarPos) and EDSM's API both use. Immutable value type since a named system's
    /// coordinates never change.
    /// </summary>
    public readonly record struct GalacticCoordinates(double X, double Y, double Z)
    {
        /// <summary>Plain 3D Euclidean distance to <paramref name="other"/>, in light-years.</summary>
        public double DistanceTo(GalacticCoordinates other)
        {
            var dx = X - other.X;
            var dy = Y - other.Y;
            var dz = Z - other.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
