shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, skip_vertex_transform;

// Heat haze: a camera-facing quad that re-samples the screen with a small animated offset. life goes 1 -> 0.
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
instance uniform float life = 1.0;
uniform float strength = 0.018;

void vertex() {
	mat4 mv = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	VERTEX = (mv * vec4(VERTEX, 1.0)).xyz;
	NORMAL = vec3(0.0, 0.0, 1.0);
}

void fragment() {
	vec2 p = UV - 0.5;
	float mask = smoothstep(0.5, 0.1, length(p));
	float t = TIME * 6.0;
	vec2 n = vec2(sin(UV.y * 22.0 + t) + sin(UV.y * 9.0 - t * 1.3), cos(UV.x * 18.0 - t * 0.9) + sin(UV.x * 11.0 + t)) * 0.5;
	float a = mask * life;
	vec2 off = n * strength * a;
	ALBEDO = textureLod(screen_tex, SCREEN_UV + off, 0.0).rgb;
	ALPHA = clamp(a * 3.0, 0.0, 1.0);
}
