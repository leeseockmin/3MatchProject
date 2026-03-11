using UnityEngine;

namespace ThreeMatch
{
    public sealed class TileView : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private SpriteRenderer _iconRenderer;
        private Color _baseColor;
        private Sprite _appliedIcon;

        public Vector2Int GridPosition { get; private set; }

        public void Initialize(SpriteRenderer renderer, SpriteRenderer iconRenderer)
        {
            _renderer = renderer;
            _iconRenderer = iconRenderer;
        }

        public void SetGridPosition(Vector2Int position, Vector3 worldPosition)
        {
            GridPosition = position;
            transform.position = worldPosition;
        }

        public void Apply(TileData tile, Color color, Sprite icon)
        {
            _baseColor = color;
            _appliedIcon = icon;
            _renderer.color = color;
            _iconRenderer.sprite = icon;
            _iconRenderer.enabled = icon != null;
            _iconRenderer.color = Color.white;
        }

        public void SetPreviewIcon(Sprite icon)
        {
            _iconRenderer.sprite = icon;
            _iconRenderer.enabled = icon != null;
            _iconRenderer.color = Color.white;
        }

        public void RestoreAppliedIcon()
        {
            _iconRenderer.sprite = _appliedIcon;
            _iconRenderer.enabled = _appliedIcon != null;
            _iconRenderer.color = Color.white;
        }

        public void SetSelected(bool selected)
        {
            _renderer.color = selected ? Color.white : _baseColor;
        }

        public void SetPulse(float amount)
        {
            _renderer.color = Color.Lerp(_baseColor, Color.white, Mathf.Clamp01(amount));
        }
    }
}
