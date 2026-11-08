using Godot;

namespace TwinRealms;

/// <summary>Step 0 test world: flat ground, light, sky, a few landmarks, the player and the camera.</summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        Inputs.Setup();
        BuildEnvironment();
        BuildGround();
        BuildLandmarks();

        var player = new Player { Name = "Player", Position = new Vector3(0, 0.1f, 0) };
        var rig = new CameraRig { Name = "CameraRig", Target = player, Position = new Vector3(0, 1.6f, 0) };
        player.Rig = rig;
        AddChild(player);
        AddChild(rig);
        rig.ExcludeFromCollision(player);
        AddChild(new Hud { Name = "Hud" });
        SpawnEnemies();
    }

    void SpawnEnemies()
    {
        // type, x, z, level
        (string, float, float, int)[] list =
        {
            ("wolf", 14, 18, 1), ("wolf", 18, 16, 1),
            ("bandit", -16, -14, 1), ("bandit", -20, -18, 2),
            ("goblin", 6, -22, 1), ("goblin", 9, -25, 1), ("goblin", 3, -26, 1),
            ("skeleton", -24, 14, 2),
            ("brute", 0, -38, 3),
        };
        foreach (var (type, x, z, lvl) in list)
            AddChild(new Enemy { Type = type, Level = lvl, Position = new Vector3(x, 0.1f, z) });
    }

    void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.25f, 0.5f, 0.85f),
            SkyHorizonColor = new Color(0.75f, 0.85f, 0.95f),
            GroundHorizonColor = new Color(0.75f, 0.85f, 0.95f),
            GroundBottomColor = new Color(0.25f, 0.3f, 0.25f),
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.75f, 0.85f, 0.95f),
            FogDensity = 0.004f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D
        {
            LightEnergy = 1.2f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 120f,
            RotationDegrees = new Vector3(-50, -35, 0),
        };
        AddChild(sun);
    }

    void BuildGround()
    {
        var body = new StaticBody3D { Name = "Ground", CollisionLayer = 1 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(400, 1, 400) }, Position = new Vector3(0, -0.5f, 0) });
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.5f, 0.22f), Roughness = 1f };
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(400, 1, 400) }, MaterialOverride = mat, Position = new Vector3(0, -0.5f, 0) });
        AddChild(body);
    }

    void BuildLandmarks()
    {
        // pillars and steps so there is something to run around, jump on and judge scale by
        var stone = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.53f, 0.5f), Roughness = 0.9f };
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.Tau;
            AddBox(new Vector3(Mathf.Cos(a) * 14, 2, Mathf.Sin(a) * 14), new Vector3(1.2f, 4, 1.2f), stone);
        }
        for (int i = 0; i < 5; i++)
            AddBox(new Vector3(8 + i * 1.6f, 0.25f + i * 0.25f, -6), new Vector3(1.6f, 0.5f + i * 0.5f, 3), stone);
        AddBox(new Vector3(-10, 0.75f, 6), new Vector3(3, 1.5f, 3), stone);
    }

    void AddBox(Vector3 pos, Vector3 size, Material mat)
    {
        var b = new StaticBody3D { Position = pos, CollisionLayer = 1 };
        b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        b.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat });
        AddChild(b);
    }
}
