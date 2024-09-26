sampler2D implicitInputSampler : register(S0);

float4 TintColor : register(c0);
float BorderThickness : register(c1);
float AspectRatio : register(c2);

float4 main(float2 uv : TEXCOORD0) : SV_TARGET
{
    float4 color = tex2D(implicitInputSampler, uv);

    // determine if the current pixel is within the border
    float2 borderDist = min(uv, 1.0 - uv);
    float2 adjustedBorderThickness = float2(BorderThickness / AspectRatio, BorderThickness);
    float isBorder = step(borderDist.x, adjustedBorderThickness.x) + step(borderDist.y, adjustedBorderThickness.y);

    return lerp(color, TintColor, saturate(isBorder));
}
