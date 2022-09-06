using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using BlockChangePatcher;
using UnityEngine;

namespace LaserMod.src
{
    public class LaserMod : ModBase
    {
        internal const string HarmonyID = "com.flsoz.ttmodding.lasermod";
        internal static Harmony harmony = new Harmony(HarmonyID);

        public static Type[] LoadBefore()
        {
            return new Type[] { typeof(BlockChangePatcherMod) };
        }

        internal static List<Change> changes = new List<Change>();

        private class LaserConditional : CustomConditional
        {
            private static BlockTypes[] VanillaBlacklist = new BlockTypes[] {
                BlockTypes.HE_RailGunTurret_213,
                BlockTypes.HE_RailGun_113,
                BlockTypes.HE_RailGun_Quadruple_535,
                BlockTypes.HE_RailGun_XboxOne_113
            };

            private static string[] ModdedBlacklist = new string[]
            {
                "GreenTech Corporation:GT_Blaze_Cannon"
            };

            public override bool Validate(BlockMetadata blockData)
            {
                if (VanillaBlacklist.Contains(blockData.VanillaID) || ModdedBlacklist.Contains(blockData.BlockID))
                {
                    return false;
                }
                Transform target = blockData.blockPrefab;
                ModuleWeaponGun moduleWeaponGun = target.GetComponent<ModuleWeaponGun>();
                if (moduleWeaponGun)
                {
                    FireData fireData = target.GetComponent<FireData>();
                    if (fireData.m_BulletCasingPrefab == null && fireData.m_BulletPrefab is LaserProjectile)
                    {
                        LineRenderer laserProjectile = fireData.m_BulletPrefab?.GetComponentInChildren<LineRenderer>();
                        if (laserProjectile != null)
                        {
                            LaserModPatch patch = target.GetComponent<LaserModPatch>();
                            if (patch != null)
                            {
                                return false;
                            }
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        private static bool Inited = false;
        public override void EarlyInit()
        {
            if (!Inited)
            {
                IngressPoint.GenerateChanges();
                changes.Add(new Change
                {
                    id = "LaserMod Generic",
                    targetType = ChangeTargetType.TRANSFORM,
                    condition = new LaserConditional(),
                    patcher = new Action<BlockMetadata>(IngressPoint.PatchGenericLaser)
                });
                Inited = true;
            }
        }

        public override bool HasEarlyInit()
        {
            return true;
        }

        public override void DeInit()
        {
            harmony.UnpatchAll(HarmonyID);
        }

        public override void Init()
        {
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            foreach (Change change in changes)
            {
                BlockChangePatcherMod.RegisterChange(change);
            }
        }
    }
}
