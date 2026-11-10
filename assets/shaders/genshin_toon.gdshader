// Genshin-Impact-style character shader for Godot 4 (Forward+, Mobile and Compatibility).
// Built by Genshin.cs from any StandardMaterial3D; can also be used by hand on a ShaderMaterial.
//
// What it reproduces from the Genshin look:
//   * half-Lambert light, cut into a hard lit/shadow split, coloured by a RAMP texture
//     (shadow is a tinted version of the base colour, with a saturated band at the edge, never grey)
//   * separate day and night ramps, blended with `night`
//   * cast shadows pushed through the same ramp, so they match the self-shadow colour
//   * optional "light map" (ILM) texture: R = metal / specular type, G = shadow bias, B = specular mask
//   * hard-edged Blinn-Phong highlight, and a banded "matcap" look for metal
//   * thin rim light on the silhouette
//   * optional face mode: SDF face-shadow texture, or flattened normals when there is none
// The dark line around the character is the second pass, genshin_outline.gdshader.
shader_type spatial;
render_mode cull_back, depth_draw_opaque;

group_uniforms Base;
uniform vec4 albedo : source_color = vec4(1.0);
uniform sampler2D albedo_tex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_vertex_color = false;
uniform float alpha_cut : hint_range(0.0, 1.0) = 0.0;      // > 0: pixels with less alpha than this are cut out (hair cards, leaves)
uniform float grow = 0.0;                                   // metres pushed out along the normal (clothes over a body)
uniform vec3 emission : source_color = vec3(0.0);
uniform float emission_energy = 0.0;

group_uniforms Shading;
uniform sampler2D ramp_day : source_color, hint_default_white, filter_linear, repeat_disable;
uniform sampler2D ramp_night : source_color, hint_default_white, filter_linear, repeat_disable;
uniform float night : hint_range(0.0, 1.0) = 0.0;
uniform float shadow_threshold : hint_range(0.0, 1.0) = 0.5;   // where light turns to shadow (0.5 = the true terminator)
uniform float shadow_feather : hint_range(0.0, 0.2) = 0.015;   // softness of that edge
uniform float receive_shadows : hint_range(0.0, 1.0) = 1.0;    // how much shadows cast by other objects show
uniform float ambient_strength : hint_range(0.0, 1.0) = 0.55;  // how much of the scene's ambient light the surface takes
uniform float point_light_floor : hint_range(0.0, 1.0) = 0.35; // unlit side under torches and spell lights

group_uniforms Ilm;
uniform bool use_light_map = false;
uniform sampler2D light_map : hint_default_white, filter_linear_mipmap, repeat_enable;

group_uniforms Spec;
uniform float spec_strength : hint_range(0.0, 4.0) = 0.0;
uniform float shininess : hint_range(1.0, 256.0) = 48.0;
uniform float spec_cut : hint_range(0.0, 1.0) = 0.5;
uniform float metal : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D metal_ramp : hint_default_white, filter_linear, repeat_disable;
uniform float metal_brightness : hint_range(0.0, 4.0) = 1.6;

group_uniforms Rim;
uniform float rim_strength : hint_range(0.0, 2.0) = 0.35;
uniform float rim_width : hint_range(0.0, 1.0) = 0.28;
uniform vec3 rim_color : source_color = vec3(1.0);

group_uniforms Face;
uniform bool face_mode = false;
uniform bool use_face_sdf = false;
uniform sampler2D face_sdf : hint_default_white, filter_linear, repeat_disable;
uniform bool face_sdf_flip = false;
uniform vec3 head_forward = vec3(0.0, 0.0, 1.0);   // in the model's own space (glTF characters face +Z)
uniform vec3 head_right = vec3(-1.0, 0.0, 0.0);

varying vec3 head_fwd_w;
varying vec3 head_right_w;

void vertex() {
	VERTEX += NORMAL * grow;
	head_fwd_w = normalize((MODEL_MATRIX * vec4(head_forward, 0.0)).xyz);
	head_right_w = normalize((MODEL_MATRIX * vec4(head_right, 0.0)).xyz);
}

void fragment() {
	vec4 t = texture(albedo_tex, UV);
	if (alpha_cut > 0.0 && t.a * albedo.a < alpha_cut) {
		discard;
	}
	vec3 c = albedo.rgb * t.rgb;
	if (use_vertex_color) {
		c *= COLOR.rgb;
	}
	ALBEDO = c;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
	AO = ambient_strength;
	AO_LIGHT_AFFECT = 0.0;
	EMISSION = emission * emission_energy;
}

void light() {
	vec3 n = normalize(NORMAL);
	vec3 l = normalize(LIGHT);
	vec3 v = normalize(VIEW);
	vec3 lc = LIGHT_COLOR / PI;
	float half_l = dot(n, l) * 0.5 + 0.5;

	if (!LIGHT_IS_DIRECTIONAL) {
		// torches, spell flashes: smooth falloff, two flat bands by direction
		float band = smoothstep(0.45, 0.55, half_l);
		DIFFUSE_LIGHT += lc * ATTENUATION * mix(point_light_floor, 1.0, band);
	} else {
		vec3 ilm = use_light_map ? texture(light_map, UV).rgb : vec3(metal, 0.5, 1.0);
		float lit = half_l;
		float feather = shadow_feather;

		if (face_mode) {
			vec3 lw = (INV_VIEW_MATRIX * vec4(l, 0.0)).xyz;
			if (use_face_sdf) {
				// the SDF stores, per pixel, how far round the light must come before that pixel is lit
				vec2 d = vec2(dot(lw, head_right_w), dot(lw, head_fwd_w));
				d = normalize(d + vec2(0.0001));
				bool flip = (d.x > 0.0) != face_sdf_flip;
				float sdf = texture(face_sdf, flip ? vec2(1.0 - UV.x, UV.y) : UV).r;
				float need = 1.0 - (d.y * 0.5 + 0.5);
				lit = shadow_threshold + (sdf - need);
				feather = max(feather, 0.02);
			} else {
				// no SDF texture: ignore most of the up/down part so the face gets one clean shadow shape
				vec3 nw = (INV_VIEW_MATRIX * vec4(n, 0.0)).xyz;
				vec3 nf = normalize(vec3(nw.x, nw.y * 0.25, nw.z) + vec3(0.0001));
				vec3 lf = normalize(vec3(lw.x, lw.y * 0.25, lw.z) + vec3(0.0001));
				lit = dot(nf, lf) * 0.5 + 0.5;
			}
		}

		lit *= ilm.g * 2.0;                                   // 0.5 = normal, darker = shadows sooner, brighter = stays lit
		lit *= mix(1.0, ATTENUATION, receive_shadows);        // cast shadows go through the same ramp
		float edge = smoothstep(shadow_threshold - feather, shadow_threshold + feather, lit);
		float u = mix(clamp(lit / max(shadow_threshold, 0.001), 0.0, 1.0) * 0.85, 1.0, edge);
		vec3 ramp = mix(texture(ramp_day, vec2(u, 0.5)).rgb, texture(ramp_night, vec2(u, 0.5)).rgb, night);

		// metal: bright and dark bands that slide with the view, instead of plain diffuse
		float m_amt = ilm.r;
		float m_uv = clamp(n.y * 0.5 + 0.5 + n.x * 0.15, 0.0, 1.0);
		float bands = texture(metal_ramp, vec2(m_uv, 0.5)).r * metal_brightness;
		DIFFUSE_LIGHT += lc * ramp * mix(1.0, bands, m_amt);

		// hard-edged highlight
		float ndh = max(dot(n, normalize(l + v)), 0.0);
		float s = pow(ndh, shininess) * ilm.b;
		s = smoothstep(spec_cut - 0.01, spec_cut + 0.01, s);
		SPECULAR_LIGHT += lc * s * spec_strength * mix(1.0, 2.0, m_amt) * edge;

		// rim: thin bright edge on the silhouette, weaker on the shadow side
		float fres = 1.0 - max(dot(n, v), 0.0);
		float rim = smoothstep(1.0 - rim_width, 1.0 - rim_width + 0.04, fres);
		DIFFUSE_LIGHT += lc * rim_color * rim * rim_strength * mix(0.35, 1.0, edge);
	}
}
