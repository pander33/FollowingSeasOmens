// A falling star, drawn per pixel: the mesh is only a strip on the sky round the path, and the light
// is worked out from where a pixel lies against the path, in radians (uv0: d behind the head, x across,
// s from the start of the path). Across, a Gaussian whose width never falls below a fraction of a pixel,
// so the line is smooth at any resolution; along, a bright round head and a tail that fades
// exponentially. The same shape serves the trains (no head, a long tail) and the sparks (a head only).
//
// Built-in includes only (UnityCG), no URP package: URP draws a pass without a LightMode tag, as it
// draws Sprites/Default, and the matrices UnityCG reads are the ones URP sets.
Shader "Hidden/FollowingSeasOmens/Meteor"
{
	Properties
	{
		_Strength ("Strength", Float) = 1
	}

	SubShader
	{
		Tags { "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
		Blend One One
		ZWrite Off
		ZTest LEqual
		Cull Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#include "UnityCG.cginc"

			float _Strength;

			struct appdata
			{
				float4 vertex : POSITION;
				float4 color : TEXCOORD3; // tail colour times brightness (a UV channel: HDR, unlike vertex colours)
				float4 path : TEXCOORD0;  // d, x, s (radians), glow size (0 none, 1 a star)
				float4 shape : TEXCOORD1; // tail length, sigma, head weight, tail weight (radians, radians, -, -)
				float4 head : TEXCOORD2;  // head colour times brightness
				// Across the strip (-1..1), the way to its front and back ends in half widths, the glow's sigma
				// (radians, 0 none): everything fades to nothing at the strip's edges, so nothing is cut there.
				float4 edge : TEXCOORD4;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float4 color : TEXCOORD3;
				float4 path : TEXCOORD0;
				float4 shape : TEXCOORD1;
				float4 head : TEXCOORD2;
				float4 edge : TEXCOORD4;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.path = v.path;
				o.shape = v.shape;
				o.head = v.head;
				o.edge = v.edge;
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float d = i.path.x;
				float x = i.path.y;
				float s = i.path.z;
				float glowSize = i.path.w;
				float tl = max(i.shape.x, 1e-5);
				float s0 = i.shape.y;

				// Radians per pixel here: the line is never thinner than about one pixel.
				float px = max(fwidth(x), fwidth(d));
				float sig = sqrt(s0 * s0 + 0.5 * px * px);

				// The head: a hot core a few pixels across and a wide soft glow round it, drawn here rather
				// than left to a bloom the game may not have.
				float r2 = d * d + x * x;
				float sigC = max(sig * 1.8, 1.3 * px);
				float sigG = i.edge.w;
				float core = exp(-r2 / (2.0 * sigC * sigC));
				float glow = sigG > 0.0 ? 0.35 * exp(-r2 / (2.0 * sigG * sigG)) : 0.0;

				// The tail: thick and bright next to the head, a thread at its end, with a soft sheen round
				// its first part.
				float behind = max(d, 0.0);
				float near = exp(-behind / tl);
				float sigT = max(sig * lerp(0.6, 1.8, near), 0.7 * px);
				float start = smoothstep(-sig, sig, d) * smoothstep(0.0, 0.5 * tl, s);
				float tail = exp(-x * x / (2.0 * sigT * sigT)) * near * start;
				float sheen = glowSize > 0.0 ? 0.18 * exp(-x * x / (2.0 * 16.0 * sigT * sigT)) * near * near * start : 0.0;
				float f = 1.0 - near;
				// White-blue at the head, a hint of green towards the end of the tail.
				float3 tailColour = lerp(i.color.rgb, i.color.rgb * float3(0.75, 1.05, 0.88), f * f);

				float fade = (1.0 - smoothstep(0.5, 1.0, abs(i.edge.x))) * smoothstep(0.0, 0.5, i.edge.y) *
				             smoothstep(0.0, 0.5, i.edge.z);

				float3 c = (i.head.rgb * (core + glow) * i.shape.z + tailColour * (tail + sheen) * i.shape.w) * fade * _Strength;
				// Rolled off rather than cut at white, so the core stays a gradient where nothing blooms;
				// what is left above 1 still feeds a bloom where there is one.
				return float4(1.0 - exp(-c) + 0.25 * max(c - 1.0, 0.0), 1.0);
			}
			ENDCG
		}
	}
}
