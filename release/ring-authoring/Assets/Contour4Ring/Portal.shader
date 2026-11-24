Shader "Contour4/Portal"
{
    Properties
    {
        _MainTex ("Portal camera", 2D) = "black" {}
        _PortalDepth ("Portal depth", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+5" "RenderType" = "Opaque" }
        Cull Off
        HLSLINCLUDE
        #include "UnityCG.cginc"

        struct PortalVaryings
        {
            float4 position : SV_POSITION;
            float4 screen : TEXCOORD0;
        };

        sampler2D _MainTex;
        UNITY_DECLARE_DEPTH_TEXTURE(_PortalDepth);
        float4x4 _PortalInverseProjection;
        float4 _PortalFogColor;
        float4 _PortalFogSettings;
        float _PortalNear;
        float _FogMode;
        float4x4 _PortalClipToWorld;
        float3 _PortalEye;
        float3 _PortalCenter;
        float3 _PortalNormal;
        float3 _PortalRight;
        float3 _PortalUp;
        float4 _PortalHalfSize;

        PortalVaryings PortalVertex(appdata_img input)
        {
            PortalVaryings output;
            output.position = UnityObjectToClipPos(input.vertex);
            output.screen = ComputeScreenPos(output.position);
            return output;
        }

        float4 PortalFragment(PortalVaryings input) : SV_Target
        {
            return tex2Dproj(_MainTex, UNITY_PROJ_COORD(input.screen));
        }

        struct NearVaryings
        {
            float4 position : SV_POSITION;
            float4 screen : TEXCOORD0;
            float2 clipXY : TEXCOORD1;
        };

        NearVaryings NearVertex(appdata_img input)
        {
            NearVaryings output;
            output.clipXY = input.vertex.xy * 2;
            output.position = float4(output.clipXY, UNITY_NEAR_CLIP_VALUE * 0.999999, 1);
            output.screen = ComputeScreenPos(output.position);
            return output;
        }

        float4 NearFragment(NearVaryings input) : SV_Target
        {
            float4 nearWorld = mul(_PortalClipToWorld, float4(input.clipXY, UNITY_NEAR_CLIP_VALUE, 1));
            float3 ray = nearWorld.xyz / nearWorld.w - _PortalEye;
            float facing = dot(ray, _PortalNormal);
            clip(facing - 0.000001);
            float fraction = dot(_PortalCenter - _PortalEye, _PortalNormal) / facing;
            clip(min(fraction, 1 - fraction));
            float3 offset = _PortalEye + ray * fraction - _PortalCenter;
            float2 across = float2(dot(offset, _PortalRight), dot(offset, _PortalUp));
            float2 remaining = _PortalHalfSize.xy - abs(across);
            clip(min(remaining.x, remaining.y));
            return tex2Dproj(_MainTex, UNITY_PROJ_COORD(input.screen));
        }

        float4 FogFragment(v2f_img input) : SV_Target
        {
            float2 uv = input.uv;
            float4 color = tex2D(_MainTex, uv);
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_PortalDepth, uv);
            float2 clipXY = uv * 2 - 1;
            #if UNITY_UV_STARTS_AT_TOP
                clipXY.y = -clipXY.y;
            #endif
            #if defined(UNITY_REVERSED_Z)
                bool sky = rawDepth <= 0.000001;
                float clipZ = rawDepth;
            #else
                bool sky = rawDepth >= 0.999999;
                float clipZ = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
            #endif
            if (sky && _PortalFogSettings.w > 0)
                return color;

            float4 view = mul(_PortalInverseProjection, float4(clipXY, clipZ, 1));
            float distance = max(0, -view.z / view.w - _PortalNear);
            float transmission;
            if (_FogMode == 1)
                transmission = (_PortalFogSettings.y - distance) /
                    max(_PortalFogSettings.y - _PortalFogSettings.x, 0.0001);
            else
            {
                float opticalDepth = _PortalFogSettings.z * distance;
                transmission = exp2(_FogMode == 2 ? -opticalDepth : -opticalDepth * opticalDepth);
            }
            return lerp(_PortalFogColor, color, saturate(transmission));
        }
        ENDHLSL

        Pass
        {
            ZWrite On
            HLSLPROGRAM
            #pragma vertex PortalVertex
            #pragma fragment PortalFragment
            ENDHLSL
        }

        Pass
        {
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment FogFragment
            ENDHLSL
        }

        Pass
        {
            ZWrite On
            HLSLPROGRAM
            #pragma vertex NearVertex
            #pragma fragment NearFragment
            ENDHLSL
        }
    }
}
