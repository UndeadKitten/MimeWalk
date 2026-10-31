// Builds MimeWalk.unitypackage. This file only makes the package and isn't included in it,
// so the package holds only assets and users have no code to compile.
//
// Run it from Tools > MimeWalk (dev) > Build Package, or with tools/build_unitypackage.ps1.
// It makes everything in Assets/MimeWalk, tests the step animation, and saves dist/MimeWalk.unitypackage.
using System;
using System.IO;
using System.Linq;
using com.vrcfury.api;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;
using Control = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;

namespace MimeWalkBuild {
    public static class BuildPackage {
        const string PackageDir = "Assets/MimeWalk/";
        const string TempAsset = "Assets/MimeWalkBuild/_new"; // new assets are made here, then copied into PackageDir
        const string ParamPrefix = "MimeWalk/";
        const float LightSize = 0.04f; // light width in metres

        static readonly string[] Prefabs = {
            PackageDir + "MimeWalk (Modular Avatar).prefab",
            PackageDir + "MimeWalk (VRCFury).prefab",
        };

        // The settings shared by the menu, the app and the avatar. IsFloat means a 0 to 1 dial instead of on/off.
        // All are local only, so they don't use any synced parameter space.
        static readonly (string Name, bool IsFloat, float Default, bool Saved)[] Params = {
            ("On", false, 0, false),       // Walk toggle (menu -> app)
            ("Feedback", false, 0, true),  // light and click toggle (menu -> avatar)
            ("BPM", true, 0.4375f, true),  // Tempo dial; 0.4375 = 110 steps a minute (menu -> app)
            ("Surge", true, 0.4f, true),   // Surge dial; 0.4 = 20% (menu -> app)
            ("Speed", true, 0.5f, true),   // Speed dial (menu -> app)
            ("Beat", false, 0, false),     // flips on every step; true = left foot (app -> avatar)
        };

        // Made in Generate, then used to build the prefabs.
        static Material lightLeft, lightRight;
        static AudioClip tick, tock;
        static AnimatorController fx;
        static VRCExpressionsMenu menu;
        static VRCExpressionParameters parameters;

        [MenuItem("Tools/MimeWalk (dev)/Build Package")]
        public static void Run() => Guard(() => {
            Generate();
            CheckPrefabs();
            Export();
        });

        // Used by tools/build_unitypackage.ps1 -ReimportTest, in a fresh project that has just imported the package.
        public static void CheckImport() => Guard(() => {
            fx = AssetDatabase.LoadAssetAtPath<AnimatorController>(PackageDir + "MimeWalk_FX.controller");
            CheckPrefabs();
        });

        // Logs OK or FAILED. When run from a script, also tells the script whether it worked.
        static void Guard(Action build) {
            var exitCode = 0;
            try {
                build();
                Debug.Log("[MimeWalk] OK");
            } catch (Exception e) {
                Debug.LogError("[MimeWalk] FAILED: " + e);
                exitCode = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        // --- Assets ---

        static void Generate() {
            var light = MakeLightTexture();
            lightLeft = Save(MakeLightMaterial(light, new Color(0.15f, 0.45f, 1f)), PackageDir + "MimeWalk_LightL.mat");  // blue
            lightRight = Save(MakeLightMaterial(light, new Color(1f, 0.15f, 0.1f)), PackageDir + "MimeWalk_LightR.mat"); // red
            tick = LoadClick("MimeWalk_Tick.wav");
            tock = LoadClick("MimeWalk_Tock.wav");

            fx = Save(MakeController(), PackageDir + "MimeWalk_FX.controller");
            parameters = Save(MakeParameters(), PackageDir + "MimeWalk_Parameters.asset");
            menu = MakeMenu();

            MakePrefab(Prefabs[0], AddModularAvatar);
            MakePrefab(Prefabs[1], AddVRCFury);
            AssetDatabase.SaveAssets();
        }

        // A white dot inside a black ring, clear outside. Each material tints the dot its own colour.
        static Texture2D MakeLightTexture() {
            const string path = PackageDir + "MimeWalk_Light.png";
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var y = 0; y < size; y++) {
                for (var x = 0; x < size; x++) {
                    // Distance from the centre: 0 in the middle, 1 at the edge.
                    var r = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                    var white = Mathf.Clamp01((0.6f - r) * 16f + 0.5f);  // white in the dot, black in the ring
                    var opaque = Mathf.Clamp01((0.9f - r) * 16f + 0.5f); // solid in the dot and ring, clear outside
                    texture.SetPixel(x, y, new Color(white, white, white, opaque));
                }
            }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            // No mipmaps: the light is always close up, and the VRChat SDK warns about them.
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Uses Poiyomi Toon 10 or newer (free or Pro), set up the same way its own menus would:
        // - Cutout: only the dot and its ring show.
        // - Emission: the light looks the same in any world.
        // - Mirror/Camera Visibility: it shows only in your own view, not in mirrors, cameras or photos.
        // Other players never see it anyway, because the animation only turns it on for you.
        static Material MakeLightMaterial(Texture texture, Color color) {
            var shader = Shader.Find(".poiyomi/Poiyomi Toon");
            if (shader == null) throw new Exception("Poiyomi Toon shader not found (install Poiyomi 10 or newer)");

            var material = new Material(shader) { renderQueue = 2450 }; // same draw order as Poiyomi's Cutout preset
            material.SetOverrideTag("RenderType", "TransparentCutout");
            var settings = new (string Name, float Value)[] {
                ("_Mode", 1), ("_AlphaForceOpaque", 0), ("_Cutoff", 0.5f),        // Cutout
                ("_Cull", 0),                                                      // visible from both sides
                ("_EnableEmission", 1), ("_EmissionStrength", 1),                  // Emission
                ("_EnableMirrorOptions", 1), ("_VisibilityMode", 1),               // Mirror/Camera Visibility:
                ("_VisibilityVRCRegular", 1),                                      //   your view: shown
                ("_VisibilityVRCMirrorVR", 0), ("_VisibilityVRCMirrorDesktop", 0), //   mirrors: hidden
                ("_VisibilityVRCCameraVR", 0), ("_VisibilityVRCCameraDesktop", 0), //   VRChat camera: hidden
                ("_VisibilityVRCCameraScreenshot", 0),                             //   photos: hidden
            };
            foreach (var (name, value) in settings) material.SetFloat(name, value);
            // Poiyomi's menus also turn these on with the settings above.
            material.EnableKeyword("_EMISSION");
            material.EnableKeyword("POI_MIRROR");

            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", color);
            material.SetTexture("_EmissionMap", texture);
            material.SetColor("_EmissionColor", color);
            return material;
        }

        static AudioClip LoadClick(string fileName) {
            var path = PackageDir + "Audio/" + fileName;
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
                throw new Exception("Missing " + path + " (run tools/make_sounds.py)");
            importer.loadInBackground = true; // the VRChat SDK warns without this
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // The step animation. It starts Off, moves to Ready when Walk and Feedback are on (for you only, not
        // other players), then plays Left or Right each time Beat flips. Turning Walk or Feedback off goes back to Off.
        static AnimatorController MakeController() {
            var off = Save(MakeClip(left: false, right: false), PackageDir + "MimeWalk_Off.anim");
            var flashLeft = Save(MakeClip(left: true, right: false), PackageDir + "MimeWalk_FlashLeft.anim");
            var flashRight = Save(MakeClip(left: false, right: true), PackageDir + "MimeWalk_FlashRight.anim");

            var controller = AnimatorController.CreateAnimatorControllerAtPath(TempAsset + ".controller");
            var layers = controller.layers;
            layers[0].name = layers[0].stateMachine.name = "MimeWalk Feedback";
            layers[0].defaultWeight = 1; // full strength; 0 would mute it once it's added to the avatar
            controller.layers = layers;
            controller.AddParameter("IsLocal", AnimatorControllerParameterType.Bool); // VRChat sets this true only for you
            foreach (var p in Params) {
                var type = p.IsFloat ? AnimatorControllerParameterType.Float : AnimatorControllerParameterType.Bool;
                controller.AddParameter(ParamPrefix + p.Name, type);
            }

            var states = layers[0].stateMachine;
            // x and y place the state in Unity's Animator window.
            AnimatorState AddState(string name, Motion motion, float x, float y) {
                var state = states.AddState(name, new Vector3(x, y));
                state.motion = motion;
                state.writeDefaultValues = false;
                return state;
            }
            var offState = AddState("Off", off, 300, 0);
            var ready = AddState("Ready", off, 300, 100);
            var leftStep = AddState("Left", flashLeft, 150, 200);
            var rightStep = AddState("Right", flashRight, 450, 200);
            states.defaultState = offState;

            When(offState.AddTransition(ready), ("On", true), ("Feedback", true), ("IsLocal", true));
            When(ready.AddTransition(leftStep), ("Beat", true));
            When(leftStep.AddTransition(rightStep), ("Beat", false));
            When(rightStep.AddTransition(leftStep), ("Beat", true));
            foreach (var param in new[] { "On", "Feedback" }) {
                var toOff = states.AddAnyStateTransition(offState);
                toOff.canTransitionToSelf = false;
                When(toOff, (param, false));
            }
            return controller;
        }

        // Makes a transition happen instantly once all its conditions are met.
        // Our parameter names get the MimeWalk/ prefix; VRChat's IsLocal doesn't.
        static void When(AnimatorStateTransition transition, params (string Param, bool Value)[] conditions) {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0;
            foreach (var (param, value) in conditions) {
                var name = param == "IsLocal" ? param : ParamPrefix + param;
                transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, name);
            }
        }

        // Makes one animation. For each foot that's on, the light grows and shrinks back to nothing within a
        // quarter of a second, and the click object turns on, which plays its sound once.
        static AnimationClip MakeClip(bool left, bool right) {
            var clip = new AnimationClip();
            var flash = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.03f, LightSize),
                new Keyframe(0.12f, LightSize), new Keyframe(0.25f, 0));
            var hidden = AnimationCurve.Constant(0, 1 / 60f, 0);
            foreach (var (side, on) in new[] { ("L", left), ("R", right) }) {
                foreach (var axis in "xyz") {
                    var scale = EditorCurveBinding.FloatCurve("Head/Visual/Light" + side, typeof(Transform), "m_LocalScale." + axis);
                    AnimationUtility.SetEditorCurve(clip, scale, on ? flash : hidden);
                }
                var active = EditorCurveBinding.FloatCurve("Head/Audio/Click" + side, typeof(GameObject), "m_IsActive");
                AnimationUtility.SetEditorCurve(clip, active, AnimationCurve.Constant(0, 1 / 60f, on ? 1 : 0));
            }
            return clip;
        }

        // The Expression Parameters asset, for the VRCFury prefab.
        static VRCExpressionParameters MakeParameters() {
            var asset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            asset.parameters = Params.Select(p => new VRCExpressionParameters.Parameter {
                name = ParamPrefix + p.Name,
                valueType = p.IsFloat ? VRCExpressionParameters.ValueType.Float : VRCExpressionParameters.ValueType.Bool,
                defaultValue = p.Default,
                saved = p.Saved,
                networkSynced = false,
            }).ToArray();
            return asset;
        }

        // A MimeWalk submenu, added to the avatar's main menu.
        static VRCExpressionsMenu MakeMenu() {
            var submenu = Save(Menu(
                Toggle("Walk", "On"),
                Toggle("Feedback", "Feedback"),
                Radial("Tempo", "BPM"),
                Radial("Surge", "Surge"),
                Radial("Speed", "Speed")), PackageDir + "MimeWalk_Submenu.asset");
            var root = Menu(new Control { name = "MimeWalk", type = Control.ControlType.SubMenu, subMenu = submenu });
            return Save(root, PackageDir + "MimeWalk_Menu.asset");
        }

        static VRCExpressionsMenu Menu(params Control[] controls) {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = controls.ToList();
            return menu;
        }

        static Control Toggle(string label, string param) => new() {
            name = label,
            type = Control.ControlType.Toggle,
            value = 1,
            parameter = new Control.Parameter { name = ParamPrefix + param },
        };

        static Control Radial(string label, string param) => new() {
            name = label,
            type = Control.ControlType.RadialPuppet,
            parameter = new Control.Parameter { name = "" },
            subParameters = new[] { new Control.Parameter { name = ParamPrefix + param } },
        };

        // --- Prefabs ---

        // Both prefabs have the same objects. Only the tool that adds them to the avatar differs.
        //   MimeWalk
        //     Head              follows your head
        //       Visual          the light: delete for sound only, or move it to move the light
        //         LightL, LightR
        //       Audio           the click: delete for light only
        //         ClickL, ClickR
        static void MakePrefab(string path, Action<GameObject, GameObject> addTool) {
            var root = new GameObject("MimeWalk");
            var head = Child(root, "Head", Vector3.zero);
            // VRChat shrinks your head in first person. Head Chop at scale 1 keeps the light full size.
            head.AddComponent<VRCHeadChop>().targetBones = new[] {
                new VRCHeadChop.HeadChopBone {
                    transform = head.transform,
                    scaleFactor = 1,
                    applyCondition = VRCHeadChop.HeadChopBone.ApplyCondition.AlwaysApply,
                },
            };
            AddLights(head);
            AddClicks(head);

            addTool(root, head);
            PrefabUtility.SaveAsPrefabAsset(root, path, out var saved);
            Object.DestroyImmediate(root);
            if (!saved) throw new Exception("Could not save " + path);
        }

        static void AddLights(GameObject head) {
            var visual = Child(head, "Visual", new Vector3(0, 0.05f, 0.35f)); // about 30 cm in front of your eyes
            foreach (var (name, x, material) in new[] { ("LightL", -0.03f, lightLeft), ("LightR", 0.03f, lightRight) }) {
                var light = Child(visual, name, new Vector3(x, 0, 0));
                light.transform.localScale = Vector3.zero; // hidden until the animation flashes it
                light.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                var renderer = light.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        static void AddClicks(GameObject head) {
            var audio = Child(head, "Audio", Vector3.zero);
            foreach (var (name, clip) in new[] { ("ClickL", tick), ("ClickR", tock) }) {
                var click = Child(audio, name, Vector3.zero);
                click.SetActive(false); // the animation turns it on each step, which plays the click

                var source = click.AddComponent<AudioSource>();
                source.clip = clip;
                source.playOnAwake = true;
                source.loop = false;
                source.volume = 1;
                source.spatialBlend = 1; // 3D, coming from your head
                source.spatialize = true;
                source.dopplerLevel = 0;

                var spatial = click.AddComponent<VRCSpatialAudioSource>();
                spatial.EnableSpatialization = true;
                spatial.UseAudioSourceVolumeCurve = false;
                spatial.Gain = 10; // VRChat's default and maximum for avatars
                spatial.Near = 1;  // full volume within 1 m
                spatial.Far = 5;   // silent beyond 5 m
                spatial.VolumetricRadius = 0;
            }
        }

        static void AddModularAvatar(GameObject root, GameObject head) {
            var merge = root.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = fx;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            merge.matchAvatarWriteDefaults = true;

            // Keep the names exactly as they are (not internal or renamed), because the app uses them.
            root.AddComponent<ModularAvatarParameters>().parameters = Params.Select(p => new ParameterConfig {
                nameOrPrefix = ParamPrefix + p.Name,
                remapTo = "",
                internalParameter = false,
                localOnly = true,
                syncType = p.IsFloat ? ParameterSyncType.Float : ParameterSyncType.Bool,
                defaultValue = p.Default,
                hasExplicitDefaultValue = true,
                saved = p.Saved,
            }).ToList();

            root.AddComponent<ModularAvatarMenuInstaller>().menuToAppend = menu;

            // Attach to the head bone without taking on the bone's own rotation.
            var proxy = head.AddComponent<ModularAvatarBoneProxy>();
            proxy.boneReference = HumanBodyBones.Head;
            proxy.subPath = "";
            proxy.attachmentMode = BoneProxyAttachmentMode.AsChildKeepRotation;
        }

        static void AddVRCFury(GameObject root, GameObject head) {
            var controller = FuryComponents.CreateFullController(root);
            controller.AddController(fx, VRCAvatarDescriptor.AnimLayerType.FX);
            controller.AddMenu(menu);
            controller.AddParams(parameters);
            controller.AddGlobalParam("*"); // keep all names as they are, because the app uses them

            var link = FuryComponents.CreateArmatureLink(head);
            link.LinkTo(HumanBodyBones.Head);
            link.SetRecursive(false);
            // Match the Modular Avatar prefab: line up the position only, not the rotation. VRCFury's API can't
            // set this, so set it directly on the link, which is the last component added to Head.
            var settings = new SerializedObject(head.GetComponents<MonoBehaviour>().Last());
            settings.FindProperty("content.alignPosition").boolValue = true;
            settings.FindProperty("content.alignRotation").boolValue = false;
            settings.FindProperty("content.alignScale").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject Child(GameObject parent, string name, Vector3 localPosition) {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = localPosition;
            return child;
        }

        // Saves the asset to path, keeping the existing file's ID. That way a new version of the package
        // updates the files in people's projects instead of adding copies next to them.
        static T Save<T>(T asset, string path) where T : Object {
            var tempPath = AssetDatabase.GetAssetPath(asset);
            if (tempPath == "") AssetDatabase.CreateAsset(asset, tempPath = TempAsset + Path.GetExtension(path));
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.SaveAssets();
            File.Copy(tempPath, path, true);
            AssetDatabase.DeleteAsset(tempPath);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<T>(path);
            if (saved == null) throw new Exception("Could not save " + path);
            return saved;
        }

        // --- Checks ---

        // Checks both prefabs: no missing scripts, the lights use Poiyomi, and the step animation works.
        static void CheckPrefabs() {
            foreach (var path in Prefabs) {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new Exception("Could not load " + path);

                var missingScripts = prefab.GetComponentsInChildren<Transform>(true)
                    .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                if (missingScripts > 0) throw new Exception(path + " has " + missingScripts + " missing scripts");

                foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true)) {
                    var material = renderer.sharedMaterial;
                    if (material == null || material.shader.name != ".poiyomi/Poiyomi Toon" || !material.IsKeywordEnabled("POI_MIRROR"))
                        throw new Exception(path + ": " + renderer.name + " must use Poiyomi Toon with Mirror/Camera Visibility on");
                }

                Simulate(prefab, isLocal: true, feedback: true, expected: "LRL-"); // the last step comes after Walk is off
                Simulate(prefab, isLocal: true, feedback: false, expected: "----");
                Simulate(prefab, isLocal: false, feedback: true, expected: "----"); // how another player sees you
            }
        }

        // Plays the animation on a copy of the prefab and flips Beat four times, turning Walk off before the
        // last flip. After each flip it notes what's on: L = left light and click, R = right light and click,
        // - = nothing, ? = anything else. A ! means a light was still on after its flash should have ended.
        static void Simulate(GameObject prefab, bool isLocal, bool feedback, string expected) {
            var copy = Object.Instantiate(prefab);
            try {
                var animator = copy.AddComponent<Animator>();
                animator.runtimeAnimatorController = fx;
                animator.Rebind();
                animator.SetBool("IsLocal", isLocal);
                animator.SetBool(ParamPrefix + "On", true);
                animator.SetBool(ParamPrefix + "Feedback", feedback);
                Advance(animator, 0.2f);

                var seen = "";
                for (var step = 0; step < 4; step++) {
                    if (step == 3) animator.SetBool(ParamPrefix + "On", false);
                    animator.SetBool(ParamPrefix + "Beat", step % 2 == 0);
                    Advance(animator, 0.1f);
                    seen += Showing(copy);
                    Advance(animator, 0.3f);
                    if (IsLit(copy, "LightL") || IsLit(copy, "LightR")) seen += "!";
                }

                var description = prefab.name + " (IsLocal " + isLocal + ", Feedback " + feedback + ")";
                if (seen != expected) throw new Exception("Simulation " + description + ": expected " + expected + ", got " + seen);
                Debug.Log("[MimeWalk] Simulation " + description + ": " + seen);
            } finally {
                Object.DestroyImmediate(copy);
            }
        }

        // Runs the animation forward this many seconds, at 60 frames per second.
        static void Advance(Animator animator, float seconds) {
            for (var t = 0f; t < seconds; t += 1 / 60f) animator.Update(1 / 60f);
        }

        static bool IsLit(GameObject avatar, string light) =>
            avatar.transform.Find("Head/Visual/" + light).localScale.x > 0.001f;

        static bool IsPlaying(GameObject avatar, string click) =>
            avatar.transform.Find("Head/Audio/" + click).gameObject.activeSelf;

        static string Showing(GameObject avatar) {
            var left = (IsLit(avatar, "LightL"), IsPlaying(avatar, "ClickL"));
            var right = (IsLit(avatar, "LightR"), IsPlaying(avatar, "ClickR"));
            return (left, right) switch {
                ((true, true), (false, false)) => "L",
                ((false, false), (true, true)) => "R",
                ((false, false), (false, false)) => "-",
                _ => "?",
            };
        }

        // --- Export ---

        // Saves dist/MimeWalk.unitypackage, after making sure no code got into the package.
        static void Export() {
            var scripts = Directory.GetFiles(PackageDir, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".asmdef") || f.EndsWith(".dll"))
                .ToArray();
            if (scripts.Length > 0) throw new Exception("The package must not contain scripts: " + string.Join(", ", scripts));

            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../dist/MimeWalk.unitypackage"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            AssetDatabase.ExportPackage(PackageDir.TrimEnd('/'), output, ExportPackageOptions.Recurse);
            Debug.Log("[MimeWalk] Exported " + output + " (" + new FileInfo(output).Length + " bytes)");
        }
    }
}
