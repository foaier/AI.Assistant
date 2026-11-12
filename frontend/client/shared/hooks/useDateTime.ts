import * as time from '@/shared/lib/dateTime';

/** Preserve the extracted application's Asia/Shanghai display convention. */
export function useDateTime() {
  return { ...time, timeZone: time.DISPLAY_TIME_ZONE, ready: true };
}
