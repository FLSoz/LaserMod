using UnityEngine;
using System;

namespace LaserMod.src
{
    [Serializable()]
    public class LaserModPatch : MonoBehaviour
    {
        [SerializeField]
        public PatchProps props;

        public bool patched;
    }
}
