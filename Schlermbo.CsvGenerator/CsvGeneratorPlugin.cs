using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CsvHelper;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Schlermbo.CsvGenerator
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInProcess("DSPGAME.exe")]
    public class CsvGeneratorPlugin : BaseUnityPlugin
    {
        public const string GUID = "schlermbo.dsp.csvgenerator";
        public const string NAME = "Schlermbo.CsvGenerator";
        public const string VERSION = "1.0.0";

        public static RectTransform TriggerButton;
        public static Image ProgressImage;

        private Harmony _harmony;
        internal static ManualLogSource Log;
        private static ConfigFile _config;

        private async void Awake()
        {
            Log = Logger;
            _config = Config;
            _harmony = new Harmony(GUID);
            _harmony.PatchAll(typeof(CsvGeneratorPlugin));
            Log.LogInfo("Schlermbo.CsvGenerator Loaded");
        }

        private void OnDestroy()
        {
            Log.LogInfo("Schlermbo.CsvGenerator Unloading");
            _harmony?.UnpatchSelf();
            Log = null;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GameMain), "Begin")]
        public static void GameMain_Begin_Prefix()
        {
            if (GameMain.instance == null || !GameObject.Find("Game Menu/button-1-bg")) return;

            var parent = GameObject.Find("Game Menu").GetComponent<RectTransform>();
            var sprite = Resources.Load<Sprite>("UI/Textures/Sprites/round-50px-border");

            if (ProgressImage != null)
            {
                ProgressImage.fillAmount = 0;
            }
            else
            {
                var prefab = GameObject.Find("Game Menu/button-1-bg").GetComponent<RectTransform>();
                var referencePosition = prefab.localPosition;
                TriggerButton = Instantiate(prefab, parent);
                TriggerButton.gameObject.name = "generate-csv-button";
                TriggerButton.GetComponent<UIButton>().tips.tipTitle = "Generate CSV";
                TriggerButton.GetComponent<UIButton>().tips.tipText = "Click to generate resource CSV file.";
                TriggerButton.GetComponent<UIButton>().tips.delay = 0f;
                TriggerButton.transform.Find("button-1/icon").GetComponent<Image>().sprite = GetSprite();
                TriggerButton.localScale = new Vector3(0.35f, 0.35f, 0.35f);
                TriggerButton.localPosition = new Vector3(referencePosition.x + 145f, referencePosition.y + 87f,
                    referencePosition.z);
                TriggerButton.GetComponent<UIButton>().OnPointerDown(null);
                TriggerButton.GetComponent<UIButton>().OnPointerEnter(null);
                TriggerButton.GetComponent<UIButton>().button.onClick.AddListener(TriggerCsvGenerationTask);

                var prefabProgress = GameObject.Find("tech-progress").GetComponent<Image>();
                ProgressImage = Instantiate(prefabProgress, parent);
                ProgressImage.gameObject.name = "generate-csv-image";
                ProgressImage.fillAmount = 0;
                ProgressImage.type = Image.Type.Filled;
                ProgressImage.rectTransform.localScale = new Vector3(3.0f, 3.0f, 3.0f);
                ProgressImage.rectTransform.localPosition = new Vector3(referencePosition.x + 145.5f,
                    referencePosition.y + 86.6f, referencePosition.z);

                // Switch from circle-thin to round-50px-border
                ProgressImage.sprite = Instantiate(sprite);
            }
        }

        public static Sprite GetSprite()
        {
            var texture = new Texture2D(48, 48, TextureFormat.RGBA32, false);
            var color = new Color(1, 1, 1, 1);

            // Draw a plane like the one re[resending drones in the Mecha Panel...
            for (var x = 0; x < 48; x++)
            {
                for (var y = 0; y < 48; y++)
                {
                    if ((x is >= 3 and <= 44 && y is >= 3 and <= 5) || // top
                        (x is >= 3 and <= 44 && y is >= 33 and <= 36) ||
                        (x is >= 3 and <= 44 && y is >= 42 and <= 44) ||
                        (x is >= 2 and <= 5 && y is >= 3 and <= 44) || // left
                        (x is >= 12 and <= 14 && y is >= 3 and <= 44) ||
                        (x is >= 27 and <= 29 && y is >= 3 and <= 44) ||
                        (x is >= 42 and <= 45 && y is >= 3 and <= 44))
                    {
                        texture.SetPixel(x, y, color);
                    }
                    else
                    {
                        texture.SetPixel(x, y, new Color(0, 0, 0, 0));
                    }
                }
            }

            texture.name = "generate-csv-icon";
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, 48f, 48f), new Vector2(0f, 0f), 1000);
        }

        public static void TriggerCsvGenerationTask()
        {
            Log.LogInfo("Calculating Planet Data");
            Task.Run(NewCalculations);
        }

        private static async Task NewCalculations()
        {
            try
            {
                var calculationTasks = (
                    from star in GameMain.galaxy.stars
                    from planet in star.planets
                    select PlanetResourceCalculation(planet)
                ).ToList();

                var completed = Task.WhenAll(calculationTasks);
                await completed.ContinueWith(async x =>
                {
                    var planets = x.Result.ToList();
                    using var writer = new StreamWriter("C:\\dsp\\resources.csv");
                    using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                    await csv.WriteRecordsAsync(planets);
                });
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }

        private static Task<PlanetDataCsvEntry> PlanetResourceCalculation(PlanetData planet)
        {
            PlanetModelingManager.RequestCalcPlanet(planet);
            while (!planet.calculated && planet.veinGroups == null && planet.gasItems == null)
            {
                Thread.Sleep(100);
            }

            var planetCsv = new PlanetDataCsvEntry()
            {
                Name = planet.displayName,
                OverrideName = planet.overrideName,
                Star = planet.star.displayName,
                StarOverrideName = planet.star.overrideName,
                StarType = planet.star.typeString,
                StarLuminosity = $"{planet.star.dysonLumino}",
                OrbitalPeriod = $"{planet.orbitalPeriod}",
                OrbitInclination = $"{planet.orbitInclination}",
                OrbitPhase = $"{planet.orbitPhase}",
                OrbitRadius = $"{planet.orbitRadius}",
                RotationPeriod = $"{planet.rotationPeriod}",
                RotationPhase = $"{planet.rotationPhase}",
                StarDistance = $"{planet.sunDistance}",
                Ocean = LDB.ItemName(planet.waterItemId)
            };

            if (planet.type == EPlanetType.Gas && planet.gasItems != null)
            {
                for (var i = 0; i < planet.gasItems.Length; i++)
                {
                    planetCsv.MapGas(planet.gasItems[i], planet.gasSpeeds[i]);
                }
            }

            var veinGroups = planet.runtimeVeinGroups ?? planet.veinGroups;
            if (veinGroups == null) return Task.FromResult(planetCsv);

            var veins = new Dictionary<EVeinType, Dictionary<string, object>>();
            foreach (var veinGroup in veinGroups)
            {
                if (!veins.ContainsKey(veinGroup.type))
                {
                    veins[veinGroup.type] = new Dictionary<string, object>
                    {
                        ["amount"] = (long)0,
                        ["veins"] = 0
                    };
                }

                veins[veinGroup.type]["amount"] = (long)veins[veinGroup.type]["amount"] + veinGroup.amount;
                veins[veinGroup.type]["veins"] = (int)veins[veinGroup.type]["veins"] + veinGroup.count;
            }

            foreach (var vein in veins)
            {
                planetCsv.MapVein(vein.Key, (int)vein.Value["veins"], (long)vein.Value["amount"]);
            }

            return Task.FromResult(planetCsv);
        }
    }


    public enum GasType
    {
        FireIce = 1011,
        Hydrogen = 1120,
        Deuterium = 1121
    }

    public enum CsvVeinType
    {
        Iron = EVeinType.Iron,
        Copper = EVeinType.Copper,
        Silicon = EVeinType.Silicium,
        Titanium = EVeinType.Titanium,
        Stone = EVeinType.Stone,
        Coal = EVeinType.Coal,
        Oil = EVeinType.Oil,
        FireIce = EVeinType.Fireice,
        Kimberlite = EVeinType.Diamond,
        FractalSilicon = EVeinType.Fractal,
        OrganicCrystal = EVeinType.Crysrub,
        OpticalGratingCrystal = EVeinType.Grat,
        SpiniformStalagmiteCrystal = EVeinType.Bamboo,
        UnipolarMagnet = EVeinType.Mag,
    }

    public class PlanetDataCsvEntry
    {
        // Generic
        public string Name { get; set; }
        public string OverrideName { get; set; }
        public string Star { get; set; }
        public string StarOverrideName { get; set; }
        public string StarType { get; set; }
        public string StarLuminosity { get; set; }
        public string OrbitalPeriod { get; set; }
        public string OrbitInclination { get; set; }
        public string OrbitPhase { get; set; }
        public string OrbitRadius { get; set; }
        public string RotationPeriod { get; set; }
        public string RotationPhase { get; set; }
        public string StarDistance { get; set; }
        

        // Mining Veins
        public string IronVeins { get; set; } = "";
        public string IronAmount { get; set; } = "";
        public string CopperVeins { get; set; } = "";
        public string CopperAmount { get; set; } = "";
        public string SiliconVeins { get; set; } = "";
        public string SiliconAmount { get; set; } = "";
        public string TitaniumVeins { get; set; } = "";
        public string TitaniumAmount { get; set; } = "";
        public string StoneVeins { get; set; } = "";
        public string StoneAmount { get; set; } = "";
        public string CoalVeins { get; set; } = "";
        public string CoalAmount { get; set; } = "";
        public string FireIceVeins { get; set; } = "";
        public string FireIceAmount { get; set; } = "";
        public string KimberliteVeins { get; set; } = "";
        public string KimberliteAmount { get; set; } = "";
        public string FractalSiliconVeins { get; set; } = "";
        public string FractalSiliconAmount { get; set; } = "";
        public string OrganicCrystalVeins { get; set; } = "";
        public string OrganicCrystalAmount { get; set; } = "";
        public string OpticalGratingCrystalVeins { get; set; } = "";
        public string OpticalGratingCrystalAmount { get; set; } = "";
        public string SpiniformStalagmiteCrystalVeins { get; set; } = "";
        public string SpiniformStalagmiteCrystalAmount { get; set; } = "";
        public string UnipolarMagnetVeins { get; set; } = "";
        public string UnipolarMagnetAmount { get; set; } = "";

        // Fluid Extraction
        public string Ocean { get; set; } = "";
        public string OilDeposits { get; set; } = "";
        public string OilRate { get; set; } = "";

        // Gas Extraction
        public string FireIceRate { get; set; } = ""; // id: 1011
        public string HydrogenRate { get; set; } = ""; // id: 1120
        public string DeuteriumRate { get; set; } = ""; // id: 1121

        public void MapGas(int gasId, float rate)
        {
            switch ((GasType)gasId)
            {
                case GasType.FireIce:
                    FireIceRate = $"{rate}";
                    break;
                case GasType.Hydrogen:
                    HydrogenRate = $"{rate}";
                    break;
                case GasType.Deuterium:
                    DeuteriumRate = $"{rate}";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(gasId), gasId,
                        $"GasType (id: {gasId}) not recognized");
            }
        }

        public void MapVein(EVeinType vein, int count, long amount)
        {
            switch (vein)
            {
                case EVeinType.None:
                    break;
                case EVeinType.Iron:
                    IronAmount = $"{amount}";
                    IronVeins = $"{count}";
                    break;
                case EVeinType.Copper:
                    CopperAmount = $"{amount}";
                    CopperVeins = $"{count}";
                    break;
                case EVeinType.Silicium:
                    SiliconAmount = $"{amount}";
                    SiliconVeins = $"{count}";
                    break;
                case EVeinType.Titanium:
                    TitaniumAmount = $"{amount}";
                    TitaniumVeins = $"{count}";
                    break;
                case EVeinType.Stone:
                    StoneAmount = $"{amount}";
                    StoneVeins = $"{count}";
                    break;
                case EVeinType.Coal:
                    CoalAmount = $"{amount}";
                    CoalVeins = $"{count}";
                    break;
                case EVeinType.Oil:
                    var oilRate = amount * VeinData.oilSpeedMultiplier;
                    OilRate = $"{oilRate}";
                    OilDeposits = $"{count}";
                    break;
                case EVeinType.Fireice:
                    FireIceAmount = $"{amount}";
                    FireIceVeins = $"{count}";
                    break;
                case EVeinType.Diamond:
                    KimberliteAmount = $"{amount}";
                    KimberliteVeins = $"{count}";
                    break;
                case EVeinType.Fractal:
                    FractalSiliconAmount = $"{amount}";
                    FractalSiliconVeins = $"{count}";
                    break;
                case EVeinType.Crysrub:
                    OrganicCrystalAmount = $"{amount}";
                    OrganicCrystalVeins = $"{count}";
                    break;
                case EVeinType.Grat:
                    OpticalGratingCrystalAmount = $"{amount}";
                    OpticalGratingCrystalVeins = $"{count}";
                    break;
                case EVeinType.Bamboo:
                    SpiniformStalagmiteCrystalAmount = $"{amount}";
                    SpiniformStalagmiteCrystalVeins = $"{count}";
                    break;
                case EVeinType.Mag:
                    UnipolarMagnetAmount = $"{amount}";
                    UnipolarMagnetVeins = $"{count}";
                    break;
                case EVeinType.Max:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(vein), vein, null);
            }
        }
    }
}