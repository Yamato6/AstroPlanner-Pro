// GLSL shared by every layer. Positions are J2000 unit vectors; the vertex shader applies the
// frame matrix computed from C# (uFrame = J2000 → north/east/up) and the Core refraction model,
// then maps north/east/up to three.js world space (x = east, y = up, z = −north).

const common = /* glsl */ `
uniform mat3 uFrame;
uniform vec4 uRefraction;      // a, b, c, d of Saemundsson's formula
uniform float uRefractionMin;  // below this true altitude no refraction is applied
uniform float uApplyRefraction;
uniform float uMode;           // 0 dome (perspective camera), 1 all-sky, 2 finder (gnomonic)
uniform mat3 uFinder;          // finder: rows east, north, center (J2000)
uniform vec2 uChartScale;      // all-sky: clip units per horizon radius; finder: per unit of tan(angle)

varying float vAltitude;

vec3 apparentWorld(vec3 j2000) {
  vec3 neu = uFrame * j2000;
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
  return projectionMatrix * viewMatrix * vec4(world, 1.0);
}
`;

const horizonCut = /* glsl */ `
uniform float uHideBelowHorizon;
varying float vAltitude;
`;

export const starVertex = /* glsl */ `
${common}
attribute float magnitude;
attribute vec3 starColor;
uniform float uLimit;
uniform float uPixelRatio;
uniform float uStarScale;  // finder chart: larger, firmer dots
varying vec3 vColor;
varying float vAlpha;

void main() {
  gl_Position = skyClip(position);
  if (magnitude > uLimit) {
    gl_PointSize = 0.0;
    vAlpha = 0.0;
    return;
  }
  // Relative flux above the limit drives size and brightness.
  float flux = pow(10.0, -0.4 * (magnitude - uLimit));
  gl_PointSize = clamp(1.3 * pow(flux, 0.17), 1.3, 9.0) * uStarScale * uPixelRatio;
  float minimumAlpha = uStarScale > 1.0 ? 0.65 : 0.3;
  vAlpha = clamp(0.3 * pow(flux, 0.3), minimumAlpha, 1.0);
  vColor = starColor;
}
`;

export const starFragment = /* glsl */ `
${horizonCut}
varying vec3 vColor;
varying float vAlpha;

void main() {
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
  bool visible = highlight > 0.5 || (known ? magnitude <= uLimit : sizePx >= 10.0);
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
varying vec3 vWorld;
void main() {
  vWorld = position;
  gl_Position = projectionMatrix * viewMatrix * vec4(position, 1.0);
}
`;

export const groundFragment = /* glsl */ `
uniform vec3 uGround;
uniform vec3 uHorizon;
varying vec3 vWorld;
void main() {
  float depth = clamp(-normalize(vWorld).y * 6.0, 0.0, 1.0);
  gl_FragColor = vec4(mix(uHorizon, uGround, depth), 1.0);
}
`;

// Sky backdrop of the dome: an upper hemisphere colored from the zenith to the (brighter) horizon.
export const skyDomeVertex = /* glsl */ `
varying vec3 vWorld;
void main() {
  vWorld = position;
  gl_Position = projectionMatrix * viewMatrix * vec4(position, 1.0);
}
`;

const skyGradient = /* glsl */ `
uniform vec3 uZenithColor;
uniform vec3 uHorizonColor;
vec3 skyGradient(float sinAltitude) {
  return mix(uHorizonColor, uZenithColor, pow(clamp(sinAltitude, 0.0, 1.0), 0.35));
}
`;

export const skyDomeFragment = /* glsl */ `
${skyGradient}
varying vec3 vWorld;
void main() {
  gl_FragColor = vec4(skyGradient(normalize(vWorld).y), 1.0);
}
`;

// Sky backdrop of the all-sky chart: a disc in clip space, r = 1 at the horizon.
export const skyDiscVertex = /* glsl */ `
uniform vec2 uChartScale;
varying float vRadius;
void main() {
  vRadius = length(position.xy);
  gl_Position = vec4(position.x * uChartScale.x, position.y * uChartScale.y, 0.0, 1.0);
}
`;

export const skyDiscFragment = /* glsl */ `
${skyGradient}
varying float vRadius;
void main() {
  gl_FragColor = vec4(skyGradient(sin((1.0 - vRadius) * 1.5707963)), 1.0);
}
`;

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
varying float vAltitude;
void main() {
  gl_FragColor = vec4(mix(uHorizon, uGround, clamp(1.0 - vAltitude / 25.0, 0.0, 1.0) * 0.6), 1.0);
}
`;
