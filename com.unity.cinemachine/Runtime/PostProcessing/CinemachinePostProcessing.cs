#if CINEMACHINE_POST_PROCESSING_V2
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using System.Collections.Generic;
using UnityEngine.Rendering.PostProcessing;

namespace Unity.Cinemachine
{
    /// <summary>
    /// This behaviour is a liaison between Cinemachine and the Post-Processing v2 module.  You must
    /// have the Post-Processing V2 stack package installed in order to use this behaviour.
    ///
    /// As a component on the Virtual Camera, it holds
    /// a Post-Processing Profile asset that will be applied to the Unity camera whenever
    /// the Virtual camera is live.  It also has the optional functionality of animating
    /// the Focus Distance and DepthOfField properties of the Camera State, and
    /// applying them to the current Post-Processing profile, provided that profile has a
    /// DepthOfField effect that is enabled.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Cinemachine/Procedural/Extensions/Cinemachine Post Processing")] // Hide in menu
    [SaveDuringPlay]
    [DisallowMultipleComponent]
    [HelpURL(Documentation.BaseURL + "manual/CinemachinePostProcessing.html")]
    public class CinemachinePostProcessing : CinemachineExtension
    {
        /// <summary>
        /// This is the priority for the vcam's PostProcessing volumes.  It's set to a high
        /// number in order to ensure that it overrides other volumes for the active vcam.
        /// You can change this value if necessary to work with other systems.
        /// </summary>
        public static float s_VolumePriority = 1000f;

        /// <summary>
        /// This is the weight that the PostProcessing profile will have when the camera is fully active.
        /// It will blend to and from 0 along with the camera.
        /// </summary>
        public float Weight = 1;

        /// <summary>The reference object for focus tracking</summary>
        public enum FocusTrackingMode
        {
            /// <summary>No focus tracking</summary>
            None,
            /// <summary>Focus offset is relative to the LookAt target</summary>
            LookAtTarget,
            /// <summary>Focus offset is relative to the Follow target</summary>
            FollowTarget,
            /// <summary>Focus offset is relative to the Custom target set here</summary>
            CustomTarget,
            /// <summary>Focus offset is relative to the camera</summary>
            Camera
        };

        /// <summary>If the profile has the appropriate overrides, will set the base focus
        /// distance to be the distance from the selected target to the camera.
        /// The Focus Offset field will then modify that distance</summary>
        [Tooltip("If the profile has the appropriate overrides, will set the base focus "
            + "distance to be the distance from the selected target to the camera."
            + "The Focus Offset field will then modify that distance.")]
        [FormerlySerializedAs("m_FocusTracking")]
        public FocusTrackingMode FocusTracking;

        /// <summary>The target to use if Focus Tracks Target is set to Custom Target</summary>
        [Tooltip("The target to use if Focus Tracks Target is set to Custom Target")]
        [FormerlySerializedAs("m_FocusTarget")]
        public Transform FocusTarget;

        /// <summary>Offset from target distance, to be used with Focus Tracks Target.
        /// Offsets the sharpest point away from the location of the focus target</summary>
        [Tooltip("Offset from target distance, to be used with Focus Tracks Target.  "
            + "Offsets the sharpest point away from the location of the focus target.")]
        [FormerlySerializedAs("m_FocusOffset")]
        public float FocusOffset;

        /// <summary>
        /// If Focus tracking is enabled, this will return the calculated focus distance
        /// </summary>
        public float CalculatedFocusDistance { get; private set; }

        /// <summary>
        /// This Post-Processing profile will be applied whenever this virtual camera is live
        /// </summary>
        [Tooltip("This Post-Processing profile will be applied whenever this virtual camera is live")]
        [FormerlySerializedAs("m_Profile")]
        public PostProcessProfile Profile;

        class VcamExtraState : VcamExtraStateBase
        {
            public PostProcessProfile ProfileCopy;

            public void CreateProfileCopy(PostProcessProfile source)
            {
                DestroyProfileCopy();
                var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
                for (int i = 0; source != null && i < source.settings.Count; ++i)
                {
                    var itemCopy = Instantiate(source.settings[i]);
                    profile.settings.Add(itemCopy);
                }
                ProfileCopy = profile;
            }

            public void DestroyProfileCopy()
            {
                if (ProfileCopy != null)
                    RuntimeUtility.DestroyObject(ProfileCopy);
                ProfileCopy = null;
            }
        }

        List<VcamExtraState> m_extraStateCache;

        /// <summary>True if the profile is enabled and nontrivial</summary>
        public bool IsValid => Profile != null && Profile.settings.Count > 0;

        void OnValidate()
        {
            Weight = Mathf.Max(0, Weight);
        }

        void Reset()
        {
            Weight = 1;
            FocusTracking = FocusTrackingMode.None;
            FocusTarget = null;
            FocusOffset = 0;
            Profile = null;
        }

        protected override void OnEnable()
        {
            InvalidateCachedProfile();
        }

        protected override void OnDestroy()
        {
            InvalidateCachedProfile();
            base.OnDestroy();
        }

        /// <summary>Called by the editor when the shared asset has been edited</summary>
        public void InvalidateCachedProfile()
        {
            m_extraStateCache ??= new();
            GetAllExtraStates(m_extraStateCache);
            for (int i = 0; i < m_extraStateCache.Count; ++i)
                m_extraStateCache[i].DestroyProfileCopy();
        }

        /// <summary>Apply PostProcessing effects</summary>
        /// <param name="vcam">The virtual camera being processed</param>
        /// <param name="stage">The current pipeline stage</param>
        /// <param name="state">The current virtual camera state</param>
        /// <param name="deltaTime">The current applicable deltaTime</param>
        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            // Set the focus after the camera has been fully positioned.
            if (stage == CinemachineCore.Stage.Finalize)
            {
                var extra = GetExtraState<VcamExtraState>(vcam);
                if (!IsValid)
                    extra.DestroyProfileCopy();
                else
                {
                    var profile = Profile;

                    // Handle Follow Focus
                    if (FocusTracking == FocusTrackingMode.None)
                        extra.DestroyProfileCopy();
                    else
                    {
                        if (extra.ProfileCopy == null)
                            extra.CreateProfileCopy(Profile);
                        profile = extra.ProfileCopy;
                        DepthOfField dof;
                        if (profile.TryGetSettings(out dof))
                        {
                            float focusDistance = FocusOffset;
                            if (FocusTracking == FocusTrackingMode.LookAtTarget)
                                focusDistance += (state.GetFinalPosition() - state.ReferenceLookAt).magnitude;
                            else
                            {
                                Transform focusTarget = null;
                                switch (FocusTracking)
                                {
                                    default: break;
                                    case FocusTrackingMode.FollowTarget: focusTarget = vcam.Follow; break;
                                    case FocusTrackingMode.CustomTarget: focusTarget = FocusTarget; break;
                                }
                                if (focusTarget != null)
                                    focusDistance += (state.GetFinalPosition() - focusTarget.position).magnitude;
                            }
                            CalculatedFocusDistance = state.Lens.PhysicalProperties.FocusDistance
                                = dof.focusDistance.value = Mathf.Max(0.01f, focusDistance);
                        }
                    }

                    // Apply the post-processing
                    state.AddCustomBlendable(new CameraState.CustomBlendableItems.Item{ Custom = profile, Weight = Weight });
                }
            }
        }

        static void OnCameraCut(ICinemachineCamera.ActivationEventParams evt)
        {
            if (!evt.IsCut)
                return;
            var brain = evt.Origin as CinemachineBrain;
            if (brain == null)
                return;
            // Debug.Log("Camera cut event");
            PostProcessLayer postFX = GetPPLayer(brain);
            if (postFX != null && postFX.isActiveAndEnabled)
                postFX.ResetHistory();
        }

        static void ApplyPostFX(CinemachineBrain brain)
        {
            PostProcessLayer ppLayer = GetPPLayer(brain);
            if (ppLayer == null || !ppLayer.enabled  || ppLayer.volumeLayer == 0)
                return;

            CameraState state = brain.State;
            int numBlendables = state.GetNumCustomBlendables();
            List<PostProcessVolume> volumes = GetDynamicBrainVolumes(brain, ppLayer, numBlendables);
            for (int i = 0; i < volumes.Count; ++i)
            {
                volumes[i].weight = 0;
                volumes[i].sharedProfile = null;
                volumes[i].profile = null;
            }
            PostProcessVolume firstVolume = null;
            int numPPblendables = 0;
            for (int i = 0; i < numBlendables; ++i)
            {
                var b = state.GetCustomBlendable(i);
                var profile = b.Custom as PostProcessProfile;
                if (profile != null) // in case it was deleted
                {
                    PostProcessVolume v = volumes[i];
                    if (firstVolume == null)
                        firstVolume = v;
                    v.sharedProfile = profile;
                    v.isGlobal = true;
                    v.priority = s_VolumePriority - (numBlendables - i) - 1;
                    v.weight = b.Weight;
                    ++numPPblendables;
                }
#if !CINEMACHINE_TRANSPARENT_POST_PROCESSING_BLENDS
                // If more than one volume, then set the first one's weight to 1 so that it's opaque
                if (numPPblendables > 1)
                    firstVolume.weight = 1;
#endif
            }
        }

        static string sVolumeOwnerName = "__CMVolumes";
        static  List<PostProcessVolume> sVolumes = new List<PostProcessVolume>();
        static List<PostProcessVolume> GetDynamicBrainVolumes(
            CinemachineBrain brain, PostProcessLayer ppLayer, int minVolumes)
        {
            // Locate the camera's child object that holds our dynamic volumes
            GameObject volumeOwner = null;
            Transform t = brain.transform;
            int numChildren = t.childCount;

            sVolumes.Clear();
            for (int i = 0; volumeOwner == null && i < numChildren; ++i)
            {
                GameObject child = t.GetChild(i).gameObject;
                if (child.hideFlags == HideFlags.HideAndDontSave)
                {
                    child.GetComponents(sVolumes);
                    if (sVolumes.Count > 0)
                        volumeOwner = child;
                }
            }

            if (minVolumes > 0)
            {
                if (volumeOwner == null)
                {
                    volumeOwner = new GameObject(sVolumeOwnerName);
                    volumeOwner.hideFlags = HideFlags.HideAndDontSave;
                    volumeOwner.transform.parent = t;
                }
                // Update the volume's layer so it will be seen
                int mask = ppLayer.volumeLayer.value;
                for (int i = 0; i < 32; ++i)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        volumeOwner.layer = i;
                        break;
                    }
                }
                while (sVolumes.Count < minVolumes)
                    sVolumes.Add(volumeOwner.AddComponent<PostProcessVolume>());
            }
            return sVolumes;
        }

        static Dictionary<CinemachineBrain, PostProcessLayer> s_BrainToLayer = new ();

        static PostProcessLayer GetPPLayer(CinemachineBrain brain)
        {
            bool found = s_BrainToLayer.TryGetValue(brain, out PostProcessLayer layer);
            if (layer != null)
                return layer;   // layer is valid and in our lookup

            // If the layer in the lookup table is a deleted object, we must remove
            // the brain's callback for it
            if (found && layer is not null)
                s_BrainToLayer.Remove(brain); // layer is a deleted object

            // If brain is not in our lookup - add it.
            brain.TryGetComponent(out layer);
            if (layer != null)
                s_BrainToLayer[brain] = layer;

            return layer;
        }

        static void CleanupLookupTable()
        {
            s_BrainToLayer.Clear();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoad]
        class EditorInitialize
        {
            static EditorInitialize()
            {
                UnityEditor.EditorApplication.playModeStateChanged += (pmsc) => CleanupLookupTable();
                InitializeModule();
            }
        }
#endif
        [RuntimeInitializeOnLoadMethod]
        static void InitializeModule()
        {
            // After the brain pushes the state to the camera, hook in to the PostFX
            CinemachineCore.CameraUpdatedEvent.RemoveListener(ApplyPostFX);
            CinemachineCore.CameraUpdatedEvent.AddListener(ApplyPostFX);

            CinemachineCore.CameraActivatedEvent.RemoveListener(OnCameraCut);
            CinemachineCore.CameraActivatedEvent.AddListener(OnCameraCut);

// Clean up our resources
            SceneManager.sceneUnloaded += (scene) => CleanupLookupTable();
        }
    }
}
#endif
