using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Unity.Cinemachine
{
    public class DynamicCinemachineMixingCamera : CinemachineCameraManagerBase
    {
        [Serializable]
        public struct CameraWeightPair : IEquatable<CameraWeightPair>
        {
            public CameraWeightPair(CinemachineVirtualCameraBase camera, float weight)
            {
                Camera = camera;
                Weight = weight;
            }
            [field: SerializeField] public CinemachineVirtualCameraBase Camera { get; private set; }
            [field: SerializeField] public float Weight { get; private set; }

            public bool Equals(CameraWeightPair other)
            {
                return Equals(Camera, other.Camera) && Weight.Equals(other.Weight);
            }

            public override bool Equals(object obj)
            {
                return obj is CameraWeightPair other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Camera, Weight);
            }
        }
        
        CameraState m_CameraState = CameraState.Default;
        float m_LiveChildPercent;

        [field: SerializeField] private List<CameraWeightPair> CameraWeightPairs { get; set; } = new();
        protected bool _childCamerasDisposed = true;

        private List<CinemachineVirtualCameraBase> _childCamerasReference = null;
        // private List<CinemachineVirtualCameraBase> _childCameras;
        // public List<CinemachineVirtualCameraBase> ChildCameras 
        // { 
        //     get => _childCameras;
        //     protected set => _childCameras = value;
        // }

        /// <summary>Makes sure the weights are non-negative</summary>
        ///
        void OnValidate()
        {
            for (int i = 0; i < CameraWeightPairs.Count; ++i)
                SetWeight(i, Mathf.Max(0, GetWeight(i)));
        }

        /// <inheritdoc />
        protected override void Reset()
        {
            base.Reset();
            for (var i = 0; i < CameraWeightPairs.Count; ++i)
                SetWeight(i, i == 0 ? 1 : 0);
        }

        protected override void Start()
        {
            base.Start();
            base.UpdateCameraCache();
            _updatingCache = true;
            _childCamerasReference = ChildCameras;
            _updatingCache = false;
            //_childCamerasReference = ChildCameras;
        }
        
        /// <inheritdoc />
        public override CameraState State => m_CameraState;

        /// <inheritdoc />
        public override string Description 
        {
            get
            {
                if (LiveChild == null)
                    return "[(none)]";
                //var sb = CinemachineDebug.SBFromPool();
                StringBuilder sb = new StringBuilder();
                sb.Append("[");
                sb.Append(LiveChild.Name);
                sb.Append(" ");
                sb.Append(Mathf.RoundToInt(m_LiveChildPercent));
                sb.Append("%]");
                var text = sb.ToString();
                sb = null;
                //CinemachineDebug.ReturnToPool(sb);
                return text;
            }
        }

        /// <summary>Get the weight of the child at an index.</summary>
        /// <param name="index">The child index. Only immediate CinemachineVirtualCameraBase
        /// children are counted.</param>
        /// <returns>The weight of the camera.  Valid only if camera is active and enabled.</returns>
        public float GetWeight(int index)
        {
            if (index < CameraWeightPairs.Count)
            {
                return CameraWeightPairs[index].Weight;
            }
            
            Debug.LogError("CinemachineMixingCamera: Invalid index: " + index);
            return 0;
        }

        /// <summary>Set the weight of the child at an index.</summary>
        /// <param name="index">The child index. Only immediate CinemachineVirtualCameraBase
        /// children are counted.</param>
        /// <param name="w">The weight to set.  Can be any non-negative number.</param>
        public void SetWeight(int index, float w)
        {
            if (index < CameraWeightPairs.Count)
            {
                CameraWeightPair newPair = new(CameraWeightPairs[index].Camera, w);
                CameraWeightPairs[index] = newPair;
                return;
            }

            Debug.LogError("CinemachineMixingCamera: Invalid index: " + index);
        }

        /// <summary>Get the weight of the child CinemachineVirtualCameraBase.</summary>
        /// <param name="vcam">The child camera.</param>
        /// <returns>The weight of the camera.  Valid only if camera is active and enabled.</returns>
        public float GetWeight(CinemachineVirtualCameraBase vcam)
        {
            UpdateCameraCache();
            int index = CameraWeightPairs.FindIndex(pair => pair.Camera == vcam);
            if (index >= 0)
                return GetWeight(index);
            return 0;
        }

        /// <summary>Set the weight of the child CinemachineVirtualCameraBase.</summary>
        /// <param name="vcam">The child camera.</param>
        /// <param name="w">The weight to set.  Can be any non-negative number.</param>
        public void SetWeight(CinemachineVirtualCameraBase vcam, float w)
        {
            UpdateCameraCache();
            int index = CameraWeightPairs.FindIndex(pair => pair.Camera == vcam);
            if (index >= 0)
                SetWeight(index, w);
            else
                Debug.LogError("CinemachineMixingCamera: Invalid child: "
                    + ((vcam != null) ? vcam.Name : "(null)"));
        }

        /// <inheritdoc />
        public override bool IsLiveChild(ICinemachineCamera vcam, bool dominantChildOnly = false)
        {
            if (dominantChildOnly)
                return LiveChild == vcam;
            var children = CameraWeightPairs;
            for (int i = 0; i < children.Count; ++i)
                if ((ICinemachineCamera)children[i].Camera == vcam)
                    return GetWeight(i) > UnityVectorExtensions.Epsilon && children[i].Camera.isActiveAndEnabled;
            return false;
        }

        public bool IsChild(ICinemachineCamera vcam)
        {
            var children = CameraWeightPairs;
            for (int i = 0; i < children.Count; ++i)
                if ((ICinemachineCamera)children[i].Camera == vcam)
                    return true;
            return false;
        }

        private bool _updatingCache = false;
        /// <inheritdoc />
        protected override bool UpdateCameraCache()
        {
            //CameraWeightPair
            if ((!_childCamerasDisposed && _childCamerasReference != null) || _updatingCache)
            {
                return false;
            }
            _updatingCache = true;
            
            _childCamerasDisposed = false;
            PreviousStateIsValid = false;
            if (_childCamerasReference == null)
            {
                _childCamerasReference = ChildCameras;
                if (_childCamerasReference == null)
                {
                    base.UpdateCameraCache();
                    _childCamerasReference = ChildCameras;
                }

                UpdateCameraList();
                
                _updatingCache = false;
                return false;
            }

            UpdateCameraList();
            //_childCamerasReference?.AddRange(CameraWeightPairs.Select(pair => pair.Camera));
            
            _updatingCache = false;
            return true;


                
            // if (!base.UpdateCameraCache())
            //     return false;
            //
            // m_IndexMap = new Dictionary<CinemachineVirtualCameraBase, int>();
            // for (var i = 0; i < ChildCameras.Count; ++i)
            //     m_IndexMap.Add(ChildCameras[i], i);
            // return true;
            
            
            // if (m_ChildCameras != null)
            //     return false;
            // PreviousStateIsValid = false;
            // m_ChildCameras = new();
            // GetComponentsInChildren(true, m_ChildCameras);
            // for (int i = m_ChildCameras.Count-1; i >= 0; --i)
            //     if (m_ChildCameras[i].transform.parent != transform)
            //         m_ChildCameras.RemoveAt(i);
            // return true;
        }
        private void UpdateCameraList()
        {
            _childCamerasReference?.Clear();
            for (int i = CameraWeightPairs.Count - 1; i >= 0; --i)
            {
                CameraWeightPair pair = CameraWeightPairs[i];
                if (!pair.Camera)
                {
                    CameraWeightPairs.Remove(pair);
                    continue;
                }
                _childCamerasReference?.Add(pair.Camera);
            }
        }
        public new void InvalidateCameraCache() 
        {
            _childCamerasReference?.Clear();
            _childCamerasDisposed = true;
            PreviousStateIsValid = false;
        }

        public void AddCameraWeightPair(CameraWeightPair pair)
        {
            CameraWeightPairs.Add(pair);
            InvalidateCameraCache();
        }
        public void RemoveCameraWeightPair(CameraWeightPair pair)
        {
            CameraWeightPairs.Remove(pair);
            InvalidateCameraCache();
        }
        public void RemoveCamera(CinemachineCamera camera)
        {
            CameraWeightPairs.Remove(CameraWeightPairs.Where(pair => pair.Camera == camera).FirstOrDefault());
            InvalidateCameraCache();
        }

        /// <inheritdoc />
        public override void OnTransitionFromCamera(
            ICinemachineCamera fromCam, Vector3 worldUp, float deltaTime)
        {
            for (int i = 0; i < _childCamerasReference.Count; ++i)
                _childCamerasReference[i].OnTransitionFromCamera(fromCam, worldUp, deltaTime);
            base.OnTransitionFromCamera(fromCam, worldUp, deltaTime);
        }


        /// <inheritdoc />
        public override void InternalUpdateCameraState(Vector3 worldUp, float deltaTime)
        {
            UpdateCameraCache();

            CinemachineVirtualCameraBase liveChild = null;
            var children = _childCamerasReference;
            float highestWeight = 0;
            float totalWeight = 0;
            for (var i = 0; i < children.Count; ++i)
            {
                var vcam = children[i];
                if (vcam.isActiveAndEnabled)
                {
                    float weight = Mathf.Max(0, GetWeight(i));
                    if (weight > UnityVectorExtensions.Epsilon)
                    {
                        totalWeight += weight;
                        if (totalWeight == weight)
                            m_CameraState = vcam.State;
                        else
                            m_CameraState = CameraState.Lerp(m_CameraState, vcam.State, weight / totalWeight);

                        if (weight > highestWeight)
                        {
                            highestWeight = weight;
                            liveChild = vcam;
                        }
                    }
                }
            }
            m_LiveChildPercent = totalWeight > 0.001f ? (highestWeight * 100 / totalWeight) : 0;
            SetLiveChild(liveChild, worldUp, deltaTime);
            InvokePostPipelineStageCallback(this, CinemachineCore.Stage.Finalize, ref m_CameraState, deltaTime);
            PreviousStateIsValid = true;
        }

        /// <inheritdoc />
        protected override CinemachineVirtualCameraBase ChooseCurrentCamera(Vector3 worldUp, float deltaTime) => null;
    }
}
