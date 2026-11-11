// Stylized anime water for Godot 4 (Forward+ / Mobile). Put it on a flat, subdivided plane.
// Colour goes from `shallow` at the shore to `deep` over `depth_range` metres of water depth,
// with a wobbly foam line where the water meets land, drifting light lines on the surface, and gentle waves.
// No textures needed. Not yet tested in the game: tune the numbers once it is on screen.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_toon, specular_toon;

uniform vec4 shallow : source_color = vec4(0.25, 0.71, 0.69, 0.72);
uniform vec4 deep : source_color = vec4(0.05, 0.23, 0.36, 0.94);
uniform vec3 foam_color : source_color = vec3(0.92, 0.96, 1.0);
uniform vec3 line_color : source_color = vec3(0.75, 0.95, 1.0);
uniform float depth_range = 3.0;
uniform float foam_width = 0.45;
uniform float line_scale = 0.35;
uniform float line_strength : hint_range(0.0, 1.0) = 0.35;
uniform float flow_speed = 0.05;
uniform float wave_height = 0.05;
uniform float wave_length = 6.0;
uniform float wave_speed = 0.8;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;

varying vec3 world_pos;

vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return fract(sin(p) * 43758.5453);
}

// distance to the nearest cell edge of a moving cell pattern: thin bright lines like light on a pool floor
float cell_lines(vec2 p, float t) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	float d1 = 8.0;
	float d2 = 8.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 g = vec2(float(x), float(y));
			vec2 o = hash2(i + g);
			o = 0.5 + 0.4 * sin(t + 6.2831 * o);
			float d = length(g + o - f);
			if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
		}
	}
	return d2 - d1;
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float k = 6.2831 / wave_length;
	float w = sin(world_pos.x * k + TIME * wave_speed) + sin(world_pos.z * k * 0.8 - TIME * wave_speed * 1.3);
	VERTEX.y += w * 0.5 * wave_height;
	world_pos.y += w * 0.5 * wave_height;
}

void fragment() {
	// how much water is between this pixel and whatever is behind it
	float raw = texture(depth_tex, SCREEN_UV).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
	float water_depth = max(-behind.z / behind.w + VERTEX.z, 0.0);

	float t = clamp(water_depth / depth_range, 0.0, 1.0);
	vec3 col = mix(shallow.rgb, deep.rgb, t);
	float alpha = mix(shallow.a, deep.a, t);

	vec2 uv = world_pos.xz * line_scale + vec2(TIME * flow_speed, TIME * flow_speed * 0.6);
	float lines = 1.0 - smoothstep(0.0, 0.09, cell_lines(uv, TIME * 0.6));
	lines *= 1.0 - smoothstep(0.0, 0.14, cell_lines(uv * 0.6 + 3.7, TIME * 0.45));   // break the pattern up so it does not tile
	col = mix(col, line_color, lines * line_strength * (1.0 - t * 0.6));

	float wobble = cell_lines(world_pos.xz * 1.6 + TIME * 0.1, TIME) * 0.5;
	float foam = 1.0 - smoothstep(foam_width * (0.5 + wobble), foam_width * (0.6 + wobble) + 0.02, water_depth);
	col = mix(col, foam_color, foam);
	alpha = max(alpha, foam);

	ALBEDO = col;
	ALPHA = alpha;
	ROUGHNESS = 0.12;
	SPECULAR = 0.5;
	METALLIC = 0.0;
}
