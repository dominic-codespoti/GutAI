export const severityColor = (n: number) =>
  n <= 3 ? '#22c55e' : n <= 6 ? '#f59e0b' : '#ef4444';

export const ratingColor = (rating: string | null | undefined) => {
  switch (rating) {
    case 'Safe': return '#22c55e';
    case 'Caution': return '#f59e0b';
    case 'Warning': return '#f97316';
    case 'Avoid': return '#ef4444';
    default: return '#94a3b8';
  }
};

export const cspiColor = (rating: string) => {
  switch (rating) {
    case 'Avoid': return '#ef4444';
    case 'CutBack': return '#f59e0b';
    case 'Caution': return '#f97316';
    default: return '#22c55e';
  }
};

export const confidenceColor = (c: string) => {
  switch (c) {
    case 'High': return '#ef4444';
    case 'Medium': return '#f59e0b';
    default: return '#22c55e';
  }
};

export const confidenceIcon = (c: string) => {
  switch (c) {
    case 'High': return 'alert-circle';
    case 'Medium': return 'warning';
    default: return 'information-circle';
  }
};
