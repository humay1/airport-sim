using System;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;

namespace AirportSim.App.Render
{
    /// <summary>The desired-promoted predicate shared by the controller and the builder. Spec: 15 §15.7, Q-101.</summary>
    internal static class Promotion
    {
        /// <summary>Visible (closed intervals) and at or below the zoom threshold and DrawAgents.</summary>
        public static bool Desired(in FlowNodeBox box, in CameraView camera, in GraphicsSettings graphics)
        {
            if (!graphics.DrawAgents || camera.ViewHeight > (float)RenderConstants.AGENT_ZOOM_THRESHOLD)
            {
                return false;
            }

            float halfW = camera.ViewHeight * camera.Aspect / 2f;
            float halfH = camera.ViewHeight / 2f;
            return box.MaxX >= camera.Centre.X - halfW
                && box.MinX <= camera.Centre.X + halfW
                && box.MaxY >= camera.Centre.Y - halfH
                && box.MinY <= camera.Centre.Y + halfH;
        }

        /// <summary>A copy of the boxes in ascending NodeId.</summary>
        public static FlowNodeBox[] SortedBoxes(in RenderLayout layout)
        {
            var boxes = new FlowNodeBox[layout.FlowNodes.Count];
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i] = layout.FlowNodes[i];
            }

            Array.Sort(boxes, (a, b) => a.Node.Value.CompareTo(b.Node.Value));
            return boxes;
        }
    }

    /// <summary>The one caller of <see cref="IFlowSystem.SetPromoted"/>. Spec: 15 §15.7.</summary>
    internal sealed class PromotionController : IPromotionController
    {
        private readonly IFlowSystem? _flow;
        private readonly FlowNodeBox[] _boxes;
        private readonly bool[] _promoted;
        private bool _first = true;

        public PromotionController(in RenderSources sources, in RenderLayout layout)
        {
            _flow = sources.Flow;
            _boxes = Promotion.SortedBoxes(layout);
            _promoted = new bool[_boxes.Length];
        }

        public void Update(in CameraView camera, in GraphicsSettings graphics)
        {
            if (_flow == null)
            {
                return;
            }

            for (int i = 0; i < _boxes.Length; i++)
            {
                bool desired = Promotion.Desired(_boxes[i], camera, graphics);
                if (_first || desired != _promoted[i])
                {
                    _flow.SetPromoted(_boxes[i].Node, desired);
                    _promoted[i] = desired;
                }
            }

            _first = false;
        }
    }
}
