export interface AssistantQuickLink {
  id: string;
  label: string;
  url: string;
  icon: string;
  iconUrl?: string | null;
  iconAssetId?: string | null;
  enabled: boolean;
}

export function splitAssistantWelcomeMessage(value: string): {
  title: string;
  description: string;
} {
  const lines = value.replace(/\r\n?/g, "\n").split("\n");
  const titleIndex = lines.findIndex((line) => line.trim().length > 0);
  if (titleIndex < 0) return { title: "", description: "" };
  return {
    title: lines[titleIndex].trim(),
    description: lines.slice(titleIndex + 1).join("\n").trim(),
  };
}

export function composeAssistantWelcomeMessage(
  title: string,
  description: string,
): string {
  const normalizedTitle = title.trim();
  const normalizedDescription = description.trim();
  return normalizedDescription
    ? `${normalizedTitle}\n${normalizedDescription}`
    : normalizedTitle;
}

export function isSafeAssistantLink(value: string): boolean {
  if (
    !value ||
    value.length > 2000 ||
    /[\u0000-\u001f\u007f\\]/.test(value) ||
    value !== value.trim()
  )
    return false;
  if (value.startsWith("/")) return !value.startsWith("//");
  try {
    const url = new URL(value);
    return (
      ["http:", "https:"].includes(url.protocol) &&
      !url.username &&
      !url.password
    );
  } catch {
    return false;
  }
}

export function isSafeAssistantIconUrl(value: string): boolean {
  if (!isSafeAssistantLink(value)) return false;
  if (value.startsWith("/")) return !value.startsWith("//");
  try {
    return new URL(value).protocol === "https:";
  } catch {
    return false;
  }
}
export function assistantQuestions(
  own: string[],
  knowledge: string[],
): string[] {
  return [
    ...new Set([...own, ...knowledge].map((x) => x.trim()).filter(Boolean)),
  ].slice(0, 20);
}
