// Titanfall 2 post composite (CBufEnginePost), translated from the ps_5_0 disassembly.
// Sharpen -> cloak aberration -> DoF -> bloom -> exposure -> filmic curve -> view fade -> sRGB 3D LUT -> fade to black.
// Vanilla writes linear color through an sRGB view of the swapchain; the HUD is drawn on top afterwards.

#include "./shared.h"

cbuffer CBufEnginePost : register(b1) {
  float c_bloomAmount : packoffset(c0.x);
  float3 c_viewFadeScale : packoffset(c0.y);
  float3 c_viewFadeBias : packoffset(c1.x);
  float c_fadeToBlackFactor : packoffset(c1.w);
  float3 c_colorCorrectionVolumeWeights : packoffset(c2.x);
  float2 c_cloakAberrationScale : packoffset(c4.x);
  float c_cloakBrightenScale : packoffset(c4.z);
  float c_cloakBrightenBias : packoffset(c4.w);
  float3 c_cloakChromaTints : packoffset(c5.x);
  float c_cloakDesaturate : packoffset(c5.w);
  float c_bloomExponentPost : packoffset(c10.z);
  float c_wideBloomAmount : packoffset(c10.w);
  float c_wideBloomExponentPost : packoffset(c11.x);
  float c_streakBloomAmount : packoffset(c11.y);
  float c_streakBloomExponentPost : packoffset(c11.z);
  float c_sharpenAmount : packoffset(c11.w);
  float c_sharpenWidth : packoffset(c12.x);
  float c_sharpenThreshold : packoffset(c12.y);
  float c_debugTonemapEnableTweaks : packoffset(c12.z);
  float c_debugTonemapDisable : packoffset(c12.w);
  float c_debugTonemapToe : packoffset(c13.x);
  float c_debugTonemapMid1 : packoffset(c13.y);
  float c_debugTonemapMid2 : packoffset(c13.z);
  float c_debugTonemapShoulder : packoffset(c13.w);
  float2 c_postprocessMaxUVs : packoffset(c14.z);
  float2 c_postprocessTinyMaxUVs : packoffset(c15.z);
  float2 c_fbMaxUVs : packoffset(c16.x);
  int2 c_fbMaxScreenPos : packoffset(c16.z);
}

cbuffer CBufCommonPerCamera : register(b2) {
  float2 c_rcpRenderTargetSize : packoffset(c29.x);
  float c_forceExposure : packoffset(c32.w);
  float2 c_framebufferViewportScale : packoffset(c35.x);
}

SamplerState BaseTextureSampler : register(s0);
SamplerState FBTextureSampler : register(s1);
SamplerState ColorCorrectionVolumeTexture0Sampler : register(s3);
SamplerState WideBloomTextureSampler : register(s8);
SamplerState StreakBloomTextureSampler : register(s9);
SamplerState DoFBlurSmallTextureSampler : register(s10);

Texture2D<float4> BaseTexture : register(t0);
Texture2D<float4> FBTexture : register(t1);
Texture3D<float4> ColorCorrectionVolumeTexture0 : register(t3);
Texture2D<float4> WideBloomTexture : register(t8);
Texture2D<float4> StreakBloomTexture : register(t9);
Texture2D<float4> DoFBlurSmallTexture : register(t10);
Texture2D<float4> CoCTexture : register(t11);
Texture2D<float4> exposureTexture : register(t30);

void main(
    float2 v0 : TEXCOORD0,
    float4 v1 : SV_Position0,
    out float4 o0 : SV_Target0) {
  const float3 LUMA_WEIGHTS = float3(0.299f, 0.587f, 0.114f);

  float3 bloom_base = BaseTexture.Sample(BaseTextureSampler, min(v0, c_postprocessMaxUVs)).rgb;

  int2 pixel = int2(v1.xy);
  float4 scene = FBTexture.Load(int3(pixel, 0));

  // Luma sharpen
  float center_luma = dot(scene.rgb, LUMA_WEIGHTS);
  float2 sharpen_offset = c_rcpRenderTargetSize * c_sharpenWidth / c_framebufferViewportScale;
  float luma_pp = dot(FBTexture.Sample(FBTextureSampler, min(v0 + sharpen_offset, c_fbMaxUVs)).rgb, LUMA_WEIGHTS);
  float luma_np = dot(FBTexture.Sample(FBTextureSampler, min(v0 + sharpen_offset * float2(-1.f, 1.f), c_fbMaxUVs)).rgb, LUMA_WEIGHTS);
  float luma_nn = dot(FBTexture.Sample(FBTextureSampler, min(v0 - sharpen_offset, c_fbMaxUVs)).rgb, LUMA_WEIGHTS);
  float luma_pn = dot(FBTexture.Sample(FBTextureSampler, min(v0 + sharpen_offset * float2(1.f, -1.f), c_fbMaxUVs)).rgb, LUMA_WEIGHTS);
  float min_luma = min(min(min(min(center_luma, luma_pp), luma_np), luma_nn), luma_pn);
  float max_luma = max(max(max(max(center_luma, luma_pp), luma_np), luma_nn), luma_pn) + 0.001f;
  if (c_sharpenThreshold < min_luma / max_luma) {
    float neighbor_delta = (luma_np + luma_pp + luma_nn + luma_pn) * 0.25f - center_luma;
    float sharpened_luma = center_luma - neighbor_delta * c_sharpenAmount;
    scene.rgb *= sharpened_luma / (center_luma + 0.001f);
  }

  // Pilot cloak chromatic aberration (scene alpha is the cloak mask)
  float cloak = 1.f - scene.a;
  if (0.f < cloak) {
    float2 aberration = cloak * c_cloakAberrationScale;
    int2 pixel_r = min(pixel + int2(aberration * float2(1.f, 0.f)), c_fbMaxScreenPos);
    int2 pixel_g = min(pixel + int2(aberration * float2(-0.5f, -0.86603f)), c_fbMaxScreenPos);
    int2 pixel_b = min(pixel + int2(aberration * float2(-0.5f, 0.86603f)), c_fbMaxScreenPos);
    float3 cloaked = FBTexture.Load(int3(pixel_r, 0)).rgb * c_cloakChromaTints.x
                     + FBTexture.Load(int3(pixel_g, 0)).rgb * c_cloakChromaTints.y
                     + FBTexture.Load(int3(pixel_b, 0)).rgb * c_cloakChromaTints.z;
    cloaked = cloaked * c_cloakBrightenScale + c_cloakBrightenBias;
    cloaked = lerp(cloaked, dot(cloaked, LUMA_WEIGHTS), c_cloakDesaturate);
    scene.rgb = lerp(scene.rgb, cloaked, saturate(cloak));
  }

  // Depth of field
  float4 dof = DoFBlurSmallTexture.Sample(DoFBlurSmallTextureSampler, min(v0 - c_rcpRenderTargetSize, c_postprocessMaxUVs))
               + DoFBlurSmallTexture.Sample(DoFBlurSmallTextureSampler, min(v0 + c_rcpRenderTargetSize, c_postprocessMaxUVs));
  float coc = max(CoCTexture.Load(int3(pixel, 0)).x, 0.f);
  float dof_blend = min(max(abs(dof.a * 0.5f), coc) * 4.f, 1.f);
  float3 color = lerp(scene.rgb, dof.rgb * 0.5f, dof_blend);

  // Bloom
  float3 wide_bloom = WideBloomTexture.Sample(WideBloomTextureSampler, min(v0, c_postprocessTinyMaxUVs)).rgb;
  float3 streak_bloom = StreakBloomTexture.Sample(StreakBloomTextureSampler, v0).rgb;
  float3 bloom = c_bloomAmount * pow(abs(bloom_base * 0.5f), c_bloomExponentPost)
                 + c_wideBloomAmount * pow(abs(wide_bloom), c_wideBloomExponentPost)
                 + c_streakBloomAmount * pow(abs(streak_bloom), c_streakBloomExponentPost);
  color += bloom * CUSTOM_BLOOM;

  float exposure = (0.f < c_forceExposure) ? c_forceExposure : exposureTexture.Load(int3(0, 0, 0)).x;
  float3 untonemapped = color * exposure;

  // Vanilla filmic curve (per channel, asymptote 1.0)
  float3 tonemapped;
  if (c_debugTonemapDisable != 0.f) {
    tonemapped = untonemapped;
  } else if (c_debugTonemapEnableTweaks != 0.f) {
    tonemapped = (untonemapped * (c_debugTonemapMid1 * untonemapped + c_debugTonemapToe))
                 / (untonemapped * (c_debugTonemapMid1 * untonemapped + c_debugTonemapShoulder) + c_debugTonemapMid2);
  } else {
    tonemapped = (untonemapped * (10.f * untonemapped + 0.3f))
                 / (untonemapped * (10.f * untonemapped + 0.5f) + 1.5f);
  }

  // View fade, then the 32^3 color correction LUT (sRGB-encoded input, sRGB texture so samples return linear)
  float3 faded = tonemapped * c_viewFadeScale + c_viewFadeBias;
  float3 lut_input = (faded <= 0.003131f) ? (faded * 12.92f) : (1.055f * pow(abs(faded), 1.f / 2.4f) - 0.055f);
  float3 lut_color = ColorCorrectionVolumeTexture0.Sample(ColorCorrectionVolumeTexture0Sampler, lut_input * 0.96875f + 0.015625f).rgb;
  float3 graded = faded * saturate(1.f - c_colorCorrectionVolumeWeights.x) + lut_color * c_colorCorrectionVolumeWeights.x;

  // RenoDX: the vanilla curve is a smooth analytic tonemap, so the graded result is a trustworthy SDR reference.
  // Vanilla relied on the RGBA8 UNORM write to clamp; the upgraded buffer is linear float.
  float3 output_color;
  if (RENODX_TONE_MAP_TYPE == 0.f) {
    output_color = saturate(graded);
  } else {
    output_color = renodx::draw::ToneMapPass(untonemapped, graded);
  }
  output_color *= c_fadeToBlackFactor;

  o0 = float4(renodx::draw::RenderIntermediatePass(output_color), 1.f);
}
