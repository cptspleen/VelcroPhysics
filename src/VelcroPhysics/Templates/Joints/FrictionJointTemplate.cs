using Genbox.VelcroPhysics.Dynamics.Joints;
using Genbox.VelcroPhysics.Dynamics.Joints.Misc;
using Microsoft.Xna.Framework;

namespace Genbox.VelcroPhysics.Templates.Joints
{
    public class FrictionJointTemplate : JointTemplate
    {
        public FrictionJointTemplate() : base(JointType.Friction) { }

        /// <summary>The local anchor point relative to bodyA's origin.</summary>
        public Vector2 LocalAnchorA { get; set; }

        /// <summary>The local anchor point relative to bodyB's origin.</summary>
        public Vector2 LocalAnchorB { get; set; }

        /// <summary>The maximum friction force in N.</summary>
        public float MaxForce { get; set; }

        /// <summary>The maximum friction torque in N-m.</summary>
        public float MaxTorque { get; set; }
    }
}