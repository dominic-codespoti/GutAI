export function shiftDate(dateStr: string, days: number) {
  const d = new Date(dateStr + 'T12:00:00');
  d.setDate(d.getDate() + days);
  return d.toISOString().split('T')[0];
}

export function formatDateLabel(dateStr: string) {
  const today = new Date().toISOString().split('T')[0];
  const yesterday = shiftDate(today, -1);
  if (dateStr === today) return 'Today';
  if (dateStr === yesterday) return 'Yesterday';
  return new Date(dateStr + 'T12:00:00').toLocaleDateString(undefined, {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
  });
}

export const today = () => new Date().toISOString().split('T')[0];
