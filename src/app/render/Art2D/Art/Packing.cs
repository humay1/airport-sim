namespace AirportSim.App.Render.Art2D
{
    /// <summary>Slot numbers of the small cells, in slot order. Spec: 15 §15.17 "Packing".</summary>
    internal enum SmallCell
    {
        Solid,
        Disc,
        RunwayEdgeLines,
        CentreStripe,
        RunwayThreshold,
        Parapet,
        SoftBox,
        JetBridge,
        StandPad,
        StandLeadIn,
        Digit0,
        Digit1,
        Digit2,
        Digit3,
        Digit4,
        Digit5,
        Digit6,
        Digit7,
        Digit8,
        Digit9,
        TerminalZone,
        LanePip,
        PaxOutline,
        PaxBottom,
        PaxBag,
        PaxTop,
        PaxSkin,
        PaxHair,
        LogoDisc,
        LogoRing,
        LogoChevron,
        LogoStar,
        LogoBars,
        LogoDiamond,
        LogoCrescent,
        SoftBar,
        Rubber,
    }

    /// <summary>Slot numbers of the large band cells, in slot order.</summary>
    internal enum BandCell
    {
        Asphalt,
        Concrete,
        Grass,
        Roof,
        ControlTower,
        GseBody,
        GseDetail,
    }

    /// <summary>Where a cell sits in the atlas, at mip 0.</summary>
    internal readonly struct CellPlace
    {
        public CellPlace(int px, int py, int side)
        {
            Px = px;
            Py = py;
            Side = side;
        }

        public int Px { get; }

        public int Py { get; }

        public int Side { get; }

        /// <summary>The visible square, exact in float: (px + c/32) / 4096 and so on.</summary>
        public AtlasRect Rect()
        {
            int b = Side / 32;
            return new AtlasRect(
                (float)((Px + b) / 4096.0),
                (float)((Py + b) / 4096.0),
                (float)((Px + Side - b) / 4096.0),
                (float)((Py + Side - b) / 4096.0));
        }
    }

    /// <summary>The packing arithmetic, with no table.</summary>
    internal static class Packing
    {
        public static CellPlace Small(SmallCell cell)
        {
            int k = (int)cell;
            return new CellPlace(Art2DConstants.SMALL_CELL * (k % 32), 3584 + (Art2DConstants.SMALL_CELL * (k / 32)), Art2DConstants.SMALL_CELL);
        }

        public static CellPlace Band(BandCell cell)
        {
            return new CellPlace(Art2DConstants.LARGE_CELL * (int)cell, 3072, Art2DConstants.LARGE_CELL);
        }

        /// <summary>Size category 0 to 5 is the row, aircraft layer 0 to 7 the column.</summary>
        public static CellPlace Aircraft(int size, int layer)
        {
            return new CellPlace(Art2DConstants.LARGE_CELL * layer, Art2DConstants.LARGE_CELL * size, Art2DConstants.LARGE_CELL);
        }

        /// <summary>Logo marks 1 to 7 are small slots 28 to 34.</summary>
        public static SmallCell Logo(int mark)
        {
            return (SmallCell)((int)SmallCell.LogoDisc + mark - 1);
        }
    }
}
