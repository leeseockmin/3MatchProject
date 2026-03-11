using UnityEngine;

namespace ThreeMatch
{
    internal sealed class ComboPopup
    {
        public string Text { get; set; } = string.Empty;
        public Vector3 WorldPosition { get; set; }
        public Color Color { get; set; } = Color.white;
        public float Lifetime { get; set; } = 0.85f;
        public float Age { get; set; }
    }
}
