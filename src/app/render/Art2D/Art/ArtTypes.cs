using System.Collections.Generic;

namespace AirportSim.App.Render.Art2D
{
    /// <summary>How a fill's parameter t runs across a shape (15 §15.17 "A fill").</summary>
    internal enum GradientKind
    {
        Flat,
        Linear,
        Radial,
    }

    /// <summary>A noise term: lattice spacing s, octaves O, salt and the smooth flag (15 §15.17 "Noise").</summary>
    internal readonly struct NoiseTerm
    {
        public NoiseTerm(int spacing, int octaves, int salt, bool smooth)
        {
            Spacing = spacing;
            Octaves = octaves;
            Salt = salt;
            Smooth = smooth;
        }

        public int Spacing { get; }

        public int Octaves { get; }

        public int Salt { get; }

        public bool Smooth { get; }
    }

    /// <summary>A fill: value pair, alpha pair, gradient and an optional noise term (15 §15.17 "A fill").</summary>
    internal readonly struct FillSpec
    {
        public FillSpec(
            int v0,
            int v1,
            int a0,
            int a1,
            GradientKind gradient,
            int gx0,
            int gy0,
            int gx1,
            int gy1,
            int radius,
            bool hasNoise,
            NoiseTerm noise,
            int nv,
            int na)
        {
            V0 = v0;
            V1 = v1;
            A0 = a0;
            A1 = a1;
            Gradient = gradient;
            Gx0 = gx0;
            Gy0 = gy0;
            Gx1 = gx1;
            Gy1 = gy1;
            Radius = radius;
            HasNoise = hasNoise;
            Noise = noise;
            NV = nv;
            NA = na;
        }

        public int V0 { get; }

        public int V1 { get; }

        public int A0 { get; }

        public int A1 { get; }

        public GradientKind Gradient { get; }

        /// <summary>Linear: P0; radial: the centre C.</summary>
        public int Gx0 { get; }

        public int Gy0 { get; }

        /// <summary>Linear: P1.</summary>
        public int Gx1 { get; }

        public int Gy1 { get; }

        /// <summary>Radial: R.</summary>
        public int Radius { get; }

        public bool HasNoise { get; }

        public NoiseTerm Noise { get; }

        public int NV { get; }

        public int NA { get; }

        /// <summary>Flat value and alpha 255.</summary>
        public static FillSpec Flat(int value)
        {
            return Flat(value, 255);
        }

        public static FillSpec Flat(int value, int alpha)
        {
            return new FillSpec(value, value, alpha, alpha, GradientKind.Flat, 0, 0, 0, 0, 0, false, default(NoiseTerm), 0, 0);
        }

        /// <summary>Linear value gradient from P0 to P1, alpha 255.</summary>
        public static FillSpec Linear(int v0, int v1, int x0, int y0, int x1, int y1)
        {
            return new FillSpec(v0, v1, 255, 255, GradientKind.Linear, x0, y0, x1, y1, 0, false, default(NoiseTerm), 0, 0);
        }

        /// <summary>Linear alpha gradient from P0 to P1 at one value.</summary>
        public static FillSpec LinearAlpha(int value, int a0, int a1, int x0, int y0, int x1, int y1)
        {
            return new FillSpec(value, value, a0, a1, GradientKind.Linear, x0, y0, x1, y1, 0, false, default(NoiseTerm), 0, 0);
        }

        /// <summary>Radial value gradient about C, alpha 255.</summary>
        public static FillSpec Radial(int v0, int v1, int cx, int cy, int radius)
        {
            return new FillSpec(v0, v1, 255, 255, GradientKind.Radial, cx, cy, 0, 0, radius, false, default(NoiseTerm), 0, 0);
        }

        /// <summary>Radial alpha gradient about C at one value.</summary>
        public static FillSpec RadialAlpha(int value, int a0, int a1, int cx, int cy, int radius)
        {
            return new FillSpec(value, value, a0, a1, GradientKind.Radial, cx, cy, 0, 0, radius, false, default(NoiseTerm), 0, 0);
        }

        /// <summary>The same fill with a noise term and its two amplitudes.</summary>
        public FillSpec WithNoise(int nv, int na, int spacing, int octaves, int salt, bool smooth)
        {
            return new FillSpec(V0, V1, A0, A1, Gradient, Gx0, Gy0, Gx1, Gy1, Radius, true, new NoiseTerm(spacing, octaves, salt, smooth), nv, na);
        }
    }

    /// <summary>A circle or a polygon of one or more rings, with a softness and a fill (15 §15.17 "A shape").</summary>
    internal sealed class ArtShape
    {
        private ArtShape(bool circle, int cx, int cy, int r, int[] xs, int[] ys, int[] ringStart, int softness, FillSpec fill)
        {
            IsCircle = circle;
            Cx = cx;
            Cy = cy;
            R = r;
            Xs = xs;
            Ys = ys;
            RingStart = ringStart;
            Softness = softness;
            Fill = fill;
            if (circle)
            {
                MinX = cx - r;
                MaxX = cx + r;
                MinY = cy - r;
                MaxY = cy + r;
            }
            else
            {
                MinX = int.MaxValue;
                MinY = int.MaxValue;
                MaxX = int.MinValue;
                MaxY = int.MinValue;
                for (int i = 0; i < xs.Length; i++)
                {
                    MinX = xs[i] < MinX ? xs[i] : MinX;
                    MaxX = xs[i] > MaxX ? xs[i] : MaxX;
                    MinY = ys[i] < MinY ? ys[i] : MinY;
                    MaxY = ys[i] > MaxY ? ys[i] : MaxY;
                }
            }
        }

        public bool IsCircle { get; }

        public int Cx { get; }

        public int Cy { get; }

        public int R { get; }

        /// <summary>Every ring's vertices, ring after ring.</summary>
        public int[] Xs { get; }

        public int[] Ys { get; }

        /// <summary>Ring k is vertices RingStart[k] .. RingStart[k + 1] - 1.</summary>
        public int[] RingStart { get; }

        public int Softness { get; }

        public FillSpec Fill { get; }

        public int MinX { get; }

        public int MinY { get; }

        public int MaxX { get; }

        public int MaxY { get; }

        public static ArtShape Circle(int cx, int cy, int r, FillSpec fill, int softness = 0)
        {
            return new ArtShape(true, cx, cy, r, new int[0], new int[0], new int[0], softness, fill);
        }

        /// <summary>One ring from x, y pairs.</summary>
        public static ArtShape Poly(int[] xy, FillSpec fill, int softness = 0)
        {
            return Rings(new[] { xy }, fill, softness);
        }

        /// <summary>Several rings, each from x, y pairs.</summary>
        public static ArtShape Rings(int[][] rings, FillSpec fill, int softness = 0)
        {
            int total = 0;
            for (int k = 0; k < rings.Length; k++)
            {
                total += rings[k].Length / 2;
            }

            var xs = new int[total];
            var ys = new int[total];
            var starts = new int[rings.Length + 1];
            int at = 0;
            for (int k = 0; k < rings.Length; k++)
            {
                starts[k] = at;
                for (int i = 0; i < rings[k].Length; i += 2)
                {
                    xs[at] = rings[k][i];
                    ys[at] = rings[k][i + 1];
                    at++;
                }
            }

            starts[rings.Length] = at;
            return new ArtShape(false, 0, 0, 0, xs, ys, starts, softness, fill);
        }

        public static ArtShape Rect(int x0, int y0, int x1, int y1, FillSpec fill, int softness = 0)
        {
            return Poly(new[] { x0, y0, x1, y0, x1, y1, x0, y1 }, fill, softness);
        }
    }

    /// <summary>One atlas cell: grow, edge value, mirror, opacity and an ordered list of shapes (15 §15.17 "A cell definition").</summary>
    internal sealed class CellDef
    {
        public CellDef(CellPlace place, bool tiled, int grow, int edge, bool mirror, int opacity)
        {
            Place = place;
            Tiled = tiled;
            Grow = grow;
            Edge = edge;
            Mirror = mirror;
            Opacity = opacity;
        }

        public CellPlace Place { get; }

        /// <summary>Repeated edge to edge: every sample point is wrapped into the visible square.</summary>
        public bool Tiled { get; }

        public int Grow { get; }

        /// <summary>The value of a pixel with no alpha, so filtering never pulls in a foreign colour.</summary>
        public int Edge { get; }

        public bool Mirror { get; }

        public int Opacity { get; }

        public List<ArtShape> Shapes { get; } = new List<ArtShape>();

        public CellDef Add(ArtShape shape)
        {
            Shapes.Add(shape);
            return this;
        }
    }
}
