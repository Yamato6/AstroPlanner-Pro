// The app channel to the C# host (bridge v2, docs/bridge.md): one per page.
import { createAppBridge } from '@astroplanner/bridge-client';

export const host = createAppBridge();
