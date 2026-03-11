using System.Collections.Generic;
using UnityEngine;

namespace ThreeMatch
{
    internal sealed class ActivationResult
    {
        public HashSet<Vector2Int> Cells { get; } = new();
        public HashSet<Vector2Int> SuppressedSpecials { get; } = new();
        public HashSet<Vector2Int> PreviewCells { get; } = new();
        public List<Vector3> Origins { get; } = new();
        public int Multiplier { get; set; } = 1;
        public string Label { get; set; } = "Match";
        public SpecialTileType PreviewSpecial { get; set; } = SpecialTileType.None;
        public float PreviewDuration { get; set; }
    }
}
