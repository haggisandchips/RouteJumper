import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SessionEventFeed } from './session-event-feed';

describe('SessionEventFeed', () => {
  let component: SessionEventFeed;
  let fixture: ComponentFixture<SessionEventFeed>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SessionEventFeed],
    }).compileComponents();

    fixture = TestBed.createComponent(SessionEventFeed);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('events', []);
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('shows an empty-state message with no events', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No events yet');
  });

  it('renders a row per event, newest first as given', async () => {
    fixture.componentRef.setInput('events', [
      { id: '2', kind: 'arrived', systemName: 'Deciat', message: 'Arrived at Deciat', clientUtc: new Date().toISOString() },
      { id: '1', kind: 'plotted', systemName: 'Deciat', message: 'Jump plotted to Deciat', clientUtc: new Date().toISOString() },
    ]);
    await fixture.whenStable();

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('.event');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Arrived at Deciat');
    expect(rows[0].className).toContain('event--arrived');
  });
});
