float4x4 Transform;
float4 Color;
float Intensity;


texture2D Tex0;
sampler Tex0Sampler = sampler_state {
    Texture = Tex0;
};


void GetVertex(
    inout float4 position : SV_Position,
    inout float2 texCoord : TEXCOORD0,
    inout float4 color : COLOR0
) {
    position = mul(position, Transform);
}



float4 GetPixel(
    float2 texCoord : TEXCOORD0,
    float4 color : COLOR0
) : SV_Target0 {
    float4 c1 = tex2D(Tex0Sampler, texCoord);
    float4 c2 = tex2D(Tex0Sampler, texCoord);
    float avg = c2.x + c2.y + c2.z;
    avg /= 3;
    c2.x = avg;
    c2.y = avg;
    c2.z = avg;
    return lerp(c1, c2, Intensity); // Lerp works inversely?
}


technique Main
{
    pass
    {
        Sampler[0] = Tex0Sampler;
        VertexShader = compile vs_3_0 GetVertex();
        PixelShader = compile ps_3_0 GetPixel();
    }
}
