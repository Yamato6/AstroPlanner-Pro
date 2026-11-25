// Public API of the sky engine. R0a keeps the v2 behaviour: one sky per page, driven by the host
// through the bridge protocol (docs/sky-renderer.md).
export { skyControl, type LightPollutionTint, type SkyQuality } from './control';
export { mountSky, type MountOptions } from './engine';
export {
  skyEvents,
  skyPoint,
  type PickedObject,
  type SkyEventMap,
  type SkyPoint,
  type SkyViewMode,
} from './events';
