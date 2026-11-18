export const SWISS_TIME_ZONE: string = 'Europe/Zurich';

export interface SwissDateTime {
  date: string;
  time: string;
}

const FORMAT: Intl.DateTimeFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: SWISS_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23'
});

export function swissDateTime(value: Date): SwissDateTime {
  const parts: Record<string, string> = {};
  FORMAT.formatToParts(value).forEach((part) => { parts[part.type] = part.value; });

  return {
    date: `${parts.year}-${parts.month}-${parts.day}`,
    time: `${parts.hour}${parts.minute}`
  };
}
