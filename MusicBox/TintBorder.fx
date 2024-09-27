sampler2D implicitInputSampler : register(S0);

float4 TintColor : register(c0);
float BorderThickness : register(c1);
float AspectRatio : register(c2);

float4 main(float2 uv : TEXCOORD0) : SV_TARGET
{
    float4 color = tex2D(implicitInputSampler, uv);
    float2 borderDist = min(uv, 1.0 - uv);
    return lerp(color, TintColor, pow(.5 * max((BorderThickness / AspectRatio) / borderDist.x, BorderThickness / borderDist.y), 1.5));
}
