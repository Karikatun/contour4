Shader "Contour4/Room surface wear"
{
    Properties
    {
        _Color ("Finish colour", Color) = (1, 1, 1, 1)
        _MainTex ("Existing albedo", 2D) = "white" {}
        _Metallic ("Existing metallic", Range(0, 1)) = 0
        _Glossiness ("Existing smoothness", Range(0, 1)) = .2
        [NoScaleOffset] _WearMap ("Finish texture", 2D) = "gray" {}
        [NoScaleOffset] _BloodAtlas ("Contact marks", 2D) = "black" {}
        _WearKind ("Concrete, plaster, enamel, wood, plywood edge, polymer, metal, painted wall", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "DisableBatching" = "True" }
        LOD 250

        CGPROGRAM
        #pragma surface Surface Standard fullforwardshadows vertex:Vertex
        #pragma target 3.5

        sampler2D _MainTex;
        sampler2D _WearMap;
        sampler2D _BloodAtlas;
        fixed4 _Color;
        half _Metallic, _Glossiness, _WearKind;
        float4x4 _WearFrame, _WearInverseFrame;
        float4 _WearMin, _WearMax, _WearProfile, _WearOil, _WearTrace;
        float4 _WallChipA, _WallChipB, _WallChipC;
        float4 _WallUpA, _WallUpB, _WallFromA, _WallFromB, _WallStation;
        float3 _WallAcross, _WallCenter;
        float _BloodCount;
        float4 _BloodCentres[6], _BloodHorizontal[6], _BloodVertical[6];

        struct Input
        {
            float2 uv_MainTex;
            float3 wearPosition;
            float3 wearNormal;
        };

        void Vertex(inout appdata_full vertex, out Input input)
        {
            UNITY_INITIALIZE_OUTPUT(Input, input);
            input.wearPosition = mul(_WearFrame, vertex.vertex).xyz;
            input.wearNormal = mul(vertex.normal, (float3x3)_WearInverseFrame);
        }

        half3 ProjectionWeights(half3 direction)
        {
            half3 weight = abs(direction);
            weight *= weight;
            weight *= weight;
            return weight / max(.001, weight.x + weight.y + weight.z);
        }

        half Grain(float3 position, half3 weight, float scale)
        {
            float3 u = position * scale;
            half3 taps = half3(
                tex2D(_WearMap, u.zy).g,
                tex2D(_WearMap, u.xz).g,
                tex2D(_WearMap, u.xy).g);
            // The measured scan ranges below are in linear light in either project colour space.
            #if defined(UNITY_COLORSPACE_GAMMA)
                taps = GammaToLinearSpace(taps);
            #endif
            return dot(weight, taps);
        }

        float RandomCell(float2 cell)
        {
            float3 key = frac(float3(cell.x, cell.y, cell.x) * float3(.1031,.1030,.0973));
            key += dot(key, key.yzx + 33.33);
            return frac((key.x + key.y) * key.z);
        }

        float ValueNoise(float2 coordinates)
        {
            float2 cell = floor(coordinates);
            float2 f = frac(coordinates);
            f = f * f * (3 - 2 * f);
            return lerp(lerp(RandomCell(cell), RandomCell(cell + float2(1,0)), f.x),
                lerp(RandomCell(cell + float2(0,1)), RandomCell(cell + 1), f.x), f.y);
        }

        float2 SurfacePlane(float3 position, half3 normal)
        {
            half3 direction = abs(normal);
            if (direction.y >= max(direction.x, direction.z)) return position.xz;
            return direction.x > direction.z ? position.zy : position.xy;
        }

        half CoarseNoise(float3 position, half3 weight, float2 scale, float offset)
        {
            // Most walls, decks and panels use one projection. Blended projections
            // carry the same noise across a smooth shell's normal transitions.
            half result = 0;
            [branch] if (max(weight.x, max(weight.y, weight.z)) > .998)
                result = ValueNoise(SurfacePlane(position, weight) * scale + offset);
            else
                result = dot(weight, half3(
                    ValueNoise(position.zy * scale + offset),
                    ValueNoise(position.xz * scale + offset),
                    ValueNoise(position.xy * scale + offset)));
            return result;
        }

        half SignedScan(half sampleValue, half middle, half range)
        {
            return clamp((sampleValue - middle) / range, -1, 1);
        }

        half ConcreteAggregate(float2 plane, float2 pixelWidth)
        {
            const float grainsPerMetre = 25;
            float2 grid = plane * grainsPerMetre;
            float2 cell = floor(grid);
            float tone = RandomCell(cell + float2(3.7,8.4));
            float2 centre = .20 + .60 * float2(RandomCell(cell + 2.1), RandomCell(cell - 7.8));
            float2 offset = frac(grid) - centre;
            float shear = tone - .5;
            float2 facets = abs(float2(offset.x + .67 * shear * offset.y, offset.y - .77 * shear * offset.x));
            facets *= float2(.75 + tone * .57, 1.16 - tone * .43);
            float edge = max(facets.x, facets.y) + .34 * min(facets.x, facets.y);
            float radius = .08 + .17 * RandomCell(cell + float2(19.3,4.5));
            float footprint = length(pixelWidth) * grainsPerMetre;
            float blend = clamp(footprint * .40, .025, .105);
            half stone = 1 - smoothstep(radius - blend, radius + blend, edge);
            half resolved = 1 - smoothstep(.32, .88, footprint);
            return stone * resolved * (tone * 1.32 - .62);
        }

        half ProjectedAggregate(float3 position, float3 pixelWidth, half3 weight)
        {
            half result = 0;
            [branch] if (max(weight.x, max(weight.y, weight.z)) > .998)
                result = ConcreteAggregate(SurfacePlane(position, weight), SurfacePlane(pixelWidth, weight));
            else
                result = dot(weight, half3(
                    ConcreteAggregate(position.zy, pixelWidth.zy),
                    ConcreteAggregate(position.xz, pixelWidth.xz),
                    ConcreteAggregate(position.xy, pixelWidth.xy)));
            return result;
        }

        half WoodGrain(float3 position, half3 normal, float woodAxis, out half endGrain)
        {
            bool alongX = woodAxis > 0;
            float along = alongX ? position.x : position.z;
            float crossGrain = abs(normal.y) > .63 ? (alongX ? position.z : position.x) : position.y;
            float warp = ValueNoise(float2(along * 3.2, crossGrain * 9.4));
            float broad = ValueNoise(float2(along * 2.1, crossGrain * 21.0));
            float knot = ValueNoise(float2(along * 4.8, crossGrain * 5.2));
            float flow = crossGrain * 34.0 + .73 * warp + .68 * broad + .46 * knot;
            endGrain = smoothstep(.64, .89, abs(alongX ? normal.x : normal.z));
            float2 crossSection = alongX ? position.zy : position.xy;
            float axialRing = length((crossSection + float2(.31,-.12)) * float2(1.0,1.28)) * 40.0 + .74*knot;
            flow = lerp(flow, axialRing, endGrain);
            if (_WearKind > 4.5) flow = position.y * 64.0 + .17 * warp;
            float outline = sin(flow * 6.2831853);
            float retained = 1 - smoothstep(.08,.48,fwidth(flow));
            float fibre = ValueNoise(float2(along * 9.0, crossGrain * 98.0)) * 2 - 1;
            return clamp((broad * 2 - 1) * .59 + outline * .35 * retained + fibre * .21, -1, 1);
        }

        // A cell-based scratch varies in position, slant and length between neighbouring patches.
        half Scuffs(float2 metres, float phase)
        {
            float2 cell = floor(metres);
            float2 u = frac(metres);
            float variation = frac(dot(cell, float2(.3251, .9173)) + phase * .173);
            float y = u.y - .24 - .46 * variation + (u.x - .5) * (variation - .48) * .43;
            float width = .006 + .009 * variation + fwidth(y) * .55;
            half stroke = 1 - smoothstep(width * .14, width, abs(y));
            half span = smoothstep(.07, .31, u.x) * (1 - smoothstep(.35 + .27 * variation, .92, u.x));
            return stroke * span * smoothstep(.28, .72, variation);
        }

        float EdgeOnSurface(half3 normal, float3 distance)
        {
            half3 facing = abs(normal);
            if (facing.y >= max(facing.x, facing.z)) return min(distance.x, distance.z);
            if (facing.x >= facing.z) return min(distance.y, distance.z);
            return min(distance.x, distance.y);
        }

        half2 ContactMarks(float3 position, half3 normal)
        {
            half2 mark = 0;
            float3 dx = ddx(position), dy = ddy(position);
            [branch] if (_BloodCount > .5)
            {
                [loop] for (int index = 0; index < min(6, (int)_BloodCount); index++)
                {
                    float3 delta = position - _BloodCentres[index].xyz;
                    float3 right = _BloodHorizontal[index].xyz;
                    float3 up = _BloodVertical[index].xyz;
                    float3 face = normalize(cross(right, up));
                    float2 uv = float2(dot(delta, right), dot(delta, up)) + .5;
                    float2 du = float2(dot(dx, right), dot(dx, up));
                    float2 dv = float2(dot(dy, right), dot(dy, up));
                    if (dot(face, normal) < .94 || abs(dot(delta, face)) > .015 ||
                        any(uv <= 0) || any(uv >= 1)) continue;
                    float variant = _BloodVertical[index].w;
                    float2 grid = float2(3.0, 2.0);
                    float2 tile = float2(fmod(variant, 3.0), floor(variant / 3.0));
                    float2 atlasUv = (tile + .0625 + uv * .875) / grid;
                    half4 contact = tex2Dgrad(_BloodAtlas, atlasUv, du * .875 / grid, dv * .875 / grid);
                    half opacity = contact.a * _BloodCentres[index].w;
                    if (opacity > mark.x) mark = half2(opacity, contact.r);
                }
            }
            return mark;
        }


        float SpallDistance(float2 plane, float4 patch)
        {
            float2 d = (plane - patch.xy) / patch.zw;
            float2 slant = abs(float2(d.x + .38 * d.y, d.y + .08 * d.x));
            float main = max(slant.x + .34 * slant.y, slant.y + .49 * slant.x);
            float2 tear = abs(d - float2(.49, .52));
            float fissure = max(tear.x * 2.9 + tear.y * .57, tear.y * 1.49 + tear.x);
            float flake = length((d - float2(-.91, -.32)) * float2(5.9, 4.8));
            return min(main, min(fissure, flake)) - .80;
        }

        void PaintedPlaster(float3 p, half3 n, inout SurfaceOutputStandard output)
        {
            float3 roomPosition = p;
            half3 roomNormal = n;
            bool floorProjection = dot(_WallUpA.xyz, _WallUpA.xyz) > .5;
            if (floorProjection && _WallStation.z > 1.5)
            {
                // The two Ring bridge closures are single meshes beside circular decks.
                float3 ray = p - _WallCenter;
                float depth = dot(ray, _WallAcross);
                ray -= _WallAcross * depth;
                float distanceToCenter = length(ray);
                float3 radialUp = -ray / max(.0001, distanceToCenter);
                float3 sampleRay = _WallFromA.xyz;
                float angle = atan2(dot(ray, cross(_WallAcross, sampleRay)), dot(ray, sampleRay));
                p = float3(angle * _WallStation.w, _WallStation.w - distanceToCenter, depth);
                n = half3(dot(n, cross(radialUp, _WallAcross)), dot(n, radialUp), dot(n, _WallAcross));
            }
            else if (floorProjection)
            {
                float4 homogeneous = float4(p, 1);
                float travel = 0;
                bool swept = _WallStation.z > .5;
                if (swept)
                {
                    float fromA = dot(_WallFromA, homogeneous);
                    float fromB = dot(_WallFromB, homogeneous);
                    travel = saturate(fromA / max(.0001, fromA + fromB));
                }
                float3 up = normalize(lerp(_WallUpA.xyz, _WallUpB.xyz, travel));
                float3 across = swept ? normalize(lerp(_WallFromA.xyz, -_WallFromB.xyz, travel)) : _WallAcross;
                float3 depth = cross(across, up);
                float along = swept ? lerp(_WallStation.x, _WallStation.y, travel) : dot(p, across);
                p = float3(along, lerp(dot(_WallUpA, homogeneous), dot(_WallUpB, homogeneous), travel), dot(p, depth));
                n = half3(dot(n, across), dot(n, up), dot(n, depth));
            }
            // Bands follow the finished floor and agree across shared corners and jambs.
            float height = p.y;
            float aa = max(.0015, fwidth(height) * .63);
            float edge = (ValueNoise(p.xz * 13.0) - .5) * .013;
            half lower = 1 - smoothstep(1.53 + edge - aa, 1.53 + edge + aa, height);
            half plinth = 1 - smoothstep(.158 - edge * .55 - aa, .158 - edge * .55 + aa, height);
            half3 color = lerp(half3(.752, .731, .675), half3(.430, .454, .350), lower);
            color = lerp(color, half3(.310, .206, .169), plinth);

            bool alongX = _WearProfile.w > 0;
            float2 uv = alongX ? p.xy : p.zy;
            float2 surface = uv + float2(_WearProfile.z, alongX ? p.z : p.x) * float2(.44, .41);
            float2 revealUV = abs(n.y) > .7 ? p.xz : (alongX ? p.zy : p.xy);
            half face = smoothstep(.18, .90, abs(alongX ? n.z : n.x));
            float2 grainUV = face > .5 ? surface : revealUV;
            half scan = tex2D(_WearMap, grainUV * .94).g;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                scan = LinearToGammaSpaceExact(scan);
            #endif
            half pores = clamp((scan - .632) * 5.6, -1, 1);
            half roller = ValueNoise(grainUV * float2(6.2, 18.4)) - .5;
            half chalk = ValueNoise(grainUV * 3.31 + 7.1) - .5;
            half baseDust = (1 - smoothstep(.015, .29, height)) *
                (.025 + .10 * ValueNoise(grainUV * float2(9.3, 2.6)));
            color *= 1 + .092 * pores + .038 * roller + .086 * chalk - baseDust;

            // The only large defects are the authored wear sites. The broken edge runs
            // through several scales so it reads as flakes of paint and exposed render.
            float distance;
            if (floorProjection)
            {
                float offset = _WearProfile.z + floor(p.z * 4 + .5) * .31;
                float cell = floor((uv.x + offset) / 3.2);
                float chance = RandomCell(float2(cell, offset));
                float2 centre = float2(cell * 3.2 + 1.25 + chance * .70 - offset,
                    .21 + 1.16 * RandomCell(float2(cell + 3.8, offset)));
                distance = chance < .51 ? SpallDistance(uv, float4(centre, .15 + .24 * chance, .18 + .42 * chance)) : 2;
            }
            else
                distance = min(SpallDistance(uv, _WallChipA),
                    min(SpallDistance(uv, _WallChipB), SpallDistance(uv, _WallChipC)));
            float fade = clamp(fwidth(distance) * .60, .018, .14);
            half torn = 0, fissure = 0;
            [branch] if (distance < .98 && face > .01)
            {
                distance += (ValueNoise(surface * 8.7 + .21) - .5) * .61
                    + (ValueNoise(surface * 24.3 - .89) - .5) * .37
                    + (ValueNoise(surface * 69.8 + 4.1) - .5) * .15;
                torn = (1 - smoothstep(-fade, fade, distance)) * face;
                half substrate = ValueNoise(surface * 23.1 + 13.93);
                half grains = ValueNoise(surface * 93.2 - 9.8);
                half3 plaster = lerp(half3(.520, .483, .429), half3(.681, .635, .568), substrate);
                plaster *= 1 + .14 * pores + .22 * (grains - .5);
                half islands = smoothstep(.72, .88, ValueNoise(surface * 31.1 + 6.4)) *
                    smoothstep(.036, .12, distance) * (1 - smoothstep(.16, .38, distance)) * face;
                half curled = smoothstep(.23, .77, ValueNoise(surface * 14.7 - 4.6));
                fissure = (1 - smoothstep(.010 + fade * .18, .065 + fade * .20, abs(distance + .023))) * face * curled;
                color = lerp(color, plaster, max(torn, islands * .82));
                color *= 1 - fissure * .24;
                half lip = (1 - smoothstep(.013, .072, abs(distance - .082))) * face * (1 - curled);
                color += half3(.072, .064, .055) * lip;
            }
            #if !defined(UNITY_COLORSPACE_GAMMA)
                color = GammaToLinearSpace(saturate(color));
            #endif
            half2 blood = ContactMarks(roomPosition, roomNormal);
            half3 pigment = lerp(half3(.115, .013, .024), half3(.40, .041, .060), blood.y);
            output.Albedo = lerp(color, pigment, blood.x);
            output.Metallic = 0;
            output.Smoothness = lerp(.121 + .074 * lower + .024 * plinth, .067, torn) + blood.x * .048;
            output.Occlusion = 1 - fissure * .22 - torn * .06;
            output.Alpha = 1;
        }

        void Surface(Input input, inout SurfaceOutputStandard output)
        {
            half3 original = tex2D(_MainTex, input.uv_MainTex).rgb * _Color.rgb;
            float3 p = input.wearPosition;
            half3 n = normalize(input.wearNormal);
            [branch] if (_WearKind > 7.5)
            {
                PaintedPlaster(p, n, output);
                return;
            }
            float3 varied = p + _WearProfile.z * float3(1.19, .67, -.87);
            half use = _WearProfile.x, service = _WearProfile.y;
            half concrete = 1 - step(1.5, _WearKind);
            half plaster = step(1.5, _WearKind) * (1 - step(2.5, _WearKind));
            half metal = step(2.5, _WearKind) * (1 - step(3.5, _WearKind));
            half wood = step(3.5, _WearKind) * (1 - step(5.5, _WearKind));
            half polymer = step(5.5, _WearKind) * (1 - step(6.5, _WearKind));
            half horizontal = smoothstep(.45, .85, abs(n.y));
            half3 weight = ProjectionWeights(n);
            float3 pixelWidth = fwidth(varied);
            half scan = .36, scanBroad = .36;
            if (max(concrete, max(plaster, metal)) > .5)
            {
                scan = Grain(varied, weight, lerp(1.01, 1.47, metal));
                scanBroad = Grain(varied + float3(2.35, -.71, 6.2), weight, .39);
            }
            half patches = CoarseNoise(varied, weight, 4.3, 2.61) * 2 - 1;
            half stain = saturate(.50 + patches * .39);
            half3 finish = original;

            float3 edgeDistance = max(0, min(p - _WearMin.xyz, _WearMax.xyz - p));
            float low = max(0, p.y - _WearMin.y);
            float top = max(0, _WearMax.y - p.y);
            float sideEdge = EdgeOnSurface(n, edgeDistance);
            half grime = (1 - smoothstep(.07, .72, low)) * smoothstep(.19, .52, stain);
            grime *= plaster * (1 - horizontal) * (.16 + .32 * service);
            half dust = (1 - smoothstep(.17, .80, sideEdge)) * smoothstep(.16, .46, stain);
            dust *= (.09 + .26 * service) * horizontal * max(concrete, metal);

            float contactHeight = min(.94, (_WearMax.y - _WearMin.y) * .60);
            half contact = 1 - smoothstep(.06, .31, abs(low - contactHeight));
            half handling = max(contact, 1 - smoothstep(.04, .21, top)) * (1 - horizontal);
            half nearEdge = 1 - smoothstep(.023, .13, sideEdge);
            half chip = 0, aggregate = 0, detail = 0;
            half metallic = _Metallic, smoothness = _Glossiness;
            if (concrete > .5)
            {
                detail = SignedScan(scan, .094, .013);
                half mottles = SignedScan(scanBroad, .094, .016);
                aggregate = ProjectedAggregate(varied, pixelWidth, weight);
                half mineral = dot(original, half3(.25,.60,.15));
                half3 tone = lerp(original, mineral * half3(1.06,1.04,.98), .58);
                half structure = .28 * detail + .105 * mottles + .132 * patches + .26 * aggregate;
                finish = tone * clamp(1 + structure, .66, 1.36);
                smoothness = .092 + .074 * saturate(.51 + .35*detail + .31*aggregate);
            }
            else if (plaster > .5)
            {
                detail = SignedScan(scan, .365, .067);
                half roller = CoarseNoise(varied, weight, float2(1.4,8.7), 11.4) * 2 - 1;
                half oldPaint = SignedScan(scanBroad, .365, .082);
                finish = original * clamp(1 + .15*detail + .082*oldPaint + .059*roller, .73, 1.27);
                half peel = max(nearEdge * .14, grime * .36) * smoothstep(.42, .75, detail*.84 + patches*.15);
                finish = lerp(finish, half3(.224,.212,.170) * (1 + .26*oldPaint), peel);
                smoothness = .15 + .067 * saturate(.47 + detail * .45);
            }
            else if (metal > .5)
            {
                detail = SignedScan(scan, .074, .014);
                half oxide = SignedScan(scanBroad, .074, .016);
                half brushed = CoarseNoise(varied, weight, float2(1.8,56), 4.18) * 2 - 1;
                half flakes = CoarseNoise(varied, weight, 7.1, 20.28) * 2 - 1;
                half edgeContact = 1 - smoothstep(.10, .24, sideEdge);
                half wearBand = max(nearEdge, handling * edgeContact * .72);
                half abrasion = .28*detail + .24*flakes + .18*handling + .28*nearEdge + .08*service;
                chip = smoothstep(.38, .66, abrasion) * wearBand * use;
                half3 steel = half3(.26,.281,.288) * (1 + .32 * brushed + .10 * flakes);
                half3 paint = original * clamp(1 + .19*detail + .063*oxide + .043*patches + .055*brushed, .72, 1.28);
                finish = lerp(paint, steel, chip);
                metallic = lerp(_Metallic, .43 + .15*brushed, chip);
                smoothness = lerp(.25 + .035*oxide + .023*brushed, .44 + .108*brushed, chip);
            }
            else if (wood > .5)
            {
                half endGrain;
                detail = WoodGrain(varied, n, _WearProfile.w, endGrain);
                half wornEdge = (1 - smoothstep(.011,.061,sideEdge)) * (.23 + .22*stain);
                half3 dark = original * half3(.60,.49,.42);
                half3 honey = original * half3(1.41,1.36,1.25) + half3(.032,.019,.010);
                half mixed = saturate(.46 + .65*detail + .052*patches);
                finish = lerp(dark, honey, mixed);
                finish = lerp(finish, honey * (.79 + .17*detail), wornEdge);
                finish *= 1 - endGrain * .14;
                smoothness = .22 + .092 * mixed;
            }
            else if (polymer > .5)
            {
                half stipple = CoarseNoise(varied, weight, 34, 17.3) * 2 - 1;
                half mould = CoarseNoise(varied, weight, float2(4.3, 18.3), 6.5) * 2 - 1;
                finish = original * (1 + .074 * stipple + .032 * mould);
                smoothness = saturate(_Glossiness * .74 + .032 + .045 * stipple);
            }
            else
            {
                half brushed = CoarseNoise(varied, weight, float2(2.8, 57), 4.18) * 2 - 1;
                half rolled = CoarseNoise(varied, weight, float2(6.4, 14.8), 13.7) * 2 - 1;
                finish = original * (1 + .092 * brushed + .041 * rolled);
                smoothness = clamp(_Glossiness + .076 * brushed + .022 * rolled, .12, .68);
            }
            finish *= 1 - grime;
            finish = lerp(finish, finish * .80 + half3(.059, .049, .035), dust);

            float2 rubPlane = abs(n.x) > abs(n.z) ? varied.zy : varied.xy;
            half scuff = Scuffs(lerp(rubPlane, varied.xz, horizontal) *
                lerp(float2(1.4, 3.2), float2(.85, 1.65), concrete), _WearProfile.z);
            half rub = scuff * use * (concrete * .52 + metal * handling * .74 + plaster * grime * .50 + wood * .22);
            finish = lerp(finish, finish * .65 + half3(.072, .072, .059), rub);

            half oil = 0;
            if (_WearOil.w > 0 && concrete > .5 && n.y > .75)
            {
                float2 d = (p.xz - _WearOil.xz) / _WearOil.w;
                float ellipse = length(float2(d.x * .76 + d.y * .36, d.y * 1.56 - d.x * .18));
                float lobe = length((d - float2(.48, .25)) * float2(1.42, 2.5));
                float pool = min(ellipse, lobe * .92) + detail * .10;
                oil = (1 - smoothstep(.57, 1.0, pool)) * (1 - smoothstep(.035, .085, abs(p.y - _WearOil.y)));
                half residue = (1 - smoothstep(.85, 1.19, pool)) * (1 - smoothstep(.035, .085, abs(p.y - _WearOil.y)));
                finish = lerp(finish, finish * half3(.45, .33, .20), residue * .32);
                finish = lerp(finish, finish * .34 + half3(.008, .006, .004), oil * .84);
            }

            if (_WearTrace.w > 0 && plaster > .5 && horizontal < .25)
            {
                float3 d = abs(p - _WearTrace.xyz);
                float across = abs(n.x) > abs(n.z) ? d.z : d.x;
                float depth = abs(n.x) > abs(n.z) ? d.x : d.z;
                float outer = max(across / _WearTrace.w, d.y / (_WearTrace.w * .73));
                half outline = (1 - smoothstep(.91, 1.01, outer)) * smoothstep(.77, .90, outer);
                outline *= 1 - smoothstep(.015, .07, depth);
                finish = lerp(finish, original * 1.19, outline * (.39 + detail * .13));
            }

            half2 blood = ContactMarks(p, n);
            half3 pigment = lerp(half3(.115, .013, .024), half3(.40, .041, .060), blood.y);
            output.Albedo = lerp(finish, pigment, blood.x);
            output.Metallic = saturate(metallic);
            output.Smoothness = saturate(smoothness - grime * .18 - dust * .19 + oil * .21 + blood.x * .048);
            output.Occlusion = 1 - concrete * .12 * max(0, -aggregate);
            output.Alpha = _Color.a;
        }

        ENDCG
    }
    Fallback "Standard"
}
