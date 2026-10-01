using UnityEngine;

namespace SoulLink.UI
{
    /// <summary>
    /// Ensures the mobile HUD stays readable across different aspect ratios and
    /// resolutions. Attach to the root Canvas or a dedicated UI manager.
    /// </summary>
    public class CanvasScalerSetup : MonoBehaviour
    {
        [SerializeField] private float referenceWidth = 1080f;
        [SerializeField] private float referenceHeight = 1920f;

        private void Awake()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }

            var scaler = canvas.gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(referenceWidth, referenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.graphicMatrix = Matrix4x4.identity;

            var raycaster = canvas.gameObject.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }
    }
}
