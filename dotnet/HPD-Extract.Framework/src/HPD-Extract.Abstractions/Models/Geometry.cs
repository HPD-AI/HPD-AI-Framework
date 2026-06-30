using System.Text.Json.Serialization;

namespace HPD.Extract.Models
{
    public readonly record struct BoundingBox(
        [property: JsonPropertyName("x")] float X,
        [property: JsonPropertyName("y")] float Y,
        [property: JsonPropertyName("width")] float Width,
        [property: JsonPropertyName("height")] float Height)
    {
        [JsonIgnore]
        public float Right => X + Width;

        [JsonIgnore]
        public float Bottom => Y + Height;

        public bool Intersects(BoundingBox other, float tolerance = 0)
        {
            return X < other.Right + tolerance
                && Right > other.X - tolerance
                && Y < other.Bottom + tolerance
                && Bottom > other.Y - tolerance;
        }
    }

    public readonly record struct PageSize(
        [property: JsonPropertyName("width")] float Width,
        [property: JsonPropertyName("height")] float Height);
}
