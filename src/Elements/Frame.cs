using Elements.Geometry;
using Elements.Geometry.Solids;
using System;

namespace Elements
{
    /// <summary>
    /// An element defined by a perimeter and a cross section swept along that perimeter.
    /// </summary>
    [UserElement]
    public class Frame : GeometricElement
    {
        /// <summary>
        /// The frame's profile.
        /// </summary>
        public Profile Profile { get; private set; }

        /// <summary>
        /// The perimeter of the frame.
        /// </summary>
        public Curve Curve { get; private set; }

        /// <summary>
        /// Create a frame.
        /// </summary>
        /// <param name="curve">The frame's perimeter.</param>
        /// <param name="profile">The frame's profile.</param>
        /// <param name="offset">The amount which the perimeter will be offset internally.</param>
        /// <param name="material">The frame's material.</param>
        /// <param name="transform">The frame's transform.</param>
        /// <param name="id">The id of the frame.</param>
        /// <param name="name">The name of the frame.</param>
        public Frame(Polygon curve,
                     Profile profile,
                     double offset = 0.0,
                     Material material = null,
                     Transform transform = null,
                     Guid id = default(Guid),
                     string name = null) : base(material, transform, id, name)
        {
            SetProperties(curve, profile, transform, offset);
        }

        private void SetProperties(Polygon curve, Profile profile, Transform transform, double offset)
        {
            this.Curve = curve.Offset(-offset)[0];
            this.Profile = profile;
            this.Representation.SolidOperations.Add(new Sweep(this.Profile, this.Curve, 0.0, 0.0));
        }

        /// <summary>
        /// Update the representations.
        /// </summary>
        public override void UpdateRepresentations()
        {
            return;
        }
    }
}