shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;

// UV.x = age (0 fresh .. 1 old), UV.y = across the ribbon (0 hilt side .. 1 blade tip = leading edge line).
// COLOR.rgb = element tint, COLOR.a = overall strength.
void fragment() {
	float age = clamp(UV.x, 0.0, 1.0);
	float fresh = pow(1.0 - age, 1.6);
	float w = clamp(UV.y, 0.0, 1.0);
	vec3 tint = COLOR.rgb;
	vec3 steel = mix(tint, vec3(1.0), 0.75);
	vec3 smoke = vec3(0.30, 0.29, 0.28);
	// soft body, thickening toward the tip; sharp bright line at the tip edge
	float body = pow(w, 2.2) * 0.45;
	float edge = smoothstep(0.88, 0.985, w) * (1.0 - smoothstep(0.985, 1.0, w) * 0.4);
	vec3 col = mix(smoke, tint, fresh) * body + steel * edge * (0.4 + 0.9 * fresh);
	col = mix(col, steel * (body + edge), smoothstep(0.7, 0.0, age) * 0.35);
	float a = (body + edge) * fresh * COLOR.a;
	ALBEDO = col * a * 1.6;
	ALPHA = clamp(a, 0.0, 1.0);
}
