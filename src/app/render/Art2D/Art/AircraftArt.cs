using System.Collections.Generic;

namespace AirportSim.App.Render.Art2D
{
    /// <summary>
    /// 15 §15.17 "Aircraft proportions" and "Aircraft parts" (Q-131): one row of the proportion
    /// table gives the eight layer cells of a size category. Every cell is mirrored and has no noise.
    /// </summary>
    internal sealed class AircraftArt
    {
        private const int Axis = 512;

        // The proportion table's row (15 §15.17), in hundredths unless noted.
        private readonly int _size;
        private readonly int _fuseWidthPermille;
        private readonly int _sweep;
        private readonly int _rootChord;
        private readonly int _rootLeadingEdge;
        private readonly int _tailSpan;

        // Derived design-unit geometry; the status outline of 32 is part of the row's span and length.
        private readonly int _span;
        private readonly int _length;
        private readonly int _halfSpan;
        private readonly int _noseY;
        private readonly int _tailY;
        private readonly int _fuseHalf;
        private readonly int _wingLeY;
        private readonly int _wingChord;
        private readonly int _wingHalf;

        public AircraftArt(int size)
        {
            _size = size;
            int spanH;
            int lengthH;
            switch (size)
            {
                case 0:
                    spanH = 40; lengthH = 38; _fuseWidthPermille = 95; _sweep = 3; _rootChord = 10; _rootLeadingEdge = 40; _tailSpan = 30;
                    break;
                case 1:
                    spanH = 46; lengthH = 56; _fuseWidthPermille = 95; _sweep = 45; _rootChord = 14; _rootLeadingEdge = 42; _tailSpan = 32;
                    break;
                case 2:
                    spanH = 60; lengthH = 64; _fuseWidthPermille = 100; _sweep = 50; _rootChord = 17; _rootLeadingEdge = 36; _tailSpan = 35;
                    break;
                case 3:
                    spanH = 70; lengthH = 80; _fuseWidthPermille = 95; _sweep = 55; _rootChord = 17; _rootLeadingEdge = 36; _tailSpan = 34;
                    break;
                case 4:
                    spanH = 84; lengthH = 92; _fuseWidthPermille = 85; _sweep = 62; _rootChord = 17; _rootLeadingEdge = 37; _tailSpan = 34;
                    break;
                default:
                    spanH = 96; lengthH = 88; _fuseWidthPermille = 95; _sweep = 65; _rootChord = 22; _rootLeadingEdge = 33; _tailSpan = 38;
                    break;
            }

            _span = ((spanH * 1024) + 50) / 100;
            _length = ((lengthH * 1024) + 50) / 100;
            _halfSpan = (_span / 2) - 32;
            int halfLength = (_length / 2) - 32;
            _noseY = Axis + halfLength;
            _tailY = Axis - halfLength;
            _fuseHalf = ((_fuseWidthPermille * _length) + 1000) / 2000;
            _wingLeY = _noseY - ((_rootLeadingEdge * _length) / 100);
            _wingChord = (_rootChord * _length) / 100;
            _wingHalf = _halfSpan;
        }

        private bool HighWing => _size == 0;

        private bool Quad => _size == 5;

        private bool RearEngines => _size == 1;

        /// <summary>The eight cells of this row, layer 0 to 7 (Shadow, Status, Wings, Engines, Fuselage, Cheatline, Tail, Glazing).</summary>
        public CellDef Layer(int layer)
        {
            CellPlace place = Packing.Aircraft(_size, layer);
            switch (layer)
            {
                case 0: return ShadowCell(place);
                case 1: return StatusCell(place);
                case 2: return WingsCell(place);
                case 3: return EnginesCell(place);
                case 4: return FuselageCell(place);
                case 5: return CheatlineCell(place);
                case 6: return TailCell(place);
                default: return GlazingCell(place);
            }
        }

        // ------------------------------------------------------------------ cells

        private CellDef ShadowCell(CellPlace place)
        {
            // Opaque soft shapes in a cell of opacity 112, so the shadow is one even alpha. The fuselage is wider
            // than the body and symmetric, drawn whole, so that the centre has full cover.
            var cell = new CellDef(place, false, 0, 0, true, 112);
            FillSpec f = FillSpec.Flat(255);
            cell.Add(ArtShape.Poly(Whole(FuselageProfile(12)), f, 48));
            cell.Add(ArtShape.Poly(WingHalf(_wingLeY, _wingChord, _wingHalf), f, 48));
            cell.Add(ArtShape.Poly(TailplaneHalf(), f, 48));
            foreach (int[] n in NacelleCentres())
            {
                cell.Add(ArtShape.Poly(NacelleRing(n[0], n[1], n[2], n[3]), f, 48));
            }

            return cell;
        }

        private CellDef StatusCell(CellPlace place)
        {
            // The silhouette, grown by 32 by the cell's grow.
            var cell = new CellDef(place, false, 32, 255, true, 255);
            FillSpec f = FillSpec.Flat(255);
            cell.Add(ArtShape.Poly(Half(FuselageProfile(0)), f));
            cell.Add(ArtShape.Poly(WingHalf(_wingLeY, _wingChord, _wingHalf), f));
            cell.Add(ArtShape.Poly(TailplaneHalf(), f));
            foreach (int[] n in NacelleCentres())
            {
                cell.Add(ArtShape.Poly(NacelleRing(n[0], n[1], n[2], n[3]), f));
            }

            return cell;
        }

        private CellDef WingsCell(CellPlace place)
        {
            var cell = new CellDef(place, false, 0, 235, true, 255);
            cell.Add(ArtShape.Poly(WingHalf(_wingLeY, _wingChord, _wingHalf), FillSpec.Linear(245, 225, Axis - 8, 0, Axis - _wingHalf, 0)));
            FillSpec line = FillSpec.Flat(175);

            // Flap and aileron lines along the trailing edge, 6 wide, and a slat line along the jets' leading edge.
            AddWingLine(cell, line, 15, 50, 28);
            AddWingLine(cell, line, 55, 90, 30);
            if (!HighWing)
            {
                AddWingLine(cell, line, 10, 95, 92);
            }

            return cell;
        }

        private CellDef EnginesCell(CellPlace place)
        {
            var cell = new CellDef(place, false, 0, 235, true, 255);
            foreach (int[] n in NacelleCentres())
            {
                int xc = n[0];
                int yf = n[1];
                int yr = n[2];
                int hn = n[3];
                int ch = hn / 3 < 2 ? 2 : hn / 3;
                cell.Add(ArtShape.Poly(
                    new[] { xc - hn, yr, xc - hn, yf - ch, xc - hn + ch, yf, xc + 1, yf, xc + 1, yr },
                    FillSpec.Linear(205, 250, xc - hn, 0, xc, 0)));
                cell.Add(ArtShape.Poly(
                    new[] { xc - 1, yr, xc - 1, yf, xc + hn - ch, yf, xc + hn, yf - ch, xc + hn, yr },
                    FillSpec.Linear(205, 250, xc + hn, 0, xc, 0)));

                // The intake rim, darker, across the front.
                cell.Add(ArtShape.Rect(xc - hn + 2, yf - 6, xc + hn - 2, yf - 2, FillSpec.Flat(150)));
            }

            return cell;
        }

        private CellDef FuselageCell(CellPlace place)
        {
            var cell = new CellDef(place, false, 0, 245, true, 255);
            FillSpec grad = FillSpec.Linear(250, 205, Axis - 8, 0, Axis - _fuseHalf, 0);
            int[] ys;
            int[] hws;
            FuselageProfile(0, out ys, out hws);
            if (HighWing)
            {
                // The wing's chord band is left empty so the wing shows through.
                int top = _wingLeY;
                int bottom = _wingLeY - _wingChord;
                Slice(ys, hws, _noseY, top, out int[] fy, out int[] fh);
                cell.Add(ArtShape.Poly(Half(fy, fh), grad));
                Slice(ys, hws, bottom, _tailY, out int[] ry, out int[] rh);
                cell.Add(ArtShape.Poly(Half(ry, rh), grad));
            }
            else
            {
                cell.Add(ArtShape.Poly(Half(ys, hws), grad));
            }

            return cell;
        }

        private CellDef CheatlineCell(CellPlace place)
        {
            var cell = new CellDef(place, false, 0, 245, true, 255);
            int bandWidth = ((12 * 2 * _fuseHalf) + 99) / 100;
            bandWidth = bandWidth < 5 ? 5 : bandWidth;
            int centre = SnapToPixel(Axis - ((55 * _fuseHalf) / 100));
            int x0 = centre - (bandWidth / 2);
            int x1 = x0 + bandWidth;
            int top = _noseY - ((12 * _length) / 100);
            int bottom = _noseY - ((85 * _length) / 100);
            FillSpec f = FillSpec.Flat(235);
            if (HighWing)
            {
                int wTop = _wingLeY;
                int wBottom = _wingLeY - _wingChord;
                cell.Add(ArtShape.Rect(x0, wTop, x1, top, f));
                cell.Add(ArtShape.Rect(x0, bottom, x1, wBottom, f));
            }
            else
            {
                cell.Add(ArtShape.Rect(x0, bottom, x1, top, f));
            }

            return cell;
        }

        private CellDef TailCell(CellPlace place)
        {
            var cell = new CellDef(place, false, 0, 235, true, 255);
            int tailHalf = (_tailSpan * _wingHalf) / 100;
            cell.Add(ArtShape.Poly(TailplaneHalf(), FillSpec.Linear(240, 220, Axis - 8, 0, Axis - tailHalf, 0)));

            // The fin top: a strip a quarter of the fuselage width along the centre line over the last 0.20 of L, symmetric, drawn whole.
            int half = _fuseHalf / 4 < 2 ? 2 : _fuseHalf / 4;
            cell.Add(ArtShape.Rect(Axis - half, _tailY, Axis + half, _tailY + ((20 * _length) / 100), FillSpec.Flat(225)));
            return cell;
        }

        private CellDef GlazingCell(CellPlace place)
        {
            var cell = new CellDef(place, false, 0, 235, true, 255);

            // The windscreen across the nose, symmetric, drawn whole.
            int front = _noseY - ((4 * _length) / 100);
            int back = front - ((3 * _length) / 100);
            int wideFront = (65 * _fuseHalf) / 100;
            int wideBack = (80 * _fuseHalf) / 100;
            cell.Add(ArtShape.Poly(
                new[] { Axis - wideFront, front, Axis + wideFront, front, Axis + wideBack, back, Axis - wideBack, back },
                FillSpec.Flat(255)));

            // A row of cabin windows along each side, about 0.014 of L apart, from 0.14 to 0.80 of L.
            int pitch = ((14 * _length) + 500) / 1000;
            int windowLength = (pitch * 2) / 3;
            int windowWidth = _fuseHalf / 5 < 3 ? 3 : _fuseHalf / 5;
            int wx0 = Axis - _fuseHalf + 2;
            int wx1 = wx0 + windowWidth;
            int last = _noseY - ((80 * _length) / 100);
            FillSpec glass = FillSpec.Flat(255, 160);
            for (int y = _noseY - ((14 * _length) / 100); y - windowLength >= last; y -= pitch)
            {
                if (HighWing && y > _wingLeY - _wingChord - 2 && y - windowLength < _wingLeY + 2)
                {
                    continue;
                }

                cell.Add(ArtShape.Rect(wx0, y - windowLength, wx1, y, glass));
            }

            // The propeller discs of the turboprop, at alpha 60.
            if (HighWing)
            {
                foreach (int[] n in NacelleCentres())
                {
                    cell.Add(ArtShape.Circle(n[0], n[1], (7 * _span) / 100, FillSpec.Flat(255, 60)));
                }
            }

            return cell;
        }

        // ------------------------------------------------------------------ parts

        /// <summary>Nacelles of the left side, each as { centre x, front y, rear y, half width }.</summary>
        private List<int[]> NacelleCentres()
        {
            var list = new List<int[]>();
            int nacelleWidth = ((_size == 4 ? 7 : 6) * _length) / 100;
            int half = nacelleWidth / 2;
            if (HighWing)
            {
                // Nacelles along the wing chord, at 0.33 of the half-span.
                int dx = (33 * _wingHalf) / 100;
                int le = LeadingEdge(dx);
                half = (5 * _length) / 200;
                list.Add(new[] { Axis - dx, le + ((6 * _wingChord) / 10), le - _wingChord - ((4 * _wingChord) / 10), half });
            }
            else if (RearEngines)
            {
                int front = _noseY - ((68 * _length) / 100);
                int rear = _noseY - ((82 * _length) / 100);
                list.Add(new[] { Axis - _fuseHalf - half - 1, front, rear, half });
            }
            else
            {
                int length = (12 * _length) / 100;
                int[] fractions = Quad ? new[] { 38, 64 } : new[] { _size == 4 ? 31 : 33 };
                foreach (int pct in fractions)
                {
                    int dx = (pct * _wingHalf) / 100;
                    int front = LeadingEdge(dx) + ((6 * length) / 10);
                    list.Add(new[] { Axis - dx, front, front - length, half });
                }
            }

            return list;
        }

        private static int[] NacelleRing(int xc, int yf, int yr, int hn)
        {
            int ch = hn / 3 < 2 ? 2 : hn / 3;
            return new[] { xc - hn, yr, xc + hn, yr, xc + hn, yf - ch, xc + hn - ch, yf, xc - hn + ch, yf, xc - hn, yf - ch };
        }

        /// <summary>The fuselage's width profile, nose to tail, as (y, half width) pairs; wide is added to every interior point.</summary>
        private void FuselageProfile(int wide, out int[] ys, out int[] hws)
        {
            int ns = (8 * _length) / 100;
            int coneStart = _tailY + ((15 * _length) / 100);
            int tail = ((3 * _fuseHalf) + 5) / 10;
            tail = tail < 2 ? 2 : tail;
            ys = new[] { _noseY, _noseY - (ns / 4), _noseY - (ns / 2), _noseY - ((3 * ns) / 4), _noseY - ns, coneStart, _tailY };
            hws = new[]
            {
                0,
                ((661 * _fuseHalf) / 1000) + wide,
                ((866 * _fuseHalf) / 1000) + wide,
                ((968 * _fuseHalf) / 1000) + wide,
                _fuseHalf + wide,
                _fuseHalf + wide,
                tail + wide,
            };
        }

        private int[] FuselageProfile(int wide)
        {
            FuselageProfile(wide, out int[] ys, out int[] hws);
            return Pack(ys, hws);
        }

        private static int[] Pack(int[] ys, int[] hws)
        {
            var packed = new int[ys.Length * 2];
            for (int i = 0; i < ys.Length; i++)
            {
                packed[2 * i] = ys[i];
                packed[(2 * i) + 1] = hws[i];
            }

            return packed;
        }

        /// <summary>The profile cut to the part between yTop and yBottom, with the ends interpolated.</summary>
        private static void Slice(int[] ys, int[] hws, int yTop, int yBottom, out int[] sy, out int[] sh)
        {
            var oy = new List<int>();
            var oh = new List<int>();
            oy.Add(yTop);
            oh.Add(HalfWidthAt(ys, hws, yTop));
            for (int i = 0; i < ys.Length; i++)
            {
                if (ys[i] < yTop && ys[i] > yBottom)
                {
                    oy.Add(ys[i]);
                    oh.Add(hws[i]);
                }
            }

            oy.Add(yBottom);
            oh.Add(HalfWidthAt(ys, hws, yBottom));
            sy = oy.ToArray();
            sh = oh.ToArray();
        }

        private static int HalfWidthAt(int[] ys, int[] hws, int y)
        {
            for (int i = 0; i + 1 < ys.Length; i++)
            {
                if (y <= ys[i] && y >= ys[i + 1])
                {
                    int span = ys[i] - ys[i + 1];
                    return span == 0 ? hws[i] : hws[i] + RoundDiv((hws[i + 1] - hws[i]) * (ys[i] - y), span);
                }
            }

            return hws[ys.Length - 1];
        }

        /// <summary>The mirror-seam rule (15 §15.17): the half extended across the axis by min(8, w), one polygon.</summary>
        private static int[] Half(int[] packed)
        {
            int n = packed.Length / 2;
            var ys = new int[n];
            var hws = new int[n];
            for (int i = 0; i < n; i++)
            {
                ys[i] = packed[2 * i];
                hws[i] = packed[(2 * i) + 1];
            }

            return Half(ys, hws);
        }

        private static int[] Half(int[] ys, int[] hws)
        {
            var ring = new List<int>();
            for (int i = 0; i < ys.Length; i++)
            {
                Append(ring, Axis - hws[i], ys[i]);
            }

            // The extension, bottom to top: min(8, w) at every height, with a vertex where w crosses 8.
            var ext = new List<int>();
            for (int i = 0; i < ys.Length; i++)
            {
                ext.Add(ys[i]);
                ext.Add(hws[i] < 8 ? hws[i] : 8);
                if (i + 1 < ys.Length && (hws[i] - 8) * (hws[i + 1] - 8) < 0)
                {
                    int num = (8 - hws[i]) * (ys[i + 1] - ys[i]);
                    int den = hws[i + 1] - hws[i];
                    int cross = ys[i] + (den > 0 ? FloorDiv(num, den) : CeilDiv(num, den));
                    ext.Add(cross);
                    ext.Add(8);
                }
            }

            for (int i = ext.Count - 2; i >= 0; i -= 2)
            {
                Append(ring, Axis + ext[i + 1], ext[i]);
            }

            return Close(ring);
        }

        /// <summary>A polygon symmetric about the axis, drawn whole.</summary>
        private static int[] Whole(int[] packed)
        {
            int n = packed.Length / 2;
            var ring = new List<int>();
            for (int i = 0; i < n; i++)
            {
                Append(ring, Axis - packed[(2 * i) + 1], packed[2 * i]);
            }

            for (int i = n - 1; i >= 0; i--)
            {
                Append(ring, Axis + packed[(2 * i) + 1], packed[2 * i]);
            }

            return Close(ring);
        }

        private static void Append(List<int> ring, int x, int y)
        {
            int n = ring.Count;
            if (n >= 2 && ring[n - 2] == x && ring[n - 1] == y)
            {
                return;
            }

            ring.Add(x);
            ring.Add(y);
        }

        private static int[] Close(List<int> ring)
        {
            int n = ring.Count;
            if (n >= 4 && ring[0] == ring[n - 2] && ring[1] == ring[n - 1])
            {
                ring.RemoveRange(n - 2, 2);
            }

            return ring.ToArray();
        }

        /// <summary>
        /// A half wing from the axis: root chord, tip chord 0.3 of it, the sweep on the leading edge, extended across the axis by 8.
        /// The trailing edge runs straight across for the inner plate, so the root is as deep at the axis as just outboard of it.
        /// </summary>
        private int[] WingHalf(int leadingEdge, int chord, int halfSpan)
        {
            int tipLe = leadingEdge - ((_sweep * halfSpan) / 100);
            int tipTe = tipLe - ((3 * chord) / 10);
            int rootTe = leadingEdge - chord;
            int le8 = leadingEdge - CeilDiv(_sweep * 8, 100);
            int plate = Plate(halfSpan);
            return new[]
            {
                Axis - halfSpan, tipLe,
                Axis - halfSpan, tipTe,
                Axis - plate, rootTe,
                Axis + 8, rootTe,
                Axis + 8, le8,
                Axis, leadingEdge,
            };
        }

        private static int Plate(int halfSpan)
        {
            return halfSpan / 3 < 40 ? halfSpan / 3 : 40;
        }

        private int[] TailplaneHalf()
        {
            // It ends 0.02 of L before the tail (a T-tail, at the very end): its tip is the aftmost point, since the sweep carries it back.
            int half = (_tailSpan * _wingHalf) / 100;
            int chord = (10 * _length) / 100;
            int aft = _tailY + (_size == 1 ? 1 : (2 * _length) / 100);
            int back = ((_sweep * half) / 100) + ((3 * chord) / 10);
            int rootLe = aft + (back > chord ? back : chord);
            return WingHalf(rootLe, chord, half);
        }

        private int LeadingEdge(int dx)
        {
            return _wingLeY - ((_sweep * dx) / 100);
        }

        private int TrailingEdge(int dx)
        {
            int tipLe = _wingLeY - ((_sweep * _wingHalf) / 100);
            int tipTe = tipLe - ((3 * _wingChord) / 10);
            int rootTe = _wingLeY - _wingChord;
            int plate = Plate(_wingHalf);
            if (dx <= plate)
            {
                return rootTe;
            }

            return rootTe + (((tipTe - rootTe) * (dx - plate)) / (_wingHalf - plate));
        }

        // A line along the wing at a fraction of the chord from the trailing edge (percent), between two spanwise positions (percent of the half-span).
        private void AddWingLine(CellDef cell, FillSpec fill, int fromPct, int toPct, int chordPct)
        {
            int dxa = (fromPct * _wingHalf) / 100;
            int dxb = (toPct * _wingHalf) / 100;
            int ya = TrailingEdge(dxa) + (((LeadingEdge(dxa) - TrailingEdge(dxa)) * chordPct) / 100);
            int yb = TrailingEdge(dxb) + (((LeadingEdge(dxb) - TrailingEdge(dxb)) * chordPct) / 100);
            if (LeadingEdge(dxb) - TrailingEdge(dxb) < 18)
            {
                return;
            }

            cell.Add(ArtShape.Poly(new[] { Axis - dxa, ya + 3, Axis - dxb, yb + 3, Axis - dxb, yb - 3, Axis - dxa, ya - 3 }, fill));
        }

        // The band is thinner than a pixel at the small mips, so it is centred on a pixel centre of the mip-2 grid
        // (design x = (2i - 7) * 64 / 15 for i from 4), which keeps it from falling between two pixels there.
        private static int SnapToPixel(int target)
        {
            int best = target;
            int bestDistance = int.MaxValue;
            for (int i = 4; i < 124; i++)
            {
                int x = ((((2 * i) - 7) * 64) + 7) / 15;
                int distance = x > target ? x - target : target - x;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = x;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ integer helpers

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            return ((a % b != 0) && ((a < 0) != (b < 0))) ? q - 1 : q;
        }

        private static int CeilDiv(int a, int b)
        {
            return -FloorDiv(-a, b);
        }

        private static int RoundDiv(int a, int b)
        {
            return FloorDiv((2 * a) + b, 2 * b);
        }
    }
}
