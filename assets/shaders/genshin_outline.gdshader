// Genshin-style character outline: the mesh drawn a second time, inside out and slightly bigger.
// Used as the "next pass" of genshin_toon.gdshader (Genshin.cs sets this up).
// The line keeps a steady width on screen, never gets fatter than max_width metres up close,
// and takes a darkened version of the surface colour, so skin gets a brown line and blue cloth a navy one.
shader_type spatial;
render_mode cull_front, depth_draw_opaque, shadows_disabled, ambient_light_disabled;

uniform vec4 albedo : source_color = vec4(1.0);
uniform sampler2D albedo_tex : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float alpha_cut : hint_range(0.0, 1.0) = 0.0;
uniform float grow = 0.0;
uniform vec3 line_color : source_color = vec3(0.32, 0.2, 0.22);
uniform float surface_tint : hint_range(0.0, 1.0) = 0.7;   // 0 = always line_color, 1 = line_color multiplied by the surface colour
uniform float width = 1.8;                                 // pixels
uniform float max_width = 0.012;                           // metres
uniform float fade_distance = 60.0;                        // metres; the line is gone by here
uniform bool width_from_vertex_alpha = false;              // Genshin models store per-vertex line width in vertex colour alpha

void vertex() {
	VERTEX += NORMAL * grow;
	vec3 vp = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 vn = normalize(MODELVIEW_NORMAL_MATRIX * NORMAL);
	float dist = max(-vp.z, 0.0);
	float metres_per_pixel = 2.0 * dist / (abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y);
	float w = min(width * metres_per_pixel, max_width);
	w *= 1.0 - smoothstep(fade_distance * 0.5, fade_distance, dist);
	if (width_from_vertex_alpha) {
		w *= COLOR.a;
	}
	vp.xy += vn.xy * w;   // sideways only, so the line never pokes through the front of the model
	POSITION = PROJECTION_MATRIX * vec4(vp, 1.0);
}

void fragment() {
	vec4 t = texture(albedo_tex, UV);
	if (alpha_cut > 0.0 && t.a * albedo.a < alpha_cut) {
		discard;
	}
	ALBEDO = mix(line_color, line_color * albedo.rgb * t.rgb * 2.0, surface_tint);
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}

void light() {
	// flat: the line dims at night and in dungeons like everything else, but has no shading of its own
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * (LIGHT_IS_DIRECTIONAL ? 1.0 : ATTENUATION);
}
