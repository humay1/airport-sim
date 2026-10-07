using System;
using System.Collections.Generic;

namespace AirportSim.App.Render.Art2D
{
    /// <summary>15 §15.17 "Visual layers" (Q-131), one visual at a time.</summary>
    internal static class VisualLayers
    {
        /// <summary>The row length in hundredths, A to F; the logo sub-square derives from it.</summary>
        private const int RowCount = 6;

        public static List<ArtLayer> Of(VisualId visual)
        {
            var layers = new List<ArtLayer>();
            switch (visual)
            {
                case VisualId.RunwaySurface:
                    layers.Add(RoleTiled(Packing.Band(BandCell.Asphalt), 32));
                    layers.Add(Sub(Fixed(Packing.Small(SmallCell.Rubber), 0x1A, 0x1A, 0x1A), 320, 77, 704, 307));
                    layers.Add(Sub(Fixed(Packing.Small(SmallCell.Rubber), 0x1A, 0x1A, 0x1A), 320, 717, 704, 947));
                    break;
                case VisualId.TaxiwaySurface:
                    layers.Add(RoleTiled(Packing.Band(BandCell.Asphalt), 32));
                    break;
                case VisualId.Apron:
                    layers.Add(RoleTiled(Packing.Band(BandCell.Concrete), 64));
                    break;
                case VisualId.QueueFill:
                    layers.Add(Role(Packing.Small(SmallCell.Solid)));
                    break;
                case VisualId.RunwayEdgeLines:
                    layers.Add(Role(Packing.Small(SmallCell.RunwayEdgeLines)));
                    break;
                case VisualId.RunwayThreshold:
                    layers.Add(Role(Packing.Small(SmallCell.RunwayThreshold)));
                    break;
                case VisualId.RunwayCentreDash:
                case VisualId.TaxiwayCentreline:
                    layers.Add(Role(Packing.Small(SmallCell.CentreStripe)));
                    break;
                case VisualId.TaxiwayJunction:
                    layers.Add(Role(Packing.Small(SmallCell.Disc)));
                    break;
                case VisualId.TerminalBuilding:
                    AddBuilding(layers, 600, -800);
                    break;
                case VisualId.Pier:
                    AddBuilding(layers, 450, -600);
                    break;
                case VisualId.ControlTower:
                    layers.Add(Shift(Sliced(Fixed(Packing.Small(SmallCell.SoftBox), 0, 0, 0), 128, 300), 900, -1200));
                    layers.Add(Role(Packing.Band(BandCell.ControlTower)));
                    break;
                case VisualId.JetBridge:
                    layers.Add(Shift(Fixed(Packing.Small(SmallCell.SoftBar), 0, 0, 0), 150, -200));
                    layers.Add(Role(Packing.Small(SmallCell.JetBridge)));
                    break;
                case VisualId.StandPad:
                    layers.Add(RoleTiled(Packing.Band(BandCell.Concrete), 64));
                    layers.Add(Sliced(Fixed(Packing.Small(SmallCell.StandPad), 0xC2, 0x3B, 0x30), 128, 200));
                    break;
                case VisualId.StandLeadIn:
                    layers.Add(Role(Packing.Small(SmallCell.StandLeadIn)));
                    layers.Add(Fixed(Packing.Band(BandCell.GseBody), 0xE0, 0xB1, 0x2A));
                    layers.Add(Fixed(Packing.Band(BandCell.GseDetail), 0x30, 0x35, 0x3B));
                    break;
                case VisualId.TerminalZone:
                    layers.Add(Sliced(Role(Packing.Small(SmallCell.TerminalZone)), 128, 100));
                    break;
                case VisualId.LanePip:
                    layers.Add(Role(Packing.Small(SmallCell.LanePip)));
                    break;
                case VisualId.Passenger:
                    layers.Add(Role(Packing.Small(SmallCell.PaxOutline)));
                    layers.Add(Region(Packing.Small(SmallCell.PaxBottom), 1));
                    layers.Add(Region(Packing.Small(SmallCell.PaxBag), 4));
                    layers.Add(Region(Packing.Small(SmallCell.PaxTop), 0));
                    layers.Add(Region(Packing.Small(SmallCell.PaxSkin), 2));
                    layers.Add(Region(Packing.Small(SmallCell.PaxHair), 3));
                    break;
                default:
                    if (visual >= VisualId.MarkingDigit0 && visual <= VisualId.MarkingDigit9)
                    {
                        layers.Add(Role(Packing.Small(SmallCell.Digit0 + (visual - VisualId.MarkingDigit0))));
                    }
                    else if (visual >= VisualId.AircraftA && visual <= VisualId.AircraftF)
                    {
                        AddAircraft(layers, visual - VisualId.AircraftA);
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(visual), visual, "not a VisualId of 15 §15.9");
                    }

                    break;
            }

            return layers;
        }

        public static ArtLayer Ground()
        {
            return new ArtLayer(
                LayerColour.Fixed, 0, new Rgb(0x6F, 0x8F, 0x5E), Packing.Band(BandCell.Grass).Rect(), false, 0, 0, Art2DConstants.ART_UNITS, Art2DConstants.ART_UNITS, 0, 0, Art2DConstants.GROUND_TILE, 0, 0);
        }

        /// <summary>The logo sub-square's integer rule (15 §15.17), for size category 0 to 5.</summary>
        public static void LogoSquare(int size, out int minX, out int minY, out int maxX, out int maxY)
        {
            int lh = LengthHundredths(size);
            int side = ((lh * 2048) + 500) / 1000;
            int off = ((lh * 3072) + 500) / 1000;
            int half = side / 2;
            minX = 512 - half;
            maxX = minX + side;
            minY = 512 - off - half;
            maxY = minY + side;
        }

        /// <summary>The row's length in hundredths of ART_UNITS (Q-131).</summary>
        public static int LengthHundredths(int size)
        {
            switch (size)
            {
                case 0: return 38;
                case 1: return 56;
                case 2: return 64;
                case 3: return 80;
                case 4: return 92;
                default: return 88;
            }
        }

        private static void AddBuilding(List<ArtLayer> layers, int shiftX, int shiftY)
        {
            layers.Add(Shift(Sliced(Fixed(Packing.Small(SmallCell.SoftBox), 0, 0, 0), 128, 300), shiftX, shiftY));
            layers.Add(RoleTiled(Packing.Band(BandCell.Roof), 32));
            layers.Add(Sliced(Role(Packing.Small(SmallCell.Parapet)), 128, 300));
        }

        private static void AddAircraft(List<ArtLayer> layers, int size)
        {
            if (size < 0 || size >= RowCount)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            // The shadow's shift is the caster's height (2 m rising 0.5 m per row) times 50 times (3, -4) / 5, in hundredths.
            int height10 = 20 + (5 * size);
            layers.Add(Shift(Fixed(Packing.Aircraft(size, 0), 0, 0, 0), height10 * 3, height10 * -4));
            layers.Add(Role(Packing.Aircraft(size, 1)));
            layers.Add(Fixed(Packing.Aircraft(size, 2), 0xD5, 0xD8, 0xDC));
            layers.Add(Region(Packing.Aircraft(size, 3), 3));
            layers.Add(Region(Packing.Aircraft(size, 4), 0));
            layers.Add(Region(Packing.Aircraft(size, 5), 2));
            layers.Add(Region(Packing.Aircraft(size, 6), 1));
            LogoSquare(size, out int minX, out int minY, out int maxX, out int maxY);
            layers.Add(new ArtLayer(LayerColour.Region, 4, default(Rgb), default(AtlasRect), true, minX, minY, maxX, maxY, 0, 0, 0, 0, 0));
            layers.Add(Fixed(Packing.Aircraft(size, 7), 0x2A, 0x31, 0x38));
        }

        private static ArtLayer Role(CellPlace cell)
        {
            return Whole(LayerColour.Role, 0, default(Rgb), cell.Rect());
        }

        private static ArtLayer RoleTiled(CellPlace cell, int tile)
        {
            ArtLayer l = Role(cell);
            return new ArtLayer(l.Colour, l.Region, l.Fixed, l.Rect, l.IsLogo, l.MinX, l.MinY, l.MaxX, l.MaxY, 0, 0, tile, 0, 0);
        }

        private static ArtLayer Region(CellPlace cell, int region)
        {
            return Whole(LayerColour.Region, region, default(Rgb), cell.Rect());
        }

        private static ArtLayer Fixed(CellPlace cell, byte r, byte g, byte b)
        {
            return Whole(LayerColour.Fixed, 0, new Rgb(r, g, b), cell.Rect());
        }

        private static ArtLayer Whole(LayerColour colour, int region, Rgb fixedColour, AtlasRect rect)
        {
            return new ArtLayer(colour, region, fixedColour, rect, false, 0, 0, Art2DConstants.ART_UNITS, Art2DConstants.ART_UNITS, 0, 0, 0, 0, 0);
        }

        private static ArtLayer Sub(ArtLayer l, int minX, int minY, int maxX, int maxY)
        {
            return new ArtLayer(l.Colour, l.Region, l.Fixed, l.Rect, l.IsLogo, minX, minY, maxX, maxY, l.SliceInset, l.SliceWorld, l.Tile, l.ShiftX, l.ShiftY);
        }

        private static ArtLayer Sliced(ArtLayer l, int inset, int world)
        {
            return new ArtLayer(l.Colour, l.Region, l.Fixed, l.Rect, l.IsLogo, l.MinX, l.MinY, l.MaxX, l.MaxY, inset, world, l.Tile, l.ShiftX, l.ShiftY);
        }

        private static ArtLayer Shift(ArtLayer l, int x, int y)
        {
            return new ArtLayer(l.Colour, l.Region, l.Fixed, l.Rect, l.IsLogo, l.MinX, l.MinY, l.MaxX, l.MaxY, l.SliceInset, l.SliceWorld, l.Tile, x, y);
        }
    }
}
