using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEssentials
{
    [RequireComponent(typeof(Camera))]
    public class CameraLuminanceCalculator : MonoBehaviour
    {
        public float Luminance => _luminance;
        [ReadOnly, SerializeField] private float _luminance;

        private Camera _camera;
        private RenderTexture _renderTexture;
        private ComputeShader _luminanceShader;
        private ComputeBuffer _resultBuffer;
        private AsyncGPUReadbackRequest _readbackRequest;
        private int _kernelHandle;
        private bool _processing = false;

        private const int ScaleFactor = 1000; // Must match HLSL

        public void Awake()
        {
            _camera = GetComponent<Camera>();
            _luminanceShader = ResourceLoader.TryGet<ComputeShader>("UnityEssentials_Shader_CameraLuminance");
            _kernelHandle = _luminanceShader.FindKernel("CalculateLuminance");
            _resultBuffer = new ComputeBuffer(1, sizeof(uint));
            _luminanceShader.SetBuffer(_kernelHandle, "Result", _resultBuffer);
        }

        public void OnDestroy() =>
            _resultBuffer?.Release();

        public void Update()
        {
            if (!_processing) CalculateLuminance();
            else CheckAsyncRequest();
        }

        private void FetchTargetTexture()
        {
            if (_renderTexture != null)
                return;

            if (_camera == null || _camera.targetTexture == null)
                return;

            _renderTexture = _camera.targetTexture;
        }

        public void CalculateLuminance()
        {
            // Reset buffer
            uint[] reset = { 0 };
            _resultBuffer.SetData(reset);

            if (_renderTexture == null)
            {
                FetchTargetTexture();
                return;
            }

            // Set texture and dispatch
            _luminanceShader.SetTexture(_kernelHandle, "Source", _renderTexture);
            int threadGroupsX = Mathf.CeilToInt(_renderTexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(_renderTexture.height / 8f);
            _luminanceShader.Dispatch(_kernelHandle, threadGroupsX / 2, threadGroupsY / 2, 1);

            _readbackRequest = AsyncGPUReadback.Request(_resultBuffer);
            _processing = true;
        }

        private void CheckAsyncRequest()
        {
            if (!_readbackRequest.done)
                return;

            if (_readbackRequest.hasError)
            {
                _processing = false;
                return;
            }

            try
            {
                if (_renderTexture == null || !_renderTexture.IsCreated())
                    return;

                float totalLuminance = _readbackRequest.GetData<uint>()[0];

                // Calculate average luminance
                int pixelCount = (_renderTexture.width * _renderTexture.height) / 4;
                float averageLuminance = totalLuminance / (float)(ScaleFactor * pixelCount);

                averageLuminance = Math.Clamp((float)(Math.Truncate(averageLuminance * ScaleFactor) / ScaleFactor), 0.0f, 1.0f);

                _luminance = averageLuminance;
            }
            catch (Exception e) { Debug.LogWarning($"Luminance calculation error: {e.Message}"); }
            finally { _processing = false; }
        }
    }
}