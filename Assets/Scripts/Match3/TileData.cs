namespace ThreeMatch
{
    public struct TileData
    {
        public TileColor Color;
        public SpecialTileType Special;

        public TileData(TileColor color, SpecialTileType special = SpecialTileType.None)
        {
            Color = color;
            Special = special;
        }
    }
}
