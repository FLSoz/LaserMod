using UnityEngine;
using System;

namespace LaserMod.src
{
    [Serializable]
    public class LaserModPatch : MonoBehaviour
    {
        [SerializeField]
        public PatchProps props;

        // Will happen after all other changes
        private void PrePool()
        {
            // setup default template
            if (props.template == null)
            {
                props.template = IngressPoint.defaultTemplate;
            }
            if (props.dpsMultiplier == 0.0f)
            {
                props.dpsMultiplier = 1.0f;
            }
            if (props.widthMultiplier == 0.0f)
            {
                props.widthMultiplier = 1.0f;
            }
            // IngressPoint.LowLevelApplyPatch(props, base.gameObject);
        }
    }
}
