import { Service } from '@angular/core';
import { initializeApp } from 'firebase/app';
import {
  collection,
  doc,
  Firestore as FirestoreDb,
  getFirestore,
  onSnapshot,
  orderBy,
  query,
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

@Service()
export class Firestore {
  /** Live updates to a session's header doc - null once/if it's deleted or was never found. */
  watchSession(sessionId: string): Observable<Session | null> {
    return new Observable((subscriber) => {
      const ref = doc(db, 'sessions', sessionId);
      const unsubscribe = onSnapshot(
        ref,
        (snap) => subscriber.next(snap.exists() ? (snap.data() as Session) : null),
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
        (snap) =>
          subscriber.next(
            snap.docs.map((d) => ({ id: d.id, ...(d.data() as Omit<SessionEvent, 'id'>) })),
          ),
        (err) => subscriber.error(err),
      );
      return unsubscribe;
    });
  }
}
