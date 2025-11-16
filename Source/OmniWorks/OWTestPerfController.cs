using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.Localization;
using KSP.UI.Screens;

namespace OWPerf
{
    public class OWPerfTestController : PartModule
    {
        [KSPField(isPersistant = false)]
        public int expectedConverterCount = 38;

        [KSPField(isPersistant = false)]
        public string[] inputResourceNames =
        {
        "OWPerfInputA",
        "OWPerfInputB",
        "OWPerfInputC",
        "OWPerfInputD",
        "OWPerfInputE"
        };

        private readonly List<ModuleResourceConverter> converterModules = new List<ModuleResourceConverter>();
        private readonly List<PartResource> inputResources = new List<PartResource>();

        private bool isTestRunning;
        private bool hasStartTime;
        private double startTimeUT;
        private double stopTimeUT;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (vessel == null)
                return;

            converterModules.Clear();
            converterModules.AddRange(vessel.FindPartModulesImplementing<ModuleResourceConverter>());

            // Cache all input PartResources
            inputResources.Clear();
            for (int index = 0; index < vessel.parts.Count; index++)
            {
                Part vesselPart = vessel.parts[index];
                PartResourceList resources = vesselPart.Resources;

                for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
                {
                    PartResource resource = resources[resourceIndex];
                    for (int resourceName = 0; resourceName < inputResourceNames.Length; resourceName++)
                    {
                        if (resource.resourceName == inputResourceNames[resourceName])
                        {
                            inputResources.Add(resource);
                            break;
                        }
                    }
                }
            }

            // Optional sanity checks:
            ScreenMessages.PostScreenMessage(
                $"[OWPerf] Found {converterModules.Count} converters, {inputResources.Count} input resource instances.",
                15f, ScreenMessageStyle.UPPER_CENTER);
        }

        [KSPEvent(guiActive = true, guiName = "Start Perf Test")]
        public void StartPerfTest()
        {
            if (isTestRunning)
                return;

            // Start all converters
            for (int i = 0; i < converterModules.Count; i++)
            {
                ModuleResourceConverter converter = converterModules[i];
                if (converter != null)
                {
                    converter.StartResourceConverter();
                }
            }

            isTestRunning = true;
            hasStartTime = false; // we’ll set it on first frame when all are really running
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || !isTestRunning)
                return;

            // Wait for all converters to actually be running before we timestamp
            if (!hasStartTime)
            {
                bool allRunning = true;

                for (int i = 0; i < converterModules.Count; i++)
                {
                    ModuleResourceConverter converter = converterModules[i];
                    if (converter == null || !converter.IsActivated)
                    {
                        allRunning = false;
                        break;
                    }
                }

                if (!allRunning)
                    return;

                startTimeUT = Planetarium.GetUniversalTime();
                hasStartTime = true;

                ScreenMessages.PostScreenMessage(
                    $"[OWPerf] Test started at UT={startTimeUT:F3}",
                    3f, ScreenMessageStyle.UPPER_CENTER);

                return;
            }

            // From here on, we’re in "measurement" mode
            double totalInputRemaining = 0.0;
            for (int i = 0; i < inputResources.Count; i++)
            {
                PartResource resource = inputResources[i];
                if (resource != null)
                {
                    totalInputRemaining += resource.amount;
                }
            }

            if (totalInputRemaining <= 0.0001)
            {
                stopTimeUT = Planetarium.GetUniversalTime();
                isTestRunning = false;

                double duration = stopTimeUT - startTimeUT;

                ScreenMessages.PostScreenMessage(
                    $"[OWPerf] Inputs depleted. Duration: {duration:F3} seconds (UT).",
                    10f, ScreenMessageStyle.UPPER_CENTER);

                string testResults = $"[OWPerf] Perf test complete. StartUT={startTimeUT:F3}, StopUT={stopTimeUT:F3}, Duration={duration:F3}s.";
                Debug.Log(testResults);

                string dir = $"{KSPUtil.ApplicationRootPath}saves/{HighLogic.SaveFolder}/OWPerf/";
                string filePath = $"{dir}/OWPerfTestResults.txt";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
                System.IO.File.AppendAllText(filePath, testResults);

                for (int i = 0; i < converterModules.Count; i++)
                {
                    ModuleResourceConverter converter = converterModules[i];
                    if (converter != null)
                        converter.StopResourceConverter();
                }
            }
        }

    }

}
