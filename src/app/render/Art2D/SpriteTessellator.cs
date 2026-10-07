using System;
using System.Collections.Generic;

namespace AirportSim.App.Render.Art2D
{
    /// <summary>15 §15.17 "Tessellation" (Q-131): the ground, then every primitive's layers, as tinted quads.</summary>
    internal sealed class SpriteTessellator : ISpriteTessellator
    {
        private readonly int _roleCount;
        private readonly LayerRec[][] _layers;
        private readonly AtlasRect[] _logos;
        private readonly byte[] _linear;
        private readonly ArtLayer _ground;

        private float[] _corners = new float[8 * 256];
        private float[] _uvs = new float[8 * 256];
        private byte[] _colours = new byte[16 * 256];
        private int _count;

        // The offset of the layer being emitted, hundredths already divided out.
        private double _shiftX;
        private double _shiftY;

        public SpriteTessellator()
        {
            _roleCount = Enum.GetValues(typeof(ColourRole)).Length;
            int visuals = 0;
            foreach (VisualId v in Enum.GetValues(typeof(VisualId)))
            {
                visuals = Math.Max(visuals, (int)v + 1);
            }

            _layers = new LayerRec[visuals][];
            for (int v = 0; v < visuals; v++)
            {
                List<ArtLayer> list = VisualLayers.Of((VisualId)v);
                var recs = new LayerRec[list.Count];
                for (int i = 0; i < recs.Length; i++)
                {
                    recs[i] = new LayerRec(list[i]);
                }

                _layers[v] = recs;
            }

            _logos = new AtlasRect[8];
            for (int m = 1; m <= 7; m++)
            {
                _logos[m] = Packing.Small(Packing.Logo(m)).Rect();
            }

            _ground = VisualLayers.Ground();

            // 15 §15.17 L(c): floor(255 * lin(c / 255) + 0.5), as literals.
            _linear = new byte[]
            {
                0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3,
                4, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7,
                8, 8, 8, 8, 9, 9, 9, 10, 10, 10, 11, 11, 12, 12, 12, 13,
                13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 17, 18, 18, 19, 19, 20,
                20, 21, 22, 22, 23, 23, 24, 24, 25, 25, 26, 27, 27, 28, 29, 29,
                30, 30, 31, 32, 32, 33, 34, 35, 35, 36, 37, 37, 38, 39, 40, 41,
                41, 42, 43, 44, 45, 45, 46, 47, 48, 49, 50, 51, 51, 52, 53, 54,
                55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70,
                71, 72, 73, 74, 76, 77, 78, 79, 80, 81, 82, 84, 85, 86, 87, 88,
                90, 91, 92, 93, 95, 96, 97, 99, 100, 101, 103, 104, 105, 107, 108, 109,
                111, 112, 114, 115, 116, 118, 119, 121, 122, 124, 125, 127, 128, 130, 131, 133,
                134, 136, 138, 139, 141, 142, 144, 146, 147, 149, 151, 152, 154, 156, 157, 159,
                161, 163, 164, 166, 168, 170, 171, 173, 175, 177, 179, 181, 183, 184, 186, 188,
                190, 192, 194, 196, 198, 200, 202, 204, 206, 208, 210, 212, 214, 216, 218, 220,
                222, 224, 226, 229, 231, 233, 235, 237, 239, 242, 244, 246, 248, 250, 253, 255,
            };
        }

        public int QuadCount => _count;

        public float[] Corners => _corners;

        public float[] Uvs => _uvs;

        public byte[] Colours => _colours;

        public int Fill(in RenderFrame frame, IReadOnlyList<Rgba> roleColours, bool linear)
        {
            if (roleColours == null)
            {
                throw new ArgumentNullException(nameof(roleColours));
            }

            if (roleColours.Count != _roleCount)
            {
                throw new ArgumentException("roleColours must have one entry per ColourRole (" + _roleCount + ")", nameof(roleColours));
            }

            _count = 0;
            EmitGround(frame.Camera, linear);
            IReadOnlyList<DrawPrimitive> prims = frame.Primitives;
            for (int i = 0; i < prims.Count; i++)
            {
                DrawPrimitive p = prims[i];
                int vi = (int)p.Visual;
                if (vi < 0 || vi >= _layers.Length)
                {
                    continue;
                }

                LayerRec[] recs = _layers[vi];
                bool dot = p.Kind == PrimitiveKind.Dot;
                double fx = 0.0;
                double fy = 1.0;
                if (dot && (p.Facing.X != 0f || p.Facing.Y != 0f))
                {
                    double n = Math.Sqrt(((double)p.Facing.X * p.Facing.X) + ((double)p.Facing.Y * p.Facing.Y));
                    fx = p.Facing.X / n;
                    fy = p.Facing.Y / n;
                }

                if (p.Kind == PrimitiveKind.Segment)
                {
                    double dx = (double)p.B.X - p.A.X;
                    double dy = (double)p.B.Y - p.A.Y;
                    double len = Math.Sqrt((dx * dx) + (dy * dy));
                    fx = len == 0.0 ? 0.0 : dx / len;
                    fy = len == 0.0 ? 1.0 : dy / len;
                }

                for (int li = 0; li < recs.Length; li++)
                {
                    LayerRec rec = recs[li];
                    AtlasRect rect = rec.Layer.Rect;
                    if (rec.Layer.IsLogo)
                    {
                        int mark = p.Paint.Mark;
                        if (mark < 1 || mark > 7)
                        {
                            continue;
                        }

                        rect = _logos[mark];
                    }

                    _shiftX = rec.Layer.ShiftX / 100.0;
                    _shiftY = rec.Layer.ShiftY / 100.0;
                    int first = _count;
                    switch (p.Kind)
                    {
                        case PrimitiveKind.Box:
                            EmitBox(in p, rec, in rect);
                            break;
                        case PrimitiveKind.Segment:
                            EmitSegment(in p, rec, in rect, fx, fy);
                            break;
                        default:
                            EmitDot(in p, rec, in rect, fx, fy);
                            break;
                    }

                    if (_count != first)
                    {
                        Tint(first, rec.Layer, in p, roleColours, linear);
                    }
                }
            }

            return _count;
        }

        private void EmitGround(in CameraView cam, bool linear)
        {
            double hw = cam.ViewHeight * (double)cam.Aspect / 2.0;
            double hh = cam.ViewHeight / 2.0;
            double minX = cam.Centre.X - hw;
            double maxX = cam.Centre.X + hw;
            double minY = cam.Centre.Y - hh;
            double maxY = cam.Centre.Y + hh;
            double t = 0;
            long nx = 0;
            long ny = 0;
            for (int k = 0; k <= 24; k++)
            {
                t = Art2DConstants.GROUND_TILE * (double)(1L << k);
                nx = (long)Math.Floor(maxX / t) - (long)Math.Floor(minX / t) + 1;
                ny = (long)Math.Floor(maxY / t) - (long)Math.Floor(minY / t) + 1;
                if (nx <= Art2DConstants.GROUND_TILES_PER_AXIS && ny <= Art2DConstants.GROUND_TILES_PER_AXIS)
                {
                    break;
                }
            }

            long ix0 = (long)Math.Floor(minX / t);
            long iy0 = (long)Math.Floor(minY / t);
            AtlasRect r = _ground.Rect;
            _shiftX = 0.0;
            _shiftY = 0.0;
            for (long iy = iy0; iy < iy0 + ny; iy++)
            {
                for (long ix = ix0; ix < ix0 + nx; ix++)
                {
                    int q = Next();
                    SetBox(q, ix * t, iy * t, (ix + 1) * t, (iy + 1) * t);
                    SetUvs(q, r.U0, r.V0, r.U1, r.V1);
                }
            }

            SetColour(0, _count, _ground.Fixed.R, _ground.Fixed.G, _ground.Fixed.B, 255, linear);
        }

        private void EmitBox(in DrawPrimitive p, LayerRec rec, in AtlasRect r)
        {
            double x0 = p.A.X;
            double y0 = p.A.Y;
            double x1 = p.B.X;
            double y1 = p.B.Y;
            ref readonly ArtLayer l = ref rec.Layer;
            if (l.Tile > 0)
            {
                double t = l.Tile;
                long ix0 = (long)Math.Floor(x0 / t);
                long iy0 = (long)Math.Floor(y0 / t);
                for (long iy = iy0; iy * t < y1; iy++)
                {
                    double qy0 = Math.Max(y0, iy * t);
                    double qy1 = Math.Min(y1, (iy + 1) * t);
                    if (qy1 <= qy0)
                    {
                        continue;
                    }

                    for (long ix = ix0; ix * t < x1; ix++)
                    {
                        double qx0 = Math.Max(x0, ix * t);
                        double qx1 = Math.Min(x1, (ix + 1) * t);
                        if (qx1 <= qx0)
                        {
                            continue;
                        }

                        int q = Next();
                        SetBox(q, qx0, qy0, qx1, qy1);
                        SetUvs(
                            q,
                            (float)(r.U0 + ((qx0 - (ix * t)) / t * ((double)r.U1 - r.U0))),
                            (float)(r.V0 + ((qy0 - (iy * t)) / t * ((double)r.V1 - r.V0))),
                            (float)(r.U0 + ((qx1 - (ix * t)) / t * ((double)r.U1 - r.U0))),
                            (float)(r.V0 + ((qy1 - (iy * t)) / t * ((double)r.V1 - r.V0))));
                    }
                }

                return;
            }

            if (l.SliceInset > 0)
            {
                double t = Math.Min(l.SliceWorld / 100.0, Math.Min((x1 - x0) / 2.0, (y1 - y0) / 2.0));
                double du = ((double)r.U1 - r.U0) * l.SliceInset / 1024.0;
                double dv = ((double)r.V1 - r.V0) * l.SliceInset / 1024.0;
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        double bx0 = col == 0 ? x0 : (col == 1 ? x0 + t : x1 - t);
                        double bx1 = col == 0 ? x0 + t : (col == 1 ? x1 - t : x1);
                        double by0 = row == 0 ? y0 : (row == 1 ? y0 + t : y1 - t);
                        double by1 = row == 0 ? y0 + t : (row == 1 ? y1 - t : y1);
                        float u0 = col == 0 ? r.U0 : (col == 1 ? (float)(r.U0 + du) : (float)(r.U1 - du));
                        float u1 = col == 0 ? (float)(r.U0 + du) : (col == 1 ? (float)(r.U1 - du) : r.U1);
                        float v0 = row == 0 ? r.V0 : (row == 1 ? (float)(r.V0 + dv) : (float)(r.V1 - dv));
                        float v1 = row == 0 ? (float)(r.V0 + dv) : (row == 1 ? (float)(r.V1 - dv) : r.V1);
                        int q = Next();
                        SetBox(q, bx0, by0, bx1, by1);
                        SetUvs(q, u0, v0, u1, v1);
                    }
                }

                return;
            }

            int one = Next();
            SetBox(one, x0, y0, x1, y1);
            SetUvs(one, r.U0, r.V0, r.U1, r.V1);
        }

        private void EmitSegment(in DrawPrimitive p, LayerRec rec, in AtlasRect r, double fx, double fy)
        {
            double ax = p.A.X;
            double ay = p.A.Y;
            double dx = (double)p.B.X - p.A.X;
            double dy = (double)p.B.Y - p.A.Y;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            double rx = fy;
            double ry = -fx;
            double size = p.Size;
            double h = size / 2.0;
            ref readonly ArtLayer l = ref rec.Layer;
            if (l.Tile > 0)
            {
                if (len == 0.0 || size == 0.0)
                {
                    return;
                }

                double t = l.Tile;
                for (long i = 0; i * t < len; i++)
                {
                    double y0 = i * t;
                    double y1 = Math.Min(y0 + t, len);
                    for (long j = 0; -h + (j * t) < h; j++)
                    {
                        double x0 = -h + (j * t);
                        double x1 = Math.Min(x0 + t, h);
                        int q = Next();
                        SetCorner(q, 0, ax + (rx * x0) + (fx * y0), ay + (ry * x0) + (fy * y0));
                        SetCorner(q, 1, ax + (rx * x1) + (fx * y0), ay + (ry * x1) + (fy * y0));
                        SetCorner(q, 2, ax + (rx * x1) + (fx * y1), ay + (ry * x1) + (fy * y1));
                        SetCorner(q, 3, ax + (rx * x0) + (fx * y1), ay + (ry * x0) + (fy * y1));
                        SetUvs(
                            q,
                            (float)(r.U0 + ((x0 + h - (j * t)) / t * ((double)r.U1 - r.U0))),
                            (float)(r.V0 + ((y0 - (i * t)) / t * ((double)r.V1 - r.V0))),
                            (float)(r.U0 + ((x1 + h - (j * t)) / t * ((double)r.U1 - r.U0))),
                            (float)(r.V0 + ((y1 - (i * t)) / t * ((double)r.V1 - r.V0))));
                    }
                }

                return;
            }

            int one = Next();
            for (int k = 0; k < 4; k++)
            {
                double a = (rec.SubX(k) - 0.5) * size;
                double b = rec.SubY(k) * len;
                SetCorner(one, k, ax + (rx * a) + (fx * b), ay + (ry * a) + (fy * b));
            }

            SetUvs(one, r.U0, r.V0, r.U1, r.V1);
        }

        private void EmitDot(in DrawPrimitive p, LayerRec rec, in AtlasRect r, double fx, double fy)
        {
            double cx = p.A.X;
            double cy = p.A.Y;
            double size = p.Size;
            double rx = fy;
            double ry = -fx;
            int one = Next();
            for (int k = 0; k < 4; k++)
            {
                double a = (rec.SubX(k) - 0.5) * size;
                double b = (rec.SubY(k) - 0.5) * size;
                SetCorner(one, k, cx + (rx * a) + (fx * b), cy + (ry * a) + (fy * b));
            }

            SetUvs(one, r.U0, r.V0, r.U1, r.V1);
        }

        private void Tint(int first, in ArtLayer layer, in DrawPrimitive p, IReadOnlyList<Rgba> roles, bool linear)
        {
            switch (layer.Colour)
            {
                case LayerColour.Role:
                    Rgba role = roles[(int)p.Colour];
                    SetColour(first, _count, role.R, role.G, role.B, role.A, linear);
                    break;
                case LayerColour.Region:
                    Rgb c = RegionOf(p.Paint, layer.Region);
                    SetColour(first, _count, c.R, c.G, c.B, 255, linear);
                    break;
                default:
                    SetColour(first, _count, layer.Fixed.R, layer.Fixed.G, layer.Fixed.B, 255, linear);
                    break;
            }
        }

        private static Rgb RegionOf(Paint paint, int region)
        {
            switch (region)
            {
                case 0: return paint.Region0;
                case 1: return paint.Region1;
                case 2: return paint.Region2;
                case 3: return paint.Region3;
                default: return paint.Region4;
            }
        }

        private void SetColour(int from, int to, byte r, byte g, byte b, byte a, bool linear)
        {
            byte lr = linear ? _linear[r] : r;
            byte lg = linear ? _linear[g] : g;
            byte lb = linear ? _linear[b] : b;
            byte[] colours = _colours;
            for (int q = from; q < to; q++)
            {
                int o = 16 * q;
                colours[o] = lr; colours[o + 1] = lg; colours[o + 2] = lb; colours[o + 3] = a;
                colours[o + 4] = lr; colours[o + 5] = lg; colours[o + 6] = lb; colours[o + 7] = a;
                colours[o + 8] = lr; colours[o + 9] = lg; colours[o + 10] = lb; colours[o + 11] = a;
                colours[o + 12] = lr; colours[o + 13] = lg; colours[o + 14] = lb; colours[o + 15] = a;
            }
        }

        private int Next()
        {
            int q = _count;
            if (8 * (q + 1) > _corners.Length)
            {
                int n = _corners.Length * 2;
                Array.Resize(ref _corners, n);
                Array.Resize(ref _uvs, n);
                Array.Resize(ref _colours, n * 2);
            }

            _count = q + 1;
            return q;
        }

        private void SetBox(int q, double x0, double y0, double x1, double y1)
        {
            SetCorner(q, 0, x0, y0);
            SetCorner(q, 1, x1, y0);
            SetCorner(q, 2, x1, y1);
            SetCorner(q, 3, x0, y1);
        }

        private void SetCorner(int q, int k, double x, double y)
        {
            int o = (8 * q) + (2 * k);
            _corners[o] = (float)(x + _shiftX);
            _corners[o + 1] = (float)(y + _shiftY);
        }

        private void SetUvs(int q, float u0, float v0, float u1, float v1)
        {
            int o = 8 * q;
            float[] uvs = _uvs;
            uvs[o] = u0;
            uvs[o + 1] = v0;
            uvs[o + 2] = u1;
            uvs[o + 3] = v0;
            uvs[o + 4] = u1;
            uvs[o + 5] = v1;
            uvs[o + 6] = u0;
            uvs[o + 7] = v1;
        }

        /// <summary>A layer with its sub-square divided by ART_UNITS once, exactly (a power of two).</summary>
        private sealed class LayerRec
        {
            private readonly double _x0;
            private readonly double _y0;
            private readonly double _x1;
            private readonly double _y1;

            public LayerRec(ArtLayer layer)
            {
                Layer = layer;
                _x0 = layer.MinX / 1024.0;
                _y0 = layer.MinY / 1024.0;
                _x1 = layer.MaxX / 1024.0;
                _y1 = layer.MaxY / 1024.0;
            }

            public readonly ArtLayer Layer;

            /// <summary>Design x of corner k, over ART_UNITS.</summary>
            public double SubX(int k)
            {
                return k == 0 || k == 3 ? _x0 : _x1;
            }

            /// <summary>Design y of corner k, over ART_UNITS.</summary>
            public double SubY(int k)
            {
                return k < 2 ? _y0 : _y1;
            }
        }
    }
}
