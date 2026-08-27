import { Service } from '@angular/core';
import { initializeApp } from 'firebase/app';
import {
  collection,
  deleteDoc,
  doc,
  DocumentData,
  Firestore as FirestoreDb,
  getFirestore,
  onSnapshot,
  orderBy,
  query,
  Timestamp,
} from 'firebase/firestore';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Session } from './models/session.model';
import { SessionEvent } from './models/session-event.model';

// Firestore access here is deliberately unauthenticated (no Firebase Auth,
// no SDK sign-in step) - privacy is UUID-obscurity in the URL, not real
// auth. Security rules allow `get`/nested `read` on an exact known session
// path but never `list` the top-level `sessions` collection, so a session
// id is required to read anything at all. See SPEC.md §13.
const firebaseApp = initializeApp(environment.firebase);
const db: FirestoreDb = getFirestore(firebaseApp);

// The JS SDK hands back `timestampValue` fields as `Timestamp` instances, not the RFC3339
// strings the desktop app writes over the REST API - `Session`/`SessionEvent` are typed as
// plain ISO strings for every consumer downstream, so the conversion happens once, here, at
// the boundary where the raw snapshot data comes in.
function toIsoString(value: Timestamp | string): string {
  return value instanceof Timestamp ? value.toDate().toISOString() : value;
}

@Service()
export class Firestore {
  /** Live updates to a session's header doc - null once/if it's deleted or was never found. */
  watchSession(sessionId: string): Observable<Session | null> {
    return new Observable((subscriber) => {
      const ref = doc(db, 'sessions', sessionId);
      const unsubscribe = onSnapshot(
        ref,
        (snap) => subscriber.next(snap.exists() ? toSession(snap.data()) : null),
        (err) => subscriber.error(err),
      );
      return unsubscribe;
    });
  }

  /** Live, newest-first updates to a session's event feed. */
  watchEvents(sessionId: string): Observable<SessionEvent[]> {
    return new Observable((subscriber) => {
      const eventsQuery = query(
        collection(db, 'sessions', sessionId, 'events'),
        orderBy('clientUtc', 'desc'),
      );
      const unsubscribe = onSnapshot(
        eventsQuery,
        (snap) => subscriber.next(snap.docs.map((d) => toSessionEvent(d.id, d.data()))),
        (err) => subscriber.error(err),
      );
      return unsubscribe;
    });
  }

  /**
   * Deletes one event doc from a session's feed. Permitted by firestore.rules the same as every
   * other operation here (the session id itself is the only "auth") - deliberately no
   * confirmation/undo at this layer, that's the caller's job.
   */
  deleteEvent(sessionId: string, eventId: string): Promise<void> {
    return deleteDoc(doc(db, 'sessions', sessionId, 'events', eventId));
  }
}

function toSession(data: DocumentData): Session {
  return { ...data, createdUtc: toIsoString(data['createdUtc']) } as Session;
}

function toSessionEvent(id: string, data: DocumentData): SessionEvent {
  return { id, ...data, clientUtc: toIsoString(data['clientUtc']) } as SessionEvent;
}
