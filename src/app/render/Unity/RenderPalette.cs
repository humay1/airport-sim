using AirportSim.App.Render;
using UnityEngine;

namespace AirportSim.App.Render.Unity
{
    /// <summary>The palette asset: one colour per <see cref="ColourRole"/>, in enum order (15 §15.10).</summary>
    [CreateAssetMenu(menuName = "AirportSim/Render palette")]
    public sealed class RenderPalette : ScriptableObject
    {
        /// <summary>Screen clear colour.</summary>
        public Color Background = Color.black;

        /// <summary>Indexed by <c>(int)ColourRole</c>; length must equal the number of roles.</summary>
        public Color[] Roles = new Color[0];
    }
}
