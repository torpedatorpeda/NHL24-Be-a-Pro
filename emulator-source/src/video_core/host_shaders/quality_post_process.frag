// SPDX-FileCopyrightText: Copyright 2024 shadPS4 Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

#version 450

layout (location = 0) in vec2 uv;
layout (location = 0) out vec4 color;

layout (binding = 0) uniform sampler2D texSampler;

layout (push_constant) uniform settings {
    float gamma;
    bool hdr;
    bool srgb_input;
} pp;

const float cutoff = 0.0031308, a = 1.055, b = 0.055, d = 12.92;
vec3 gamma(vec3 rgb) {
    return mix(
        a * pow(rgb, vec3(1.0 / (2.4 + 1.0 - pp.gamma))) - b,
        d * rgb / pp.gamma,
        lessThan(rgb, vec3(cutoff))
    );
}

// Exact inverse of gamma() at unit gamma, for buffers that are sRGB encoded but must be sampled
// through a UNORM view because Vulkan has no sRGB variant of the 10-bit format.
vec3 degamma(vec3 rgb) {
    return mix(
        pow(max(rgb + b, 0.0) / a, vec3(2.4)),
        rgb / d,
        lessThan(rgb, vec3(d * cutoff))
    );
}

// Spatial edge smoothing. The source frame and its alpha remain unchanged
// in flat regions; filtering is limited to local high-contrast edges.
float luma(vec3 rgb) { return dot(rgb, vec3(0.299, 0.587, 0.114)); }
vec4 smoothEdges(vec2 coord) {
    vec2 stepSize = 1.0 / vec2(textureSize(texSampler, 0));
    vec4 center = texture(texSampler, coord);
    vec3 nw = texture(texSampler, coord + vec2(-1, -1) * stepSize).rgb;
    vec3 ne = texture(texSampler, coord + vec2(1, -1) * stepSize).rgb;
    vec3 sw = texture(texSampler, coord + vec2(-1, 1) * stepSize).rgb;
    vec3 se = texture(texSampler, coord + vec2(1, 1) * stepSize).rgb;
    float lc = luma(center.rgb), lnw = luma(nw), lne = luma(ne);
    float lsw = luma(sw), lse = luma(se);
    float low = min(lc, min(min(lnw, lne), min(lsw, lse)));
    float high = max(lc, max(max(lnw, lne), max(lsw, lse)));
    if (high - low < max(0.0312, high * 0.125)) return center;
    vec2 direction = vec2(-(lnw + lne - lsw - lse), lnw + lsw - lne - lse);
    float reduce = max((lnw + lne + lsw + lse) * 0.03125, 0.0078125);
    direction = clamp(direction / (min(abs(direction.x), abs(direction.y)) + reduce),
                      vec2(-8.0), vec2(8.0)) * stepSize;
    vec3 inner = 0.5 * (texture(texSampler, coord + direction * (-1.0 / 6.0)).rgb +
                        texture(texSampler, coord + direction * (1.0 / 6.0)).rgb);
    vec3 outer = inner * 0.5 + 0.25 *
        (texture(texSampler, coord - direction * 0.5).rgb +
         texture(texSampler, coord + direction * 0.5).rgb);
    float lo = luma(outer);
    return vec4((lo < low || lo > high) ? inner : outer, center.a);
}
void main() {
    vec4 color_linear = pp.hdr ? texture(texSampler, uv) : smoothEdges(uv);
    if (pp.hdr) {
        color = color_linear;
    } else {
        if (pp.srgb_input) color_linear.rgb = degamma(color_linear.rgb);
        color = vec4(gamma(color_linear.rgb), color_linear.a);
    }
}
