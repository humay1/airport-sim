using System.Collections.Generic;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>15 §15.17 "Visual layers" (Q-130).</summary>
    public sealed class Art2DLayerTests
    {
        [Fact]
        public void test_art2d_layers_match_the_visual_table()
        {
            // The table covers every VisualId of 15 §15.9.
            Assert.Equal(34, ArtTable.AllVisuals().Length);

            foreach (VisualId v in ArtTable.AllVisuals())
            {
                List<ArtLayer> want = ArtTable.Layers(v);
                IReadOnlyList<ArtLayer> got = Art2DFactory.LayersOf(v);
                Assert.True(got != null, "LayersOf(" + v + ") is null");
                string w = ArtCells.Show(want);
                string g = ArtCells.Show(got!);
                Assert.True(w == g, "LayersOf(" + v + ") in painter order:\n" + g + "\nexpected:\n" + w);
            }

            // The logo sub-square's integer rule, spelled out for two sizes.
            AssertLogo(VisualId.AircraftC, 447, 250, 578, 381);
            AssertLogo(VisualId.AircraftF, 416, 127, 609, 320);

            // A fresh list per call.
            foreach (VisualId v in new[] { VisualId.Apron, VisualId.Passenger, VisualId.AircraftD })
            {
                Assert.NotSame(Art2DFactory.LayersOf(v), Art2DFactory.LayersOf(v));
            }
        }

        private static void AssertLogo(VisualId v, int minX, int minY, int maxX, int maxY)
        {
            ArtTable.LogoSquare(v - VisualId.AircraftA, out int kx0, out int ky0, out int kx1, out int ky1);
            Assert.Equal(new[] { minX, minY, maxX, maxY }, new[] { kx0, ky0, kx1, ky1 });

            IReadOnlyList<ArtLayer> layers = Art2DFactory.LayersOf(v);
            Assert.Equal(8, layers.Count);
            ArtLayer logo = layers[6];
            Assert.True(logo.IsLogo, v + " layer 6 is the logo");
            Assert.Equal(LayerColour.Region, logo.Colour);
            Assert.Equal(4, logo.Region);
            Assert.Equal(new[] { minX, minY, maxX, maxY }, new[] { logo.MinX, logo.MinY, logo.MaxX, logo.MaxY });
        }
    }
}
