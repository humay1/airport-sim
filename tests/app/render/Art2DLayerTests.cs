using System;
using System.Collections.Generic;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>15 §15.17 "Visual layers" (Q-130, table replaced by Q-131) and GroundLayer().</summary>
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
                string w = ArtShow.Layers(want);
                string g = ArtShow.Layers(got!);
                Assert.True(w == g, "LayersOf(" + v + ") in painter order:\n" + g + "\nexpected:\n" + w);
            }

            // Spelled out: the building shadow, the stand pad's two layers and an aircraft shadow.
            ArtLayer soft = Art2DFactory.LayersOf(VisualId.TerminalBuilding)[0];
            Assert.Equal(new[] { 128, 300, 0, 600, -800 }, new[] { soft.SliceInset, soft.SliceWorld, soft.Tile, soft.ShiftX, soft.ShiftY });
            IReadOnlyList<ArtLayer> pad = Art2DFactory.LayersOf(VisualId.StandPad);
            Assert.Equal(new[] { 0, 0, 64 }, new[] { pad[0].SliceInset, pad[0].SliceWorld, pad[0].Tile });
            Assert.Equal(new[] { 128, 200, 0 }, new[] { pad[1].SliceInset, pad[1].SliceWorld, pad[1].Tile });
            ArtLayer shadowF = Art2DFactory.LayersOf(VisualId.AircraftF)[0];
            Assert.Equal(LayerColour.Fixed, shadowF.Colour);
            Assert.Equal(new[] { 135, -180 }, new[] { shadowF.ShiftX, shadowF.ShiftY });

            // The logo sub-square's integer rule, for all six sizes.
            int[][] logos =
            {
                new[] { 473, 356, 551, 434 },
                new[] { 455, 283, 570, 398 },
                new[] { 447, 250, 578, 381 },
                new[] { 430, 184, 594, 348 },
                new[] { 418, 135, 606, 323 },
                new[] { 422, 152, 602, 332 },
            };
            for (int s = 0; s < 6; s++)
            {
                ArtTable.LogoSquare(s, out int kx0, out int ky0, out int kx1, out int ky1);
                Assert.Equal(logos[s], new[] { kx0, ky0, kx1, ky1 });

                VisualId v = VisualId.AircraftA + s;
                IReadOnlyList<ArtLayer> layers = Art2DFactory.LayersOf(v);
                Assert.Equal(9, layers.Count);
                ArtLayer logo = layers[7];
                Assert.True(logo.IsLogo, v + " layer 7 is the logo");
                Assert.Equal(LayerColour.Region, logo.Colour);
                Assert.Equal(4, logo.Region);
                Assert.Equal(logos[s], new[] { logo.MinX, logo.MinY, logo.MaxX, logo.MaxY });
            }

            // GroundLayer() matches its definition.
            ArtLayer ground = Art2DFactory.GroundLayer();
            Assert.Equal(ArtShow.Layer(ArtTable.Ground()), ArtShow.Layer(ground));
            Assert.Equal(LayerColour.Fixed, ground.Colour);
            Assert.Equal("#6F8F5E", Prims.Show(ground.Fixed));
            Assert.Equal(Art2DConstants.GROUND_TILE, ground.Tile);

            // A fresh list per call; LogoRect(None) has no cell.
            foreach (VisualId v in new[] { VisualId.Apron, VisualId.Passenger, VisualId.AircraftD })
            {
                Assert.NotSame(Art2DFactory.LayersOf(v), Art2DFactory.LayersOf(v));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => Art2DFactory.LogoRect(LogoMark.None));
        }
    }
}
