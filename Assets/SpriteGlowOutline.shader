Shader "Custom/SpriteGlowOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
        
        [Header(Toggle Settings)]
        [Toggle] _UseOutline ("Enable Outline", Float) = 0.0

        [Header(Outline Settings)]
        _OutlineColor ("Outline Color", Color) = (1, 1, 0, 1)
        _OutlineWidth ("Outline Max Width", Range(0, 16)) = 4.0
        _OutlineFading ("Fading Falloff Strength", Range(0.1, 5.0)) = 1.0
        _AlphaThreshold ("Sprite Alpha Threshold", Range(0.0, 1.0)) = 0.1
    }

    SubShader
    {
        Tags
        { 
            "Queue"="Transparent" 
            "IgnoreProjector"="True" 
            "RenderType"="Transparent" 
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float4 worldPosition : TEXCOORD1;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };
            
            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _ClipRect;

            fixed4 _OutlineColor;
            float _UseOutline;
            float _OutlineWidth;
            float _OutlineFading;
            float _AlphaThreshold;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.worldPosition = IN.vertex;
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 baseColor = tex2D(_MainTex, IN.texcoord);
                
                if (_UseOutline < 0.5)
                {
                    fixed4 standardColor = baseColor * IN.color;
                    standardColor.rgb *= standardColor.a; // Premultiply alpha
                    #ifdef UNITY_UI_CLIP_RECT
                        standardColor *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                        clip(standardColor.a - 0.001);
                    #endif
                    return standardColor;
                }

                // IF OUTLINE IS ENABLED: Execute outline calculation logic
                if (baseColor.a >= 0.95)
                {
                    fixed4 solidColor = baseColor * IN.color;
                    solidColor.rgb *= solidColor.a;
                    #ifdef UNITY_UI_CLIP_RECT
                        solidColor *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                        clip(solidColor.a - 0.001);
                    #endif
                    return solidColor;
                }

                float maxDistance = max(0.1, _OutlineWidth);
                float minAlphaDist = 999.0; 

                [unroll(33)] 
                for (int x = -16; x <= 16; x++)
                {
                    if (abs(x) > maxDistance) continue;

                    for (int y = -16; y <= 16; y++)
                    {
                        if (abs(y) > maxDistance) continue;

                        float2 offset = float2(x, y);
                        float dist = length(offset);
                        
                        if (dist <= maxDistance)
                        {
                            float2 sampleUV = IN.texcoord + offset * _MainTex_TexelSize.xy;
                            fixed4 neighbor = tex2D(_MainTex, sampleUV);
                            
                            if (neighbor.a > _AlphaThreshold)
                            {
                                minAlphaDist = min(minAlphaDist, dist);
                            }
                        }
                    }
                }

                if (minAlphaDist <= maxDistance)
                {
                    float normDistance = minAlphaDist / maxDistance;
                    float effectStrength = 1.0 - normDistance;
                    effectStrength = pow(effectStrength, _OutlineFading);

                    fixed4 outline = _OutlineColor * effectStrength;

                    fixed4 finalColor;
                    finalColor.rgb = baseColor.rgb + outline.rgb * (1.0 - baseColor.a);
                    finalColor.a = max(baseColor.a, outline.a);

                    finalColor.rgb *= finalColor.a;
                    finalColor *= IN.color;
                    #ifdef UNITY_UI_CLIP_RECT
                        finalColor *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                        clip(finalColor.a - 0.001);
                    #endif
                    return finalColor;
                }

                fixed4 transparentColor = baseColor * IN.color;
                transparentColor.rgb *= transparentColor.a;
                #ifdef UNITY_UI_CLIP_RECT
                    transparentColor *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(transparentColor.a - 0.001);
                #endif
                return transparentColor;
            }
        ENDCG
        }
    }
}
