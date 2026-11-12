export function normalizeLocale(locale?: string): string | undefined {
  if (!locale) return undefined;

  const normalized = String(locale).trim();
  if (!normalized) return undefined;

  if (/^[a-z]{2}[A-Z]{2}$/.test(normalized)) {
    return `${normalized.slice(0, 2)}-${normalized.slice(2)}`;
  }

  return normalized;
}

/** Small, explicit pattern adapter for legacy consumers; date-only values never shift. */
export function formatInstantPattern(value: string | number | Date | null | undefined,
  pattern = 'YYYY/MM/DD HH:mm:ss', timeZone = DISPLAY_TIME_ZONE): string {
  if (value == null || value === '') return '';
  const dateOnly = typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value);
  const instant = value instanceof Date ? value : typeof value === 'number' ? new Date(value) : parseIsoDate(value);
  if (!instant || Number.isNaN(instant.getTime())) return '';
  const parts = getWallClockParts(instant.toISOString(), dateOnly ? 'UTC' : timeZone);
  if (!parts) return '';
  const [YYYY, MM, DD] = parts.date.split('-');
  const [HH, mm] = parts.time.split(':');
  const tokens: Record<string, string> = { YYYY, MM, DD, HH, mm, ss: String(instant.getUTCSeconds()).padStart(2, '0') };
  return pattern.replace(/YYYY|MM|DD|HH|mm|ss/g, (token) => tokens[token]);
}

// 后端（Dapper 读 MySQL datetime）序列化的 UTC 时间不带时区标记，
// 如 "2026-07-27T03:15:05" 或 "2026-07-27 03:15:05"。
// 直接 new Date() 会被浏览器当作本地时间，导致 UTC 墙钟时间被原样显示。
// 统一约定：不带时区标记的日期时间字符串一律按 UTC 解析。
const NAIVE_DATETIME_PATTERN = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?$/;

export function parseIsoDate(value?: string | null): Date | null {
  if (!value) return null;

  const trimmed = String(value).trim();
  const normalized = NAIVE_DATETIME_PATTERN.test(trimmed)
    ? `${trimmed.replace(' ', 'T')}Z`
    : trimmed;

  const parsed = new Date(normalized);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }

  return parsed;
}

export function toIsoString(value?: Date | string | null): string | undefined {
  if (!value) return undefined;

  if (typeof value === 'string') {
    const parsed = parseIsoDate(value);
    return parsed ? parsed.toISOString() : undefined;
  }

  return Number.isNaN(value.getTime()) ? undefined : value.toISOString();
}

// 本地墙钟切片（datetime-local 风格 "2026-03-22T09:00"）↔ UTC ISO 的互转。
// 注意与 parseIsoDate 的"naive 一律按 UTC 解析"约定不同：这组 adapter 里
// naive 输入表示**本地时间**，专门用于 DateTimePicker（ISO 接口）与沿用
// 本地切片契约的表单 state/payload 之间的边界适配。
export function localSliceToIso(local?: string | null): string | undefined {
  if (!local) return undefined;
  const parsed = new Date(local);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString();
}

export function isoToLocalSlice(iso?: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

// date-only 契约（"2026-03-22"）↔ ISO 的互转：以本地零点为锚，规避
// 裸 slice(0,10) 切 UTC ISO 造成的时区跨天问题。
export function dateSliceToIso(dateSlice?: string | null): string | undefined {
  if (!dateSlice) return undefined;
  return localSliceToIso(`${dateSlice.slice(0, 10)}T00:00`);
}

export function isoToDateSlice(iso?: string | null): string {
  return isoToLocalSlice(iso).slice(0, 10);
}

// ── Asia/Shanghai（UTC+8）墙钟切片边界（TAPD 1130533319001143380 价目表生效时间）──
// 后端 naive=UTC 的 datetime 与 DateTimePicker 的"墙钟切片"（'yyyy-MM-ddTHH:mm'，
// datetime-local 风格）语义不同：切片直出显示，因此表单 state 统一存北京墙钟切片。
// Asia/Shanghai 无夏令时（固定 UTC+8），+8h 固定偏移换算是安全的。
// REQ-20260913-005 P0/Q2：本组 utc8* 是下方 zone 参数化族的 compat 特化
// （zone='Asia/Shanghai'）；存量调用点零改动保持北京口径，编辑/显示链按 P1/P2
// 逐表单显式切换到 zoned 族，不做原地改签名、不做全仓机械替换。
const UTC8_OFFSET_MS = 8 * 60 * 60 * 1000;

/** 后端值（naive 按 UTC，Z/offset 原语义）→ 北京墙钟切片（DateTimePicker 载入边界）。 */
export function utc8IsoToLocalSlice(value?: string | null): string {
  const parsed = parseIsoDate(value);
  if (!parsed) return '';
  // instant + 8h 后按 UTC 字段读出的墙钟即北京墙钟（固定偏移，无夏令时）；
  // 切到分钟与 DateTimePicker 切片契约对齐。
  return new Date(parsed.getTime() + UTC8_OFFSET_MS).toISOString().slice(0, 16);
}

/** 北京墙钟切片 → UTC ISO（提交/payload 出口边界）。 */
export function utc8LocalSliceToIso(value?: string | null): string | undefined {
  if (!value) return undefined;
  const trimmed = String(value).trim();
  if (!trimmed) return undefined;

  if (!NAIVE_DATETIME_PATTERN.test(trimmed)) {
    // 已带 Z/offset：已是 UTC instant，原语义透传（防御性兜底）。
    return toIsoString(trimmed);
  }

  // naive 切片按北京墙钟解释：instant = 墙钟数字按 UTC 解析 − 8h。
  const wallAsUtc = parseIsoDate(trimmed);
  if (!wallAsUtc) return undefined;
  return new Date(wallAsUtc.getTime() - UTC8_OFFSET_MS).toISOString();
}

// ── zone 参数化墙钟切片族（REQ-20260913-005 P0）──
// 编辑/显示基准收敛 event.timezone（Q1 裁决）后的通用换算：真 IANA 换算（含 DST），
// 不依赖浏览器本地时区。切片契约与 utc8* 相同（'yyyy-MM-ddTHH:mm'，datetime-local 风格）；
// ±8h 固定算术仅存在于北京特化（utc8*），本族一律走 Intl。

/** 给定 instant 在 zone 的墙钟数字按 UTC 解释的 epoch − instant epoch = zone 偏移毫秒。 */
function zoneOffsetMs(instant: Date, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(instant);
  const pick = (type: Intl.DateTimeFormatPartTypes): number =>
    Number(parts.find((part) => part.type === type)?.value);
  const wallAsUtc = Date.UTC(
    pick('year'),
    pick('month') - 1,
    pick('day'),
    pick('hour'),
    pick('minute'),
    pick('second'),
  );
  return wallAsUtc - instant.getTime();
}

/** 后端值（naive 按 UTC，Z/offset 原语义）→ 指定 zone 墙钟切片（缺省平台种子；非法/空 → ''）。 */
export function zonedIsoToWallSlice(value?: string | null, timeZone: string = DISPLAY_TIME_ZONE): string {
  const parts = getWallClockParts(value, timeZone);
  return parts ? `${parts.date}T${parts.time}` : '';
}

/** 指定 zone 墙钟切片 → UTC ISO（提交/payload 出口边界；缺省平台种子；空 → undefined）。
 * naive 切片按该 zone 墙钟解释；已带 Z/offset 的输入原语义透传（与 utc8 对同口径）。
 * DST 缺失/重复时间拒绝静默改写，重复时间可用显式 offset 表达。 */
export function wallSliceToZonedIso(value?: string | null, timeZone: string = DISPLAY_TIME_ZONE): string | undefined {
  if (!value) return undefined;
  const trimmed = String(value).trim();
  if (!trimmed) return undefined;

  if (!NAIVE_DATETIME_PATTERN.test(trimmed)) {
    return toIsoString(trimmed);
  }

  const wallAsUtc = parseIsoDate(trimmed);
  if (!wallAsUtc) return undefined;

  const zone = requireTimeZone(timeZone);
  const wallMs = wallAsUtc.getTime();
  // Sample both sides of an offset transition, then require an exact inverse.
  // A fixed point iteration can silently move gaps or choose one side of folds.
  const offsets = new Set<number>();
  const wholeSecond = Math.floor(wallMs / 1000) * 1000;
  for (let hours = -48; hours <= 48; hours += 6)
    offsets.add(zoneOffsetMs(new Date(wholeSecond + hours * 3600000), zone));
  const matches = [...offsets].map((offset) => wallMs - offset).filter((instant) =>
    instant + zoneOffsetMs(new Date(Math.floor(instant / 1000) * 1000), zone) === wallMs);
  if (matches.length === 0) throw new RangeError('该时间因夏令时调整不存在，请选择其他时间。');
  if (matches.length > 1) throw new RangeError('该时间在夏令时切换时出现两次，请选择其他时间或提供明确的时差。');
  return new Date(matches[0]).toISOString();
}

export function requireTimeZone(timeZone: string): string {
  const zone = timeZone?.trim();
  if (!zone) throw new RangeError('请先选择时区。');
  new Intl.DateTimeFormat('en', { timeZone: zone }).format(0);
  return zone;
}

/** Inclusive calendar dates → UTC half-open interval. Date-only values remain calendar dates. */
export function zonedDateRange(from?: string, through?: string, timeZone = DISPLAY_TIME_ZONE): {
  from?: string; toExclusive?: string;
} {
  const parseDay = (value: string) => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) throw new RangeError('请选择有效日期。');
    const day = new Date(`${value}T00:00:00Z`);
    if (!Number.isFinite(day.getTime()) || day.toISOString().slice(0, 10) !== value)
      throw new RangeError('请选择有效日期。');
    return day;
  };
  const start = from ? parseDay(from) : undefined;
  const end = through ? parseDay(through) : undefined;
  if (start && end && start > end) throw new RangeError('结束日期不能早于开始日期。');
  if (end) end.setUTCDate(end.getUTCDate() + 1);
  return {
    from: start ? wallSliceToZonedIso(`${from}T00:00`, timeZone) : undefined,
    toExclusive: end ? wallSliceToZonedIso(`${end.toISOString().slice(0, 10)}T00:00`, timeZone) : undefined,
  };
}

/**
 * DateTimePicker onChange 值 → 规范化北京墙钟切片。
 * picker 交互后吐 toISOString()，其中编码的是"浏览器本地墙钟"——而用户看到的
 * /输入的数字是北京墙钟切片的直出显示，因此取浏览器本地墙钟数字作为切片保存；
 * naive 输入（已是切片）原样保留。
 */
export function utc8PickerValueToLocalSlice(value?: string | null): string {
  if (!value) return '';
  const trimmed = String(value).trim();
  if (!trimmed) return '';
  if (NAIVE_DATETIME_PATTERN.test(trimmed)) return trimmed.slice(0, 16);
  return isoToLocalSlice(trimmed);
}

export function formatDateLabel(value?: string | null): string {
  const parsed = parseIsoDate(value);
  if (!parsed) return '—';
  return parsed.toISOString().slice(0, 10);
}

// Spec 溯源：TAPD 1130533319001143383 / 1130533319001142715 / 1130533319001142578 ——
// 产品口径：后端裸 UTC 串的展示一律按 UTC+8 北京时间（Asia/Shanghai）墙钟，
// 与浏览器本地时区无关（QA 在 UTC 时区的浏览器上必须同样看到北京时间）。
export const DISPLAY_TIME_ZONE = 'Asia/Shanghai';

// §5-⑤：可选 timeZone 参数（缺省平台种子）——运营页接线时传 useOperatorDisplayTimeZone()，
// 参与者侧传活动时区；不传 = 现行为，向后兼容。空串防御回落平台种子。
export function formatDateDisplay(value?: string | null, locale?: string, timeZone: string = DISPLAY_TIME_ZONE): string {
  const parsed = parseIsoDate(value);
  if (!parsed) return '—';
  // A birthday/check-in date is a calendar date, never an instant at UTC midnight.
  const zone = /^\d{4}-\d{2}-\d{2}$/.test(value?.trim() ?? '') ? 'UTC' : requireTimeZone(timeZone);
  return parsed.toLocaleDateString(normalizeLocale(locale), { timeZone: zone });
}

export function formatDateTimeDisplay(value?: string | null, locale?: string, timeZone: string = DISPLAY_TIME_ZONE): string {
  const parsed = parseIsoDate(value);
  if (!parsed) return '—';
  return parsed.toLocaleString(normalizeLocale(locale), { timeZone: requireTimeZone(timeZone) });
}

// Wave H-H2 跨文件 dedup 收单：6 处页面级副本（paymentConfigForm /
// AdminInvoiceTaxConfigPage / AdminEventInvoiceSettingsPage.model /
// AgreementConsoleListPage / agreementConsoleFormat / GuestArrangementLocalTransportTab）
// 字节相同或可证等价（if 提前返回 vs 三元式）。
// 其中 GuestArrangementLocalTransportTab 已于 REQ-20260913-001（2026-09-13）迁至
// formatDateTimeDisplay（北京墙钟口径），现存消费者为其余 5 处。
// 保留旧导出名以兼容已有页面入口；所有存量/新页面时间统一采用产品约定的北京时间。
// 新代码直接调用 formatDateTimeDisplay，避免继续扩散 legacy 名称。
export function formatDateTimeBrowserLocal(value?: string | null, configuredTimeZone = 'Asia/Shanghai'): string {
  return formatDateTimeDisplay(value, undefined, configuredTimeZone);
}

export function sortByIsoDateDesc<T>(items: T[], getter: (item: T) => string | undefined | null): T[] {
  return items.slice().sort((left, right) => {
    const leftTime = parseIsoDate(getter(left))?.getTime() || 0;
    const rightTime = parseIsoDate(getter(right))?.getTime() || 0;
    return rightTime - leftTime;
  });
}

// RFC-078 P3-S3：板块/议程组 effective 时间窗的展示格式化。
// 单日 `YYYY-MM-DD HH:mm – HH:mm`；跨日 `MM-DD HH:mm – MM-DD HH:mm`；
// 仅单侧有界时缺界侧显示 —；双侧皆空返回 ''。
//
// 时区口径与本文件 DISPLAY_TIME_ZONE 系列一致：先按 UTC 解析（后端 Dapper 读 MySQL
// datetime 后序列化不带 Z，实为 UTC，见 parseIsoDate），再换算到 UTC+8 北京时间。
//
// 历史修正（TAPD 日程时区缺陷）：原实现按字符切片（raw.slice(11,16)）直接显示后端串，
// 并援引 REQ-20260828-001 A2「后端日程时间为墙钟 ISO，不做任何时区换算」。该前提不成立
// —— 日程时间 UTC 存储是 002 的规格（spec.md FR-040 / data-model.md / research.md），
// 切片等于把 UTC 小时当北京时间显示（01:00 应为 09:00）。A2 结论就此推翻，
// 本组函数与 scheduleGridGrouping.getScheduleTimeText 统一到同一口径。
// REQ-20260909-003 §5-③：墙钟分量泛化——formatter 按时区缓存（北京种子为缺省；参与者侧传活动时区）。
const wallClockFormatters = new Map<string, Intl.DateTimeFormat>();
const displayDateTimeFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: DISPLAY_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

const displayDateTimeSecondFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: DISPLAY_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
});

function getWallClockFormatter(requested: string): Intl.DateTimeFormat {
  const timeZone = requireTimeZone(requested);
  let formatter = wallClockFormatters.get(timeZone);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat('en-CA', {
        timeZone,
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        hourCycle: 'h23',
    });
    wallClockFormatters.set(timeZone, formatter);
  }
  return formatter;
}


/** 后端时间串 → 指定时区墙钟的日期/时间分量（缺省平台种子；与 getDisplayDateTimeParts 同构）。 */
export function getWallClockParts(
  value?: string | null,
  timeZone: string = DISPLAY_TIME_ZONE,
): { date: string; time: string } | null {
  const parsed = parseIsoDate(value);
  if (!parsed) return null;
  const parts = (timeZone === DISPLAY_TIME_ZONE ? displayDateTimeFormatter : getWallClockFormatter(timeZone)).formatToParts(parsed);
  const pick = (type: Intl.DateTimeFormatPartTypes): string =>
    parts.find((part) => part.type === type)?.value ?? '';
  const [year, month, day, hour, minute] =
    (['year', 'month', 'day', 'hour', 'minute'] as const).map(pick);
  if (!year || !month || !day || !hour || !minute) return null;
  return { date: `${year}-${month}-${day}`, time: `${hour}:${minute}` };
}

/**
 * 后端时间串 → 指定时区的日期/时间分量（缺省北京，供 `MM-DD HH:mm` 等自定义格式组合）。
 * REQ-20260909-003：时区由调用方按受众解析（resolveDisplayTimeConfig）后注入。
 * 需要现成整串时优先用 formatTimeDisplay / formatDateTimeMinuteDisplay。
 * 空/非法输入返回 null。
 */
export function getDisplayDateTimeParts(
  value?: string | null,
  timeZone: string = DISPLAY_TIME_ZONE,
): { date: string; time: string } | null {
  // §5-③ 泛化后委托 getWallClockParts；timeZone 参数生效（缺省平台种子，不传行为不变）。
  return getWallClockParts(value, timeZone);
}

/** 后端时间串 → 指定时区 `HH:mm`（缺省北京）；空/非法输入返回 —。 */
export function formatTimeDisplay(value?: string | null, timeZone: string = DISPLAY_TIME_ZONE): string {
  return getDisplayDateTimeParts(value, timeZone)?.time ?? '—';
}

/** 后端时间串 → 指定时区 `YYYY-MM-DD HH:mm`（缺省北京，分钟精度）；空/非法输入返回 —。 */
export function formatDateTimeMinuteDisplay(value?: string | null, timeZone: string = DISPLAY_TIME_ZONE): string {
  const parts = getDisplayDateTimeParts(value, timeZone);
  return parts ? `${parts.date} ${parts.time}` : '—';
}

/** 后端时间串 → 指定时区 `YYYY-MM-DD HH:mm:ss`（缺省北京）；空/非法输入返回 —。 */
export function formatDateTimeSecondDisplay(value?: string | null, timeZone: string = DISPLAY_TIME_ZONE): string {
  const parsed = parseIsoDate(value);
  if (!parsed) return '—';

  let formatter = displayDateTimeSecondFormatter;
  if (timeZone !== DISPLAY_TIME_ZONE) {
    formatter = new Intl.DateTimeFormat('en-CA', {
        timeZone: requireTimeZone(timeZone),
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hourCycle: 'h23',
    });
  }

  const parts = formatter.formatToParts(parsed);
  const pick = (type: Intl.DateTimeFormatPartTypes): string =>
    parts.find((part) => part.type === type)?.value ?? '';
  const year = pick('year');
  const month = pick('month');
  const day = pick('day');
  const hour = pick('hour');
  const minute = pick('minute');
  const second = pick('second');
  if (![year, month, day, hour, minute, second].every(Boolean)) return '—';
  return `${year}-${month}-${day} ${hour}:${minute}:${second}`;
}

export function formatWindowRangeDisplay(start?: string | null, end?: string | null, timeZone = DISPLAY_TIME_ZONE): string {
  const startParts = getDisplayDateTimeParts(start, timeZone);
  const endParts = getDisplayDateTimeParts(end, timeZone);
  if (!startParts && !endParts) return '';
  if (startParts && endParts) {
    if (startParts.date === endParts.date) {
      return `${startParts.date} ${startParts.time} – ${endParts.time}`;
    }
    return `${startParts.date.slice(5)} ${startParts.time} – ${endParts.date.slice(5)} ${endParts.time}`;
  }
  const single = (startParts ?? endParts)!;
  return startParts ? `${single.date} ${single.time} – —` : `— – ${single.date} ${single.time}`;
}

/**
 * REQ-20260918-001 D3：列表级紧凑窗区间（去年份，到分钟；子会面板 2026-09-14 口径提升为共享件）。
 * 两侧都缺/非法 → ''；同日 `MM-DD HH:mm – HH:mm`；跨日 `MM-DD HH:mm – MM-DD HH:mm`；
 * 仅单侧时另一侧以 — 占位。时区缺省 DISPLAY_TIME_ZONE（北京墙钟）。
 */
export function formatWindowRangeCompact(start?: string | null, end?: string | null, timeZone = DISPLAY_TIME_ZONE): string {
  const startParts = getDisplayDateTimeParts(start, timeZone);
  const endParts = getDisplayDateTimeParts(end, timeZone);
  if (!startParts && !endParts) return '';
  if (startParts && endParts) {
    if (startParts.date === endParts.date) {
      return `${startParts.date.slice(5)} ${startParts.time} – ${endParts.time}`;
    }
    return `${startParts.date.slice(5)} ${startParts.time} – ${endParts.date.slice(5)} ${endParts.time}`;
  }
  const single = (startParts ?? endParts)!;
  return startParts ? `${single.date.slice(5)} ${single.time} – —` : `— – ${single.date.slice(5)} ${single.time}`;
}

/**
 * REQ-20260918-001 D3：窗口对串（后端 ScheduleWindowDisplay.Window / 前端 windowLabel 镜像产的
 * "start~end"，可缺侧 "start~" / "~end"）→ 紧凑区间显示。按第一个 "~" 拆对（两侧 trim，空串 → null）；
 * 无 "~" 时整体按 start 单侧渲染；整对空/非法 → ''。渲染层变换，不改契约串生产端。
 */
export function formatWindowPairCompactDisplay(pair?: string | null, timeZone = DISPLAY_TIME_ZONE): string {
  const trimmed = pair?.trim() ?? '';
  if (!trimmed) return '';
  const sepIndex = trimmed.indexOf('~');
  if (sepIndex < 0) return formatWindowRangeCompact(trimmed, null, timeZone);
  const startSide = trimmed.slice(0, sepIndex).trim();
  const endSide = trimmed.slice(sepIndex + 1).trim();
  return formatWindowRangeCompact(startSide || null, endSide || null, timeZone);
}

// ── 时区标签惯例（REQ-20260909-003 owner 裁决 2026-09-09）──
// 中国标准时区显示 locale 亲和「北京时间」，其余时区显示 GMT±H[:MM] 偏移
// （Intl shortOffset，随 DST 动态：夏 GMT-4 / 冬 GMT-5 是正确行为），任何 UI
// 不裸拼 IANA id。口径对齐后端 NotificationEventTimeFormatter（TAPD
// 1130533319001143450：中文环境 + 中国标准时区用「北京时间」，其余用中性
// GMT 偏移标签，不再裸拼 IANA 时区 ID）。
const BEIJING_ZONE_IDS = new Set(['Asia/Shanghai', 'Asia/Chongqing', 'Asia/Harbin', 'China Standard Time']);

const BEIJING_ZONE_LABELS: Record<string, string> = {
  'zh-CN': '北京时间',
  'zh-TW': '北京時間',
};

/** 时区 → 展示标签：北京时区别名 → locale 亲和「北京时间」；其余 → GMT 偏移；未知防御性透传原值。 */
export function timeZoneLabel(
  timeZone?: string | null,
  locale?: string,
  referenceDate: Date | number = Date.now(),
): string {
  const trimmed = timeZone?.trim();
  if (!trimmed) return '';

  if (BEIJING_ZONE_IDS.has(trimmed)) {
    return BEIJING_ZONE_LABELS[normalizeLocale(locale) ?? ''] ?? 'Beijing Time';
  }

  try {
    const parts = new Intl.DateTimeFormat(normalizeLocale(locale) ?? 'en-US', {
      timeZone: trimmed,
      timeZoneName: 'shortOffset',
    }).formatToParts(referenceDate);
    return parts.find((part) => part.type === 'timeZoneName')?.value ?? trimmed;
  } catch {
    // 非法/未知时区（或运行时不支持 shortOffset）：防御性透传原值，与 unknown 值直出惯例一致。
    return trimmed;
  }
}

// ── 显示时区解析器（REQ-20260909-003 §3 slice ①，audience 契约）──
// 全仓时间显示的单一出口：判据按「数据的受众」不按页面平面（§0-1）。
//   participant → event.timezone（创建时物化；blank → throw（R4 fail-fast，REQ-20260915-004）；
//                 加载态不调用本函数——zone 未到位时调用方 gate 到页面 loading 分支）
//   operator    → 租户 default.timezone → 平台种子 Asia/Shanghai（北京=中国用户习惯的种子，非展示规则）
// 纯函数：租户/活动时区由调用方注入（接线与取数属 slice ②）；locale 透传，格式化层自行 normalize。
export type DisplayTimeAudience = 'participant' | 'operator';

interface DisplayTimeConfigInputBase {
  locale?: string;
}

export interface ParticipantDisplayTimeConfigInput extends DisplayTimeConfigInputBase {
  audience: 'participant';
  /** participant 上下文的活动时区（event.timezone，已物化）。键必填（值可 null）：
   * 编译期逼调用方显式接线——缺失/blank 在运行期 throw（REQ-20260915-004 R4 fail-fast）。 */
  eventTimeZone: string | null;
}

export interface OperatorDisplayTimeConfigInput extends DisplayTimeConfigInputBase {
  audience: 'operator';
  /** operator 上下文的租户默认时区（config-dict default.timezone） */
  tenantTimeZone?: string | null;
  platformTimeZone?: string | null;
}

export type DisplayTimeConfigInput = ParticipantDisplayTimeConfigInput | OperatorDisplayTimeConfigInput;

export interface DisplayTimeConfig {
  timeZone: string;
  locale?: string;
}

export function resolveDisplayTimeConfig(input: DisplayTimeConfigInput): DisplayTimeConfig {
  if (input.audience === 'participant') {
    const zone = (input.eventTimeZone ?? '').trim();
    if (!zone) {
      throw new Error(
        'resolveDisplayTimeConfig: participant audience requires explicit eventTimeZone' +
          '（REQ-20260915-004 / R4 fail-fast，禁止 blank 静默回落 UTC）——' +
          '调用方必须接 event.timezone 或显式传值；zone 未到位的加载态不要调用本函数' +
          '（gate 到页面已有 loading 分支）。',
      );
    }
    return { timeZone: zone, locale: input.locale };
  }
  return { timeZone: (input.tenantTimeZone ?? '').trim() || (input.platformTimeZone ?? '').trim() || DISPLAY_TIME_ZONE, locale: input.locale };
}

// ── 域墙钟透传（REQ-20260913-004 D 组 / 117 域裁决）──
// 部分域的时间串不是 UTC instant，而是「域墙钟 naive 串」——以 117 正式行程
// departAt/arriveAt 为代表：写侧原样收发、后端透传透存，串里的数字就是活动时区
// 墙钟。这类值的显示口径 = 墙钟透传（存什么显什么），禁止走 parseIsoDate 按
// UTC instant 解析再换算——那会引入固定偏移（如 +8h）二次偏移，把正确数据显错。
// 与 GuestArrangementTravelTab.toFormalDateTimeLocal 的 slice(0,16) 同基准。
const WALL_CLOCK_DATETIME_PATTERN = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})/;

/**
 * 域墙钟串 → { date, time }：date 为墙钟日期钉在 UTC 零点的 Date（渲染时必须配
 * timeZone:'UTC'，保证与宿主时区无关），time 为 'HH:mm' 原文。Z/offset 后缀忽略
 * （与 slice(0,16) 同口径）。非法/越界输入返回 null。
 */
export function parseWallClockDateTime(value?: string | null): { date: Date; time: string } | null {
  const match = value?.match(WALL_CLOCK_DATETIME_PATTERN);
  if (!match) return null;

  const [, yearText, monthText, dayText, hourText, minuteText] = match;
  const year = Number(yearText);
  const month = Number(monthText);
  const day = Number(dayText);
  const hour = Number(hourText);
  const minute = Number(minuteText);
  const date = new Date(Date.UTC(year, month - 1, day));
  if (
    date.getUTCFullYear() !== year
    || date.getUTCMonth() !== month - 1
    || date.getUTCDate() !== day
    || hour > 23
    || minute > 59
  ) return null;

  return { date, time: `${hourText}:${minuteText}` };
}

/** 域墙钟串 → 'M/D HH:mm'（locale 亲和月日 + 墙钟时分）；非法输入返回 null。 */
export function formatWallClockDateTime(value?: string | null, locale?: string): string | null {
  const parsed = parseWallClockDateTime(value);
  if (!parsed) return null;
  const dateText = parsed.date.toLocaleDateString(normalizeLocale(locale), {
    timeZone: 'UTC',
    month: 'numeric',
    day: 'numeric',
  });
  return `${dateText} ${parsed.time}`;
}
