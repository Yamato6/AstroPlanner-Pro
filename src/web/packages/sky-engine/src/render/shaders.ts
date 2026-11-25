// GLSL shared by every layer. Positions are J2000 unit vectors; the vertex shader applies the
// frame matrix computed from C# (uFrame = J2000 → north/east/up) and the Core refraction model,
// then maps north/east/up to three.js world space (x = east, y = up, z = −north).

// Dome projection (mirrors render/projection.ts, domeProject): r(θ) = k·tan(θ/k) around the view
// direction, k = 1 perspective … 2 stereographic. Directions beyond uMaxTheta are culled: the
// vertex goes past the far plane and vCull makes the fragments of its segments/triangles discard.
const domeUniforms = /* glsl */ `
uniform mat3 uDomeBasis;       // rows: right, up, forward (world space)
uniform float uProjK;
uniform float uProjScale;      // clip units (y) per unit of r
uniform float uAspect;         // width / height
uniform float uMaxTheta;       // radians
`;

const domeProjection = /* glsl */ `
${domeUniforms}
varying float vCull;

vec4 domeClip(vec3 world) {
  vec3 d = uDomeBasis * normalize(world);
  float perpendicular = length(d.xy);
  float theta = atan(perpendicular, d.z);
  if (theta > uMaxTheta) {
    vCull = 1.0;
    return vec4(0.0, 0.0, 2.0, 1.0);
  }
  float r = uProjK * tan(theta / uProjK) * uProjScale;
  vec2 u = perpendicular > 1e-9 ? d.xy / perpendicular : vec2(0.0);
  return vec4(u.x * r / uAspect, u.y * r, 0.0, 1.0);
}
`;

// Sky brightness over the sky (mirrors render/photometry.ts; spec §8.2): Core's zenith levels
// spread with SkyBackground's laws — the site's glow brightened by air mass, Krisciunas & Schaefer
// moonlight, and twilight shaped by the scattering table (atmosphere.ts). Fluxes are 10^(−0.4·SQM).
const photometry = /* glsl */ `
uniform float uSiteFlux;
uniform float uNaturalFlux;
uniform float uTwilightFlux;
uniform float uMoonFlux;
uniform float uExtinction;
uniform float uBrightening;
uniform float uBrightnessOn;   // the sky brightness layer (off: a dark sky, no extinction)
uniform vec3 uSunNeu;          // Sun and Moon, north/east/up unit vectors
uniform vec3 uMoonNeu;
uniform sampler2D uAtmosphere; // scattering table: x = view altitude, y = azimuth from the Sun
uniform float uHasAtmosphere;
const float MULTIPLE_SCATTERING = 0.15;

float log10f(float x) { return log(x) * 0.4342944819; }

float kastenYoung(float altitude) {
  float shifted = altitude + 6.07995;
  if (shifted <= 0.0) return 1e4;
  float d = sin(radians(altitude)) + 0.50572 * pow(shifted, -1.6364);
  return d <= 0.0 ? 1e4 : 1.0 / d;
}

float siteGlow(float altitude) {
  return 1.0 + max(0.0, uBrightening) * (min(kastenYoung(max(altitude, 0.0)), 5.6) - 1.0);
}

float ksAirmass(float zenithDistance) {
  float s = sin(radians(zenithDistance));
  return inversesqrt(1.0 - 0.96 * s * s);
}

float ksScatter(float rho) {
  float c = cos(radians(rho));
  return 229086.765 * (1.06 + c * c) + pow(10.0, 6.15 - rho / 40.0);
}

float moonRatio(float rho, float zenithRho, float zenithDistance) {
  float toward = 1.0 - pow(10.0, -0.4 * uExtinction * ksAirmass(min(zenithDistance, 90.0)));
  float atZenith = 1.0 - pow(10.0, -0.4 * uExtinction);
  return atZenith <= 0.0 ? 0.0 : ksScatter(rho) / ksScatter(zenithRho) * toward / atZenith;
}

vec3 atmosphereAt(vec3 neu) {
  float altitude = degrees(asin(clamp(neu.z, -1.0, 1.0)));
  float az = atan(neu.y, neu.x);
  float sunAz = atan(uSunNeu.y, uSunNeu.x);
  float fromSun = abs(mod(degrees(az - sunAz) + 540.0, 360.0) - 180.0);
  vec2 uv = vec2(sqrt(clamp((altitude + 5.0) / 95.0, 0.0, 1.0)), fromSun / 180.0);
  return max(texture2D(uAtmosphere, uv).rgb, vec3(0.0));
}

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

// The parts of the sky's flux toward a direction: site (natural, artificial), moonlight (with
// a glare halo) and twilight; twilightColor = the scattering colour there.
void skyParts(vec3 neu, out float natural, out float artificial, out float moon, out float twilight, out vec3 twilightColor) {
  float altitude = degrees(asin(clamp(neu.z, -1.0, 1.0)));
  float glow = siteGlow(altitude);
  float site = uSiteFlux * glow;
  natural = min(site, uNaturalFlux * glow);
  artificial = site - natural;
  moon = 0.0;
  if (uMoonFlux > 0.0 && uMoonNeu.z > 0.0) {
    float rho = degrees(acos(clamp(dot(neu, uMoonNeu), -1.0, 1.0)));
    float moonAltitude = degrees(asin(clamp(uMoonNeu.z, -1.0, 1.0)));
    moon = uMoonFlux * (moonRatio(rho, 90.0 - moonAltitude, 90.0 - max(altitude, 0.0)) + 6.0 * exp(-rho / 2.0));
  }
  twilight = 0.0;
  twilightColor = vec3(0.0);
  if (uTwilightFlux > 0.0 && uHasAtmosphere > 0.5) {
    // Single scattering plus a diffuse share of the zenith light, a stand-in for multiple
    // scattering: it lights the Earth's shadow opposite a set Sun, which single scattering
    // leaves black.
    vec3 zenithLight = atmosphereAt(vec3(0.0, 0.0, 1.0));
    vec3 here = atmosphereAt(neu) + MULTIPLE_SCATTERING * zenithLight;
    float zenith = lum(zenithLight) * (1.0 + MULTIPLE_SCATTERING);
    twilight = uTwilightFlux * lum(here) / max(zenith, 1e-12);
    // Display white balance: single scattering leaves a sunset zenith lavender; photographs and
    // the eye see it blue (calibration by eye, not physics). The horizon keeps its own colours.
    vec3 balance = mix(vec3(1.0), vec3(0.6, 0.9, 1.15), smoothstep(5.0, 40.0, altitude));
    twilightColor = sqrt(here * balance);
  } else if (uTwilightFlux > 0.0) {
    twilight = uTwilightFlux;
  }
}

float skySqm(vec3 neu) {
  float natural; float artificial; float moon; float twilight; vec3 c;
  skyParts(neu, natural, artificial, moon, twilight, c);
  return -2.5 * log10f(max(natural + artificial + moon + twilight, 1e-30));
}

float nakedEyeLimit(float sqm) {
  return 7.93 - 5.0 * log10f(pow(10.0, 4.316 - sqm / 5.0) + 1.0);
}
`;

const common = /* glsl */ `
${domeProjection}
${photometry}
uniform mat3 uFrame;
uniform vec4 uRefraction;      // a, b, c, d of Saemundsson's formula
uniform float uRefractionMin;  // below this true altitude no refraction is applied
uniform float uApplyRefraction;
uniform float uMode;           // 0 dome (domeClip), 1 all-sky, 2 finder (gnomonic)
uniform mat3 uFinder;          // finder: rows east, north, center (J2000)
uniform vec2 uChartScale;      // all-sky: clip units per horizon radius; finder: per unit of tan(angle)

varying float vAltitude;
vec3 gNeu; // north/east/up direction of the last projected vertex (for the photometry)

vec3 apparentWorld(vec3 j2000) {
  vec3 neu = uFrame * j2000;
  gNeu = neu;
  float trueAlt = degrees(asin(clamp(neu.z, -1.0, 1.0)));
  float alt = trueAlt;
  if (uApplyRefraction > 0.5 && trueAlt >= uRefractionMin) {
    alt += max(0.0, (uRefraction.x / tan(radians(trueAlt + uRefraction.y / (trueAlt + uRefraction.z))) + uRefraction.w) / 60.0);
  }
  vAltitude = alt;
  float horizontal = length(neu.xy);
  vec2 direction = horizontal > 1e-9 ? neu.xy / horizontal : vec2(1.0, 0.0);
  vec3 n = vec3(direction * cos(radians(alt)), sin(radians(alt)));
  return vec3(n.y, n.z, -n.x);
}

// Clip position of a J2000 direction in the current view mode (mirrors render/projection.ts).
vec4 skyClip(vec3 j2000) {
  vCull = 0.0;
  gNeu = uFrame * j2000;
  if (uMode > 1.5) {
    // Gnomonic: w = cos(distance from the center), so the GPU clips the far hemisphere itself.
    vec3 p = uFinder * j2000;
    vAltitude = 90.0;
    return vec4(-p.x * uChartScale.x, p.y * uChartScale.y, 0.0, p.z);
  }
  vec3 world = apparentWorld(j2000);
  if (uMode > 0.5) {
    // Zenithal equidistant, north up, east left.
    vec2 northEast = vec2(-world.z, world.x);
    float horizontal = length(northEast);
    vec2 direction = horizontal > 1e-9 ? northEast / horizontal : vec2(1.0, 0.0);
    float r = (90.0 - vAltitude) / 90.0;
    return vec4(-direction.y * r * uChartScale.x, direction.x * r * uChartScale.y, 0.0, 1.0);
  }
  return domeClip(world);
}
`;

const horizonCut = /* glsl */ `
uniform float uHideBelowHorizon;
varying float vAltitude;
varying float vCull;
`;

export const starVertex = /* glsl */ `
${common}
attribute float magnitude;
attribute vec3 starColor;
uniform float uLimit;
uniform float uPixelRatio;
uniform float uStarScale;  // finder chart: larger, firmer dots
uniform float uFieldBonus; // fainter stars as the field narrows (magnitudes)
uniform float uScintillation;
uniform float uTime;       // seconds
varying vec3 vColor;
varying float vAlpha;

void main() {
  gl_Position = skyClip(position);
  float shown = magnitude;
  float limit = uLimit;
  vColor = starColor;
  // In the dome and the all-sky chart each star is dimmed and reddened by the air it shines
  // through, and hidden when the sky around it is too bright (the naked-eye limit there + 2).
  if (uBrightnessOn > 0.5 && uMode < 1.5) {
    float airmass = min(kastenYoung(vAltitude), 40.0);
    shown += uExtinction * airmass;
    limit = min(limit, nakedEyeLimit(skySqm(gNeu)) + 2.0 + uFieldBonus);
    float reddening = clamp(uExtinction * (airmass - 1.0), 0.0, 3.0);
    vColor *= vec3(1.0, 1.0 - 0.09 * reddening, 1.0 - 0.2 * reddening);
  }
  if (shown > limit) {
    gl_PointSize = 0.0;
    vAlpha = 0.0;
    return;
  }
  // Relative flux above the limit drives size and brightness.
  float flux = pow(10.0, -0.4 * (shown - limit));
  gl_PointSize = clamp(1.3 * pow(flux, 0.17), 1.3, 9.0) * uStarScale * uPixelRatio;
  float minimumAlpha = uStarScale > 1.0 ? 0.65 : 0.3;
  vAlpha = clamp(0.3 * pow(flux, 0.3), minimumAlpha, 1.0);
  if (uScintillation > 0.5 && uMode < 1.5) {
    // Twinkling grows toward the horizon (more air); a per-star phase from its position.
    float strength = 0.35 * clamp((kastenYoung(vAltitude) - 1.0) / 6.0, 0.05, 1.0);
    float phase = dot(position, vec3(127.1, 311.7, 74.7));
    vAlpha *= 1.0 - strength * (0.5 + 0.5 * sin(uTime * 9.0 + phase) * sin(uTime * 5.3 + phase * 1.7));
  }
}
`;

export const starFragment = /* glsl */ `
${horizonCut}
varying vec3 vColor;
varying float vAlpha;

void main() {
  if (vCull > 0.001) discard;
  if (vAlpha <= 0.0 || (uHideBelowHorizon > 0.5 && vAltitude < 0.0)) discard;
  float d = length(gl_PointCoord - 0.5) * 2.0;
  float shape = 1.0 - smoothstep(0.35, 1.0, d);
  if (shape <= 0.01) discard;
  gl_FragColor = vec4(vColor, vAlpha * shape);
}
`;

export const lineVertex = /* glsl */ `
${common}
attribute float group;
varying float vGroup;

void main() {
  vGroup = group;
  gl_Position = skyClip(position);
}
`;

export const lineFragment = /* glsl */ `
${horizonCut}
uniform vec3 uColor;
uniform float uOpacity;
uniform float uGroupFade;   // Milky Way: inner density levels are drawn stronger
varying float vGroup;

void main() {
  if (vCull > 0.001) discard;
  if (uHideBelowHorizon > 0.5 && vAltitude < -0.05) discard;
  float alpha = uOpacity * (uGroupFade > 0.0 ? (0.45 + uGroupFade * vGroup) : 1.0);
  gl_FragColor = vec4(uColor, alpha);
}
`;

export const dsoVertex = /* glsl */ `
${common}
attribute float magnitude;
attribute float majorAxis;   // arcmin, 0 = unknown
attribute float minorAxis;
attribute float positionAngle; // degrees, < 0 = unknown
attribute float shape;       // 0 ellipse (galaxies), 1 circle (clusters), 2 square (nebulae), 3 ring (planetary)
attribute float highlight;   // 0 normal, 1 in the plan, 2 selected
uniform float uLimit;
uniform float uPixelsPerDegree;
uniform float uPixelRatio;
uniform vec2 uViewport;
varying float vShape;
varying float vRatio;
varying float vAngle;
varying float vHighlight;
varying float vSize;

vec2 screen(vec3 j2000) {
  vec4 clip = skyClip(j2000);
  return clip.xy / clip.w * 0.5 * uViewport;
}

void main() {
  gl_Position = skyClip(position);
  vHighlight = highlight;
  vShape = shape;

  float sizePx = majorAxis / 60.0 * uPixelsPerDegree;
  bool known = magnitude < 98.0;
  float shown = magnitude;
  float limit = uLimit;
  if (uBrightnessOn > 0.5 && uMode < 1.5) {
    // Dimmed by the air, and the limit falls by what the sky there hides (dark sky: 6.62).
    shown += uExtinction * min(kastenYoung(vAltitude), 40.0);
    limit -= max(0.0, 6.62 - nakedEyeLimit(skySqm(gNeu)));
  }
  bool visible = highlight > 0.5 || (known ? shown <= limit : sizePx >= 10.0);
  if (!visible) {
    gl_PointSize = 0.0;
    vSize = 0.0;
    return;
  }

  // Screen directions of celestial north and east at this point, to orient the ellipse.
  vec3 north = normalize(vec3(0.0, 0.0, 1.0) - position * position.z + vec3(1e-9));
  vec3 east = normalize(cross(vec3(0.0, 0.0, 1.0), position) + vec3(1e-9));
  vec2 here = screen(position);
  vec2 n = normalize(screen(normalize(position + north * 1e-3)) - here);
  vec2 e = normalize(screen(normalize(position + east * 1e-3)) - here);
  float pa = positionAngle < 0.0 ? 0.0 : radians(positionAngle);
  vec2 axis = cos(pa) * n + sin(pa) * e;
  vAngle = atan(axis.y, axis.x);
  vRatio = majorAxis > 0.0 ? clamp(minorAxis / majorAxis, 0.15, 1.0) : 1.0;
  skyClip(position); // the offsets above overwrote vAltitude

  float size = clamp(sizePx, 9.0, 480.0) + (highlight > 1.5 ? 10.0 : 0.0);
  vSize = size * uPixelRatio;
  gl_PointSize = vSize;
}
`;

export const dsoFragment = /* glsl */ `
${horizonCut}
uniform vec3 uColors[4];
uniform vec3 uPlanColor;
uniform vec3 uSelectedColor;
varying float vShape;
varying float vRatio;
varying float vAngle;
varying float vHighlight;
varying float vSize;

void main() {
  if (vCull > 0.001) discard;
  if (vSize <= 0.0 || (uHideBelowHorizon > 0.5 && vAltitude < 0.0)) discard;
  vec2 p = (gl_PointCoord - 0.5) * 2.0;
  p.y = -p.y;
  float c = cos(-vAngle);
  float s = sin(-vAngle);
  vec2 q = vec2(c * p.x - s * p.y, s * p.x + c * p.y);
  float line = clamp(2.2 / vSize, 0.02, 0.5);
  float r;
  int shape = int(vShape + 0.5);
  if (shape == 0) {
    r = length(vec2(q.x, q.y / vRatio));
  } else if (shape == 2) {
    r = max(abs(p.x), abs(p.y)) * 1.05;
  } else {
    r = length(p);
  }
  float outline = 1.0 - smoothstep(0.0, line, abs(r - (1.0 - line)));
  if (shape == 3) {
    outline = max(outline, 1.0 - smoothstep(0.0, line, abs(r - 0.35)));
  }
  if (outline <= 0.02) discard;
  vec3 color = vHighlight > 1.5 ? uSelectedColor : vHighlight > 0.5 ? uPlanColor : uColors[shape];
  gl_FragColor = vec4(color, outline * (vHighlight > 0.5 ? 1.0 : 0.8));
}
`;

export const groundVertex = /* glsl */ `
${domeProjection}
varying vec3 vWorld;
void main() {
  vWorld = position;
  vCull = 0.0;
  gl_Position = domeClip(position);
}
`;

export const groundFragment = /* glsl */ `
uniform vec3 uGround;
uniform vec3 uHorizon;
varying float vCull;
varying vec3 vWorld;
void main() {
  if (vCull > 0.001) discard;
  float depth = clamp(-normalize(vWorld).y * 6.0, 0.0, 1.0);
  gl_FragColor = vec4(mix(uHorizon, uGround, depth), 1.0);
}
`;

// The sky (dome and all-sky chart): one full-screen pass that finds the direction under each
// pixel (the inverse of the projection) and draws its brightness with the display curve and the
// colour of what lights it: the natural sky, light pollution, moonlight, twilight or daylight.
export const skyPassVertex = /* glsl */ `
void main() {
  gl_Position = vec4(position.xy, 0.0, 1.0);
}
`;

export function skyPassFragment(displayLevel: string): string {
  return /* glsl */ `
${domeUniforms}
${photometry}
uniform float uMode;
uniform vec2 uChartScale;
uniform vec2 uViewport;      // drawing buffer, px
uniform vec3 uNightChroma;   // colours of luminance 1
uniform vec3 uTintChroma;
uniform vec3 uMoonChroma;

${displayLevel}

void main() {
  vec2 clip = gl_FragCoord.xy / uViewport * 2.0 - 1.0;
  vec3 neu;
  if (uMode < 0.5) {
    vec2 p = vec2(clip.x * uAspect, clip.y) / uProjScale;
    float r = length(p);
    float theta = uProjK * atan(r / uProjK);
    vec2 u = r > 1e-9 ? p / r : vec2(0.0);
    vec3 world = transpose(uDomeBasis) * vec3(u * sin(theta), cos(theta));
    neu = vec3(-world.z, world.x, world.y);
  } else {
    float n = clip.y / uChartScale.y;
    float e = -clip.x / uChartScale.x;
    float r = length(vec2(n, e));
    if (r > 1.0) discard;
    float altitude = radians(90.0 * (1.0 - r));
    float az = atan(e, n);
    neu = vec3(cos(altitude) * cos(az), cos(altitude) * sin(az), sin(altitude));
  }
  float natural; float artificial; float moon; float twilight; vec3 twilightColor;
  skyParts(neu, natural, artificial, moon, twilight, twilightColor);
  float total = max(natural + artificial + moon + twilight, 1e-30);
  float level = displayLevel(-2.5 * log10f(total));
  // Twilight colours a little desaturated (a display, not a spectrum).
  vec3 tw = twilight > 0.0 ? mix(vec3(1.0), twilightColor / max(lum(twilightColor), 1e-9), 0.8) : vec3(0.0);
  vec3 chroma = (natural * uNightChroma + artificial * uTintChroma + moon * uMoonChroma + twilight * tw) / total;
  gl_FragColor = vec4(min(chroma / max(lum(chroma), 1e-6) * level, vec3(1.0)), 1.0);
}
`;
}

// Horizon profile: opaque ground drawn over the sky (positions in north/east/up).
export const silhouetteVertex = /* glsl */ `
${common}
void main() {
  gl_Position = skyClip(position);
}
`;

export const silhouetteFragment = /* glsl */ `
uniform vec3 uGround;
uniform vec3 uHorizon;
varying float vCull;
varying float vAltitude;
void main() {
  if (vCull > 0.001) discard;
  gl_FragColor = vec4(mix(uHorizon, uGround, clamp(1.0 - vAltitude / 25.0, 0.0, 1.0) * 0.6), 1.0);
}
`;
