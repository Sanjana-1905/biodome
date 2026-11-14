using System.Collections.Generic;
using BioDome.Irrigation;
using BioDome.Plants;
using UnityEngine;

namespace BioDome.Environment
{
    /// <summary>
    /// A bed's plants plus the bed's own irrigation controller. The controller's plant list is runtime-only, so the bed registers its
    /// plants at Start. The GreenhouseManager skips these plants for the main irrigation (they are flagged UsesOwnIrrigation).
    /// </summary>
    public class BedIrrigationGroup : MonoBehaviour
    {
        [SerializeField] private IrrigationController irrigation;
        [SerializeField] private List<PlantController> plants = new List<PlantController>();

        public IrrigationController Irrigation => irrigation;

        private void Start()
        {
            if (irrigation == null) return;
            for (int i = 0; i < plants.Count; i++) irrigation.RegisterPlant(plants[i]);
        }
    }
}
