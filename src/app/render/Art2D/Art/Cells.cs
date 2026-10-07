using System.Collections.Generic;

namespace AirportSim.App.Render.Art2D
{
    /// <summary>
    /// 15 §15.17 style guide (Q-131): one definition per atlas cell, in integer design units.
    /// Definitions are built inside each BuildAtlas call; there is no static mutable state.
    /// </summary>
    internal static class Cells
    {
        private const int Lo = -64;
        private const int Hi = 1088;

        public static List<CellDef> All()
        {
            var all = new List<CellDef>();
            for (int s = 0; s < 6; s++)
            {
                var row = new AircraftArt(s);
                for (int l = 0; l < 8; l++)
                {
                    all.Add(row.Layer(l));
                }
            }

            all.Add(Asphalt());
            all.Add(Concrete());
            all.Add(Grass());
            all.Add(Roof());
            all.Add(ControlTower());
            all.Add(GseBody());
            all.Add(GseDetail());

            all.Add(Solid());
            all.Add(Disc());
            all.Add(RunwayEdgeLines());
            all.Add(CentreStripe());
            all.Add(RunwayThreshold());
            all.Add(Parapet());
            all.Add(SoftBox());
            all.Add(JetBridge());
            all.Add(StandPad());
            all.Add(StandLeadIn());
            for (int n = 0; n < 10; n++)
            {
                all.Add(Digit(n));
            }

            all.Add(TerminalZone());
            all.Add(LanePip());
            all.Add(PaxOutline());
            all.Add(PaxBottom());
            all.Add(PaxBag());
            all.Add(PaxTop());
            all.Add(PaxSkin());
            all.Add(PaxHair());
            all.Add(LogoDisc());
            all.Add(LogoRing());
            all.Add(LogoChevron());
            all.Add(LogoStar());
            all.Add(LogoBars());
            all.Add(LogoDiamond());
            all.Add(LogoCrescent());
            all.Add(SoftBar());
            all.Add(Rubber());
            return all;
        }

        private static CellDef Small(SmallCell c, int edge, bool tiled = false, bool mirror = false)
        {
            return new CellDef(Packing.Small(c), tiled, 0, edge, mirror, 255);
        }

        private static CellDef Band(BandCell c, int edge, bool tiled = false)
        {
            return new CellDef(Packing.Band(c), tiled, 0, edge, false, 255);
        }

        // Worn paint: alpha 235 with NA = 20 and noise(64, 3, salt), so it fades in patches but never below 215.
        private static FillSpec Worn(int salt)
        {
            return FillSpec.Flat(255, 235).WithNoise(0, 20, 64, 3, salt, true);
        }

        // ------------------------------------------------------------------ surfaces and tiled textures

        // Asphalt's two fills: the base, then the aggregate over it. Disc uses them on a circle with its own salts.
        private static void AsphaltFills(CellDef cell, int saltBase, int saltGrain, bool disc)
        {
            FillSpec baseFill = FillSpec.Flat(236).WithNoise(14, 0, 128, 4, saltBase, true);
            FillSpec grain = FillSpec.Flat(236, 64).WithNoise(40, 0, 8, 1, saltGrain, false);
            cell.Add(disc ? ArtShape.Circle(512, 512, 496, baseFill) : FullSquare(baseFill));
            cell.Add(disc ? ArtShape.Circle(512, 512, 496, grain) : FullSquare(grain));
        }

        private static ArtShape FullSquare(FillSpec f)
        {
            return ArtShape.Rect(Lo, Lo, Hi, Hi, f);
        }

        private static CellDef Asphalt()
        {
            CellDef cell = Band(BandCell.Asphalt, 236, true);
            AsphaltFills(cell, 100, 110, false);
            return cell;
        }

        private static CellDef Disc()
        {
            CellDef cell = Small(SmallCell.Disc, 236);
            AsphaltFills(cell, 120, 130, true);
            return cell;
        }

        private static CellDef Concrete()
        {
            CellDef cell = Band(BandCell.Concrete, 234, true);
            cell.Add(FullSquare(FillSpec.Flat(234).WithNoise(8, 0, 128, 1, 200, false)));
            cell.Add(FullSquare(FillSpec.Flat(234, 96).WithNoise(12, 0, 64, 3, 210, true)));
            cell.Add(FullSquare(FillSpec.Flat(150, -220).WithNoise(0, 300, 256, 2, 220, true)));
            FillSpec joint = FillSpec.Flat(180);
            for (int k = 0; k <= 8; k++)
            {
                cell.Add(ArtShape.Rect((128 * k) - 3, Lo, (128 * k) + 3, Hi, joint));
                cell.Add(ArtShape.Rect(Lo, (128 * k) - 3, Hi, (128 * k) + 3, joint));
            }

            return cell;
        }

        private static CellDef Grass()
        {
            CellDef cell = Band(BandCell.Grass, 228, true);
            cell.Add(FullSquare(FillSpec.Flat(228).WithNoise(20, 0, 256, 4, 300, true)));
            cell.Add(FullSquare(FillSpec.Flat(228, 80).WithNoise(30, 0, 8, 1, 310, false)));
            return cell;
        }

        private static CellDef Roof()
        {
            CellDef cell = Band(BandCell.Roof, 238, true);
            cell.Add(FullSquare(FillSpec.Flat(238).WithNoise(5, 0, 256, 2, 400, true)));
            FillSpec seam = FillSpec.Flat(222);
            for (int k = 0; k <= 32; k++)
            {
                cell.Add(ArtShape.Rect((32 * k) - 2, Lo, (32 * k) + 2, Hi, seam));
            }

            cell.Add(ArtShape.Rect(Lo, 224, Hi, 288, FillSpec.Flat(150)));
            cell.Add(ArtShape.Rect(Lo, 736, Hi, 800, FillSpec.Flat(150)));
            FillSpec mullion = FillSpec.Flat(205);
            for (int k = 0; k <= 16; k++)
            {
                cell.Add(ArtShape.Rect((64 * k) - 2, 224, (64 * k) + 2, 288, mullion));
                cell.Add(ArtShape.Rect((64 * k) - 2, 736, (64 * k) + 2, 800, mullion));
            }

            cell.Add(ArtShape.Rect(600, 400, 856, 560, FillSpec.Flat(175, 140), 16));
            cell.Add(ArtShape.Rect(576, 432, 832, 592, FillSpec.Flat(210)));
            cell.Add(ArtShape.Circle(704, 512, 48, FillSpec.Flat(165)));
            return cell;
        }

        // ------------------------------------------------------------------ markings

        private static CellDef Solid()
        {
            return Small(SmallCell.Solid, 255).Add(ArtShape.Rect(Lo, Lo, Hi, Hi, FillSpec.Flat(255)));
        }

        private static CellDef RunwayEdgeLines()
        {
            CellDef cell = Small(SmallCell.RunwayEdgeLines, 255);
            cell.Add(ArtShape.Rect(16, Lo, 48, Hi, Worn(700)));
            cell.Add(ArtShape.Rect(976, Lo, 1008, Hi, Worn(700)));
            return cell;
        }

        private static CellDef CentreStripe()
        {
            return Small(SmallCell.CentreStripe, 255).Add(ArtShape.Rect(480, Lo, 544, Hi, Worn(710)));
        }

        private static CellDef RunwayThreshold()
        {
            CellDef cell = Small(SmallCell.RunwayThreshold, 255);
            FillSpec worn = Worn(720);
            cell.Add(ArtShape.Rect(64, 128, 128, 832, worn));
            cell.Add(ArtShape.Rect(176, 128, 240, 832, worn));
            cell.Add(ArtShape.Rect(288, 128, 352, 832, worn));
            cell.Add(ArtShape.Rect(400, 128, 464, 832, worn));
            cell.Add(ArtShape.Rect(560, 128, 624, 832, worn));
            cell.Add(ArtShape.Rect(672, 128, 736, 832, worn));
            cell.Add(ArtShape.Rect(784, 128, 848, 832, worn));
            cell.Add(ArtShape.Rect(896, 128, 960, 832, worn));
            return cell;
        }

        private static CellDef StandLeadIn()
        {
            return Small(SmallCell.StandLeadIn, 255).Add(ArtShape.Poly(
                new[] { 488, 16, 536, 16, 536, 800, 672, 800, 672, 848, 352, 848, 352, 800, 488, 800 },
                Worn(730)));
        }

        private static CellDef Digit(int n)
        {
            CellDef cell = Small(SmallCell.Digit0 + n, 255);
            FillSpec worn = Worn(740 + (10 * n));
            switch (n)
            {
                case 0:
                    cell.Add(ArtShape.Rings(
                        new[]
                        {
                            new[] { 256, 128, 768, 128, 768, 896, 256, 896 },
                            new[] { 352, 224, 352, 800, 672, 800, 672, 224 },
                        },
                        worn));
                    break;
                case 1:
                    cell.Add(ArtShape.Poly(new[] { 352, 128, 672, 128, 672, 224, 560, 224, 560, 896, 464, 896, 352, 792, 352, 696, 464, 800, 464, 224, 352, 224 }, worn));
                    break;
                case 2:
                    cell.Add(ArtShape.Poly(new[] { 256, 128, 768, 128, 768, 224, 352, 224, 352, 464, 768, 464, 768, 896, 256, 896, 256, 800, 672, 800, 672, 560, 256, 560 }, worn));
                    break;
                case 3:
                    cell.Add(ArtShape.Poly(new[] { 256, 128, 768, 128, 768, 896, 256, 896, 256, 800, 672, 800, 672, 560, 352, 560, 352, 464, 672, 464, 672, 224, 256, 224 }, worn));
                    break;
                case 4:
                    cell.Add(ArtShape.Rings(
                        new[]
                        {
                            new[] { 672, 128, 768, 128, 768, 896, 256, 896, 256, 464, 672, 464 },
                            new[] { 352, 560, 352, 800, 672, 800, 672, 560 },
                        },
                        worn));
                    break;
                case 5:
                    cell.Add(ArtShape.Poly(new[] { 256, 128, 768, 128, 768, 560, 352, 560, 352, 800, 768, 800, 768, 896, 256, 896, 256, 464, 672, 464, 672, 224, 256, 224 }, worn));
                    break;
                case 6:
                    cell.Add(ArtShape.Rings(
                        new[]
                        {
                            new[] { 256, 128, 768, 128, 768, 560, 352, 560, 352, 800, 768, 800, 768, 896, 256, 896 },
                            new[] { 352, 224, 352, 464, 672, 464, 672, 224 },
                        },
                        worn));
                    break;
                case 7:
                    cell.Add(ArtShape.Poly(new[] { 256, 800, 256, 896, 768, 896, 768, 128, 672, 128, 672, 800 }, worn));
                    break;
                case 8:
                    cell.Add(ArtShape.Rings(
                        new[]
                        {
                            new[] { 256, 128, 768, 128, 768, 896, 256, 896 },
                            new[] { 352, 224, 352, 464, 672, 464, 672, 224 },
                            new[] { 352, 560, 352, 800, 672, 800, 672, 560 },
                        },
                        worn));
                    break;
                default:
                    cell.Add(ArtShape.Rings(
                        new[]
                        {
                            new[] { 256, 128, 768, 128, 768, 896, 256, 896, 256, 464, 672, 464, 672, 224, 256, 224 },
                            new[] { 352, 560, 352, 800, 672, 800, 672, 560 },
                        },
                        worn));
                    break;
            }

            return cell;
        }

        // ------------------------------------------------------------------ buildings

        private static CellDef Parapet()
        {
            CellDef cell = Small(SmallCell.Parapet, 205);
            FillSpec dark = FillSpec.Flat(160);
            FillSpec lit = FillSpec.Flat(205);
            cell.Add(ArtShape.Rect(16, 16, 1008, 80, dark));
            cell.Add(ArtShape.Rect(944, 16, 1008, 1008, dark));
            cell.Add(ArtShape.Rect(16, 944, 1008, 1008, lit));
            cell.Add(ArtShape.Rect(16, 16, 80, 1008, lit));

            // The roof shade, 80 to 120 from each edge, fading from alpha 120 at the parapet to 0.
            cell.Add(ArtShape.Rect(80, 904, 944, 944, FillSpec.LinearAlpha(140, 120, 0, 0, 944, 0, 904)));
            cell.Add(ArtShape.Rect(80, 80, 944, 120, FillSpec.LinearAlpha(140, 120, 0, 0, 80, 0, 120)));
            cell.Add(ArtShape.Rect(80, 80, 120, 944, FillSpec.LinearAlpha(140, 120, 0, 80, 0, 120, 0)));
            cell.Add(ArtShape.Rect(904, 80, 944, 944, FillSpec.LinearAlpha(140, 120, 0, 944, 0, 904, 0)));
            return cell;
        }

        private static CellDef SoftBox()
        {
            return Small(SmallCell.SoftBox, 255).Add(ArtShape.Rect(64, 64, 960, 960, FillSpec.Flat(255, 112), 96));
        }

        private static CellDef SoftBar()
        {
            return Small(SmallCell.SoftBar, 255).Add(ArtShape.Rect(128, Lo, 896, Hi, FillSpec.Flat(255, 112), 96));
        }

        private static CellDef ControlTower()
        {
            CellDef cell = Band(BandCell.ControlTower, 215);
            cell.Add(ArtShape.Rect(96, 96, 928, 928, FillSpec.Linear(215, 170, 96, 928, 928, 96)));
            cell.Add(ArtShape.Circle(512, 512, 384, FillSpec.Flat(70)));
            cell.Add(ArtShape.Circle(512, 512, 320, FillSpec.Radial(250, 200, 448, 576, 400)));
            cell.Add(ArtShape.Circle(512, 512, 32, FillSpec.Flat(140)));
            return cell;
        }

        private static CellDef JetBridge()
        {
            CellDef cell = Small(SmallCell.JetBridge, 220, false, true);
            cell.Add(ArtShape.Rect(128, Lo, 520, Hi, FillSpec.Linear(195, 245, 128, 0, 512, 0)));
            for (int k = 0; k <= 8; k++)
            {
                cell.Add(ArtShape.Rect(128, (128 * k) - 4, 520, (128 * k) + 4, FillSpec.Flat(190)));
            }

            cell.Add(ArtShape.Rect(128, Lo, 176, Hi, FillSpec.Flat(165)));
            cell.Add(ArtShape.Rect(96, 896, 520, Hi, FillSpec.Flat(225)));
            return cell;
        }

        private static CellDef StandPad()
        {
            CellDef cell = Small(SmallCell.StandPad, 255);
            FillSpec line = FillSpec.Flat(255, 230);
            cell.Add(ArtShape.Rect(48, 944, 976, 976, line));
            cell.Add(ArtShape.Rect(48, 48, 976, 80, line));
            cell.Add(ArtShape.Rect(48, 48, 80, 976, line));
            cell.Add(ArtShape.Rect(944, 48, 976, 976, line));
            return cell;
        }

        private static CellDef TerminalZone()
        {
            CellDef cell = Small(SmallCell.TerminalZone, 205);
            cell.Add(ArtShape.Rect(16, 16, 1008, 1008, FillSpec.Flat(205)));
            cell.Add(ArtShape.Rect(120, 120, 904, 904, FillSpec.Flat(245)));
            return cell;
        }

        private static CellDef LanePip()
        {
            CellDef cell = Small(SmallCell.LanePip, 230);
            cell.Add(ArtShape.Rect(96, 96, 928, 928, FillSpec.Flat(140)));
            cell.Add(ArtShape.Rect(128, 128, 896, 896, FillSpec.Flat(230)));
            cell.Add(ArtShape.Circle(512, 512, 128, FillSpec.Flat(150)));
            return cell;
        }

        // ------------------------------------------------------------------ ground equipment

        private static CellDef GseBody()
        {
            CellDef cell = Band(BandCell.GseBody, 235);

            // Left: a pushback tug about 2.6 x 6 m with its cab, and a ground power unit about 1.5 x 3 m.
            cell.Add(ArtShape.Rect(137, 600, 204, 754, FillSpec.Radial(255, 215, 170, 677, 90)));
            cell.Add(ArtShape.Rect(145, 640, 196, 722, FillSpec.Radial(255, 225, 170, 681, 60)));
            cell.Add(ArtShape.Rect(151, 818, 189, 895, FillSpec.Radial(255, 215, 170, 856, 44)));

            // Right: a baggage tractor and two baggage carts, in a line along Y.
            cell.Add(ArtShape.Rect(835, 600, 873, 677, FillSpec.Radial(255, 215, 854, 638, 44)));
            cell.Add(ArtShape.Rect(840, 612, 868, 654, FillSpec.Radial(255, 230, 854, 633, 30)));
            cell.Add(ArtShape.Rect(835, 741, 873, 818, FillSpec.Radial(255, 215, 854, 779, 44)));
            cell.Add(ArtShape.Rect(835, 882, 873, 959, FillSpec.Radial(255, 215, 854, 920, 44)));
            return cell;
        }

        private static CellDef GseDetail()
        {
            CellDef cell = Band(BandCell.GseDetail, 215);

            // The soft shade under each item: its footprint grown by 16, alpha 60, softness 24.
            FillSpec shade = FillSpec.Flat(255, 60);
            cell.Add(ArtShape.Rect(121, 584, 220, 770, shade, 24));
            cell.Add(ArtShape.Rect(135, 802, 205, 911, shade, 24));
            cell.Add(ArtShape.Rect(819, 584, 889, 693, shade, 24));
            cell.Add(ArtShape.Rect(819, 725, 889, 834, shade, 24));
            cell.Add(ArtShape.Rect(819, 866, 889, 975, shade, 24));

            // Tyres, cab glazing and cart beds.
            FillSpec tyre = FillSpec.Flat(255);
            cell.Add(ArtShape.Rect(130, 612, 138, 642, tyre));
            cell.Add(ArtShape.Rect(203, 612, 211, 642, tyre));
            cell.Add(ArtShape.Rect(130, 704, 138, 734, tyre));
            cell.Add(ArtShape.Rect(203, 704, 211, 734, tyre));
            cell.Add(ArtShape.Rect(146, 830, 152, 860, tyre));
            cell.Add(ArtShape.Rect(188, 830, 194, 860, tyre));
            cell.Add(ArtShape.Rect(830, 608, 836, 628, tyre));
            cell.Add(ArtShape.Rect(872, 608, 878, 628, tyre));
            cell.Add(ArtShape.Rect(830, 649, 836, 669, tyre));
            cell.Add(ArtShape.Rect(872, 649, 878, 669, tyre));
            cell.Add(ArtShape.Rect(830, 749, 836, 809, FillSpec.Flat(205)));
            cell.Add(ArtShape.Rect(872, 749, 878, 809, FillSpec.Flat(205)));
            cell.Add(ArtShape.Rect(830, 890, 836, 950, FillSpec.Flat(205)));
            cell.Add(ArtShape.Rect(872, 890, 878, 950, FillSpec.Flat(205)));
            FillSpec glass = FillSpec.Flat(200);
            cell.Add(ArtShape.Rect(151, 665, 190, 700, glass));
            cell.Add(ArtShape.Rect(844, 620, 864, 640, glass));
            FillSpec bed = FillSpec.Flat(210);
            cell.Add(ArtShape.Rect(843, 750, 865, 809, bed));
            cell.Add(ArtShape.Rect(843, 891, 865, 950, bed));
            return cell;
        }

        // ------------------------------------------------------------------ passengers

        private static int[] PaxShoulders()
        {
            return new[] { 192, 420, 240, 330, 380, 290, 644, 290, 784, 330, 832, 420, 832, 540, 760, 640, 264, 640, 192, 540 };
        }

        private static CellDef PaxOutline()
        {
            CellDef cell = new CellDef(Packing.Small(SmallCell.PaxOutline), false, 48, 255, true, 255);
            FillSpec f = FillSpec.Flat(255);
            cell.Add(ArtShape.Poly(PaxShoulders(), f));
            cell.Add(ArtShape.Circle(512, 560, 150, f));
            cell.Add(ArtShape.Rect(340, 690, 450, 820, f));
            cell.Add(ArtShape.Circle(185, 500, 70, f));
            return cell;
        }

        private static CellDef PaxBottom()
        {
            CellDef cell = Small(SmallCell.PaxBottom, 230, false, true);
            cell.Add(ArtShape.Rect(340, 690, 450, 820, FillSpec.Radial(255, 215, 395, 755, 100)));
            return cell;
        }

        private static CellDef PaxBag()
        {
            CellDef cell = Small(SmallCell.PaxBag, 235);
            cell.Add(ArtShape.Rect(790, 330, 870, 440, FillSpec.Flat(235)));
            cell.Add(ArtShape.Rect(804, 346, 856, 402, FillSpec.Flat(255)));
            return cell;
        }

        private static CellDef PaxTop()
        {
            CellDef cell = Small(SmallCell.PaxTop, 235, false, true);
            cell.Add(ArtShape.Poly(PaxShoulders(), FillSpec.Radial(255, 215, 512, 465, 340)));
            return cell;
        }

        private static CellDef PaxSkin()
        {
            CellDef cell = Small(SmallCell.PaxSkin, 235, false, true);
            cell.Add(ArtShape.Circle(185, 500, 70, FillSpec.Radial(255, 225, 185, 500, 70)));
            return cell;
        }

        private static CellDef PaxHair()
        {
            CellDef cell = Small(SmallCell.PaxHair, 235, false, true);
            cell.Add(ArtShape.Circle(512, 560, 150, FillSpec.Radial(255, 215, 512, 560, 150)));
            return cell;
        }

        // ------------------------------------------------------------------ logo marks

        private static CellDef LogoDisc()
        {
            return Small(SmallCell.LogoDisc, 255).Add(ArtShape.Circle(512, 512, 440, FillSpec.Flat(255)));
        }

        private static CellDef LogoRing()
        {
            return Small(SmallCell.LogoRing, 255).Add(ArtShape.Rings(
                new[]
                {
                    new[]
                    {
                        962, 512, 928, 684, 830, 830, 684, 928, 512, 962, 340, 928, 194, 830, 96, 684,
                        62, 512, 96, 340, 194, 194, 340, 96, 512, 62, 684, 96, 830, 194, 928, 340,
                    },
                    new[]
                    {
                        762, 512, 743, 608, 689, 689, 608, 743, 512, 762, 416, 743, 335, 689, 281, 608,
                        262, 512, 281, 416, 335, 335, 416, 281, 512, 262, 608, 281, 689, 335, 743, 416,
                    },
                },
                FillSpec.Flat(255)));
        }

        private static CellDef LogoChevron()
        {
            return Small(SmallCell.LogoChevron, 255).Add(ArtShape.Poly(new[] { 512, 900, 912, 500, 912, 340, 512, 740, 112, 340, 112, 500 }, FillSpec.Flat(255)));
        }

        private static CellDef LogoStar()
        {
            return Small(SmallCell.LogoStar, 255).Add(ArtShape.Poly(
                new[] { 512, 972, 400, 666, 75, 654, 331, 453, 242, 140, 512, 322, 782, 140, 693, 453, 950, 654, 624, 666 },
                FillSpec.Flat(255)));
        }

        private static CellDef LogoBars()
        {
            return Small(SmallCell.LogoBars, 255).Add(ArtShape.Rings(
                new[]
                {
                    new[] { 160, 160, 320, 160, 320, 480, 160, 480 },
                    new[] { 432, 160, 592, 160, 592, 700, 432, 700 },
                    new[] { 704, 160, 864, 160, 864, 900, 704, 900 },
                },
                FillSpec.Flat(255)));
        }

        private static CellDef LogoDiamond()
        {
            return Small(SmallCell.LogoDiamond, 255).Add(ArtShape.Poly(new[] { 512, 960, 912, 512, 512, 64, 112, 512 }, FillSpec.Flat(255)));
        }

        private static CellDef LogoCrescent()
        {
            return Small(SmallCell.LogoCrescent, 255).Add(ArtShape.Poly(
                new[] { 781, 847, 512, 942, 208, 816, 82, 512, 208, 208, 512, 82, 781, 177, 650, 152, 395, 257, 290, 512, 395, 767, 650, 872 },
                FillSpec.Flat(255)));
        }

        // ------------------------------------------------------------------ rubber

        private static CellDef Rubber()
        {
            CellDef cell = Small(SmallCell.Rubber, 255);
            cell.Add(ArtShape.Rect(64, 16, 960, 1008, FillSpec.RadialAlpha(255, 150, 0, 512, 512, 496).WithNoise(0, 110, 32, 3, 600, true)));
            return cell;
        }
    }
}
