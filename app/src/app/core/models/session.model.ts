export type SessionStatus = 'active' | 'completed' | 'panicked';

/** The header doc at `sessions/{uuid}` - shown fixed at the top of the page. */
export interface Session {
  startSystem: string;
  endSystem: string;
  createdUtc: string;
  status: SessionStatus;
}
