using System.Collections.Generic;
using UnityEngine;

namespace AirportSim.App.Render.Unity
{
    /// <summary>
    /// The Unity render backend (15 §15.10). Lives on the scene's main camera object. The bootstrap
    /// (16 §16.7) calls <see cref="ReadCameraView"/> and <see cref="Draw"/> once per engine frame; this
    /// component runs no frame order, calls no sim member and issues no command.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RenderBackend : MonoBehaviour
    {
        const int RoleCount = (int)ColourRole.LaneClosed + 1;
        const int SceneLayer = 31;
        const int MsaaSamples = 4;
        const int InitialPrimitives = 1024;

        /// <summary>Colour per <see cref="ColourRole"/>; "maps ColourRole to colour through a palette asset".</summary>
        public RenderPalette Palette;

        /// <summary>Camera at scene start.</summary>
        public float StartCentreX;
        public float StartCentreY;
        public float StartViewHeight = 200f;

        /// <summary>Zoom limits and speed. They keep <c>ViewHeight</c> positive.</summary>
        public float MinViewHeight = 10f;
        public float MaxViewHeight = 5000f;
        public float ZoomPerNotch = 1.1f;

        Camera sceneCamera;     // renders the mesh into target
        Mesh mesh;              // the one mesh: every primitive, in list order, vertex-coloured
        Material material;
        RenderTexture target;
        Color32[] roleColours;
        Vector3[] vertices;
        Color32[] colours;
        int[] triangles;
        bool ready;

        WorldPoint centre;
        float viewHeight;
        Vector3 lastMouse;

        // Last applied graphics values (15 §15.10: apply only when they differ).
        int appliedFrameRateCap = -1;
        int appliedScalePercent = -1;
        bool appliedAntiAliasing;
        int appliedScreenWidth;
        int appliedScreenHeight;

        void Awake()
        {
            Shader shader = Shader.Find("Sprites/Default");   // vertex colour, no depth write: triangle order is draw order
            if (Palette == null || Palette.Roles.Length != RoleCount || shader == null)
            {
                Debug.LogError("RenderBackend: needs a palette with one colour per ColourRole, and the Sprites/Default shader.");
                return;
            }

            roleColours = new Color32[RoleCount];
            for (int i = 0; i < RoleCount; i++)
            {
                roleColours[i] = Palette.Roles[i];
            }

            // The presenter is this object's camera: it draws nothing itself and copies target to the screen.
            Camera presenter = GetComponent<Camera>();
            presenter.cullingMask = 0;
            presenter.clearFlags = CameraClearFlags.SolidColor;
            presenter.backgroundColor = Color.black;
            presenter.depth = 0;

            var go = new GameObject("RenderBackend scene camera", typeof(Camera));
            go.transform.SetParent(transform, false);
            sceneCamera = go.GetComponent<Camera>();
            sceneCamera.orthographic = true;
            sceneCamera.cullingMask = 1 << SceneLayer;
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.backgroundColor = Palette.Background;
            sceneCamera.depth = -1;

            material = new Material(shader);
            mesh = new Mesh();
            mesh.MarkDynamic();
            Grow(InitialPrimitives);

            centre = new WorldPoint(StartCentreX, StartCentreY);
            viewHeight = Mathf.Clamp(StartViewHeight, MinViewHeight, MaxViewHeight);
            ApplyTarget(100, false);
            ready = true;
        }

        void OnDestroy()
        {
            if (target != null)
            {
                target.Release();
                Destroy(target);
            }

            if (mesh != null)
            {
                Destroy(mesh);
            }

            if (material != null)
            {
                Destroy(material);
            }

            if (sceneCamera != null)
            {
                Destroy(sceneCamera.gameObject);
            }
        }

        /// <summary>
        /// Turns this frame's pan (right or middle mouse drag) and zoom (scroll wheel) into the
        /// <see cref="CameraView"/>. <c>ViewHeight</c> stays within positive limits and <c>Aspect</c>
        /// is the full-screen width over height.
        /// </summary>
        public CameraView ReadCameraView()
        {
            float screenHeight = Mathf.Max(1, Screen.height);
            Vector3 mouse = Input.mousePosition;
            bool held = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            bool started = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            if (held && !started)
            {
                float worldPerPixel = viewHeight / screenHeight;
                centre = new WorldPoint(
                    centre.X - (mouse.x - lastMouse.x) * worldPerPixel,
                    centre.Y - (mouse.y - lastMouse.y) * worldPerPixel);
            }

            lastMouse = mouse;

            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f)
            {
                viewHeight = Mathf.Clamp(viewHeight * Mathf.Pow(ZoomPerNotch, -scroll), Mathf.Max(MinViewHeight, 0.001f), MaxViewHeight);
            }

            return new CameraView(centre, viewHeight, Mathf.Max(1, Screen.width) / screenHeight);
        }

        /// <summary>Applies the frame's graphics knobs if they changed, then draws its primitives in list order.</summary>
        public void Draw(in RenderFrame frame)
        {
            if (!ready)
            {
                return;
            }

            GraphicsSettings g = frame.Graphics;
            if (g.FrameRateCap != appliedFrameRateCap)
            {
                QualitySettings.vSyncCount = 0;   // targetFrameRate is ignored while vsync is on
                Application.targetFrameRate = g.FrameRateCap == 0 ? -1 : g.FrameRateCap;
                appliedFrameRateCap = g.FrameRateCap;
            }

            ApplyTarget(g.ResolutionScalePercent, g.AntiAliasing);

            CameraView view = frame.Camera;
            sceneCamera.orthographicSize = view.ViewHeight * 0.5f;
            sceneCamera.aspect = view.Aspect;
            sceneCamera.transform.position = new Vector3(view.Centre.X, view.Centre.Y, -10f);

            FillMesh(frame.Primitives);
            Graphics.DrawMesh(mesh, Matrix4x4.identity, material, SceneLayer, sceneCamera);   // one draw call for every layer and role
        }

        // Resolution scale and anti-aliasing: the scene renders into a texture of scale% of the screen
        // (MSAA when on) that is stretched to the full screen. Camera, FrameInput and clicks are unaffected.
        void ApplyTarget(int scalePercent, bool antiAliasing)
        {
            if (target != null
                && scalePercent == appliedScalePercent
                && antiAliasing == appliedAntiAliasing
                && Screen.width == appliedScreenWidth
                && Screen.height == appliedScreenHeight)
            {
                return;
            }

            sceneCamera.targetTexture = null;
            if (target != null)
            {
                target.Release();
                Destroy(target);
            }

            target = new RenderTexture(Mathf.Max(1, Screen.width * scalePercent / 100), Mathf.Max(1, Screen.height * scalePercent / 100), 0)
            {
                antiAliasing = antiAliasing ? MsaaSamples : 1,
                filterMode = FilterMode.Bilinear,
            };
            sceneCamera.targetTexture = target;
            appliedScalePercent = scalePercent;
            appliedAntiAliasing = antiAliasing;
            appliedScreenWidth = Screen.width;
            appliedScreenHeight = Screen.height;
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (target != null)
            {
                Graphics.Blit(target, destination);
            }
        }

        // One quad per primitive, none filtered or skipped, in list order. Dots are squares of side Size.
        void FillMesh(IReadOnlyList<DrawPrimitive> primitives)
        {
            int count = primitives.Count;
            if (count * 4 > vertices.Length)
            {
                Grow(count);
            }

            for (int i = 0; i < count; i++)
            {
                DrawPrimitive p = primitives[i];
                float ax = p.A.X;
                float ay = p.A.Y;
                float half = p.Size * 0.5f;
                int v = i * 4;
                switch (p.Kind)
                {
                    case PrimitiveKind.Box:
                        Quad(v, ax, ay, p.B.X, ay, p.B.X, p.B.Y, ax, p.B.Y);
                        break;
                    case PrimitiveKind.Dot:
                        Quad(v, ax - half, ay - half, ax + half, ay - half, ax + half, ay + half, ax - half, ay + half);
                        break;
                    default:   // Segment: a quad of width Size from A to B
                        float dx = p.B.X - ax;
                        float dy = p.B.Y - ay;
                        float length = Mathf.Sqrt(dx * dx + dy * dy);
                        float nx = length > 0f ? -dy / length * half : 0f;
                        float ny = length > 0f ? dx / length * half : half;
                        Quad(v, ax + nx, ay + ny, ax - nx, ay - ny, p.B.X - nx, p.B.Y - ny, p.B.X + nx, p.B.Y + ny);
                        break;
                }

                Color32 colour = roleColours[(int)p.Colour];
                colours[v] = colour;
                colours[v + 1] = colour;
                colours[v + 2] = colour;
                colours[v + 3] = colour;
            }

            mesh.Clear(false);
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices, 0, count * 4);
            mesh.SetColors(colours, 0, count * 4);
            mesh.SetTriangles(triangles, 0, count * 6, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
        }

        void Quad(int v, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3)
        {
            vertices[v] = new Vector3(x0, y0, 0f);
            vertices[v + 1] = new Vector3(x1, y1, 0f);
            vertices[v + 2] = new Vector3(x2, y2, 0f);
            vertices[v + 3] = new Vector3(x3, y3, 0f);
        }

        // Managed scratch arrays only; they grow when a frame holds more primitives than ever before.
        void Grow(int primitiveCount)
        {
            int capacity = InitialPrimitives;
            while (capacity < primitiveCount)
            {
                capacity *= 2;
            }

            vertices = new Vector3[capacity * 4];
            colours = new Color32[capacity * 4];
            triangles = new int[capacity * 6];
            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }
        }
    }
}
