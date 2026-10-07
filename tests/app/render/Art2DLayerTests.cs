using System;
using System.Collections.Generic;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.17's ArtLayer and LogoRect contracts, as the public surface states
    /// them. §15.18's test_art2d_layers_match_the_visual_table (the per-visual
    /// table) waits for the realistic-2D amendment of the style.
    /// </summary>
    public sealed class Art2DLayerTests
    {
        [Fact]
        public void test_art2d_layers_are_well_formed_for_every_visual()
        {
            foreach (VisualId v in ArtShow.AllVisuals())
            {
                IReadOnlyList<ArtLayer> layers = Art2DFactory.LayersOf(v);
                Assert.True(layers != null, "LayersOf(" + v + ") is null");
                Assert.True(layers!.Count > 0, "LayersOf(" + v + ") is empty: a visual fills its primitive");
                for (int i = 0; i < layers.Count; i++)
                {
                    ArtLayer l = layers[i];
                    string what = v + " layer " + i;

                    // Region: the Paint region index when Colour = Region, else 0.
                    if (l.Colour == LayerColour.Region)
                    {
                        Assert.True(l.Region >= 0 && l.Region <= 4, what + ": region " + l.Region + " is not a Paint region (0 to 4)");
                    }
                    else
                    {
                        Assert.True(l.Region == 0, what + ": region " + l.Region + " on a " + l.Colour + " layer, expected 0");
                    }

                    // Fixed: the constant when Colour = Fixed, else (0, 0, 0).
                    if (l.Colour != LayerColour.Fixed)
                    {
                        Assert.True(l.Fixed.R == 0 && l.Fixed.G == 0 && l.Fixed.B == 0, what + ": fixed " + Prims.Show(l.Fixed) + " on a " + l.Colour + " layer, expected #000000");
                    }

                    // The sub-square lies in the design square 0..1024 and is not empty.
                    Assert.True(
                        l.MinX >= 0 && l.MinY >= 0 && l.MaxX <= 1024 && l.MaxY <= 1024 && l.MinX < l.MaxX && l.MinY < l.MaxY,
                        what + ": sub-square (" + l.MinX + "," + l.MinY + ")-(" + l.MaxX + "," + l.MaxY + ") is not a non-empty part of 0..1024");

                    // A non-logo layer's rect is a non-empty part of the texture (0..1).
                    if (!l.IsLogo)
                    {
                        AssertRect(l.Rect, what);
                    }
                }
            }

            // LogoRect: a rect for every mark, ArgumentOutOfRangeException for None.
            for (int k = 1; k <= 7; k++)
            {
                AssertRect(Art2DFactory.LogoRect((LogoMark)k), "LogoRect(" + (LogoMark)k + ")");
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => Art2DFactory.LogoRect(LogoMark.None));

            // A fresh list per call, with equal contents.
            foreach (VisualId v in ArtShow.AllVisuals())
            {
                IReadOnlyList<ArtLayer> a = Art2DFactory.LayersOf(v);
                IReadOnlyList<ArtLayer> b = Art2DFactory.LayersOf(v);
                Assert.NotSame(a, b);
                Assert.Equal(a.Count, b.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.True(a[i].Equals(b[i]), v + " layer " + i + " differs between two calls");
                }
            }
        }

        private static void AssertRect(in AtlasRect r, string what)
        {
            Assert.True(
                r.U0 >= 0f && r.V0 >= 0f && r.U1 <= 1f && r.V1 <= 1f && r.U0 < r.U1 && r.V0 < r.V1,
                what + ": rect " + ArtShow.Rect(r) + " is not a non-empty part of 0..1");
        }
    }
}
