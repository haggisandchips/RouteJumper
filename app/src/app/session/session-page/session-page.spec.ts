import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { Firestore } from '../../core/firestore';
import { SessionPage } from './session-page';

describe('SessionPage', () => {
  let component: SessionPage;
  let fixture: ComponentFixture<SessionPage>;
  let deleteEventSpy: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    deleteEventSpy = vi.fn(() => Promise.resolve());

    await TestBed.configureTestingModule({
      imports: [SessionPage],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ sessionId: 'test-session-id' })) },
        },
        {
          provide: Firestore,
          useValue: {
            watchSession: () => of(null),
            watchEvents: () => of([]),
            deleteEvent: deleteEventSpy,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SessionPage);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('onDeleteEvent deletes the event from the current session in Firestore', () => {
    component.onDeleteEvent('evt-1');

    expect(deleteEventSpy).toHaveBeenCalledWith('test-session-id', 'evt-1');
  });
});
