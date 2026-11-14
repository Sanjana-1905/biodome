using System;
using System.Collections.Generic;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>Vertical layer of the planting. Taller layers are placed first so they get the best spots.</summary>
    public enum PlantLayer
    {
        /// <summary>Tall statement plants (palms, banana).</summary>
        Statement = 0,
        Canopy = 1,
        /// <summary>Medium foliage.</summary>
        Mid = 2,
        /// <summary>Ferns and small foliage.</summary>
        Low = 3,
        /// <summary>Moss and ground cover, placed last into the gaps.</summary>
        Ground = 4
    }

    /// <summary>Where inside a bed a species prefers to go.</summary>
    public enum PlacementBias
    {
        Anywhere = 0,
        /// <summary>The side of the bed away from the central aisle.</summary>
        BackRow = 1,
        /// <summary>The side of the bed next to the central aisle.</summary>
        FrontRow = 2,
        Corners = 3,
        Edges = 4,
        Center = 5
    }

    /// <summary>
    /// One species to ADD to a few chosen planting zones, on top of whatever already grows there.
    /// </summary>
    [Serializable]
    public class PlantAdditionEntry
    {
        [Tooltip("Species to add: prefab (with a PlantGenerator and PlantController), PlantData, and how many looks to make.")]
        public PlantSpeciesEntry species = new PlantSpeciesEntry();

        [Tooltip("Planting zones to add to, by zone name (the PlantingZone's Zone Name). A trailing * matches a prefix, e.g. 'Bed_N1_*'. Zones that are not listed are never touched.")]
        public List<string> zoneNames = new List<string>();

        [Min(0), Tooltip("Most plants (or patches) of this species added to each listed zone. Fewer are added if there is no room.")]
        public int maxPerZone = 2;

        [Tooltip("Placement order and role. Tall layers are placed first; ground cover fills the gaps last.")]
        public PlantLayer layer = PlantLayer.Mid;

        [Tooltip("Where in each bed this species prefers to go.")]
        public PlacementBias bias = PlacementBias.Anywhere;

        [Min(0f), Tooltip("Minimum distance in metres from the plants already in the scene (the originals and earlier additions).")]
        public float clearanceFromExisting = 0.9f;

        [Min(0f), Tooltip("Minimum distance in metres between two plants of this species added by this tool.")]
        public float minSpacingBetweenNew = 1.4f;

        [Range(0.2f, 1.5f), Tooltip("Footprint radius as a fraction of half the mature canopy width. Used to stop different species piling on top of each other.")]
        public float radiusScale = 0.7f;

        [Range(0f, 1f), Tooltip("How much this species may overlap the footprints of others: 0 = none, 1 = may sit right under them (moss and ground cover).")]
        public float overlapAllowance = 0.45f;

        [Min(0f), Tooltip("Distance in metres kept clear of the zone's edges (keeps plants off paths).")]
        public float edgeMargin = 0.4f;

        [Range(0f, 0.25f), Tooltip("Plus or minus scale variation, e.g. 0.08 = 8%. Applied on top of the shared initial growth.")]
        public float sizeVariation = 0.08f;

        public bool randomRotation = true;
    }

    /// <summary>
    /// Species to add to selected zones WITHOUT clearing or regenerating the existing population.
    /// Edit this asset (or use BioDome > Plants > Plant Additions Window), then add the species.
    /// Added plants live under their own root (BioDome_PlantAdditions), separate from BioDome_Plants.
    /// </summary>
    [CreateAssetMenu(fileName = "PlantAdditionPlan", menuName = "BioDome/Plant Addition Plan", order = 4)]
    public class PlantAdditionPlan : ScriptableObject
    {
        [Tooltip("Seed for placement. The same seed and settings give the same result every time.")]
        public int seed = 21;

        [Min(1), Tooltip("Safety limit: the tool stops adding once this many plants have been added in total (earlier additions count).")]
        public int maxTotalAdded = 450;

        public List<PlantAdditionEntry> additions = new List<PlantAdditionEntry>();

        /// <summary>True if <paramref name="zoneName"/> matches an entry's zone list (exact name, or prefix when the pattern ends with *).</summary>
        public static bool Matches(PlantAdditionEntry entry, string zoneName)
        {
            if (entry == null || entry.zoneNames == null || string.IsNullOrEmpty(zoneName)) return false;
            for (int i = 0; i < entry.zoneNames.Count; i++)
            {
                string pattern = entry.zoneNames[i];
                if (string.IsNullOrEmpty(pattern)) continue;
                if (pattern.EndsWith("*", StringComparison.Ordinal))
                {
                    if (zoneName.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal)) return true;
                }
                else if (string.Equals(pattern, zoneName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private void OnValidate()
        {
            maxTotalAdded = Mathf.Max(1, maxTotalAdded);
            for (int i = 0; i < additions.Count; i++)
            {
                PlantAdditionEntry e = additions[i];
                if (e == null) continue;
                e.maxPerZone = Mathf.Max(0, e.maxPerZone);
                if (e.species != null) e.species.variantCount = Mathf.Clamp(e.species.variantCount, 1, 8);
            }
        }
    }
}
