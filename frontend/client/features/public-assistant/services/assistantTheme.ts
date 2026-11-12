const CHAT_TEXT_COLOR = "#172033";

export const LEGACY_ASSISTANT_CHAT_BACKGROUND =
  "linear-gradient(145deg, color-mix(in srgb, var(--brand-primary) 14%, #f8fbff) 0%, #f8f7ff 58%, #eef9ff 100%)";

export const assistantThemePresets = [
  { id: "blue", primaryColor: "#2563eb", chatBackgroundColor: "#f0f5fc" },
  { id: "teal", primaryColor: "#0f766e", chatBackgroundColor: "#eef7f5" },
  { id: "orange", primaryColor: "#c2410c", chatBackgroundColor: "#fff7ed" },
  { id: "slate", primaryColor: "#334155", chatBackgroundColor: "#f3f5f8" },
] as const;

function relativeLuminance(color: string): number | undefined {
  if (!/^#[0-9a-f]{6}$/i.test(color)) return undefined;
  const channel = (offset: number) => {
    const value = parseInt(color.slice(offset, offset + 2), 16) / 255;
    return value <= 0.04045
      ? value / 12.92
      : ((value + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
}

export function colorContrastRatio(first: string, second: string): number {
  const firstLuminance = relativeLuminance(first);
  const secondLuminance = relativeLuminance(second);
  if (firstLuminance === undefined || secondLuminance === undefined) return 0;
  return (
    (Math.max(firstLuminance, secondLuminance) + 0.05) /
    (Math.min(firstLuminance, secondLuminance) + 0.05)
  );
}

export function primaryColorReadable(color: string): boolean {
  return colorContrastRatio(color, "#ffffff") >= 4.5;
}

export function chatBackgroundReadable(color: string): boolean {
  return colorContrastRatio(color, CHAT_TEXT_COLOR) >= 4.5;
}

export function assistantThemeReadable(
  primaryColor: string,
  chatBackgroundColor?: string | null,
): boolean {
  return (
    primaryColorReadable(primaryColor) &&
    (!chatBackgroundColor || chatBackgroundReadable(chatBackgroundColor))
  );
}

export function resolveChatBackground(color?: string | null): string {
  return color && /^#[0-9a-f]{6}$/i.test(color)
    ? color
    : LEGACY_ASSISTANT_CHAT_BACKGROUND;
}
