// Host state through TanStack Query (spec §4): every area is a snapshot (`area.get`) with a
// revision, refetched when its `area.changed` event carries a newer revision (docs/bridge.md).
import type { EventName, MethodName, RequestOf, ResponseOf } from '@astroplanner/bridge-client';
import { QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { host } from '../host';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Events say when a snapshot is stale; no polling, no refetch on focus.
      staleTime: Infinity,
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
});

/** Snapshot methods that take no parameters, with the event that announces their changes. */
const snapshots = {
  'observer.get': 'observer.changed',
  'sky.get': 'sky.changed',
  'ui.get': 'ui.changed',
} as const satisfies Partial<Record<MethodName, EventName>>;

export type SnapshotMethod = keyof typeof snapshots;

/** A host snapshot, kept fresh by its change event. */
export function useSnapshot<M extends SnapshotMethod>(method: M) {
  const client = useQueryClient();
  const event = snapshots[method];

  useEffect(
    () =>
      host.on(event, ({ revision }) => {
        const current = client.getQueryData<{ revision: number }>([method]);
        if (!current || revision !== current.revision) {
          void client.invalidateQueries({ queryKey: [method] });
        }
      }),
    [client, event, method],
  );

  return useQuery({
    queryKey: [method],
    queryFn: ({ signal }) => host.request(method, {} as RequestOf<M>, { signal }),
    enabled: host.connected,
  });
}

/** Runs a `ui.*` command (theme, transparency, full screen) and stores the snapshot it answers with. */
export function useUiCommand<M extends 'ui.setTheme' | 'ui.setReduceTransparency' | 'ui.setFullScreen'>(
  method: M,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (params: RequestOf<M>) => host.request(method, params),
    onSuccess: (snapshot) => {
      client.setQueryData(['ui.get'], snapshot);
    },
    onError: (error) => {
      console.warn(method, 'failed', error);
    },
  });
}

/** Commands that answer with the new sky snapshot. */
type SkyCommand = Exclude<
  {
    [M in MethodName]: ResponseOf<M> extends ResponseOf<'sky.get'> ? M : never;
  }[MethodName],
  'sky.get'
>;

/** Runs a `sky.*` command and stores the snapshot it answers with. */
export function useSkyCommand<M extends SkyCommand>(method: M) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (params: RequestOf<M>) => host.request(method, params),
    onSuccess: (snapshot) => {
      client.setQueryData(['sky.get'], snapshot);
    },
    onError: (error) => {
      console.warn(method, 'failed', error);
    },
  });
}
