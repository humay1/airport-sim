using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AirportSim.App.Host;
using AirportSim.App.Render;
using AirportSim.App.Render.Unity;
using AirportSim.App.Ui;
using AirportSim.App.Ui.UnityBackend;
using AirportSim.Sim.Core;
using UnityEngine;

namespace AirportSim.Shell
{
    /// <summary>
    /// The Unity bootstrap (spec 16 section 16.7). It holds no decisions: it hands the host the bundle
    /// and the content, hands the backends what they draw, and quits with the checkpoint run's code in
    /// batch mode. It calls no sim member, never branches on sim state and never reads a bundle file.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class AirportSimBootstrap : MonoBehaviour
    {
        private const int ExitBadArguments = 2;

        private RenderBackend render;
        private UiBackend ui;
        private IFrameLoop loop;

        private void Awake()
        {
            if (!Application.isBatchMode)
            {
                return;
            }

            // Nothing is drawn in a checkpoint run: deactivating the object, first in Awake order,
            // keeps the backends on it from starting.
            gameObject.SetActive(false);
            string streaming = Application.streamingAssetsPath;
            RunBatch(
                new DirectoryBundle(Path.Combine(streaming, "Scenario")),
                new DirectoryContentSource(Path.Combine(streaming, "Content")));
        }

        private void Start()
        {
            render = GetComponent<RenderBackend>();
            ui = GetComponent<UiBackend>();

            string streaming = Application.streamingAssetsPath;
            var bundle = new DirectoryBundle(Path.Combine(streaming, "Scenario"));
            var contentSource = new DirectoryContentSource(Path.Combine(streaming, "Content"));
            var content = HostFactory.LoadContent(contentSource);
            ui.SetStringTable(UiFactory.LoadStringTable(contentSource));

            ComposedSim sim = HostFactory.CreateSimComposer(content).Compose(bundle, new DiscardCheckpoints());
            Presentation presentation = HostFactory.CreatePresentationComposer().Compose(sim, bundle, new PlayerPrefsStore());
            loop = presentation.Frame;
        }

        private void RunBatch(IScenarioBundle bundle, IContentSource contentSource)
        {
            string[] all = Environment.GetCommandLineArgs();
            var args = new List<string>(all.Length);
            for (int i = 1; i < all.Length; i++)
            {
                args.Add(all[i]);
            }

            if (!HostFactory.CreateCommandLine().TryParse(args, out CheckpointRunRequest request))
            {
                Application.Quit(ExitBadArguments);
                return;
            }

            TextWriter original = Console.Error;
            int code;
            Console.SetError(new LogErrorWriter());
            try
            {
                code = HostFactory.CreateHeadlessRun(HostFactory.CreateSimComposer(HostFactory.LoadContent(contentSource))).Run(bundle, request);
            }
            finally
            {
                Console.SetError(original);
            }

            Application.Quit(code);
        }

        private void Update()
        {
            if (loop == null)
            {
                return;
            }

            CameraView camera = render.ReadCameraView();
            long micros = (long)(Time.unscaledDeltaTime * 1000000f);
            var input = new FrameInput(camera, Screen.width, Screen.height, ui.Inputs, micros);
            FrameOutput output = loop.RunFrame(input);
            ui.ClearInputs();
            render.Draw(output.Render);
            ui.Show(output.Ui);
        }

        /// <summary>The bundle over one directory, by exact name.</summary>
        private sealed class DirectoryBundle : IScenarioBundle
        {
            private readonly string root;

            public DirectoryBundle(string root)
            {
                this.root = root;
            }

            public bool Has(string fileName)
            {
                return File.Exists(Path.Combine(root, fileName));
            }

            public byte[] ReadAll(string fileName)
            {
                return File.ReadAllBytes(Path.Combine(root, fileName));
            }
        }

        /// <summary>The content over one directory: every file, relative and '/'-separated.</summary>
        private sealed class DirectoryContentSource : IContentSource
        {
            private readonly string root;

            public DirectoryContentSource(string root)
            {
                this.root = root;
            }

            public IReadOnlyList<string> Files()
            {
                string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                var relative = new List<string>(files.Length);
                foreach (string file in files)
                {
                    relative.Add(file.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/'));
                }

                return relative;
            }

            public byte[] ReadAll(string path)
            {
                return File.ReadAllBytes(Path.Combine(root, path));
            }
        }

        /// <summary>The engine's player preferences, one string per key.</summary>
        private sealed class PlayerPrefsStore : IPreferenceStore
        {
            public bool TryRead(string key, out string value)
            {
                if (PlayerPrefs.HasKey(key))
                {
                    value = PlayerPrefs.GetString(key);
                    return true;
                }

                value = string.Empty;
                return false;
            }

            public void Write(string key, string value)
            {
                PlayerPrefs.SetString(key, value);
            }
        }

        /// <summary>A play session keeps no checkpoints: the sink takes each one and drops it.</summary>
        private sealed class DiscardCheckpoints : ICheckpointSink
        {
            public void Record(in Checkpoint cp)
            {
            }
        }

        /// <summary>
        /// Forwards each completed line of Console.Error, without its line break, to Debug.LogError.
        /// A line break is LF, or CR followed by LF as one; a CR not followed by LF is part of the line.
        /// </summary>
        private sealed class LogErrorWriter : TextWriter
        {
            private readonly StringBuilder line = new StringBuilder();
            private bool pendingCr;

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                if (pendingCr)
                {
                    pendingCr = false;
                    if (value == '\n')
                    {
                        Emit();
                        return;
                    }

                    line.Append('\r');
                }

                if (value == '\r')
                {
                    pendingCr = true;
                }
                else if (value == '\n')
                {
                    Emit();
                }
                else
                {
                    line.Append(value);
                }
            }

            private void Emit()
            {
                Debug.LogError(line.ToString());
                line.Length = 0;
            }
        }
    }
}
