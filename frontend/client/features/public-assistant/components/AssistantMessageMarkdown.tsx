import type { ReactNode } from "react";

type Block =
  | { kind: "heading"; level: 1 | 2 | 3; text: string }
  | { kind: "list"; ordered: boolean; items: string[] }
  | { kind: "paragraph"; text: string };

const INLINE_PATTERN =
  /\*\*([^*]+)\*\*|`([^`]+)`|\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)|(https?:\/\/[^\s<>]+)/g;
const TRAILING_URL_PUNCTUATION = /[.,;:!?，。；：！？、)\]}]+$/;

function renderInline(text: string, keyPrefix: string): ReactNode[] {
  const nodes: ReactNode[] = [];
  let lastIndex = 0;
  let match: RegExpExecArray | null;
  let index = 0;
  INLINE_PATTERN.lastIndex = 0;

  while ((match = INLINE_PATTERN.exec(text)) !== null) {
    if (match.index > lastIndex) nodes.push(text.slice(lastIndex, match.index));

    const key = `${keyPrefix}-${index}`;
    if (match[1] !== undefined) {
      nodes.push(<strong key={key}>{match[1]}</strong>);
    } else if (match[2] !== undefined) {
      nodes.push(
        <code
          key={key}
          className="rounded bg-[var(--surface-subtle)] px-1 py-0.5 font-mono text-[0.85em]"
        >
          {match[2]}
        </code>,
      );
    } else if (match[4] !== undefined) {
      nodes.push(
        <a
          key={key}
          href={match[4]}
          target="_blank"
          rel="noreferrer noopener"
          className="break-all text-[var(--text-link)] underline underline-offset-2"
        >
          {match[3]}
        </a>,
      );
    } else {
      const rawUrl = match[5];
      const url = rawUrl.replace(TRAILING_URL_PUNCTUATION, "");
      const suffix = rawUrl.slice(url.length);
      nodes.push(
        <a
          key={key}
          href={url}
          target="_blank"
          rel="noreferrer noopener"
          className="break-all text-[var(--text-link)] underline underline-offset-2"
        >
          {url}
        </a>,
      );
      if (suffix) nodes.push(suffix);
    }

    lastIndex = match.index + match[0].length;
    index += 1;
  }

  if (lastIndex < text.length) nodes.push(text.slice(lastIndex));
  return nodes;
}

function parseBlocks(body: string): Block[] {
  const blocks: Block[] = [];
  let paragraphLines: string[] = [];
  let listItems: string[] = [];
  let listOrdered: boolean | null = null;

  const flushParagraph = () => {
    if (paragraphLines.length > 0) {
      blocks.push({ kind: "paragraph", text: paragraphLines.join(" ") });
      paragraphLines = [];
    }
  };
  const flushList = () => {
    if (listItems.length > 0) {
      blocks.push({ kind: "list", ordered: listOrdered === true, items: listItems });
      listItems = [];
      listOrdered = null;
    }
  };

  for (const rawLine of body.replace(/\r\n?/g, "\n").split("\n")) {
    const line = rawLine.trim();
    if (!line) {
      flushParagraph();
      flushList();
      continue;
    }

    const heading = /^(#{1,3})\s+(.+?)\s*#*$/.exec(line);
    if (heading) {
      flushParagraph();
      flushList();
      blocks.push({
        kind: "heading",
        level: heading[1].length as 1 | 2 | 3,
        text: heading[2],
      });
      continue;
    }

    const unordered = /^[-*]\s+(.*)$/.exec(line);
    if (unordered) {
      flushParagraph();
      if (listOrdered === true) flushList();
      listOrdered = false;
      listItems.push(unordered[1]);
      continue;
    }

    const ordered = /^\d+[.)]\s+(.*)$/.exec(line);
    if (ordered) {
      flushParagraph();
      if (listOrdered === false) flushList();
      listOrdered = true;
      listItems.push(ordered[1]);
      continue;
    }

    flushList();
    paragraphLines.push(line);
  }

  flushParagraph();
  flushList();
  return blocks;
}

export default function AssistantMessageMarkdown({ text }: { text: string }) {
  const blocks = parseBlocks(text);

  return (
    <div className="space-y-2 leading-6">
      {blocks.map((block, blockIndex) => {
        if (block.kind === "heading") {
          const Heading = `h${block.level}` as const;
          return (
            <Heading
              key={blockIndex}
              className="break-words font-semibold text-[var(--text-primary)]"
            >
              {renderInline(block.text, `h${blockIndex}`)}
            </Heading>
          );
        }

        if (block.kind === "list") {
          const List = block.ordered ? "ol" : "ul";
          return (
            <List
              key={blockIndex}
              className={`space-y-1 pl-5 ${block.ordered ? "list-decimal" : "list-disc"}`}
            >
              {block.items.map((item, itemIndex) => (
                <li key={itemIndex}>
                  {renderInline(item, `l${blockIndex}-${itemIndex}`)}
                </li>
              ))}
            </List>
          );
        }

        return (
          <p key={blockIndex}>
            {renderInline(block.text, `p${blockIndex}`)}
          </p>
        );
      })}
    </div>
  );
}
