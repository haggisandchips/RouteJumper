export type SessionEventKind = 'plotted' | 'arrived' | 'refueled' | 'panic';

/** One doc from `sessions/{uuid}/events` - the live feed, newest first. */
export interface SessionEvent {
  id: string;
  kind: SessionEventKind;
  systemName: string;
  message: string;
  clientUtc: string;
}
