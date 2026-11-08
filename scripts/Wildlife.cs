using Godot;

namespace TwinRealms;

/// <summary>Places wildlife herds (deer, rabbits, boars, shade stags) from data/wildlife.json. The animals themselves are Enemies
/// with flee/graze behaviour and material drops defined in enemies.json; they respawn like other overworld creatures.</summary>
public static class Wildlife
{
    public static void Spawn(Node3D over)
    {
        using var doc = JsonUtil.Load("res://data/wildlife.json"); var root = doc.RootElement;
        var rg = World.Rng((uint)JsonUtil.F(root, "seed", 5511)); float R(float a, float b) => a + rg() * (b - a);
        foreach (var h in root.GetProperty("herds").EnumerateArray())
        {
            string type = JsonUtil.S(h, "type"); int groups = (int)JsonUtil.F(h, "groups"); string where = JsonUtil.S(h, "where", "meadow");
            float minH = JsonUtil.F(h, "minH", 2), maxH = JsonUtil.F(h, "maxH", 30); int s0 = 1, s1 = 1;
            if (h.TryGetProperty("size", out var sz)) { s0 = sz[0].GetInt32(); s1 = sz[1].GetInt32(); }
            int placed = 0, tries = 0;
            while (placed < groups && tries++ < groups * 60)
            {
                float x = R(-World.Half + 20, World.Half - 20), z = R(-World.Half + 20, World.Half - 20), gh = World.GroundAt(x, z);
                if (gh < minH || gh > maxH || !World.FreeSpot(x, z, 6f)) continue;
                bool forest = World.Fbm(x * .02f + 3, z * .02f + 9, 3) >= .42f; if ((where == "forest") != forest) continue;
                if (new Vector2(x, z).Length() < 45f) continue; // keep the village outskirts calm
                int n = s0 + (int)(rg() * (s1 - s0 + 1));
                for (int i = 0; i < n; i++)
                {
                    float ax = x + R(-4, 4), az = z + R(-4, 4);
                    over.AddChild(new Enemy { Type = type, Level = Game.LevelAt(ax, az, 0), Position = World.OnGround(ax, az, 0.3f), Rotation = new Vector3(0, R(0, Mathf.Tau), 0) });
                }
                placed++;
            }
        }
    }
}
