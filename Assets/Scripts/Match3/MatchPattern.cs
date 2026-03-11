using System.Collections.Generic;
using UnityEngine;

namespace ThreeMatch
{
    internal sealed class MatchPattern
    {
        public HashSet<Vector2Int> Cells { get; } = new();
        public SpecialTileType SpawnSpecial { get; set; }
        public Vector2Int SpawnCell { get; set; } = new(-1, -1);
        public int Priority { get; set; }
        public bool IsRunPattern { get; set; }
        public bool IsHorizontalRun { get; set; }
    }
}
