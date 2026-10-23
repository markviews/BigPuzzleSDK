// BigPuzzleSDK: editor-only shader for the game model previews. Vertex colours (which is where
// Big Walk keeps nearly all its colour) with simple fixed lighting, so it looks the same in
// any render pipeline. Not used by anything that gets exported.
Shader "Hidden/BigPuzzleSDK/GamePreview" {
    Properties {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _VertexColor ("Use vertex colours", Float) = 1
        _MainTex ("Picture", 2D) = "white" {}
        _UseTexture ("Picture: 0 off, 1 multiply, 2 peg tile (white = ink)", Float) = 0
        [HideInInspector] _SrcBlend ("", Float) = 1
        [HideInInspector] _DstBlend ("", Float) = 0
        [HideInInspector] _ZWrite ("", Float) = 1
        [HideInInspector] _Cull ("", Float) = 2   // 2 = hide back faces, 0 = show both sides
    }
    SubShader {
        Tags { "RenderType" = "Opaque" }
        Pass {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _VertexColor;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _UseTexture;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float4 color : COLOR; float2 uv : TEXCOORD1; };

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = lerp(float4(1, 1, 1, 1), v.color, _VertexColor) * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                float3 n = normalize(i.normal);
                float3 sun = normalize(float3(0.35, 0.8, 0.45));
                float light = 0.45 + 0.55 * saturate(dot(n, sun)) + 0.1 * saturate(n.y);
                float4 c = i.color;
                if (_UseTexture > 1.5) {
                    // Peg tile sheets are masks: the drawing is white on black, and the game prints
                    // it as dark ink on the light tile face
                    float4 t = tex2D(_MainTex, i.uv);
                    float ink = max(t.r, max(t.g, t.b));
                    c.rgb *= 1.0 - ink * 0.92;
                } else if (_UseTexture > 0.5) {
                    c *= tex2D(_MainTex, i.uv);
                }
                return float4(c.rgb * light, c.a);   // (the picture's own see-through parts count too)
            }
            ENDCG
        }
    }
}
