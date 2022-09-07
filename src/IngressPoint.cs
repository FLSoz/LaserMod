using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using System.IO;
using BlockChangePatcher;

namespace LaserMod.src
{
    [Serializable]
    public struct PatchProps
    {
        public int laserId;
        public BeamWeapon template;
        public MuzzleFlash flashOverride;
        public ParticleSystem hitParticlesOverride;
        public float widthMultiplier;
        public float dpsMultiplier;
        public Vector3 adjustSpawn;
        public Vector3 adjustMuzzleFlash;
        public bool colorOverride;
        public Color color;
        public float range;
    }

    sealed class IngressPoint
    {
        public static bool hitscan = false;

        public static BeamWeapon defaultTemplate;

        internal static bool initialized = false;

        private readonly static FieldInfo m_LifeTime = typeof(Projectile).GetField("m_LifeTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_Damage = typeof(WeaponRound).GetField("m_Damage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_DamageType = typeof(WeaponRound).GetField("m_DamageType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        internal readonly static FieldInfo m_Range = typeof(BeamWeapon).GetField("m_Range", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        public readonly static FieldInfo m_DamagePerSecond = typeof(BeamWeapon).GetField("m_DamagePerSecond", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        public readonly static FieldInfo m_BeamLine = typeof(BeamWeapon).GetField("m_BeamLine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        public readonly static FieldInfo m_FadeOutTime = typeof(BeamWeapon).GetField("m_FadeOutTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_DamageTypeBeam = typeof(BeamWeapon).GetField("m_DamageType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_BeamParticlesPrefab = typeof(BeamWeapon).GetField("m_BeamParticlesPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_HitParticlesPrefab = typeof(BeamWeapon).GetField("m_HitParticlesPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo recoilAnim = typeof(CannonBarrel).GetField("recoilAnim", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo animState = typeof(CannonBarrel).GetField("animState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_Animator = typeof(ModuleWeaponGun).GetField("m_Animator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);


        internal struct ParticleColorOverride
        {
            public Color color;
            public string name;
        }
        internal static Dictionary<ParticleColorOverride, ParticleSystem> particlePrefabMap = new Dictionary<ParticleColorOverride, ParticleSystem>();

        private static void ChangeColor(ParticleSystem system, Color color)
        {
            ParticleSystem.MainModule main = system.main;
            main.startColor = color;

            ParticleSystem.ColorOverLifetimeModule colorModule = system.colorOverLifetime;
            ParticleSystem.MinMaxGradient gradient = colorModule.color;
            gradient.mode = ParticleSystemGradientMode.Color;
            gradient.color = color;

            ParticleSystemRenderer renderer = system.gameObject.GetComponent<ParticleSystemRenderer>();
            if (renderer)
            {
                renderer.material.color = color;
            }
        }

        internal static void LowLevelApplyPatch(PatchProps props, GameObject target)
        {
            LineRenderer sourceRenderer = props.template.GetComponent<LineRenderer>();
            FireData fireData = target.GetComponentInChildren<FireData>();
            ModuleWeaponGun gun = target.GetComponentInChildren<ModuleWeaponGun>();

            Console.WriteLine($"Patching Laser {target.name}");
            Console.WriteLine($"  props: {props.laserId}, widthMultiplier: {props.widthMultiplier}, dpsMultiplier: {props.dpsMultiplier}, colorOverride: {props.colorOverride}, color: {props.color}, adjustSpawn: {props.adjustSpawn}, adjustMuzzleFlash: {props.adjustMuzzleFlash}");

            float scale = props.widthMultiplier > 0 ? props.widthMultiplier : 1.0f;
            float cooldown = 0.5f;

            if (gun)
            {
                CannonBarrel[] barrels = target.GetComponentsInChildren<CannonBarrel>(true);
                int numBarrels = barrels.Length;
                gun.m_CooldownVariancePct = 0.0f;
                cooldown = gun.m_ShotCooldown;
                float minCooldown = 0.2f;
                // Adjust cooldowns up so that min cooldown is 0.2f
                if (gun.m_BurstShotCount > 0)
                {
                    cooldown += gun.m_BurstCooldown;
                    if (cooldown < minCooldown) {
                        if (gun.m_FireControlMode == ModuleWeaponGun.FireControlMode.Sequenced)
                        {
                            gun.m_ShotCooldown = minCooldown;
                            props.dpsMultiplier *= ((minCooldown / numBarrels * gun.m_BurstShotCount + gun.m_BurstCooldown) / (cooldown / numBarrels * gun.m_BurstShotCount + gun.m_BurstCooldown));
                        }
                        else if (gun.m_FireControlMode == ModuleWeaponGun.FireControlMode.AllAtOnce)
                        {
                            int numCycles = Mathf.CeilToInt(gun.m_BurstShotCount / numBarrels);
                            float newCycleTime = numCycles * minCooldown;
                            float oldCycleTime = (numCycles - 1) * gun.m_ShotCooldown + gun.m_BurstCooldown;

                            gun.m_ShotCooldown = minCooldown * numBarrels;
                            gun.m_BurstCooldown = minCooldown;
                            props.dpsMultiplier *= newCycleTime / oldCycleTime;
                        }
                    }
                }
                else
                {
                    // no burst
                    gun.m_BurstCooldown = 0.0f;
                    if (gun.m_FireControlMode == ModuleWeaponGun.FireControlMode.Sequenced)
                    {
                        if (cooldown < minCooldown)
                        {
                            gun.m_ShotCooldown = minCooldown;
                            props.dpsMultiplier *= minCooldown / cooldown;
                        }
                    }
                    else if (gun.m_FireControlMode == ModuleWeaponGun.FireControlMode.AllAtOnce)
                    {
                        cooldown = gun.m_ShotCooldown / numBarrels;
                        if (cooldown < minCooldown)
                        {
                            gun.m_ShotCooldown = minCooldown * numBarrels;
                            props.dpsMultiplier *= minCooldown / cooldown;
                        }
                    }
                }
                // Console.WriteLine($"  {numBarrels} BARRELS, adjusted to Burst Cooldown {gun.m_BurstCooldown} for {gun.m_BurstShotCount} shots, cooldown {cooldown} ({gun.m_ShotCooldown})");
            }
            if (fireData)
            {
                WeaponRound round = fireData.m_BulletPrefab;
                if (round && round is Projectile projectile)
                {
                    float range = props.range > 0 ? props.range : 1000.0f;
                    float lifetime = (float)m_LifeTime.GetValue(projectile);
                    float speed = fireData.m_MuzzleVelocity;
                    if (lifetime > 0f)
                    {
                        range = speed * lifetime * 1.5f;
                    }
                    else
                    {
                        Rigidbody rbody = projectile.GetComponent<Rigidbody>();
                        if (rbody && rbody.useGravity)
                        {
                            range = 0.75f * speed * speed / Physics.gravity.magnitude;
                        }
                    }

                    int damage = Mathf.FloorToInt((float)((int)m_Damage.GetValue(round)) * (props.dpsMultiplier > 0f ? props.dpsMultiplier : 1.0f));
                    ManDamage.DamageType damageType = (ManDamage.DamageType)m_DamageType.GetValue(round);

                    ParticleSystem copySystem = null;
                    if (props.colorOverride || props.hitParticlesOverride)
                    {
                        ParticleSystem originalSystem = (ParticleSystem)m_HitParticlesPrefab.GetValue(props.template);
                        if (props.hitParticlesOverride)
                        {
                            originalSystem = props.hitParticlesOverride;
                        }

                        if (originalSystem)
                        {
                            ParticleColorOverride colorProps = new ParticleColorOverride { color = props.color, name = originalSystem.name };
                            if (!particlePrefabMap.TryGetValue(colorProps, out ParticleSystem prefab))
                            {
                                Color colorOverride = props.color;
                                if (!props.colorOverride)
                                {
                                    ParticleSystem.MainModule main = originalSystem.main;
                                    colorOverride = main.startColor.color;
                                }

                                copySystem = UnityEngine.Object.Instantiate(originalSystem);
                                copySystem.gameObject.SetActive(false);
                                ChangeColor(copySystem, colorOverride);

                                ParticleSystem.SubEmittersModule subEmitters = copySystem.subEmitters;
                                int subEmittersCount = subEmitters.subEmittersCount;

                                for (int i = 0; i < subEmittersCount; i++)
                                {
                                    ParticleSystem originalSubSystem = subEmitters.GetSubEmitterSystem(i);
                                    ParticleSystem copySubSystem = UnityEngine.Object.Instantiate(originalSubSystem);
                                    ChangeColor(copySystem, colorOverride);
                                    subEmitters.SetSubEmitterSystem(i, copySubSystem);
                                }
                                particlePrefabMap[colorProps] = copySystem;
                            }
                            else
                            {
                                copySystem = prefab;
                            }
                        }
                    }

                    CannonBarrel[] cannonBarrels = target.GetComponentsInChildren<CannonBarrel>();
                    if (cannonBarrels != null && cannonBarrels.Length > 0)
                    {
                        foreach (CannonBarrel barrel in cannonBarrels)
                        {
                            if (cooldown == 0f)
                            {
                                AnimationState animationState = (AnimationState) animState.GetValue(barrel);
                                Animation recoil = (Animation) recoilAnim.GetValue(barrel);
                                if (animationState) {
                                    cooldown = animationState.length / animationState.speed;
                                }
                                else if (recoil != null && recoil.clip != null)
                                {
                                    cooldown = recoil.clip.length;
                                }
                                else
                                {
                                    cooldown = 0.2f;
                                }
                            }

                            // Console.WriteLine("Has CannonBarrel");
                            Transform spawnPoint = barrel.projectileSpawnPoint;
                            if (props.adjustSpawn != null && props.adjustSpawn != Vector3.zero)
                            {
                                spawnPoint.localPosition += props.adjustSpawn;
                            }

                            barrel.beamWeapon = spawnPoint.gameObject.AddComponent<BeamWeapon>();
                            LineRenderer renderer = barrel.beamWeapon.gameObject.AddComponent<LineRenderer>();

                            ShallowCopy(typeof(LineRenderer), sourceRenderer, renderer, true);
                            renderer.material = sourceRenderer.material;
                            renderer.sharedMaterial = sourceRenderer.material;
                            renderer.widthMultiplier *= scale;
                            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            m_BeamLine.SetValue(barrel.beamWeapon, renderer);

                            if (copySystem != null)
                            {
                                m_HitParticlesPrefab.SetValue(barrel.beamWeapon, copySystem);
                            }

                            if (props.colorOverride)
                            {
                                renderer.startColor = props.color;
                                renderer.endColor = props.color;
                            }

                            if (props.flashOverride != null)
                            {
                                MuzzleFlash currentFlash = barrel.muzzleFlash;
                                GameObject newFlash = UnityEngine.Object.Instantiate(props.flashOverride.gameObject);
                                newFlash.transform.parent = currentFlash.transform.parent;
                                newFlash.transform.localEulerAngles = currentFlash.transform.localEulerAngles;
                                newFlash.transform.localPosition = currentFlash.transform.localPosition;

                                if (props.adjustMuzzleFlash != null && props.adjustMuzzleFlash != Vector3.zero)
                                {
                                    newFlash.transform.localPosition += props.adjustMuzzleFlash;
                                }

                                barrel.muzzleFlash = newFlash.GetComponentInChildren<MuzzleFlash>();
                                UnityEngine.GameObject.Destroy(currentFlash.gameObject);
                                newFlash.SetActive(false);
                            }

                            ShallowCopy(typeof(BeamWeapon), props.template, barrel.beamWeapon, true);

                            m_BeamParticlesPrefab.SetValue(barrel.beamWeapon, null);

                            m_Range.SetValue(barrel.beamWeapon, range);
                            m_DamageTypeBeam.SetValue(barrel.beamWeapon, damageType);
                            m_DamagePerSecond.SetValue(barrel.beamWeapon, -damage);
                            // fade out will always take 0.2s, or cooldown / 2.5, whichever is shorter
                            m_FadeOutTime.SetValue(barrel.beamWeapon, Mathf.Min(0.2f, cooldown / 2f));
                        }
                    }

                    // Console.WriteLine($"Range {range}, Damage {damage}, Fire Rate {1 / cooldown} ({cooldown})");
                    fireData.m_BulletPrefab = null;
                    fireData.m_MuzzleVelocity = 0f;
                }
            }
        }

        // copy the shallow copy from BlockInjector
        private static void ShallowCopy(Type sharedType, object source, object target, bool DeclaredVarsOnly)
        {
            var bf = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
            if (DeclaredVarsOnly) bf |= BindingFlags.DeclaredOnly;
            var fields = sharedType.GetFields(bf);
            foreach (var field in fields)
            {
                try
                {
                    field.SetValue(target, field.GetValue(source));
                }
                catch { }
            }
            var props = sharedType.GetProperties(bf);
            foreach (var prop in props)
            {
                try
                {
                    if (prop.CanRead && prop.CanWrite)
                        prop.SetValue(target, prop.GetValue(source), null);
                }
                catch { }
            }
        }

        private static PatchProps GetPropsByFaction(FactionSubTypes corp, LineRenderer laserProjectile, bool useFlashOverrides)
        {
            PatchProps props = new PatchProps
            {
                template = IngressPoint.defaultTemplate,
                widthMultiplier = laserProjectile.endWidth * 2.0f,
                dpsMultiplier = 1.0f,
                colorOverride = true,
                color = laserProjectile.endColor
            };

            switch (corp)
            {
                case FactionSubTypes.GSO:
                    // normal default range
                    // thin green
                    props.dpsMultiplier = 0.75f;
                    props.range = 500.0f;
                    props.template = orangeBeam;
                    props.widthMultiplier = 0.3f;
                    props.color = new Color(64.0f / 255.0f, 1.0f, 134.0f / 255.0f, 1.0f);
                    break;
                case FactionSubTypes.GC:
                    // short default range
                    // thick orange
                    props.template = orangeBeam;
                    props.widthMultiplier = 0.7f;
                    props.range = 450.0f;
                    break;
                case FactionSubTypes.VEN:
                    // short default range
                    // thin orange
                    props.dpsMultiplier = 0.85f;
                    props.template = orangeBeam;
                    props.widthMultiplier = 0.45f;
                    props.range = 400.0f;
                    props.colorOverride = false;
                    break;
                case FactionSubTypes.HE:
                    // long default range
                    // recolor red, make thick
                    props.dpsMultiplier = 1.5f;
                    props.widthMultiplier = 1.0f;
                    props.color = new Color(1.0f, 0.3f, 0.3f, 1.0f);
                    props.range = 750.0f;
                    break;
                case FactionSubTypes.BF:
                    // normal default range
                    props.range = 550.0f;
                    props.template = blueBeam;
                    if (useFlashOverrides)
                    {
                        props.flashOverride = BFFlash;
                    }
                    props.widthMultiplier = 1.0f;
                    props.hitParticlesOverride = BFHitParticles;
                    props.colorOverride = false;
                    break;
                case FactionSubTypes.EXP:
                    // super long default range
                    // thick orange
                    props.template = orangeBeam;
                    props.widthMultiplier = 0.7f;
                    props.range = 1500.0f;
                    props.colorOverride = false;
                    break;
            }

            return props;
        }

        private readonly static FieldInfo m_CurrentSession = AccessTools.Field(typeof(ManMods), "m_CurrentSession");
        private readonly static FieldInfo m_RequestedSession = AccessTools.Field(typeof(ManMods), "m_RequestedSession");
        private readonly static MethodInfo GetCorpIndex = AccessTools.Method(typeof(ManMods), "GetCorpIndex");
        internal static void PatchGenericLaser(BlockMetadata blockData)
        {
            Transform editablePrefab = blockData.blockPrefab;

            FactionSubTypes faction;
            bool useFlashOverrides = true;
            if (blockData.VanillaID > 0)
            {
                faction = Singleton.Manager<ManSpawn>.inst.GetCorporation(blockData.VanillaID);
            }
            else if (blockData.SessionID > 0)
            {
                ModSessionInfo loadingSession = (ModSessionInfo)m_CurrentSession.GetValue(Singleton.Manager<ManMods>.inst);
                ModdedBlockDefinition moddedBlockDefinition = Singleton.Manager<ManMods>.inst.FindModdedAsset<ModdedBlockDefinition>(blockData.BlockID);
                if (blockData.BlockID.StartsWith("BF Plus Additional Block Pack"))
                {
                    useFlashOverrides = false;
                }
                faction = (FactionSubTypes) GetCorpIndex.Invoke(Singleton.Manager<ManMods>.inst, new object[] { moddedBlockDefinition.m_Corporation, loadingSession });
            }
            else // should only be called on modded blocks, but those should be handled by SessionID
            {
                // this is generic, will run 
                string BlockID = blockData.BlockID;
                Console.WriteLine($"FAILED to patch detected laser {BlockID}");
                return;
            }

            // only give one patch
            LaserModPatch patch = editablePrefab.GetComponent<LaserModPatch>();
            if (patch == null)
            {
                Console.WriteLine($"Applied generic laser patch to {editablePrefab.name}, corp: {faction}");
                FireData fireData = editablePrefab.GetComponent<FireData>();
                LineRenderer laserProjectile = fireData.m_BulletPrefab.GetComponentInChildren<LineRenderer>();
                PatchProps props = GetPropsByFaction(faction, laserProjectile, useFlashOverrides);
                patch = editablePrefab.gameObject.AddComponent<LaserModPatch>();
                patch.props = props;
                LowLevelApplyPatch(props, editablePrefab.gameObject);
            }
        }

        internal static BeamWeapon orangeBeam;
        internal static BeamWeapon blueBeam;
        internal static MuzzleFlash BFFlash;
        internal static ParticleSystem BFHitParticles;

        public static void GenerateChanges()
        {
            if (!IngressPoint.initialized)
            {
                Console.WriteLine("Patching Laser Weapons");

                TankBlock orangeLaser = Singleton.Manager<ManSpawn>.inst.GetBlockPrefab((BlockTypes) 1032);
                orangeBeam = orangeLaser.GetComponentInChildren<BeamWeapon>();
                defaultTemplate = orangeBeam;

                TankBlock blueLaser = Singleton.Manager<ManSpawn>.inst.GetBlockPrefab((BlockTypes)861);
                blueBeam = blueLaser.GetComponentInChildren<BeamWeapon>();

                TankBlock laserGatling = Singleton.Manager<ManSpawn>.inst.GetBlockPrefab((BlockTypes)831);
                BFFlash = laserGatling.GetComponentInChildren<MuzzleFlash>();

                BFHitParticles = (ParticleSystem) m_BeamParticlesPrefab.GetValue(orangeBeam);

                // vanilla patches
                PatchProps[] vanillaPatches = new PatchProps[16] {
                    // GSO Cab
                    new PatchProps {
                        laserId = 8,
                        template = orangeBeam,
                        widthMultiplier = 0.25f,
                        colorOverride = true,
                        color = new Color(127.0f / 255.0f, 64.0f / 255.0f, 1.0f, 1.0f)
                    },
                    // GSO Wide Cab
                    new PatchProps {
                        laserId = (int) BlockTypes.GSO_Cab_211,
                        template = orangeBeam,
                        widthMultiplier = 0.25f,
                        colorOverride = true,
                        color = new Color(127.0f / 255.0f, 64.0f / 255.0f, 1.0f, 1.0f)
                    },
                    // GSO coil
                    new PatchProps {
                        laserId = 15,
                        template = orangeBeam,
                        widthMultiplier = 0.25f,
                        dpsMultiplier = 0.5f,
                        colorOverride = true,
                        color = new Color(64.0f / 255.0f, 225.0f / 255.0f, 134.0f / 255.0f, 1.0f)
                    },
                    // GSO stud (Front only)
                    new PatchProps {
                        laserId = 16,
                        template = orangeBeam,
                        widthMultiplier = 0.3f,
                        colorOverride = true,
                        color = new Color(64.0f / 255.0f, 225.0f / 255.0f, 134.0f / 255.0f, 1.0f)
                    },
                    // VEN lance (singular)
                    new PatchProps {
                        laserId = 679,
                        template = orangeBeam,
                        widthMultiplier = 0.45f,
                        dpsMultiplier = 0.8f
                    },
                    // VEN needle (machine gun)
                    new PatchProps {
                        laserId = 680,
                        template = orangeBeam,
                        widthMultiplier = 0.45f,
                        dpsMultiplier = 1.2f
                    },
                    // Zeus laser
                    new PatchProps {
                        laserId = 681,
                        template = orangeBeam,
                        widthMultiplier = 1.0f,
                        dpsMultiplier = 1.5f,
                        colorOverride = true,
                        color = new Color(1.0f, 0.3f, 0.3f, 1.0f)
                    },
                    // BF laser rifle
                    new PatchProps {
                        laserId = 821,
                        template = blueBeam,
                        dpsMultiplier = 0.75f,
                        flashOverride = BFFlash,
                        adjustSpawn = new Vector3(0, 0, -0.25f),
                        hitParticlesOverride = BFHitParticles
                    },
                    // Streamlined assault laser (112)
                    new PatchProps {
                        laserId = 829,
                        template = blueBeam,
                        dpsMultiplier = 0.65f,
                        flashOverride = BFFlash,
                        adjustSpawn = new Vector3(0, 0, -0.1f),
                        adjustMuzzleFlash = new Vector3(0, 0, 0.25f),
                        hitParticlesOverride = BFHitParticles
                    },
                    // Speed lance laser (113)
                    new PatchProps {
                        laserId = (int) BlockTypes.BF_Laser_Streamlined_113,
                        template = blueBeam,
                        dpsMultiplier = 0.65f,
                        flashOverride = BFFlash,
                        adjustSpawn = new Vector3(0, 0, 0.5f),
                        adjustMuzzleFlash = new Vector3(0, 0, 0.5f),
                        hitParticlesOverride = BFHitParticles
                    },
                    // Gatling laser
                    new PatchProps {
                        laserId = 831,
                        template = blueBeam,
                        dpsMultiplier = 0.5f,
                        hitParticlesOverride = BFHitParticles
                    },
                    // Trapdoor laser
                    new PatchProps {
                        laserId = 857,
                        template = blueBeam,
                        flashOverride = BFFlash,
                        adjustSpawn = new Vector3(0, 0, -0.75f),
                        hitParticlesOverride = BFHitParticles
                    },
                    // Dot laser (111)
                    new PatchProps {
                        laserId = 885,
                        template = blueBeam,
                        dpsMultiplier = 0.75f,
                        flashOverride = BFFlash,
                        adjustSpawn = new Vector3(0, 0, -0.25f),
                        hitParticlesOverride = BFHitParticles
                    },
                    // D class laser
                    new PatchProps {
                        laserId = 886,
                        template = blueBeam,
                        dpsMultiplier = 2.0f,
                        widthMultiplier = 2.0f,
                        flashOverride = BFFlash,
                        adjustSpawn = new Vector3(0, 0.15f, -0.25f),
                        adjustMuzzleFlash = new Vector3(0, 0.15f, 0.25f),
                        hitParticlesOverride = BFHitParticles
                    },
                    // BF Cab
                    new PatchProps {
                        laserId = 785,
                        template = blueBeam,
                        flashOverride = BFFlash,
                        hitParticlesOverride = BFHitParticles,
                        // adjustSpawn = new Vector3(0, 0, -0.25f)
                    },
                    // BF AI Cab
                    new PatchProps
                    {
                        laserId = 917,
                        template = blueBeam,
                        flashOverride = BFFlash,
                        hitParticlesOverride = BFHitParticles,
                    }
                };

                foreach (PatchProps props in vanillaPatches)
                {
                    Change change = new Change {
                        id = $"LaserMod {props.laserId}",
                        targetType = ChangeTargetType.VANILLA_ID,
                        condition = new VanillaIDConditional((BlockTypes) props.laserId),
                        patcher = (BlockMetadata blockData) => {
                            Transform blockPrefab = blockData.blockPrefab;
                            LaserModPatch patch = blockPrefab.gameObject.AddComponent<LaserModPatch>();
                            patch.props = props;
                            LowLevelApplyPatch(props, blockPrefab.gameObject);
                        }
                    };
                    LaserMod.changes.Add(change);
                }

                IngressPoint.initialized = true;
            }
        }
    }

    [HarmonyPatch(typeof(BeamWeapon), "OnPool")]
    public static class PatchPool
    {
        public static void Prefix(BeamWeapon __instance)
        {
            LineRenderer renderer = (LineRenderer) IngressPoint.m_BeamLine.GetValue(__instance);
            if (renderer == null)
            {
                renderer = __instance.GetComponent<LineRenderer>();
                if (renderer != null)
                {
                    IngressPoint.m_BeamLine.SetValue(__instance, renderer);
                    renderer.SetPosition(0, Vector3.zero);
                    renderer.SetPosition(1, new Vector3(0f, 0f, (float) IngressPoint.m_Range.GetValue(__instance)));
                }
            }
        }
    }

    [HarmonyPatch(typeof(BeamWeapon), "Update")]
    public static class PatchLaserBeam
    {
        internal readonly static FieldInfo m_FadeTimer = typeof(BeamWeapon).GetField("m_FadeTimer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public struct State {
            public int damage;
            public float fadeTimer;
        }

        public static bool Prefix(ref BeamWeapon __instance, out State __state)
        {
            float fadeTimer = (float)m_FadeTimer.GetValue(__instance);
            int dps = (int) IngressPoint.m_DamagePerSecond.GetValue(__instance);
            __state = new State { damage = dps, fadeTimer = fadeTimer };
            if (dps < 0f)
            {
                int damage = 0;

                float defaultFade = (float)IngressPoint.m_FadeOutTime.GetValue(__instance);
                int properDPS = Mathf.FloorToInt(-dps / Mathf.Max(defaultFade, Time.deltaTime));
                if (fadeTimer == defaultFade)
                {
                    damage = IngressPoint.hitscan ? Mathf.FloorToInt(((float)-dps) / Time.deltaTime) : properDPS;
                }
                else if (fadeTimer > 0f)
                {
                    damage = IngressPoint.hitscan ? 0 : Mathf.FloorToInt(properDPS * Mathf.Min(Time.deltaTime, fadeTimer) / Time.deltaTime);
                    m_FadeTimer.SetValue(__instance, defaultFade);
                }
                IngressPoint.m_DamagePerSecond.SetValue(__instance, damage);
            }
            return true;
        }

        public static void Postfix(ref BeamWeapon __instance, State __state)
        {
            IngressPoint.m_DamagePerSecond.SetValue(__instance, __state.damage);
            if (__state.fadeTimer < 0f)
            {
                m_FadeTimer.SetValue(__instance, 0f);
            }
            else
            {
                m_FadeTimer.SetValue(__instance, __state.fadeTimer - Time.deltaTime);
            }
        }
    }
}
