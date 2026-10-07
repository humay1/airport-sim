using System;

namespace AirportSim.App.Render.Art2D
{
    /// <summary>
    /// 15 §15.17 "Rasterisation" (Q-131): integer arithmetic only. No float, no double, no Math.
    /// Every quantity is a long except the hash, which is a uint with wrap-around multiplication.
    /// </summary>
    internal sealed class Rasteriser
    {
        private const int MaxSide = 512;
        private const long Q8Units = 262144;

        private readonly long[] _alpha = new long[MaxSide * MaxSide];
        private readonly long[] _colour = new long[MaxSide * MaxSide];
        private readonly long[] _sample = new long[MaxSide];
        private readonly int[] _cols = new int[MaxSide];
        private readonly int[] _rows = new int[MaxSide];

        /// <summary>Rasterises one cell at every mip into the atlas.</summary>
        public void RenderCell(CellDef def, byte[][] mips)
        {
            for (int m = 0; m < mips.Length; m++)
            {
                RenderMip(def, m, mips[m]);
            }
        }

        // fdiv(a, b) for b > 0: rounds toward minus infinity.
        private static long Fdiv(long a, long b)
        {
            long q = a / b;
            if (a < 0 && (q * b) != a)
            {
                q--;
            }

            return q;
        }

        // isqrt(x) for x >= 0: the largest r with r * r <= x, by Newton's method in integers.
        private static long Isqrt(long x)
        {
            if (x < 2)
            {
                return x;
            }

            int bits = 0;
            for (long t = x; t > 0; t >>= 1)
            {
                bits++;
            }

            long r = 1L << ((bits + 1) >> 1);
            while (true)
            {
                long next = (r + (x / r)) >> 1;
                if (next >= r)
                {
                    return r;
                }

                r = next;
            }
        }

        private static long Clamp(long x, long lo, long hi)
        {
            return x < lo ? lo : (x > hi ? hi : x);
        }

        private void RenderMip(CellDef def, int m, byte[] dest)
        {
            int c = def.Place.Side;
            int n = c >> m;
            int destSide = Art2DConstants.ATLAS_SIZE >> m;
            int dx0 = def.Place.Px >> m;
            int dy0 = def.Place.Py >> m;
            long cc = c;
            long unit = Fdiv((2L * 4194304L * (1L << m)) + (15L * cc), 30L * cc);

            for (int i = 0; i < n; i++)
            {
                long num = ((32L << m) * i) + (16L << m) - cc;
                long s = Fdiv((2L * num * 131072L) + (15L * cc), 30L * cc);
                _sample[i] = def.Tiled ? s & (Q8Units - 1) : s;
            }

            int count = n * n;
            Array.Clear(_alpha, 0, count);
            Array.Clear(_colour, 0, count);

            for (int si = 0; si < def.Shapes.Count; si++)
            {
                ArtShape shape = def.Shapes[si];
                for (int inst = 0; inst < (def.Mirror ? 2 : 1); inst++)
                {
                    DrawInstance(def, shape, inst == 1, n, unit);
                }
            }

            long op = def.Opacity;
            for (int j = 0; j < n; j++)
            {
                int row = ((((dy0 + j) * destSide) + dx0) * 4);
                for (int i = 0; i < n; i++)
                {
                    long a = _alpha[(j * n) + i];
                    long a8 = (a * op + 32768) >> 16;
                    byte v;
                    if (a8 > 0)
                    {
                        long r = ((2 * _colour[(j * n) + i]) + a) / (2 * a);
                        v = (byte)(r > 255 ? 255 : r);
                    }
                    else
                    {
                        v = (byte)def.Edge;
                    }

                    int o = row + (4 * i);
                    dest[o] = v;
                    dest[o + 1] = v;
                    dest[o + 2] = v;
                    dest[o + 3] = (byte)a8;
                }
            }
        }

        private void DrawInstance(CellDef def, ArtShape shape, bool mirrored, int n, long unit)
        {
            long w = unit + (256L * shape.Softness);
            long grow256 = 256L * def.Grow;
            long reach = grow256 + ((w + 1) >> 1);
            long loX = (256L * shape.MinX) - reach;
            long hiX = (256L * shape.MaxX) + reach;
            long loY = (256L * shape.MinY) - reach;
            long hiY = (256L * shape.MaxY) + reach;

            int cols = 0;
            int rows = 0;
            for (int i = 0; i < n; i++)
            {
                long x = mirrored ? Q8Units - _sample[i] : _sample[i];
                if (x >= loX && x <= hiX)
                {
                    _cols[cols++] = i;
                }

                long y = _sample[i];
                if (y >= loY && y <= hiY)
                {
                    _rows[rows++] = i;
                }
            }

            if (cols == 0 || rows == 0)
            {
                return;
            }

            // Thresholds that decide cov = 0 or cov = 256 without a square root, exactly.
            long outside = reach;
            long insideLimit = Fdiv((127L * w) - (65536L * def.Grow), 256);
            FillSpec fill = shape.Fill;
            bool needT = fill.V0 != fill.V1 || fill.A0 != fill.A1;
            bool needNoise = fill.HasNoise && (fill.NV != 0 || fill.NA != 0);
            bool circle = shape.IsCircle;
            long radius256 = 256L * (shape.R + def.Grow);

            // A circle has cov = 0 once dist reaches radius256 + ceil(w / 2), and cov = 256 while 256 * dist < 256 * radius256 - 127 w.
            long circleOut = radius256 + ((w + 1) >> 1);
            long circleM = (256L * radius256) - (127L * w);
            long circleFull = circleM <= 0 ? -1 : Fdiv(circleM - 1, 256);

            for (int rj = 0; rj < rows; rj++)
            {
                int j = _rows[rj];
                long py = _sample[j];
                for (int ci = 0; ci < cols; ci++)
                {
                    int i = _cols[ci];
                    long px = mirrored ? Q8Units - _sample[i] : _sample[i];
                    long cov;
                    if (circle)
                    {
                        long dx = px - (256L * shape.Cx);
                        long dy = py - (256L * shape.Cy);
                        long d2 = (dx * dx) + (dy * dy);
                        if (d2 >= circleOut * circleOut)
                        {
                            continue;
                        }

                        if (circleFull >= 0 && d2 < (circleFull + 1) * (circleFull + 1))
                        {
                            cov = 256;
                        }
                        else
                        {
                            long d = Isqrt(d2) - radius256;
                            cov = Clamp(128 - Fdiv(256 * d, w), 0, 256);
                        }
                    }
                    else
                    {
                        long e2 = PolygonMinE2(shape, px, py, out bool inside);
                        if (!inside)
                        {
                            if (e2 >= (outside * outside))
                            {
                                continue;
                            }
                        }

                        if (inside && (insideLimit < 0 || e2 >= (insideLimit + 1) * (insideLimit + 1)))
                        {
                            cov = 256;
                        }
                        else
                        {
                            long dist = Isqrt(e2);
                            long d = (inside ? -dist : dist) - grow256;
                            cov = Clamp(128 - Fdiv(256 * d, w), 0, 256);
                        }
                    }

                    if (cov == 0)
                    {
                        continue;
                    }

                    long t = 0;
                    if (needT)
                    {
                        t = GradientT(fill, px, py);
                    }

                    long noise = 0;
                    if (needNoise)
                    {
                        noise = NoiseAt(fill.Noise, px, py, unit);
                    }

                    long alpha = Clamp(fill.A0 + (((fill.A1 - fill.A0) * t) >> 8) + ((fill.NA * noise) >> 15), 0, 255);
                    if (alpha == 0)
                    {
                        continue;
                    }

                    long value = Clamp(fill.V0 + (((fill.V1 - fill.V0) * t) >> 8) + ((fill.NV * noise) >> 15), 0, 255);
                    long a = (alpha * cov * 256) / 255;
                    int at = (j * n) + i;
                    long oldA = _alpha[at];
                    long oldC = _colour[at];
                    _alpha[at] = a + ((oldA * (65536 - a)) >> 16);
                    _colour[at] = (value * a) + ((oldC * (65536 - a)) >> 16);
                }
            }
        }

        // The least squared distance to any edge of any ring, and the even-odd inside test.
        private static long PolygonMinE2(ArtShape s, long px, long py, out bool inside)
        {
            int[] xs = s.Xs;
            int[] ys = s.Ys;
            int[] starts = s.RingStart;
            long best = long.MaxValue;
            int crossings = 0;
            for (int ring = 0; ring + 1 < starts.Length; ring++)
            {
                int first = starts[ring];
                int last = starts[ring + 1] - 1;
                for (int k = first; k <= last; k++)
                {
                    int kb = k == last ? first : k + 1;
                    long ax = xs[k];
                    long ay = ys[k];
                    long bx = xs[kb];
                    long by = ys[kb];
                    long ex = bx - ax;
                    long ey = by - ay;
                    long l2 = (ex * ex) + (ey * ey);
                    long qx = px - (256 * ax);
                    long qy = py - (256 * ay);
                    long dot = (qx * ex) + (qy * ey);
                    long cr = (qx * ey) - (qy * ex);
                    long e2;
                    if (dot <= 0)
                    {
                        e2 = (qx * qx) + (qy * qy);
                    }
                    else if (dot >= 256 * l2)
                    {
                        long rx = px - (256 * bx);
                        long ry = py - (256 * by);
                        e2 = (rx * rx) + (ry * ry);
                    }
                    else
                    {
                        e2 = Fdiv(cr * cr, l2);
                    }

                    if (e2 < best)
                    {
                        best = e2;
                    }

                    if (((256 * ay) > py) != ((256 * by) > py) && ((ey > 0 && cr < 0) || (ey < 0 && cr > 0)))
                    {
                        crossings++;
                    }
                }
            }

            inside = (crossings & 1) == 1;
            return best;
        }

        private static long GradientT(FillSpec f, long px, long py)
        {
            if (f.Gradient == GradientKind.Linear)
            {
                long gx = f.Gx1 - f.Gx0;
                long gy = f.Gy1 - f.Gy0;
                long num = ((px - (256L * f.Gx0)) * gx) + ((py - (256L * f.Gy0)) * gy);
                return Clamp(Fdiv(num, (gx * gx) + (gy * gy)), 0, 256);
            }

            if (f.Gradient == GradientKind.Radial)
            {
                long dx = px - (256L * f.Gx0);
                long dy = py - (256L * f.Gy0);
                return Clamp(Fdiv(Isqrt((dx * dx) + (dy * dy)), f.Radius), 0, 256);
            }

            return 0;
        }

        // 15 §15.17 "Noise": n in -32768 .. 32767, the octaves finer than two pixels left out.
        private static long NoiseAt(NoiseTerm term, long px, long py, long unit)
        {
            long sum = 0;
            int octaves = term.Octaves;
            for (int o = 0; o < octaves; o++)
            {
                int spacing = term.Spacing >> o;
                long s = 256L * spacing;
                if (s < 2 * unit)
                {
                    continue;
                }

                int shift = 8;
                for (int sp = spacing; sp > 1; sp >>= 1)
                {
                    shift++;
                }

                int period = Art2DConstants.ART_UNITS / spacing;
                long a = px >> shift;
                long b = py >> shift;
                int salt = term.Salt + o;
                int ia = (int)(a & (period - 1));
                int ib = (int)(b & (period - 1));
                long oct;
                if (!term.Smooth)
                {
                    oct = Lattice(salt, ia, ib);
                }
                else
                {
                    long fx = ((px - (a << shift)) << 16) >> shift;
                    long fy = ((py - (b << shift)) << 16) >> shift;
                    long wx = (((fx * fx) >> 16) * (196608 - (2 * fx))) >> 16;
                    long wy = (((fy * fy) >> 16) * (196608 - (2 * fy))) >> 16;
                    int ia1 = (ia + 1) & (period - 1);
                    int ib1 = (ib + 1) & (period - 1);
                    long g00 = Lattice(salt, ia, ib);
                    long g10 = Lattice(salt, ia1, ib);
                    long g01 = Lattice(salt, ia, ib1);
                    long g11 = Lattice(salt, ia1, ib1);
                    long g0 = g00 + (((g10 - g00) * wx) >> 16);
                    long g1 = g01 + (((g11 - g01) * wx) >> 16);
                    oct = g0 + (((g1 - g0) * wy) >> 16);
                }

                sum += oct << (octaves - 1 - o);
            }

            return Fdiv(sum, (1L << octaves) - 1);
        }

        // g(a, b) = (H & 0xFFFF) - 32768, H being FNV-1a-32 over three little-endian int32 values.
        private static int Lattice(int salt, int a, int b)
        {
            unchecked
            {
                uint h = 0x811C9DC5u;
                h = Mix(h, salt);
                h = Mix(h, a);
                h = Mix(h, b);
                return (int)(h & 0xFFFFu) - 32768;
            }
        }

        private static uint Mix(uint h, int v)
        {
            unchecked
            {
                h = (h ^ (uint)(v & 0xFF)) * 0x01000193u;
                h = (h ^ (uint)((v >> 8) & 0xFF)) * 0x01000193u;
                h = (h ^ (uint)((v >> 16) & 0xFF)) * 0x01000193u;
                h = (h ^ (uint)((v >> 24) & 0xFF)) * 0x01000193u;
                return h;
            }
        }
    }
}
