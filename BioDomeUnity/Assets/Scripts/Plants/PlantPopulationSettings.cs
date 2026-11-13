using System;
using System.Collections.Generic;
using UnityEngine;

namespace BioDome.Plants
{
    public enum DensityPreset
    {
        /// <summary>Open planting: mature canopies stay apart.</summary>
        Low = 0,
        /// <summary>Mature canopies overlap a little at their edges. A good default.</summary>
        Medium = 1,
        /// <summary>Lush planting: mature canopies overlap by about a quarter.</summary>
        High = 2,
        /// <summary>Use the custom spacing factor below.</summary>
        Custom = 3
    }

    /// <summary>A species that can be planted: its prefab, its configuration and how many visual variants to make.</summary>
    [Serializable]
    public class PlantSpeciesEntry
    {
        public string label = "Monstera";
        public GameObject prefab;
        public PlantData data;
        [Range(1, 8), Tooltip("Number of distinct looks (seeds) made at the initial growth stage.")]
        public int variantCount = 4;
        [Tooltip("Seed of the first variant. Later variants add 17 each.")]
        public int baseSeed = 101;
    }

    /// <summary>Planting zones whose name starts with <see cref="zonePrefix"/> get the species at <see cref="speciesIndex"/>.</summary>
    [Serializable]
    public class ZoneRule
    {
        [Tooltip("Matches PlantingZone names, e.g. 'Bed_' or 'Border_'. Rules are checked in order; the first match wins.")]
        public string zonePrefix = "Bed_";
        public int speciesIndex;
    }

    /// <summary>
    /// How the greenhouse is populated. Edit this asset, then run BioDome > Plants > Populate Planting Zones.
    /// Plant counts come from bed size and spacing, never from a fixed number per bed.
    /// </summary>
    [CreateAssetMenu(fileName = "PlantPopulationSettings", menuName = "BioDome/Plant Population Settings", order = 3)]
    public class PlantPopulationSettings : ScriptableObject
    {
        [Header("Density and spacing")]
        public DensityPreset densityPreset = DensityPreset.Medium;
        [Range(0.5f, 1.6f), Tooltip("Multiplies the density of any preset. 1.2 plants roughly 20% more tightly.")]
        public float plantDensity = 1f;
        [Min(0.3f), Tooltip("Spacing as a multiple of the species' mature canopy width (Custom preset only).")]
        public float customSpacingFactor = 0.9f;
        [Min(0f), Tooltip("Fixed spacing in metres between plants. 0 = work it out from the canopy width.")]
        public float spacingOverrideMetres = 0f;
        [Min(1), Tooltip("Performance protection: if the layout would exceed this, spacing is widened until it fits.")]
        public int maxPlantCount = 400;

        [Header("Initial growth")]
        [Range(0.05f, 0.3f), Tooltip("Growth every plant starts with (fraction of maturity). All plants are introduced together, so this is the same for all.")]
        public float initialGrowth01 = 0.12f;

        [Header("Natural variation (never makes a plant look older)")]
        public bool randomRotation = true;
        [Range(0f, 0.2f), Tooltip("Plus or minus scale variation, e.g. 0.06 = 6%.")]
        public float sizeVariation = 0.06f;
        [Range(0f, 0.15f), Tooltip("Position jitter as a fraction of spacing (kept small so plants never collide).")]
        public float positionJitter = 0.12f;
        public bool staggerRows = true;
        public int seed = 7;

        [Header("Layout")]
        [Range(0f, 0.6f), Tooltip("Distance kept clear at bed edges, as a fraction of the mature canopy width, so canopies stay off the paths.")]
        public float edgeMarginFactor = 0.3f;
        [Range(1, 3), Tooltip("Mesh detail of placed plants. 1 keeps hundreds of plants affordable.")]
        public int meshDetail = 1;

        [Header("Species and botanical zones")]
        public List<PlantSpeciesEntry> species = new List<PlantSpeciesEntry>();
        public List<ZoneRule> zoneRules = new List<ZoneRule>();

        /// <summary>Spacing as a multiple of canopy width for the chosen preset.</summary>
        public float SpacingFactor
        {
            get
            {
                switch (densityPreset)
                {
                    case DensityPreset.Low: return 1.25f;
                    case DensityPreset.Medium: return 0.9f;
                    case DensityPreset.High: return 0.75f;
                    default: return Mathf.Max(0.3f, customSpacingFactor);
                }
            }
        }

        /// <summary>Distance between plants for a species with the given mature canopy width.</summary>
        public float ComputeSpacing(float matureCanopyWidth)
        {
            if (spacingOverrideMetres > 0f) return spacingOverrideMetres;
            return Mathf.Max(0.3f, matureCanopyWidth * SpacingFactor / Mathf.Max(0.1f, plantDensity));
        }

        /// <summary>The species planted in a zone, or null if no rule matches or the species is not set up.</summary>
        public PlantSpeciesEntry ResolveSpecies(string zoneName)
        {
            for (int i = 0; i < zoneRules.Count; i++)
            {
                ZoneRule rule = zoneRules[i];
                if (rule == null || string.IsNullOrEmpty(rule.zonePrefix)) continue;
                if (!zoneName.StartsWith(rule.zonePrefix, StringComparison.Ordinal)) continue;
                if (rule.speciesIndex < 0 || rule.speciesIndex >= species.Count) return null;
                return species[rule.speciesIndex];
            }
            return null;
        }

        private void OnValidate()
        {
            maxPlantCount = Mathf.Max(1, maxPlantCount);
            customSpacingFactor = Mathf.Max(0.3f, customSpacingFactor);
            for (int i = 0; i < species.Count; i++)
            {
                if (species[i] != null) species[i].variantCount = Mathf.Clamp(species[i].variantCount, 1, 8);
            }
        }
    }
}
