using System.Collections.Generic;

namespace AirportSim.App.Render.Art2D
{
    /// <summary>A straight-alpha colour. Spec: 15 §15.17.</summary>
    public readonly struct Rgba
    {
        /// <summary>Red.</summary>
        public byte R { get; }

        /// <summary>Green.</summary>
        public byte G { get; }

        /// <summary>Blue.</summary>
        public byte B { get; }

        /// <summary>Alpha.</summary>
        public byte A { get; }

        /// <summary>Constructs the colour.</summary>
        public Rgba(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    /// <summary>A rectangle in texture coordinates, 0 to 1, V up. Spec: 15 §15.17.</summary>
    public readonly struct AtlasRect
    {
        /// <summary>Left.</summary>
        public float U0 { get; }

        /// <summary>Bottom.</summary>
        public float V0 { get; }

        /// <summary>Right.</summary>
        public float U1 { get; }

        /// <summary>Top.</summary>
        public float V1 { get; }

        /// <summary>Constructs the rectangle.</summary>
        public AtlasRect(float u0, float v0, float u1, float v1)
        {
            U0 = u0;
            V0 = v0;
            U1 = u1;
            V1 = v1;
        }
    }

    /// <summary>The rasterised atlas. Spec: 15 §15.17.</summary>
    public readonly struct SpriteAtlas
    {
        /// <summary>Side of mip 0, in pixels (ATLAS_SIZE).</summary>
        public int Size { get; }

        /// <summary>ATLAS_MIP_COUNT entries; entry m is RGBA32, (Size >> m) squared times 4 bytes, rows bottom to top.</summary>
        public IReadOnlyList<byte[]> Mips { get; }

        /// <summary>Constructs the atlas.</summary>
        public SpriteAtlas(int size, IReadOnlyList<byte[]> mips)
        {
            Size = size;
            Mips = mips;
        }
    }

    /// <summary>Where a layer's colour comes from. Spec: 15 §15.17.</summary>
    public enum LayerColour
    {
        /// <summary>The primitive's colour role.</summary>
        Role,

        /// <summary>A Paint region.</summary>
        Region,

        /// <summary>A constant sRGB colour.</summary>
        Fixed,
    }

    /// <summary>One layer of a visual, in painter order. Spec: 15 §15.17.</summary>
    public readonly struct ArtLayer
    {
        /// <summary>Where the colour comes from.</summary>
        public LayerColour Colour { get; }

        /// <summary>Paint region index when Colour is Region; else 0.</summary>
        public int Region { get; }

        /// <summary>The constant colour when Colour is Fixed; else (0, 0, 0).</summary>
        public Rgb Fixed { get; }

        /// <summary>The cell's visible square; unused when IsLogo.</summary>
        public AtlasRect Rect { get; }

        /// <summary>Drawn from the Mark's cell, only when Paint.Mark is not None.</summary>
        public bool IsLogo { get; }

        /// <summary>Dot and Segment: the sub-square of the primitive it covers, design units, left.</summary>
        public int MinX { get; }

        /// <summary>Sub-square bottom.</summary>
        public int MinY { get; }

        /// <summary>Sub-square right.</summary>
        public int MaxX { get; }

        /// <summary>Sub-square top.</summary>
        public int MaxY { get; }

        /// <summary>Box: design inset of a nine-sliced layer; 0 is not sliced.</summary>
        public int SliceInset { get; }

        /// <summary>Box: its world inset, hundredths of a world unit.</summary>
        public int SliceWorld { get; }

        /// <summary>Box and Segment: tile side in whole world units; 0 is not tiled.</summary>
        public int Tile { get; }

        /// <summary>Offset added to every corner, hundredths of a world unit, never rotated.</summary>
        public int ShiftX { get; }

        /// <summary>Offset added to every corner, hundredths of a world unit, never rotated.</summary>
        public int ShiftY { get; }

        /// <summary>Constructs the layer.</summary>
        public ArtLayer(
            LayerColour colour,
            int region,
            Rgb fixedColour,
            AtlasRect rect,
            bool isLogo,
            int minX,
            int minY,
            int maxX,
            int maxY,
            int sliceInset,
            int sliceWorld,
            int tile,
            int shiftX,
            int shiftY)
        {
            Colour = colour;
            Region = region;
            Fixed = fixedColour;
            Rect = rect;
            IsLogo = isLogo;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            SliceInset = sliceInset;
            SliceWorld = sliceWorld;
            Tile = tile;
            ShiftX = shiftX;
            ShiftY = shiftY;
        }
    }

    /// <summary>Turns a frame into tinted quads. Spec: 15 §15.17.</summary>
    public interface ISpriteTessellator
    {
        /// <summary>The quad count of the last fill.</summary>
        int QuadCount { get; }

        /// <summary>8 per quad: x, y of corners 0 to 3, world units.</summary>
        float[] Corners { get; }

        /// <summary>8 per quad: u, v of corners 0 to 3.</summary>
        float[] Uvs { get; }

        /// <summary>16 per quad: R, G, B, A of corners 0 to 3.</summary>
        byte[] Colours { get; }

        /// <summary>Emits the ground, then every primitive's layers; returns QuadCount. Buffers are valid until the next call.</summary>
        int Fill(in RenderFrame frame, IReadOnlyList<Rgba> roleColours, bool linear);
    }

    /// <summary>The 2D art's constants. Spec: 15 §15.17.</summary>
    public static class Art2DConstants
    {
        /// <summary>Side of the atlas, in pixels.</summary>
        public const int ATLAS_SIZE = 4096;

        /// <summary>Side of a large cell, in pixels.</summary>
        public const int LARGE_CELL = 512;

        /// <summary>Side of a small cell, in pixels.</summary>
        public const int SMALL_CELL = 128;

        /// <summary>Number of mip levels.</summary>
        public const int ATLAS_MIP_COUNT = 6;

        /// <summary>Design units across a cell's visible square.</summary>
        public const int ART_UNITS = 1024;

        /// <summary>World side of one ground tile at the nearest zoom.</summary>
        public const int GROUND_TILE = 64;

        /// <summary>Most ground tiles along either axis.</summary>
        public const int GROUND_TILES_PER_AXIS = 32;
    }
}
