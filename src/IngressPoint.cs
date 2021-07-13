using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using System.IO;
using Nuterra.BlockInjector;


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
    }

    sealed class IngressPoint
    {
        internal static string asm_path = Assembly.GetExecutingAssembly().Location.Replace("LaserMod.dll", "");
        internal static string assets_path = Path.Combine(asm_path, "Assets");
        public static bool hitscan = false;

        public static BeamWeapon defaultTemplate;

        internal static bool initialized = false;

        private readonly static FieldInfo m_LifeTime = typeof(Projectile).GetField("m_LifeTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_Damage = typeof(WeaponRound).GetField("m_Damage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_DamageType = typeof(WeaponRound).GetField("m_DamageType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_Range = typeof(BeamWeapon).GetField("m_Range", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        public readonly static FieldInfo m_DamagePerSecond = typeof(BeamWeapon).GetField("m_DamagePerSecond", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        public readonly static FieldInfo m_FadeOutTime = typeof(BeamWeapon).GetField("m_FadeOutTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_DamageTypeBeam = typeof(BeamWeapon).GetField("m_DamageType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_BeamParticlesPrefab = typeof(BeamWeapon).GetField("m_BeamParticlesPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo m_HitParticlesPrefab = typeof(BeamWeapon).GetField("m_HitParticlesPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static MethodInfo TrimForSafeSearch = typeof(BlockPrefabBuilder).GetMethod("TrimForSafeSearch", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo _gameBlocksIDDict = typeof(BlockPrefabBuilder).GetField("_gameBlocksIDDict", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly static FieldInfo _gameBlocksNameDict = typeof(BlockPrefabBuilder).GetField("_gameBlocksNameDict", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
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

        // Manage the block table for Block Injector to avoid contamination
        internal static void ApplyPatch(PatchProps props)
        {
            BlockPrefabBuilder.GameBlocksByID(props.laserId, out GameObject original);

            string blockName = (string) TrimForSafeSearch.Invoke(null, new object[] { original.name });
            GameObject copy = GameObject.Instantiate(original);

            original.SetActive(false);
            copy.SetActive(false);

            Dictionary<string, GameObject> blockNameDict = (Dictionary<string, GameObject>)_gameBlocksNameDict.GetValue(null);
            Dictionary<int, GameObject> blockIdDict = (Dictionary<int, GameObject>)_gameBlocksIDDict.GetValue(null);

            blockIdDict[props.laserId] = copy;
            blockNameDict[blockName] = copy;

            LowLevelApplyPatch(props, original);
        }

        internal static void LowLevelApplyPatch(PatchProps props, GameObject target)
        {
            LineRenderer sourceRenderer = props.template.GetComponent<LineRenderer>();
            FireData fireData = target.GetComponentInChildren<FireData>();
            ModuleWeaponGun gun = target.GetComponentInChildren<ModuleWeaponGun>();

            Console.WriteLine($"Patching Laser {target.name}");

            float scale = props.widthMultiplier > 0 ? props.widthMultiplier : 1.0f;
            float cooldown = 0.5f;

            if (gun)
            {
                cooldown = gun.m_ShotCooldown;
            }
            if (fireData)
            {
                WeaponRound round = fireData.m_BulletPrefab;
                if (round && round is Projectile projectile)
                {
                    float range = 1000.0f;
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

                            LineRenderer renderer = spawnPoint.gameObject.AddComponent<LineRenderer>();

                            GameObjectJSON.ShallowCopy(typeof(LineRenderer), sourceRenderer, renderer, true);
                            renderer.material = sourceRenderer.material;
                            renderer.sharedMaterial = sourceRenderer.material;
                            renderer.widthMultiplier *= scale;
                            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                            barrel.beamWeapon = spawnPoint.gameObject.AddComponent<BeamWeapon>();

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

                            GameObjectJSON.ShallowCopy(typeof(BeamWeapon), props.template, barrel.beamWeapon, true);

                            m_BeamParticlesPrefab.SetValue(barrel.beamWeapon, null);

                            m_Range.SetValue(barrel.beamWeapon, range);
                            m_DamageTypeBeam.SetValue(barrel.beamWeapon, damageType);
                            m_DamagePerSecond.SetValue(barrel.beamWeapon, -damage);
                            m_FadeOutTime.SetValue(barrel.beamWeapon, Mathf.Min(0.2f, cooldown / 2.5f));
                        }
                    }

                    Console.WriteLine($"Range {range}, Damage {damage}, Fire Rate {1 / cooldown} ({cooldown})");
                    fireData.m_BulletPrefab = null;
                    fireData.m_MuzzleVelocity = 0f;
                }
            }
        }

        public static void PatchBlocks()
        {
            if (!IngressPoint.initialized)
            {
                Console.WriteLine("Patching Laser Weapons");

                BlockPrefabBuilder.GameBlocksByID(1032, out GameObject orangeLaser);
                BeamWeapon orangeBeam = orangeLaser.GetComponentInChildren<BeamWeapon>();
                defaultTemplate = orangeBeam;

                BlockPrefabBuilder.GameBlocksByID(861, out GameObject blueLaser);
                BeamWeapon blueBeam = blueLaser.GetComponentInChildren<BeamWeapon>();

                BlockPrefabBuilder.GameBlocksByID(831, out GameObject laserGatling);
                MuzzleFlash flash = laserGatling.GetComponentInChildren<MuzzleFlash>();

                ParticleSystem hitParticles = (ParticleSystem) m_BeamParticlesPrefab.GetValue(orangeBeam);

                PatchProps[] patches = new PatchProps[15] {
                    // GSO Cab
                    new PatchProps {
                        laserId = 8,
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
                        dpsMultiplier = 1.25f,
                        colorOverride = true,
                        color = new Color(1.0f, 0.3f, 0.3f, 1.0f)
                    },
                    // BF laser rifle
                    new PatchProps {
                        laserId = 821,
                        template = blueBeam,
                        dpsMultiplier = 0.75f,
                        flashOverride = flash,
                        adjustSpawn = new Vector3(0, 0, -0.25f),
                        hitParticlesOverride = hitParticles
                    },
                    // Streamlined assault laser (112)
                    new PatchProps {
                        laserId = 829,
                        template = blueBeam,
                        dpsMultiplier = 0.65f,
                        flashOverride = flash,
                        adjustSpawn = new Vector3(0, 0, -0.25f),
                        adjustMuzzleFlash = new Vector3(0, 0, 0.25f),
                        hitParticlesOverride = hitParticles
                    },
                    // Speed lance laser (113)
                    new PatchProps {
                        laserId = 830,
                        template = blueBeam,
                        dpsMultiplier = 0.65f,
                        flashOverride = flash,
                        adjustSpawn = new Vector3(0, 0, -0.25f),
                        adjustMuzzleFlash = new Vector3(0, 0, 0.5f),
                        hitParticlesOverride = hitParticles
                    },
                    // Gatling laser
                    new PatchProps {
                        laserId = 831,
                        template = blueBeam,
                        dpsMultiplier = 0.5f,
                        hitParticlesOverride = hitParticles
                    },
                    // Trapdoor laser
                    new PatchProps {
                        laserId = 857,
                        template = blueBeam,
                        flashOverride = flash,
                        adjustSpawn = new Vector3(0, 0, -0.75f),
                        hitParticlesOverride = hitParticles
                    },
                    // Dot laser (111)
                    new PatchProps {
                        laserId = 885,
                        template = blueBeam,
                        dpsMultiplier = 0.75f,
                        flashOverride = flash,
                        adjustSpawn = new Vector3(0, 0, -0.25f),
                        hitParticlesOverride = hitParticles
                    },
                    // D class laser
                    new PatchProps {
                        laserId = 886,
                        template = blueBeam,
                        dpsMultiplier = 2.0f,
                        widthMultiplier = 2.0f,
                        flashOverride = flash,
                        adjustSpawn = new Vector3(0, 0.15f, -0.25f),
                        adjustMuzzleFlash = new Vector3(0, 0.15f, 0.25f),
                        hitParticlesOverride = hitParticles
                    },
                    // BF Cab
                    new PatchProps {
                        laserId = 785,
                        template = blueBeam,
                        flashOverride = flash,
                        hitParticlesOverride = hitParticles,
                        // adjustSpawn = new Vector3(0, 0, -0.25f)
                    },
                    // BF AI Cab
                    new PatchProps
                    {
                        laserId = 917,
                        template = blueBeam,
                        flashOverride = flash,
                        hitParticlesOverride = hitParticles,
                    }
                };

                foreach (PatchProps props in patches)
                {
                    ApplyPatch(props);
                }

                IngressPoint.initialized = true;
            }
        }

        public static void Main()
        {
            new Harmony("flsoz.ttmm.lasermod.mod").PatchAll(Assembly.GetExecutingAssembly());
            /* assetBundle = AssetBundle.LoadFromFile(Path.Combine(assets_path, "beams"));
            beamMaterial = new Material(assetBundle.LoadAsset<Material>("BeamMaterial")); */

            PatchBlocks();
        }
    }

    [HarmonyPatch(typeof(BlockLoader), "Register", new Type[1] { typeof(CustomBlock) })]
    public static class PatchBlockRegistration
    {
        public static bool Prefix(ref CustomBlock block)
        {
            LaserModPatch patch = block.Prefab.GetComponentInChildren<LaserModPatch>();
            if (patch)
            {
                PatchProps props = patch.props;
                // setup default template
                if (props.template == null)
                {
                    props.template = IngressPoint.defaultTemplate;
                }
                IngressPoint.LowLevelApplyPatch(props, block.Prefab);
            }
            return true;
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
