using System;
using System.Collections.Generic;

namespace AirportSim.App.Render.Art2D
{
    /// <summary>Creates the 2D art's parts. Spec: 15 §15.17, 08 §8.11a's factory rule.</summary>
    public static class Art2DFactory
    {
        /// <summary>Rasterises the code-held art into a fresh atlas: ATLAS_SIZE square, ATLAS_MIP_COUNT mips, every one rasterised directly.</summary>
        public static SpriteAtlas BuildAtlas()
        {
            var mips = new byte[Art2DConstants.ATLAS_MIP_COUNT][];
            for (int m = 0; m < mips.Length; m++)
            {
                int side = Art2DConstants.ATLAS_SIZE >> m;
                mips[m] = new byte[side * side * 4];
            }

            var rasteriser = new Rasteriser();
            List<CellDef> cells = Cells.All();
            for (int i = 0; i < cells.Count; i++)
            {
                rasteriser.RenderCell(cells[i], mips);
            }

            return new SpriteAtlas(Art2DConstants.ATLAS_SIZE, mips);
        }

        /// <summary>The layers of a visual in painter order, a fresh list per call.</summary>
        public static IReadOnlyList<ArtLayer> LayersOf(VisualId visual)
        {
            return VisualLayers.Of(visual);
        }

        /// <summary>The cell of a logo mark; throws ArgumentOutOfRangeException for None.</summary>
        public static AtlasRect LogoRect(LogoMark mark)
        {
            int m = (int)mark;
            if (m < 1 || m > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(mark), mark, "a logo mark has a cell; None has none");
            }

            return Packing.Small(Packing.Logo(m)).Rect();
        }

        /// <summary>The grass under every frame.</summary>
        public static ArtLayer GroundLayer()
        {
            return VisualLayers.Ground();
        }

        /// <summary>A fresh tessellator.</summary>
        public static ISpriteTessellator CreateTessellator()
        {
            return new SpriteTessellator();
        }
    }
}
