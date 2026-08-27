import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { Firestore } from '../../core/firestore';
import { SessionPage } from './session-page';

describe('SessionPage', () => {
  let component: SessionPage;
  let fixture: ComponentFixture<SessionPage>;

  beforeEach(async () => {
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
});
