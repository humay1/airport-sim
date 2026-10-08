using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AirportSim.Shell.Editor
{
    /// <summary>
    /// The build step (spec 16 section 16.3): at the start of every player build it assembles
    /// Assets/StreamingAssets/Scenario/ and Assets/StreamingAssets/Content/ from the repository.
    /// It is the only code that copies them and it makes no decision.
    /// </summary>
    public sealed class PlaytestBundleBuildStep : IPreprocessBuildWithReport
    {
        // Bundle file name, then its source relative to the repository root (16 section 16.3's table).
        private static readonly string[,] Rows =
        {
            { "bundle.json", "unity/AirportSim/Scenario/bundle.json" },
            { "world.fixture", "tests/fixtures/world/phase0-landside.json" },
            { "schedule.csv", "tests/fixtures/schedule/phase0-200.csv" },
            { "airside.fixture", "tests/fixtures/harness/checkpoints-phase1/airside.fixture" },
            { "airside_rules.json", "data/balance/airside_rules.json" },
            { "turnaround.fixture", "tests/fixtures/turnaround/phase1-five-vehicles.json" },
            { "flow.fixture", "tests/fixtures/flow/phase0-landside.flow.json" },
            { "render_layout.fixture", "tests/fixtures/render/playtest-layout.json" },
        };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string data = Path.Combine(root, "data");

            var missing = new List<string>();
            for (int i = 0; i < Rows.GetLength(0); i++)
            {
                string source = Path.Combine(root, Rows[i, 1]);
                if (!File.Exists(source))
                {
                    missing.Add(source);
                }
            }

            if (!Directory.Exists(data))
            {
                missing.Add(data);
            }

            if (missing.Count > 0)
            {
                throw new BuildFailedException("PlaytestBundleBuildStep: missing source: " + string.Join(", ", missing));
            }

            string streaming = Path.Combine(Application.dataPath, "StreamingAssets");
            string scenario = Path.Combine(streaming, "Scenario");
            string content = Path.Combine(streaming, "Content");
            Recreate(scenario);
            Recreate(content);

            for (int i = 0; i < Rows.GetLength(0); i++)
            {
                File.Copy(Path.Combine(root, Rows[i, 1]), Path.Combine(scenario, Rows[i, 0]));
            }

            foreach (string file in Directory.GetFiles(data, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(content, file.Substring(data.Length).TrimStart('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void Recreate(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }

            string meta = directory + ".meta";
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }

            Directory.CreateDirectory(directory);
        }
    }
}
