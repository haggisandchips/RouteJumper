import { TestBed } from '@angular/core/testing';

// `vi.hoisted` is required (not a plain top-level const) because vitest
// hoists `vi.mock` factory registration above ordinary module code - a
// factory that closes over a not-yet-hoisted variable would see it as
// undefined the first time the mocked module is evaluated.
const { onSnapshotMock, FakeTimestamp } = vi.hoisted(() => ({
  onSnapshotMock: vi.fn(),
  // A minimal stand-in for the real SDK's Timestamp - only `instanceof` and
  // `toDate()` are ever used by firestore.ts, so that's all this fakes.
  FakeTimestamp: class {
    constructor(private readonly date: Date) {}
    toDate(): Date {
      return this.date;
    }
  },
}));

vi.mock('firebase/app', () => ({
  initializeApp: vi.fn(() => ({})),
}));

vi.mock('firebase/firestore', () => ({
  getFirestore: vi.fn(() => ({})),
  doc: vi.fn((_db, ...segments: string[]) => ({ path: segments.join('/') })),
  collection: vi.fn((_db, ...segments: string[]) => ({ path: segments.join('/') })),
  query: vi.fn((ref) => ref),
  orderBy: vi.fn(),
  onSnapshot: onSnapshotMock,
  Timestamp: FakeTimestamp,
}));

// Imported after the mocks above so `firestore.ts`'s module-level
// initializeApp()/getFirestore() calls hit the fakes, not the real SDK.
import { Firestore } from './firestore';

describe('Firestore', () => {
  let service: Firestore;

  beforeEach(() => {
    onSnapshotMock.mockReset();
    TestBed.configureTestingModule({});
    service = TestBed.inject(Firestore);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('watchSession subscribes to the exact session doc and forwards its data', () => {
    let received: unknown;
    onSnapshotMock.mockImplementation((_ref, onNext: (snap: unknown) => void) => {
      onNext({ exists: () => true, data: () => ({ startSystem: 'Sol', endSystem: 'Colonia' }) });
      return vi.fn(); // unsubscribe
    });

    service.watchSession('abc-123').subscribe((session) => (received = session));

    expect(onSnapshotMock).toHaveBeenCalledTimes(1);
    expect(received).toEqual({ startSystem: 'Sol', endSystem: 'Colonia' });
  });

  it('watchSession forwards null when the doc does not exist', () => {
    let received: unknown = 'unset';
    onSnapshotMock.mockImplementation((_ref, onNext: (snap: unknown) => void) => {
      onNext({ exists: () => false });
      return vi.fn();
    });

    service.watchSession('missing').subscribe((session) => (received = session));

    expect(received).toBeNull();
  });

  it('watchEvents maps each doc to a SessionEvent carrying its id', () => {
    let received: unknown;
    onSnapshotMock.mockImplementation((_ref, onNext: (snap: unknown) => void) => {
      onNext({
        docs: [{ id: 'evt-1', data: () => ({ kind: 'plotted', systemName: 'Deciat' }) }],
      });
      return vi.fn();
    });

    service.watchEvents('abc-123').subscribe((events) => (received = events));

    expect(received).toEqual([{ id: 'evt-1', kind: 'plotted', systemName: 'Deciat' }]);
  });

  it('watchEvents converts a Firestore Timestamp clientUtc into an ISO string', () => {
    const jumpedAt = new Date('2026-08-27T12:34:56.000Z');
    let received: unknown;
    onSnapshotMock.mockImplementation((_ref, onNext: (snap: unknown) => void) => {
      onNext({
        docs: [
          {
            id: 'evt-1',
            data: () => ({ kind: 'plotted', systemName: 'Deciat', clientUtc: new FakeTimestamp(jumpedAt) }),
          },
        ],
      });
      return vi.fn();
    });

    service.watchEvents('abc-123').subscribe((events) => (received = events));

    expect((received as { clientUtc: string }[])[0].clientUtc).toBe(jumpedAt.toISOString());
  });

  it('watchSession converts a Firestore Timestamp createdUtc into an ISO string', () => {
    const startedAt = new Date('2026-08-27T09:00:00.000Z');
    let received: unknown;
    onSnapshotMock.mockImplementation((_ref, onNext: (snap: unknown) => void) => {
      onNext({ exists: () => true, data: () => ({ createdUtc: new FakeTimestamp(startedAt) }) });
      return vi.fn();
    });

    service.watchSession('abc-123').subscribe((session) => (received = session));

    expect((received as { createdUtc: string }).createdUtc).toBe(startedAt.toISOString());
  });

  it('unsubscribing the Observable calls the Firestore unsubscribe function', () => {
    const unsubscribe = vi.fn();
    onSnapshotMock.mockReturnValue(unsubscribe);

    const subscription = service.watchEvents('abc-123').subscribe();
    subscription.unsubscribe();

    expect(unsubscribe).toHaveBeenCalledTimes(1);
  });
});
