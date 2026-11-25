using Godot;

namespace TwinRealms;

/// <summary>Cheap moody dusk backdrop for the title screen: sky, fog, gothic silhouettes, embers, slow camera drift.</summary>
public partial class TitleBackdrop : Node3D
{
    Camera3D? _cam;
    double _t;

    public override void _Ready()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(.05f, .04f, .16f),
            SkyHorizonColor = new Color(.85f, .38f, .12f),
            GroundHorizonColor = new Color(.35f, .15f, .08f),
            GroundBottomColor = new Color(.02f, .015f, .03f),
            SkyCurve = .12f,
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = .4f,
            FogEnabled = true,
            FogLightColor = new Color(.35f, .17f, .14f),
            FogDensity = .006f,
            GlowEnabled = true,
            GlowIntensity = .8f,
            GlowBloom = .1f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-8, 160, 0),
            LightColor = new Color(1f, .55f, .25f),
            LightEnergy = 1.1f,
            ShadowEnabled = false,
        });

        var black = new StandardMaterial3D { AlbedoColor = new Color(.015f, .015f, .025f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

        // ground
        AddMesh(new PlaneMesh { Size = new Vector2(600, 600) }, black, new Vector3(0, 0, -150));

        // jagged ridges
        var rng = new RandomNumberGenerator { Seed = 7 };
        for (int i = 0; i < 26; i++)
        {
            float x = -220 + i * 17f + rng.RandfRange(-6, 6);
            float h = rng.RandfRange(20, 55), w = rng.RandfRange(18, 38);
            var prism = new PrismMesh { Size = new Vector3(w, h, rng.RandfRange(10, 20)), LeftToRight = rng.RandfRange(.2f, .8f) };
            AddMesh(prism, black, new Vector3(x, h / 2, -190 - rng.RandfRange(0, 30)));
        }

        // gothic skyline
        for (int i = 0; i < 9; i++)
        {
            float x = -80 + i * 20f + rng.RandfRange(-5, 5), z = -110 - rng.RandfRange(0, 30);
            float bw = rng.RandfRange(3, 6), bh = rng.RandfRange(18, 48);
            AddMesh(new BoxMesh { Size = new Vector3(bw, bh, bw) }, black, new Vector3(x, bh / 2, z));
            float rh = bw * rng.RandfRange(2.5f, 4.5f);
            AddMesh(new CylinderMesh { TopRadius = 0, BottomRadius = bw * .8f, Height = rh, RadialSegments = 4 }, black,
                new Vector3(x, bh + rh / 2, z), new Vector3(0, 45, 0));
            if (i % 2 == 0)
            {   // flanking turrets
                float th = bh * .6f;
                AddMesh(new BoxMesh { Size = new Vector3(bw * .5f, th, bw * .5f) }, black, new Vector3(x + bw, th / 2, z + 1));
                AddMesh(new CylinderMesh { TopRadius = 0, BottomRadius = bw * .45f, Height = bw * 1.6f, RadialSegments = 4 }, black,
                    new Vector3(x + bw, th + bw * .8f, z + 1), new Vector3(0, 45, 0));
            }
        }
        // a wide cathedral nave
        AddMesh(new BoxMesh { Size = new Vector3(40, 14, 10) }, black, new Vector3(0, 7, -100));
        AddMesh(new PrismMesh { Size = new Vector3(40, 10, 10) }, black, new Vector3(0, 19, -100));

        // embers
        var ember = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, .5f, .15f),
            EmissionEnabled = true, Emission = new Color(1f, .45f, .1f), EmissionEnergyMultiplier = 3f,
        };
        AddChild(new CpuParticles3D
        {
            Amount = 60, Lifetime = 9, Preprocess = 9,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(40, 1, 25),
            Position = new Vector3(0, 0, -30),
            Direction = Vector3.Up, Spread = 25,
            InitialVelocityMin = 1.5f, InitialVelocityMax = 4f,
            Gravity = new Vector3(.4f, .3f, 0),
            ScaleAmountMin = .5f, ScaleAmountMax = 1f,
            Mesh = new SphereMesh { Radius = .08f, Height = .16f, RadialSegments = 6, Rings = 3, Material = ember },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        _cam = new Camera3D { Position = new Vector3(0, 6, 30), RotationDegrees = new Vector3(6, 0, 0), Fov = 60, Far = 800, Current = true };
        AddChild(_cam);
        _cam.MakeCurrent();   // after entering the tree, so it wins over any other camera
    }

    void AddMesh(Mesh mesh, Material mat, Vector3 pos, Vector3? rotDeg = null)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = mat, Position = pos,
            RotationDegrees = rotDeg ?? Vector3.Zero,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public override void _Process(double delta)
    {
        if (_cam == null) return;
        _t += delta;
        float a = (float)(_t * .6);
        _cam.RotationDegrees = new Vector3(8 + Mathf.Sin(a * .5f) * .8f, 14 + Mathf.Sin(a) * 4f, 0);   // yawed left so the skyline sits right of the UI panel
        _cam.Position = new Vector3(Mathf.Sin(a) * 4f, 6, 30 - (float)(Mathf.Sin(a * .7f) * 4f));
    }
}
